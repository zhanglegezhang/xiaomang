//! 单击中 / 英切换键的判定，喂的是击键 sink 的 `OnTestKeyDown` / `OnTestKeyUp`（被吃掉的键也经过它们，
//! 与 `WH_KEYBOARD` 钩子不同）。按下切换键到抬起之间没插进别的键，就是一次单击。
//!
//! 切换键来自 `[shortcut] switch_mode`，单击 Shift / 单击 Ctrl 可以都勾；Ctrl + Alt + Space 是组合键，走保留键。
//! 系统热键（如 Ctrl + Space）的第二个键被系统截走、到不了这里，看起来就像单击了 Ctrl：
//! 系统热键生效时调 [`KeyTap::cancel`] 作废这次按下。

use std::cell::Cell;

use windows::Win32::Foundation::LPARAM;
use windows::Win32::UI::Input::KeyboardAndMouse::{
    VK_CONTROL, VK_LCONTROL, VK_LMENU, VK_LSHIFT, VK_LWIN, VK_MENU, VK_RCONTROL, VK_RMENU,
    VK_RSHIFT, VK_RWIN, VK_SHIFT,
};

use qingjian_platform::{SwitchKey, SwitchKeys};

#[derive(Default)]
pub(crate) struct KeyTap {
    /// 按下了哪个切换键、之后还没有别的键插进来。
    pressed: Cell<Option<SwitchKey>>,

    /// 系统热键会截走字母，仍需记住未勾选为切换键的修饰键。
    held_modifiers: Cell<u8>,
}

impl KeyTap {
    /// 焦点边界不能继承旧窗口的按键序列（松开事件可能交给了别的窗口）。
    pub(crate) fn reset(&self) {
        self.cancel();
        self.held_modifiers.set(0);
    }

    /// 使用当前消息队列的键盘状态纠正漏收的修饰键事件。
    /// 不用异步状态，避免快速输入时把尚未处理的松开误认为已经发生。
    pub(crate) fn reconcile_modifiers(&self, ctrl: bool, shift: bool, alt: bool, win: bool) {
        let mask =
            u8::from(ctrl) | (u8::from(shift) << 1) | (u8::from(alt) << 2) | (u8::from(win) << 3);
        self.held_modifiers.set(mask);
        if let Some(key) = self.pressed.get() {
            let bit = match key {
                SwitchKey::Shift => 2,
                SwitchKey::Control => 1,
                _ => 0,
            };
            if mask & bit == 0 {
                self.cancel();
            }
        }
    }

    /// 任一键按下。`lparam` 第 30 位是按下前的状态（1 = 自动重复，不算新按下）。
    pub(crate) fn key_down(&self, vk: u32, lparam: LPARAM, keys: SwitchKeys) {
        let modifier = modifier_bit(vk);
        let held = self.held_modifiers.get();
        self.held_modifiers.set(held | modifier);
        if modifier != 0 && held & !modifier != 0 {
            self.cancel();
            return;
        }
        let Some(key) = tap_key(keys, vk) else {
            self.cancel();
            return;
        };
        if (lparam.0 >> 30) & 1 != 0 {
            return;
        }
        // 两个切换键一起按（Ctrl + Shift 是系统换布局的键）不算单击
        let pressed = match self.pressed.get() {
            Some(other) if other != key => None,
            _ => Some(key),
        };
        self.pressed.set(pressed);
    }

    /// 任一键抬起；切换键单独抬起返回 `true`，一次抬起只算一次。
    pub(crate) fn key_up(&self, vk: u32, keys: SwitchKeys) -> bool {
        self.held_modifiers
            .set(self.held_modifiers.get() & !modifier_bit(vk));
        let Some(key) = tap_key(keys, vk) else {
            return false;
        };
        if self.pressed.get() == Some(key) {
            self.pressed.set(None);
            true
        } else {
            false
        }
    }

    /// 作废正按着的切换键（按下之后发生了别的事，这次抬起不算单击）。
    pub(crate) fn cancel(&self) {
        self.pressed.set(None);
    }
}

