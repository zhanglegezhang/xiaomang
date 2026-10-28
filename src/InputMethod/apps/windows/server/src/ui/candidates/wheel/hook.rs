//! 非激活候选窗的悬停滚轮入口：回调只做命中检查与投递，失败时放行系统事件。

use std::cell::Cell;

use windows::Win32::Foundation::{HWND, LPARAM, LRESULT, WPARAM};
use windows::Win32::UI::WindowsAndMessaging::{
    CallNextHookEx, HHOOK, IsWindowVisible, MSLLHOOKSTRUCT, PostMessageW, SetWindowsHookExW,
    UnhookWindowsHookEx, WH_MOUSE_LL, WM_MOUSEWHEEL, WindowFromPoint,
};

use super::WM_CANDIDATE_WHEEL;

thread_local! { static TARGET: Cell<isize> = const { Cell::new(0) }; }

#[derive(Default)]
pub(in crate::ui::candidates) struct WheelHook(Cell<Option<HHOOK>>);

impl WheelHook {
    pub(in crate::ui::candidates) fn show(&self, hwnd: HWND) {
        if self.0.get().is_some() {
            return;
        }
        match unsafe {
            SetWindowsHookExW(
                WH_MOUSE_LL,
                Some(callback),
                Some(crate::ui::module_handle()),
                0,
            )
        } {
            Ok(hook) => {
                TARGET.with(|target| target.set(hwnd.0 as isize));
                self.0.set(Some(hook));
            }
            Err(error) => tracing::warn!(%error, "候选窗口悬停滚轮暂不可用"),
        }
    }

    pub(in crate::ui::candidates) fn hide(&self) {
        TARGET.with(|target| target.set(0));
        if let Some(hook) = self.0.take() {
            let _ = unsafe { UnhookWindowsHookEx(hook) };
        }
    }
}

impl Drop for WheelHook {
    fn drop(&mut self) {
        self.hide();
    }
}

unsafe extern "system" fn callback(code: i32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    if code == 0 && wparam.0 == WM_MOUSEWHEEL as usize {
        let handled = std::panic::catch_unwind(|| {
            let mouse = unsafe { &*(lparam.0 as *const MSLLHOOKSTRUCT) };
            let target = TARGET.with(Cell::get);
            let hwnd = HWND(target as *mut _);
            if target != 0
                && unsafe { IsWindowVisible(hwnd) }.as_bool()
                && unsafe { WindowFromPoint(mouse.pt) } == hwnd
                && super::super::wheel_hit(hwnd, mouse.pt)
            {
                // 消费前先确认投递成功，避免窗口消失时误吞原应用的滚轮。
                return unsafe {
                    PostMessageW(
                        Some(hwnd),
                        WM_CANDIDATE_WHEEL,
                        WPARAM((mouse.mouseData >> 16) as usize),
                        LPARAM(0),
                    )
                }
                .is_ok();
            }
            super::super::reset_wheel();
            false
        })
        .unwrap_or(false);
        if handled {
            return LRESULT(1);
        }
    }
    unsafe { CallNextHookEx(None, code, wparam, lparam) }
}
