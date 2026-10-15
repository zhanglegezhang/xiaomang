using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;

namespace ShareX
{
    internal enum VoiceTypingKeyAction { None, Arm, Release, Cancel, Interference }

    internal sealed class VoiceTypingChord
    {
        private readonly HashSet<Keys> pressed = new HashSet<Keys>();
        private bool latched, blocked;
        internal bool Held => pressed.Any(IsControl) && pressed.Any(IsAlt);
        internal bool IsPressed(Keys key) => pressed.Contains(key);
        internal Keys[] HeldModifiers => pressed.Where(k => IsControl(k) || IsAlt(k)).ToArray();
        internal static bool IsControl(Keys k) => k == Keys.LControlKey || k == Keys.RControlKey || k == Keys.ControlKey;
        internal static bool IsAlt(Keys k) => k == Keys.LMenu || k == Keys.RMenu || k == Keys.Menu;
        internal static bool IsModifier(Keys k) => IsControl(k) || IsAlt(k) ||
            k == Keys.LShiftKey || k == Keys.RShiftKey || k == Keys.ShiftKey || k == Keys.LWin || k == Keys.RWin;

        internal VoiceTypingKeyAction Process(Keys key, bool down)
        {
            bool wasHeld = Held;
            if (down) { if (!pressed.Add(key)) return VoiceTypingKeyAction.None; }
            else pressed.Remove(key);
            if (!pressed.Any(IsControl) && !pressed.Any(IsAlt)) { blocked = false; latched = false; }
            if (down && key == Keys.Escape) { blocked = true; return VoiceTypingKeyAction.Cancel; }
            if (down && !IsControl(key) && !IsAlt(key))
            {
                blocked = true;
                return VoiceTypingKeyAction.Interference;
            }
            if (wasHeld && !Held) return VoiceTypingKeyAction.Release;
            if (Held && !blocked && !latched && pressed.All(k => IsControl(k) || IsAlt(k)))
            {
                latched = true;
                return VoiceTypingKeyAction.Arm;
            }
            return VoiceTypingKeyAction.None;
        }
        internal void Reset() { pressed.Clear(); blocked = latched = false; }
    }

    internal sealed class VoiceTypingHooks : IDisposable
    {
        private delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
        private readonly HookProc keyboardProc, mouseProc;
        private IntPtr keyboard, mouse;
        private bool suppressedEscape;
        internal readonly VoiceTypingChord Chord = new VoiceTypingChord();
        internal Action<VoiceTypingKeyAction> Changed;
        internal Func<bool> Active;

        internal VoiceTypingHooks()
        {
            keyboardProc = Keyboard;
            mouseProc = Mouse;
            IntPtr module = GetModuleHandle(null);
            keyboard = SetWindowsHookEx(13, keyboardProc, module, 0);
            mouse = SetWindowsHookEx(14, mouseProc, module, 0);
            if (keyboard == IntPtr.Zero || mouse == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Dispose();
                throw new Win32Exception(error, "无法启动语音打字按键监听");
            }
        }

        private IntPtr Keyboard(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0)
            {
                int flags = Marshal.ReadInt32(l, 8);
                if ((flags & 0x10) != 0 && Marshal.ReadIntPtr(l, 16) == VoiceTypingTarget.InputTag &&
                    (w.ToInt32() == 0x100 || w.ToInt32() == 0x104))
                {
                    Keys restored = (Keys)Marshal.ReadInt32(l);
                    if ((VoiceTypingChord.IsControl(restored) || VoiceTypingChord.IsAlt(restored)) && !Chord.IsPressed(restored))
                        return (IntPtr)1;
                }
                // Ignore injected events (including AltGr's synthetic Ctrl and our Unicode input).
                if ((flags & 0x10) == 0)
                {
                    Keys key = (Keys)Marshal.ReadInt32(l);
                    bool down = w.ToInt32() == 0x100 || w.ToInt32() == 0x104;
                    if (key == Keys.Escape && down && Active?.Invoke() == true) suppressedEscape = true;
                    VoiceTypingKeyAction action = Chord.Process(key, down);
                    if (action != VoiceTypingKeyAction.None) Changed?.Invoke(action);
                    if (key == Keys.Escape && suppressedEscape)
                    {
                        if (!down) suppressedEscape = false;
                        return (IntPtr)1;
                    }
                }
            }
            return CallNextHookEx(keyboard, code, w, l);
        }

