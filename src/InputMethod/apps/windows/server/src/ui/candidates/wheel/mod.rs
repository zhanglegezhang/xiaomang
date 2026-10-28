//! 候选窗可见时才接收悬停滚轮；不激活窗口，不拦截框外滚动。

mod delta;
mod hook;

pub(super) use delta::WheelDelta;
pub(super) use hook::WheelHook;

pub(super) const WM_CANDIDATE_WHEEL: u32 = windows::Win32::UI::WindowsAndMessaging::WM_APP + 2;