fn modifier_bit(vk: u32) -> u8 {
    let is = |codes: &[u16]| codes.iter().any(|code| u32::from(*code) == vk);
    if is(&[VK_CONTROL.0, VK_LCONTROL.0, VK_RCONTROL.0]) {
        1
    } else if is(&[VK_SHIFT.0, VK_LSHIFT.0, VK_RSHIFT.0]) {
        2
    } else if is(&[VK_MENU.0, VK_LMENU.0, VK_RMENU.0]) {
        4
    } else if is(&[VK_LWIN.0, VK_RWIN.0]) {
        8
    } else {
        0
    }
}

/// 这个虚拟键码是不是勾着的单击切换键（左右两个都算）；组合键 Ctrl + Alt + Space 不走单击判定。
fn tap_key(keys: SwitchKeys, vk: u32) -> Option<SwitchKey> {
    let is = |codes: [u16; 3]| codes.iter().any(|code| u32::from(*code) == vk);
    if keys.shift && is([VK_SHIFT.0, VK_LSHIFT.0, VK_RSHIFT.0]) {
        Some(SwitchKey::Shift)
    } else if keys.control && is([VK_CONTROL.0, VK_LCONTROL.0, VK_RCONTROL.0]) {
        Some(SwitchKey::Control)
    } else {
        None
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// 按下再抬起的 lparam（第 30 位为 0 表示新按下）。
    const DOWN: LPARAM = LPARAM(0);
    const VK_SHIFT_LEFT: u32 = 0xA0;
    const VK_CONTROL_LEFT: u32 = 0xA2;

    fn only(key: SwitchKey) -> SwitchKeys {
        SwitchKeys::NONE.with(key, true)
    }

    #[test]
    fn missed_modifier_release_does_not_block_next_shift() {
        for vk in [VK_CONTROL_LEFT, 0xA4, 0x5B] {
            let tap = KeyTap::default();
            let keys = only(SwitchKey::Shift);
            tap.key_down(vk, DOWN, keys);
            // 原窗口没收到 Ctrl / Alt / Win 的抬起，下一次仅 Shift 实际按下。
            tap.reconcile_modifiers(false, true, false, false);
            tap.key_down(VK_SHIFT_LEFT, DOWN, keys);
            assert!(tap.key_up(VK_SHIFT_LEFT, keys));
        }
    }

    #[test]
    fn focus_reset_cancels_old_tap_and_clears_stale_modifiers() {
        let tap = KeyTap::default();
        let keys = only(SwitchKey::Shift);
        tap.key_down(VK_SHIFT_LEFT, DOWN, keys);
        tap.reset();
        assert!(!tap.key_up(VK_SHIFT_LEFT, keys));
        tap.key_down(VK_CONTROL_LEFT, DOWN, keys);
        tap.reset();
        tap.key_down(VK_SHIFT_LEFT, DOWN, keys);
        assert!(tap.key_up(VK_SHIFT_LEFT, keys));
    }

    #[test]
    fn actual_modifier_held_before_focus_still_blocks_shift_tap() {
        for (ctrl, alt, win) in [
            (true, false, false),
            (false, true, false),
            (false, false, true),
        ] {
            let tap = KeyTap::default();
            let keys = only(SwitchKey::Shift);
            tap.reset();
            tap.reconcile_modifiers(ctrl, true, alt, win);
            tap.key_down(VK_SHIFT_LEFT, DOWN, keys);
            assert!(!tap.key_up(VK_SHIFT_LEFT, keys));
        }
    }

    #[test]
    fn repeated_test_and_real_callback_only_toggle_once() {
        let tap = KeyTap::default();
        let keys = only(SwitchKey::Shift);
        for _ in 0..2 {
            tap.reconcile_modifiers(false, true, false, false);
            tap.key_down(VK_SHIFT_LEFT, DOWN, keys);
        }
        assert!(tap.key_up(VK_SHIFT_LEFT, keys));
        assert!(!tap.key_up(VK_SHIFT_LEFT, keys));
    }

    #[test]
    fn shift_tap_fires_only_when_nothing_else_interrupts() {
        let tap = KeyTap::default();
        let keys = only(SwitchKey::Shift);
        tap.key_down(VK_SHIFT_LEFT, DOWN, keys);
        assert!(tap.key_up(VK_SHIFT_LEFT, keys));
        // 一次抬起只算一次
        assert!(!tap.key_up(VK_SHIFT_LEFT, keys));

        tap.key_down(VK_SHIFT_LEFT, DOWN, keys);
        tap.key_down(0x41, DOWN, keys); // 中间插了一个 A
        assert!(!tap.key_up(VK_SHIFT_LEFT, keys));
    }

    #[test]
    fn only_checked_keys_fire() {
        let tap = KeyTap::default();
        let keys = only(SwitchKey::Control);
        tap.key_down(VK_SHIFT_LEFT, DOWN, keys);
        assert!(!tap.key_up(VK_SHIFT_LEFT, keys));
        tap.key_down(VK_CONTROL_LEFT, DOWN, keys);
        assert!(tap.key_up(VK_CONTROL_LEFT, keys));

        // 一个都不勾、只勾组合键：修饰键单击都不算
        for keys in [SwitchKeys::NONE, only(SwitchKey::CtrlAltSpace)] {
            tap.key_down(VK_SHIFT_LEFT, DOWN, keys);
            assert!(!tap.key_up(VK_SHIFT_LEFT, keys));
            tap.key_down(VK_CONTROL_LEFT, DOWN, keys);
            assert!(!tap.key_up(VK_CONTROL_LEFT, keys));
        }
    }

    #[test]
    fn both_taps_work_when_both_are_checked_but_not_together() {
        let tap = KeyTap::default();
        let keys = only(SwitchKey::Shift).with(SwitchKey::Control, true);
        tap.key_down(VK_SHIFT_LEFT, DOWN, keys);
        assert!(tap.key_up(VK_SHIFT_LEFT, keys));
        tap.key_down(VK_CONTROL_LEFT, DOWN, keys);
        assert!(tap.key_up(VK_CONTROL_LEFT, keys));

        // Ctrl + Shift 一起按：谁抬起都不算
        tap.key_down(VK_CONTROL_LEFT, DOWN, keys);
        tap.key_down(VK_SHIFT_LEFT, DOWN, keys);
        assert!(!tap.key_up(VK_SHIFT_LEFT, keys));
        assert!(!tap.key_up(VK_CONTROL_LEFT, keys));
    }

    #[test]
    fn a_system_hotkey_cancels_the_pending_tap() {
        let tap = KeyTap::default();
        let keys = only(SwitchKey::Control);
        // Ctrl + Space 的 Space 被系统截走，这里只看到 Ctrl 按下又抬起
        tap.key_down(VK_CONTROL_LEFT, DOWN, keys);
        tap.cancel();
        assert!(!tap.key_up(VK_CONTROL_LEFT, keys));
    }

    #[test]
    fn auto_repeat_does_not_rearm() {
        let tap = KeyTap::default();
        let keys = only(SwitchKey::Shift);
        // 第 30 位为 1：自动重复，不算新按下
        let repeat = LPARAM(1 << 30);
        tap.key_down(VK_SHIFT_LEFT, DOWN, keys);
        assert!(tap.key_up(VK_SHIFT_LEFT, keys));
        tap.key_down(VK_SHIFT_LEFT, repeat, keys);
        assert!(!tap.key_up(VK_SHIFT_LEFT, keys));
    }

    #[test]
    fn swallowed_global_hotkey_does_not_become_a_modifier_tap() {
        for keys in [
            only(SwitchKey::Shift),
            only(SwitchKey::Control),
            only(SwitchKey::Shift).with(SwitchKey::Control, true),
        ] {
            for order in [
                [VK_CONTROL_LEFT, VK_SHIFT_LEFT],
                [VK_SHIFT_LEFT, VK_CONTROL_LEFT],
            ] {
                for releases in [order, [order[1], order[0]]] {
                    let tap = KeyTap::default();
                    tap.key_down(order[0], DOWN, keys);
                    tap.key_down(order[1], DOWN, keys);
                    // J 被 RegisterHotKey 截走，TSF 看不到它。
                    assert!(!tap.key_up(releases[0], keys));
                    assert!(!tap.key_up(releases[1], keys));
                    tap.key_down(VK_SHIFT_LEFT, DOWN, only(SwitchKey::Shift));
                    assert!(tap.key_up(VK_SHIFT_LEFT, only(SwitchKey::Shift)));
                }
            }
        }
    }
}
