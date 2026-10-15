using System;

namespace ShareX
{
    // Policy is separate from hotkey registration so failures never become a toggle
    // or an installation/registration operation.
    internal sealed class InputMethodSwitchAction
    {
        private readonly Func<InputProfile?> active;
        private readonly Func<InputProfile> target;
        private readonly Func<bool> registered, ready;
        private readonly Func<InputProfile, int> activate;

        internal InputMethodSwitchAction(Func<InputProfile?> active, Func<InputProfile> target,
            Func<bool> registered, Func<bool> ready, Func<InputProfile, int> activate)
        {
            this.active = active; this.target = target; this.registered = registered;
            this.ready = ready; this.activate = activate;
        }

        // null means success (including an intentional no-op).
        internal string Execute()
        {
            if (!registered()) return "小蟒文本输入法尚未注册，请在「文本输入」页注册后重试。";
            InputProfile destination = target();
            if ((destination.Flags & 2) == 0) return "小蟒输入法尚未添加到 Windows 键盘列表，请先添加后重试。";
            if (!ready()) return "小蟒输入法后台尚未就绪，请稍后重试；仍失败可在「文本输入」页重试后台。";
            if (active() is InputProfile current && current.SameIdentity(destination)) return null;
            int result = activate(destination);
            if (result != 0) return "Windows 未能切换到小蟒输入法（0x" + result.ToString("X8") + "），请检查当前输入法后重试。";
            if (active() is not InputProfile selected || !selected.SameIdentity(destination))
                return "Windows 尚未确认小蟒输入法已激活，请稍后重试。";
            return null;
        }
    }
}
