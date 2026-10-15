using ShareX.HelpersLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace ShareX
{
    // TSF 必须在主 UI 线程上操作；后台重试不应改变用户选择的输入法。
    internal sealed class InputMethodExitSwitcher : IDisposable
    {
        private readonly TsfInputProfiles profiles = new();
        private readonly System.Windows.Forms.Timer observer = new() { Interval = 750 };
        private InputProfile? previous;
        private readonly InputProfilePauseState pauseState;
        private readonly Func<bool> backendReady;
        private string lastObservationError;

        internal InputMethodExitSwitcher(Func<bool> backendReady = null, string pausePath = null)
        {
            this.backendReady = backendReady;
            if (pausePath != null) pauseState = new InputProfilePauseState(pausePath);
            observer.Tick += (_, _) => Observe();
            Observe();
            observer.Start();
        }

        private void Observe()
        {
            try
            {
                if (pauseState?.Resume(backendReady?.Invoke() == true, () => profiles.XiaomangEnabled,
                    profiles.EnableXiaomang) == true)
                    DebugHelper.WriteLine("Input profile availability restored after worker became ready.");
                InputProfile? current = profiles.Active;
                if (current.HasValue && current.Value.IsUsableAlternative) previous = current;
                lastObservationError = null;
            }
            catch (Exception error)
            {
                // 临时恢复失败仍可重试；相同错误只记录一次，避免每个观察周期刷日志。
                string signature = error.GetType().FullName + ":" + error.Message;
                if (lastObservationError != signature) DebugHelper.WriteException(error, "Input profile observation");
                lastObservationError = signature;
            }
        }

        internal bool SwitchAway()
        {
            observer.Stop();
            InputProfile? current = profiles.Active;
            if (!current.HasValue || !current.Value.IsXiaomang) return true;

            foreach (InputProfile candidate in Alternatives(profiles.List(), previous, current.Value.Language))
            {
                int result = profiles.Activate(candidate);
                if (result == 0 && profiles.Active is InputProfile active && active.SameIdentity(candidate))
                {
                    DebugHelper.WriteLine("Input method switched before shutdown: " + candidate.ClassId + "/" + candidate.ProfileId);
                    return true;
                }
                DebugHelper.WriteLine("Input method exit switch failed: 0x" + result.ToString("X8"));
            }
            DebugHelper.WriteLine("No usable input profile could be activated before input worker shutdown.");
            return false;
        }

        internal bool PauseBeforeExit()
        {
            if (!SwitchAway()) return false;
            pauseState?.Pause(() => profiles.XiaomangEnabled, profiles.EnableXiaomang);
            DebugHelper.WriteLine("Input profile paused before worker shutdown.");
            return true;
        }

        internal static IEnumerable<InputProfile> Alternatives(IEnumerable<InputProfile> available, InputProfile? previous, ushort language)
        {
            // 只使用已启用的键盘输入法，不启用隐藏的注册项，也不把语音服务当作替代输入法。
            return available.Where(p => p.IsUsableAlternative)
                .OrderBy(p => previous.HasValue && p.SameIdentity(previous.Value) ? 0 :
                    p.Language == language && p.Type == 1 ? 1 : p.Language == language ? 2 : 3);
        }

        public void Dispose()
        {
            observer.Dispose();
            profiles.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct InputProfile
    {
        internal uint Type;
        internal ushort Language;
        internal Guid ClassId;
        internal Guid ProfileId;
        internal Guid Category;
        internal IntPtr Substitute;
        internal uint Capabilities;
        internal IntPtr Layout;
        internal uint Flags;

        internal static readonly Guid KeyboardCategory = new("34745C63-B2F0-4784-8B67-5E12C8701A31");
        internal bool IsXiaomang => ClassId == new Guid(XiaomangInputMethodManager.ClassId);
        internal bool IsUsableAlternative => !IsXiaomang && (Flags & 2) != 0 &&
            (Type == 1 && Category == KeyboardCategory || Type == 2 && Layout != IntPtr.Zero && (Flags & 4) == 0);

        internal bool SameIdentity(InputProfile other) => Type == other.Type && Language == other.Language &&
            (Type == 1 ? ClassId == other.ClassId && ProfileId == other.ProfileId : Layout == other.Layout);
    }

    internal sealed class TsfInputProfiles : IDisposable
    {
        private readonly ITfInputProcessorProfileMgr manager = (ITfInputProcessorProfileMgr)Activator.CreateInstance(
            System.Type.GetTypeFromCLSID(new Guid("33C53A50-F456-4884-B049-85FD643ECFED"), true));

        internal InputProfile? Active
        {
            get
            {
                Guid category = InputProfile.KeyboardCategory;
                int result = manager.GetActiveProfile(ref category, out InputProfile profile);
                if (result < 0) Marshal.ThrowExceptionForHR(result);
                return result == 0 ? profile : null;
            }
        }

        internal List<InputProfile> List()
        {
            Marshal.ThrowExceptionForHR(manager.EnumProfiles(0, out IEnumTfInputProcessorProfiles enumerator));
            try
            {
                var result = new List<InputProfile>();
                while (true)
                {
                    int status = enumerator.Next(1, out InputProfile profile, out uint fetched);
                    Marshal.ThrowExceptionForHR(status);
                    if (fetched == 0) return result;
                    result.Add(profile);
                }
            }
            finally { Marshal.ReleaseComObject(enumerator); }
        }

        internal int Activate(InputProfile profile)
        {
            // FORSESSION 更新当前桌面的选择；不使用 ENABLE/DISABLE，不改用户已安装的键盘配置。
            return manager.ActivateProfile(profile.Type, profile.Language, ref profile.ClassId,
                ref profile.ProfileId, profile.Layout, 0x20000000);
        }

        internal int ActivateFromShortcut(InputProfile profile)
        {
            int result = Activate(profile);
            if (result == 0 || result == 1) return result;
            var languages = (ITfInputProcessorProfiles)manager;
            int read = languages.GetCurrentLanguage(out ushort previousLanguage);
            if (read != 0 || previousLanguage == profile.Language) return result;
            InputProfile? previousProfile = Active;
            int change = languages.ChangeCurrentLanguage(profile.Language);
            if (change != 0) return change;
            result = Activate(profile);
            if (result != 0)
            {
                // Restore the previous language/profile on a failed cross-language switch.
                int restored = languages.ChangeCurrentLanguage(previousLanguage);
                if (restored == 0 && previousProfile.HasValue) restored = Activate(previousProfile.Value);
                if (restored != 0) DebugHelper.WriteLine("Input shortcut rollback failed: 0x" + restored.ToString("X8"));
            }
            return result;
        }

        internal InputProfile Get(Guid clsid, Guid profile, ushort language)
        {
            Marshal.ThrowExceptionForHR(manager.GetProfile(1, language, ref clsid, ref profile, IntPtr.Zero, out InputProfile info));
            return info;
        }

        internal int Deactivate(InputProfile profile) => manager.DeactivateProfile(profile.Type, profile.Language,
            ref profile.ClassId, ref profile.ProfileId, profile.Layout, 0x20000000);

        internal InputProfile Xiaomang
        {
            get
            {
                Guid clsid = new(XiaomangInputMethodManager.ClassId);
                Guid profile = new("81C29015-D151-48C4-9643-EA1BA891612E");
                Marshal.ThrowExceptionForHR(manager.GetProfile(1, 0x804, ref clsid, ref profile, IntPtr.Zero, out InputProfile info));
                return info;
            }
        }

        internal bool XiaomangEnabled
        {
            get
            {
                InputProfile profile = Xiaomang;
                return UserKeyboardProfiles().Any(p => p.ClassId == profile.ClassId && p.ProfileId == profile.ProfileId && (p.Flags & 2) == 0);
            }
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("input.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool InstallLayoutOrTip(string profile, uint flags);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("input.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint EnumEnabledLayoutOrTip(IntPtr user, IntPtr system, IntPtr software, IntPtr profiles, uint count);

        internal static List<UserKeyboardProfile> UserKeyboardProfiles()
        {
            uint count = EnumEnabledLayoutOrTip(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0);
            if (count > 4096) throw new InvalidOperationException("键盘输入法列表异常。");
            var result = new List<UserKeyboardProfile>();
            if (count == 0) return result;
            int size = Marshal.SizeOf<UserKeyboardProfile>();
            IntPtr memory = Marshal.AllocHGlobal(checked(size * (int)count));
            try
            {
                uint copied = EnumEnabledLayoutOrTip(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, memory, count);
                if (copied > count) throw new InvalidOperationException("键盘输入法列表发生异常变化。");
                for (int i = 0; i < copied; i++) result.Add(Marshal.PtrToStructure<UserKeyboardProfile>(IntPtr.Add(memory, i * size)));
            }
            finally { Marshal.FreeHGlobal(memory); }
            return result;
        }

        internal void EnableXiaomang(bool enabled)
        {
            InputProfile profile = Xiaomang;
            string identifier = "0x" + profile.Language.ToString("X4") + ":" + profile.ClassId.ToString("B") + profile.ProfileId.ToString("B");
            // 更新 Windows 的用户键盘列表，不能只改变旧 TSF API 的 Enabled 值。
            // 0x80 仅禁用指定条目；不用 CLEANINSTALL / DEFPROFILE，不清理其他输入法或改默认项。
            if (!InstallLayoutOrTip(identifier, enabled ? 0u : 0x80u))
                throw new InvalidOperationException("Windows 未能更新小蟒输入法的可选状态。");
            Marshal.ThrowExceptionForHR(((ITfInputProcessorProfiles)manager).EnableLanguageProfile(
                ref profile.ClassId, profile.Language, ref profile.ProfileId, enabled ? 1 : 0));
            if (!enabled)
                Marshal.ThrowExceptionForHR(manager.DeactivateProfile(profile.Type, profile.Language,
                    ref profile.ClassId, ref profile.ProfileId, profile.Layout, 0x20000002));
            if (XiaomangEnabled != enabled) throw new InvalidOperationException("输入法启用状态未按预期更新。");
        }

        public void Dispose() => Marshal.ReleaseComObject(manager);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct UserKeyboardProfile
    {
        internal uint Type;
        internal ushort Language;
        internal Guid ClassId;
        internal Guid ProfileId;
        internal Guid Category;
        internal uint SubstituteLayout;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Identifier;
    }

    [ComImport, Guid("71C6E74C-0F28-11D8-A82A-00065B84435C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITfInputProcessorProfileMgr
    {
        [PreserveSig] int ActivateProfile(uint type, ushort language, ref Guid clsid, ref Guid profile, IntPtr layout, uint flags);
        [PreserveSig] int DeactivateProfile(uint type, ushort language, ref Guid clsid, ref Guid profile, IntPtr layout, uint flags);
        [PreserveSig] int GetProfile(uint type, ushort language, ref Guid clsid, ref Guid profile, IntPtr layout, out InputProfile info);
        [PreserveSig] int EnumProfiles(ushort language, out IEnumTfInputProcessorProfiles enumerator);
        [PreserveSig] int ReleaseInputProcessor(ref Guid clsid, uint flags);
        [PreserveSig] int RegisterProfile(ref Guid clsid, ushort language, ref Guid profile, IntPtr description, uint descriptionLength,
            IntPtr iconFile, uint iconLength, uint iconIndex, IntPtr substitute, uint preferredLayout, int enabled, uint flags);
        [PreserveSig] int UnregisterProfile(ref Guid clsid, ushort language, ref Guid profile, uint flags);
        [PreserveSig] int GetActiveProfile(ref Guid category, out InputProfile profile);
    }

    [ComImport, Guid("71C6E74D-0F28-11D8-A82A-00065B84435C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumTfInputProcessorProfiles
    {
        [PreserveSig] int Clone(out IEnumTfInputProcessorProfiles enumerator);
        [PreserveSig] int Next(uint count, out InputProfile profile, out uint fetched);
        [PreserveSig] int Reset();
        [PreserveSig] int Skip(uint count);
    }

    [ComImport, Guid("1F02B6C5-7842-4EE6-8A0B-9A24183A95CA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ITfInputProcessorProfiles
    {
        [PreserveSig] int Register(ref Guid clsid);
        [PreserveSig] int Unregister(ref Guid clsid);
        [PreserveSig] int AddLanguageProfile(ref Guid clsid, ushort language, ref Guid profile, IntPtr description,
            uint descriptionLength, IntPtr icon, uint iconLength, uint iconIndex);
        [PreserveSig] int RemoveLanguageProfile(ref Guid clsid, ushort language, ref Guid profile);
        [PreserveSig] int EnumInputProcessorInfo(out IntPtr enumerator);
        [PreserveSig] int GetDefaultLanguageProfile(ushort language, ref Guid category, out Guid clsid, out Guid profile);
        [PreserveSig] int SetDefaultLanguageProfile(ushort language, ref Guid clsid, ref Guid profile);
        [PreserveSig] int ActivateLanguageProfile(ref Guid clsid, ushort language, ref Guid profile);
        [PreserveSig] int GetActiveLanguageProfile(ref Guid clsid, out ushort language, out Guid profile);
        [PreserveSig] int GetLanguageProfileDescription(ref Guid clsid, ushort language, ref Guid profile, out IntPtr description);
        [PreserveSig] int GetCurrentLanguage(out ushort language);
        [PreserveSig] int ChangeCurrentLanguage(ushort language);
        [PreserveSig] int GetLanguageList(out IntPtr languages, out uint count);
        [PreserveSig] int EnumLanguageProfiles(ushort language, out IntPtr enumerator);
        [PreserveSig] int EnableLanguageProfile(ref Guid clsid, ushort language, ref Guid profile, int enabled);
        [PreserveSig] int IsEnabledLanguageProfile(ref Guid clsid, ushort language, ref Guid profile, out int enabled);
        [PreserveSig] int EnableLanguageProfileByDefault(ref Guid clsid, ushort language, ref Guid profile, int enabled);
        [PreserveSig] int SubstituteKeyboardLayout(ref Guid clsid, ushort language, ref Guid profile, IntPtr layout);
    }
}
