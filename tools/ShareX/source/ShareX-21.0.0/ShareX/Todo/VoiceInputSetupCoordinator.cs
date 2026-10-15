using System;
using System.Threading.Tasks;

namespace ShareX
{
    internal enum VoiceInputRegistrationState { Unregistered, Registered, NeedsRepair, Unsupported, NeedsUpdate, BundleIncomplete }
    internal readonly record struct VoiceInputSetupResult(bool Ready, bool Attempted, string Message, bool Cancelled = false);

    // Shared by the settings button and first-use shortcut; a cancelled/failed automatic
    // attempt stays suppressed until the process exits. Manual retry is always available.
    internal sealed class VoiceInputSetupCoordinator
    {
        internal static VoiceInputSetupCoordinator Default { get; } = new(
            () => VoiceInputComponent.RegistrationState,
            (install, repair) => VoiceInputComponent.InstallAsync(install, repair),
            () =>
            {
                if (VoiceTypingController.IsTyping) throw new InvalidOperationException("请先结束当前语音输入，再维护组件。");
                VoiceInputOutput.CloseBroker();
            });
        private readonly Func<VoiceInputRegistrationState> inspect;
        private readonly Func<bool, bool, Task> changeRegistration;
        private readonly Action prepare;
        private Task<VoiceInputSetupResult> operation;
        private bool automaticAttempted;
        internal event Action Changed;
        internal VoiceInputRegistrationState State => inspect();
        internal bool IsBusy => operation != null && !operation.IsCompleted;
        internal string Message { get; private set; } = "";

        internal VoiceInputSetupCoordinator(Func<VoiceInputRegistrationState> inspect, Func<Task> install)
            : this(inspect, (register, repair) => install(), () => { }) { }

        internal VoiceInputSetupCoordinator(Func<VoiceInputRegistrationState> inspect,
            Func<bool, bool, Task> changeRegistration, Action prepare)
        { this.inspect = inspect; this.changeRegistration = changeRegistration; this.prepare = prepare; }

        internal Task<VoiceInputSetupResult> EnsureRegisteredAsync(bool automatic)
            => BeginOperation(true, false, automatic);

        internal Task<VoiceInputSetupResult> RepairAsync() => BeginOperation(true, true, false);
        internal Task<VoiceInputSetupResult> UninstallAsync() => BeginOperation(false, false, false);

        private Task<VoiceInputSetupResult> BeginOperation(bool install, bool forceRepair, bool automatic)
        {
            if (IsBusy) return operation;
            var state = State;
            if (install && !forceRepair && state == VoiceInputRegistrationState.Registered)
                return Task.FromResult(new VoiceInputSetupResult(true, false, "已注册，无需重复注册。"));
            if (!install && state == VoiceInputRegistrationState.Unregistered)
                return Task.FromResult(new VoiceInputSetupResult(false, false, "尚未注册，无需卸载。"));
            if (state == VoiceInputRegistrationState.Unsupported)
                return Task.FromResult(new VoiceInputSetupResult(false, false, "当前系统架构不支持此语音输入组件。"));
            if (state == VoiceInputRegistrationState.BundleIncomplete)
                return Task.FromResult(new VoiceInputSetupResult(false, false, "随包语音组件缺失或无法读取，请重新获取完整使用包。"));
            if (automatic && automaticAttempted)
                return Task.FromResult(new VoiceInputSetupResult(false, false, "尚未完成注册。可到输入法 → 小蟒语音输入手动注册。"));
            automaticAttempted = true;
            // Yield before invoking the installer so the shared task is published first.
            Task<VoiceInputSetupResult> pending = PerformSetupAsync(install, forceRepair);
            operation = pending;
            Changed?.Invoke();
            return pending;
        }

        private async Task<VoiceInputSetupResult> PerformSetupAsync(bool install, bool forceRepair)
        {
            await Task.Yield();
            VoiceInputSetupResult result;
            try
            {
                prepare();
                await changeRegistration(install, forceRepair);
                var state = State;
                bool ready = state == VoiceInputRegistrationState.Registered;
                bool absent = state == VoiceInputRegistrationState.Unregistered;
                result = new(ready, true, install ? ready ? "已注册。请回到输入框，再按住 Ctrl + Alt 开始说话，无需手动切换输入法。" :
                    "安装器已结束，但未检测到完整注册或组件与当前包不一致。请在此页点击修复注册。" :
                    absent ? "语音组件已卸载；模型和个人数据保留。再次使用请先注册组件。" :
                    "卸载程序已结束，但仍检测到注册残留，请查看安装日志后重试。");
            }
            catch (OperationCanceledException)
            { result = new(State == VoiceInputRegistrationState.Registered, true, "已取消管理员授权，当前注册状态保留。本次运行不再自动提示，可在此页重试。", true); }
            catch (Exception error)
            { result = new(false, true, (install ? "注册失败：" : "卸载失败：") + error.Message + " 本次运行不再自动提示，可手动重试。"); }
            Message = result.Message;
            // The continuation that refreshes controls must see the operation as complete.
            operation = null;
            Changed?.Invoke();
            return result;
        }
    }
}