        private IntPtr Mouse(int code, IntPtr w, IntPtr l)
        {
            // Pointer motion is harmless. Clicks/wheel invalidate automatic insertion,
            // including moves between two browser fields sharing the same HWND.
            if (code >= 0 && Active?.Invoke() == true && w.ToInt32() != 0x200)
                Changed?.Invoke(VoiceTypingKeyAction.Interference);
            return CallNextHookEx(mouse, code, w, l);
        }

        internal static bool CtrlAltDown() => (GetAsyncKeyState(0x11) & 0x8000) != 0 &&
            (GetAsyncKeyState(0x12) & 0x8000) != 0;
        public void Dispose()
        {
            if (keyboard != IntPtr.Zero) UnhookWindowsHookEx(keyboard);
            if (mouse != IntPtr.Zero) UnhookWindowsHookEx(mouse);
            keyboard = mouse = IntPtr.Zero;
        }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    }

    internal sealed class VoiceTypingTarget
    {
        internal static readonly IntPtr InputTag = new IntPtr(0x584D5654);
        internal IntPtr Window { get; private set; }
        internal IntPtr Focus { get; private set; }
        internal string ElementId { get; private set; }
        internal bool Editable { get; private set; }
        internal bool Password { get; private set; }
        internal string FailureReason { get; private set; } = "not-inspected";
        internal bool HasCaretSnapshot { get; private set; }
        private int controlTypeId;
        private bool hasValuePattern, hasTextPattern;
        private string lastDiagnostic;
        private string originalText;
        private int insertionOffset;
        internal static VoiceTypingTarget CaptureNative()
        {
            IntPtr window = NativeMethods.GetForegroundWindow();
            uint thread = GetWindowThreadProcessId(window, out uint process);
            GUIThreadInfo info = new GUIThreadInfo { Size = Marshal.SizeOf<GUIThreadInfo>() };
            bool known = window != IntPtr.Zero && thread != 0 && GetGUIThreadInfo(thread, ref info);
            return new VoiceTypingTarget { Window = window, Focus = known ? info.Focus : IntPtr.Zero };
        }

        // UI Automation may call other processes; always invoke this on a background thread.
        internal void CaptureElement() => InspectElement(true);

        private void InspectElement(bool rememberText)
        {
            try
            {
                AutomationElement element = AutomationElement.FocusedElement;
                Editable = false;
                if (element == null) { FailureReason = "no-focused-element"; return; }
                Password = element.Current.IsPassword;
                ControlType type = element.Current.ControlType;
                controlTypeId = type.Id;
                hasValuePattern = element.TryGetCurrentPattern(ValuePattern.Pattern, out object value);
                hasTextPattern = element.TryGetCurrentPattern(TextPattern.Pattern, out object textPattern);
                bool? valueReadOnly = hasValuePattern ? ((ValuePattern)value).Current.IsReadOnly : null;
                object readOnlyAttribute = hasTextPattern ? ((TextPattern)textPattern).DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute) : null;
                bool? textReadOnly = readOnlyAttribute is bool readOnly ? readOnly : null;
                Editable = IsEditableControl(type, element.Current.IsEnabled, Password, valueReadOnly, textReadOnly);
                FailureReason = Editable ? "none" : Password ? "password" : !element.Current.IsEnabled ? "disabled" :
                    valueReadOnly == true || textReadOnly == true ? "read-only" : "unsupported-control";
                ElementId = string.Join("/", element.GetRuntimeId());
                if (rememberText && Editable)
                {
                    if (hasTextPattern)
                    {
                        if (!TryReadCaret(element, out _, out _, out string text, out int offset))
                        { Editable = false; FailureReason = "caret-or-selection-unavailable"; return; }
                        originalText = text;
                        insertionOffset = offset;
                        HasCaretSnapshot = true;
                    }
                    else if (valueReadOnly == false && ((ValuePattern)value).Current.Value == "")
                    {
                        // Some search/combo editors expose ValuePattern but no caret API.
                        // Only an initially empty field has a provable insertion position.
                        // Later batches must match our entire expected value; no replacement
                        // of previous text is permitted without a real selection API.
                        originalText = "";
                        insertionOffset = 0;
                        HasCaretSnapshot = false;
                    }
                    else { Editable = false; FailureReason = "no-safe-insertion-position"; }
                }
            }
            catch { Editable = false; FailureReason = "accessibility-query-failed"; }
        }

        internal static bool IsEditableControl(ControlType type, bool enabled, bool password,
            bool? valueReadOnly, bool? textReadOnly)
        {
            if (!enabled || password || valueReadOnly == true || textReadOnly == true) return false;
            if (type == ControlType.Document) return textReadOnly == false;
            if (type == ControlType.ComboBox) return valueReadOnly == false || textReadOnly == false;
            return type == ControlType.Edit;
        }

        internal void ReportDiagnostic(string stage)
        {
            // Only fixed status codes and UIA type/capability flags. Never include
            // spoken text, field value/name, title, URL, process path or exception text.
            string diagnostic = $"stage={stage}; reason={FailureReason}; type={controlTypeId}; value={hasValuePattern}; text={hasTextPattern}; caret={HasCaretSnapshot}";
            if (diagnostic == lastDiagnostic) return;
            lastDiagnostic = diagnostic;
            DebugHelper.WriteLine("Voice typing target: " + diagnostic);
        }

        internal bool NativeMatches()
        {
            VoiceTypingTarget current = CaptureNative();
            bool matches = Window != IntPtr.Zero && Focus != IntPtr.Zero && Window == current.Window && Focus == current.Focus;
            if (!matches) FailureReason = "native-focus-changed";
            return matches;
        }

        internal bool ElementMatches()
        {
            if (!Editable || Password || string.IsNullOrEmpty(ElementId)) return false;
            VoiceTypingTarget current = CaptureNative();
            current.InspectElement(false);
            if (!current.Editable || current.Password) { FailureReason = current.FailureReason; return false; }
            if (ElementId != current.ElementId) { FailureReason = "focused-element-changed"; return false; }
            return NativeMatches();
        }

        private static bool TryReadCaret(AutomationElement element, out TextPattern pattern,
            out TextPatternRange caret, out string text, out int offset)
        {
            pattern = null; caret = null; text = null; offset = 0;
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out object value)) return false;
            pattern = (TextPattern)value;
            TextPatternRange[] selection = pattern.GetSelection();
            if (selection.Length != 1 || selection[0].CompareEndpoints(TextPatternRangeEndpoint.Start,
                selection[0], TextPatternRangeEndpoint.End) != 0) return false;
            caret = selection[0];
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object fieldValue) &&
                !((ValuePattern)fieldValue).Current.IsReadOnly && ((ValuePattern)fieldValue).Current.Value == "")
            {
                // Chromium can expose an empty editor's accessible name/placeholder
                // through DocumentRange.GetText. The actual empty value has one valid
                // insertion position; do not preserve that label as user-written text.
                text = "";
                offset = 0;
                return true;
            }
            TextPatternRange document = pattern.DocumentRange;
            text = document.GetText(65537);
            if (text.Length > 65536) return false;
            TextPatternRange prefix = document.Clone();
            prefix.MoveEndpointByRange(TextPatternRangeEndpoint.End, caret, TextPatternRangeEndpoint.Start);
            string before = prefix.GetText(65537);
            offset = before.Length;
            return offset <= text.Length && text.StartsWith(before, StringComparison.Ordinal);
        }

        private string ExpectedText(string sent) => originalText == null ? null :
            originalText.Substring(0, insertionOffset) + sent + originalText.Substring(insertionOffset);

        internal bool OutputMatches(string sent)
        {
            if (!ElementMatches() || originalText == null) return false;
            try
            {
                AutomationElement element = AutomationElement.FocusedElement;
                bool matches;
                if (HasCaretSnapshot)
                    matches = TryReadCaret(element, out _, out _, out string text, out int offset) &&
                        text == ExpectedText(sent) && offset == insertionOffset + sent.Length;
                else
                    matches = element.TryGetCurrentPattern(ValuePattern.Pattern, out object value) &&
                        !((ValuePattern)value).Current.IsReadOnly && ((ValuePattern)value).Current.Value == ExpectedText(sent);
                if (!matches) FailureReason = "output-or-caret-changed";
                return matches;
            }
            catch { FailureReason = "output-query-failed"; return false; }
        }

        internal Replacement PrepareReplacement(string sent)
        {
            if (string.IsNullOrEmpty(sent) || !ElementMatches() || originalText == null || !HasCaretSnapshot)
            { FailureReason = "replacement-unavailable"; return null; }
            AutomationElement element = AutomationElement.FocusedElement;
            if (!TryReadCaret(element, out TextPattern pattern, out TextPatternRange caret,
                out string text, out int offset) || text != ExpectedText(sent) ||
                offset != insertionOffset + sent.Length)
            { FailureReason = "replacement-output-mismatch"; return null; }
            // Search only up to the caret. This identifies our own occurrence even when
            // the user's earlier/later text contains the same phrase, without counting
            // UIA characters as UTF-16 units or synthesizing Backspace/Select All.
            TextPatternRange search = pattern.DocumentRange.Clone();
            search.MoveEndpointByRange(TextPatternRangeEndpoint.End, caret, TextPatternRangeEndpoint.Start);
            TextPatternRange range = search.FindText(sent, true, false);
            if (range == null || range.GetText(-1) != sent || range.CompareEndpoints(
                TextPatternRangeEndpoint.End, caret, TextPatternRangeEndpoint.Start) != 0)
            {
                // Chromium may fail FindText on an otherwise valid editor range.
                // Character units can differ from UTF-16 units, so this is only a
                // candidate: exact text, end position and original prefix must all
                // agree before it can ever be selected.
                range = caret.Clone();
                range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -sent.Length);
                if (range.GetText(-1) != sent || range.CompareEndpoints(TextPatternRangeEndpoint.End,
                    caret, TextPatternRangeEndpoint.Start) != 0)
                { FailureReason = "replacement-range-not-found"; return null; }
            }
            TextPatternRange before = pattern.DocumentRange.Clone();
            before.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.Start);
            if (before.GetText(-1) != originalText.Substring(0, insertionOffset))
            { FailureReason = "replacement-prefix-mismatch"; return null; }
            return new Replacement(this, pattern, range, text, sent);
        }

        internal sealed class Replacement
        {
            private readonly VoiceTypingTarget target;
            private readonly TextPattern pattern;
            private readonly TextPatternRange range;
            private readonly string expected;
            private readonly string sent;
            internal Replacement(VoiceTypingTarget target, TextPattern pattern, TextPatternRange range, string expected, string sent)
            { this.target = target; this.pattern = pattern; this.range = range; this.expected = expected; this.sent = sent; }

            internal bool Select(Func<bool> stillSafe)
            {
                if (!stillSafe() || !target.OutputMatches(sent) || pattern.DocumentRange.GetText(65537) != expected ||
                    !stillSafe()) return false;
                range.Select();
                // Browser selection changes are delivered asynchronously. Re-read
                // the confirmed range before inserting; never send text on a timer
                // merely because Select returned successfully.
                Stopwatch wait = Stopwatch.StartNew();
                do
                {
                    if (!stillSafe() || !target.ElementMatches() || pattern.DocumentRange.GetText(65537) != expected)
                    { target.FailureReason = "replacement-target-changed"; return false; }
                    TextPatternRange[] selection = pattern.GetSelection();
                    if (selection.Length == 1 && selection[0].CompareEndpoints(TextPatternRangeEndpoint.Start,
                        range, TextPatternRangeEndpoint.Start) == 0 && selection[0].CompareEndpoints(
                        TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.End) == 0) return true;
                    if (selection.Length == 1 && selection[0].GetText(-1) == sent)
                    {
                        // A rich editor can return equivalent positions in different
                        // accessibility nodes. Prove the selected text's location by
                        // reading both surrounding ranges instead of trusting node IDs.
                        TextPatternRange before = pattern.DocumentRange.Clone();
                        before.MoveEndpointByRange(TextPatternRangeEndpoint.End, selection[0], TextPatternRangeEndpoint.Start);
                        TextPatternRange after = pattern.DocumentRange.Clone();
                        after.MoveEndpointByRange(TextPatternRangeEndpoint.Start, selection[0], TextPatternRangeEndpoint.End);
                        if (before.GetText(-1) == target.originalText.Substring(0, target.insertionOffset) &&
                            after.GetText(-1) == target.originalText.Substring(target.insertionOffset) &&
                            stillSafe() && target.ElementMatches()) return true;
                    }
                    System.Threading.Thread.Sleep(15);
                } while (wait.ElapsedMilliseconds < 3000);
                target.FailureReason = "replacement-selection-not-confirmed";
                return false;
            }
        }

        internal static INPUT[] BuildInputs(string text, Keys[] heldModifiers = null)
        {
            // This is an append-only delta: its leading space may separate two English words.
            string normalized = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            List<INPUT> events = new List<INPUT>(normalized.Length * 2);
            Keys[] held = heldModifiers ?? Array.Empty<Keys>();
            INPUT Modifier(Keys key, bool up)
            {
                INPUT input = new INPUT { Type = InputType.InputKeyboard };
                input.Data.Keyboard.wVk = (VirtualKeyCode)key;
                input.Data.Keyboard.dwFlags = up ? KeyboardEventFlags.KEYEVENTF_KEYUP : 0;
                if (key == Keys.RControlKey || key == Keys.RMenu) input.Data.Keyboard.dwFlags |= KeyboardEventFlags.KEYEVENTF_EXTENDEDKEY;
                input.Data.Keyboard.dwExtraInfo = InputTag;
                return input;
            }
            // Ctrl/Alt must not turn Unicode packets into application shortcuts. Neutralize
            // them only for this atomic input batch, then restore keys still physically held.
            foreach (Keys k in held.OrderBy(k => VoiceTypingChord.IsAlt(k) ? 0 : 1)) events.Add(Modifier(k, true));
            foreach (char c in normalized)
            {
                if (char.IsControl(c)) continue;
                INPUT down = new INPUT { Type = InputType.InputKeyboard };
                down.Data.Keyboard.wScan = c;
                down.Data.Keyboard.dwFlags = KeyboardEventFlags.KEYEVENTF_UNICODE;
                down.Data.Keyboard.dwExtraInfo = InputTag;
                events.Add(down);
                down.Data.Keyboard.dwFlags |= KeyboardEventFlags.KEYEVENTF_KEYUP;
                events.Add(down);
            }
            foreach (Keys k in held.OrderBy(k => VoiceTypingChord.IsControl(k) ? 0 : 1)) events.Add(Modifier(k, false));
            return events.ToArray();
        }

        internal bool Insert(string text, Keys[] heldModifiers)
        {
            if (!NativeMatches()) return false;
            INPUT[] inputs = BuildInputs(text, heldModifiers);
            bool inserted = inputs.Length > 0 && NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
            if (!inserted) FailureReason = "send-input-rejected-or-partial";
            return inserted;
        }

        [StructLayout(LayoutKind.Sequential)] private struct GUIThreadInfo
        {
            public int Size, Flags;
            public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            public System.Drawing.Rectangle CaretRect;
        }
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GUIThreadInfo info);
    }
}
