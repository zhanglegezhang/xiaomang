//! 标点 / 数字按当前键盘布局解析，测键时不改变 dead key 状态。

use windows::Win32::UI::Input::KeyboardAndMouse::{
    GetKeyboardLayout, GetKeyboardState, HKL, MAPVK_VK_TO_VSC, MapVirtualKeyExW, ToUnicodeEx,
};

pub(super) fn character(vk: u32) -> Option<char> {
    // 主键盘数字、小键盘数字与运算符（不解出字符的键 would_eat 会直接放行给应用）、OEM 标点和空格。
    if !matches!(vk, 0x30..=0x39 | 0x60..=0x6F | 0xBA..=0xC0 | 0xDB..=0xDE | 0x20) {
        return None;
    }
    let mut state = [0; 256];
    unsafe { GetKeyboardState(&mut state) }.ok()?;
    resolve(vk, &state, unsafe { GetKeyboardLayout(0) })
}

fn resolve(vk: u32, state: &[u8; 256], layout: HKL) -> Option<char> {
    let scan = unsafe { MapVirtualKeyExW(vk, MAPVK_VK_TO_VSC, Some(layout)) };
    let mut buffer = [0; 8];
    // Windows 10 1607 起，bit 2 可避免测键修改 dead key 状态。
    let count = unsafe { ToUnicodeEx(vk, scan, state, &mut buffer, 1 << 2, Some(layout)) };
    // 仅接受单个 UTF-16 单元；dead key、无映射和较长结果不生成字符。
    if count != 1 {
        return None;
    }
    char::from_u32(u32::from(buffer[0]))
}

#[cfg(test)]
mod tests {
    use super::*;
    use windows::Win32::UI::Input::KeyboardAndMouse::{
        GetKeyboardLayoutList, VK_NUMLOCK, VK_SHIFT,
    };

    // 不安装布局：LoadKeyboardLayoutW 会污染 Windows 会话，即使未激活或用了 NOTELLSHELL。
    // 这些 OS 集成测试只在预装测试布局的隔离 Windows 环境中手动运行。
    fn preinstalled_layout(klid: &str) -> HKL {
        assert_eq!(
            std::env::var("XIAOMANG_ISOLATED_LAYOUT_TESTS").as_deref(),
            Ok("1"),
            "run only on a disposable Windows desktop with preinstalled test layouts"
        );
        let count = unsafe { GetKeyboardLayoutList(None) };
        assert!(count > 0);
        let key = windows_registry::LOCAL_MACHINE
            .open(format!(
                "SYSTEM\\CurrentControlSet\\Control\\Keyboard Layouts\\{klid}"
            ))
            .expect("test layout not installed in the isolated desktop");
        let language = u32::from_str_radix(&klid[4..], 16).unwrap();
        // 变体的 HKL 高位来自机器上的 Layout Id，不能把 KLID 的高位当作它。
        let high = key.get_string("Layout Id").map_or(language, |value| {
            0xF000 | u32::from_str_radix(&value, 16).unwrap()
        });
        let id = (high << 16) | language;
        let mut layouts = vec![HKL::default(); count as usize];
        let count = unsafe { GetKeyboardLayoutList(Some(&mut layouts)) };
        layouts.truncate(count.max(0) as usize);
        layouts
            .into_iter()
            .find(|layout| layout.0 as usize as u32 == id)
            .expect("required layout missing; this test must not load or install it")
    }

    #[test]
    #[ignore = "isolated Windows only: preinstalled US/German layouts"]
    fn punctuation_and_digits_follow_layout() {
        let us = preinstalled_layout("00000409");
        let de = preinstalled_layout("00000407");
        let mut state = [0; 256];
        assert_eq!(resolve(0xBA, &state, us), Some(';'));
        assert_eq!(resolve(0x32, &state, de), Some('2'));
        assert_eq!(resolve(0x20, &state, us), Some(' '));
        state[VK_SHIFT.0 as usize] = 0x80;
        assert_eq!(resolve(0xBA, &state, us), Some(':'));
        assert_eq!(resolve(0xBB, &state, us), Some('+'));
        assert_eq!(resolve(0xBB, &state, de), Some('*'));
        assert_eq!(resolve(0x32, &state, us), Some('@'));
        assert_eq!(resolve(0x32, &state, de), Some('"'));
    }

    /// 小键盘数字与运算符也要能解出字符：V 模式（表达式模式）里敲小键盘才会进缓冲区。
    /// 关掉 NumLock 时系统上报的 vk 是导航键（VK_INSERT / VK_END…），到不了这里。
    #[test]
    #[ignore = "isolated Windows only: preinstalled US layout"]
    fn keypad_digits_and_operators_resolve() {
        let us = preinstalled_layout("00000409");
        let mut state = [0; 256];
        state[VK_NUMLOCK.0 as usize] = 0x01;
        for (vk, expected) in [
            (0x60, '0'),
            (0x61, '1'),
            (0x62, '2'),
            (0x63, '3'),
            (0x64, '4'),
            (0x65, '5'),
            (0x66, '6'),
            (0x67, '7'),
            (0x68, '8'),
            (0x69, '9'),
            (0x6A, '*'),
            (0x6B, '+'),
            (0x6D, '-'),
            (0x6E, '.'),
            (0x6F, '/'),
        ] {
            assert_eq!(resolve(vk, &state, us), Some(expected), "vk={vk:#x}");
        }
    }

    #[test]
    #[ignore = "isolated Windows only: preinstalled US International layout and dead-key buffer"]
    fn dead_key_lookup_does_not_change_next_character() {
        let intl = preinstalled_layout("00020409");
        let state = [0; 256];
        for _ in 0..2 {
            assert_eq!(resolve(0xDE, &state, intl), None);
            // 每次预读后立即检查，避免两次 dead key 抵消副作用。
            assert_eq!(resolve(0x45, &state, intl), Some('e'));
        }
        assert_eq!(resolve(0, &state, intl), None);
        let mut buffer = [0; 8];
        // 与上面的重音检查串行：内核键盘缓冲会让并行测试互相影响。
        let dead = unsafe { ToUnicodeEx(0xDE, 0, &state, &mut buffer, 0, Some(intl)) };
        let result = resolve(0x31, &state, intl);
        // 清掉待组合的重音后再断言。
        let count = unsafe { ToUnicodeEx(0x31, 0, &state, &mut buffer, 0, Some(intl)) };
        assert!(dead < 0);
        assert_eq!(count, 2);
        assert_eq!(result, None);
    }

    #[test]
    fn other_keys_are_not_resolved() {
        for vk in [0x41, 0x70, 0x0D, 0x14] {
            assert_eq!(character(vk), None);
        }
    }

    #[test]
    #[ignore = "isolated Windows only: layout activation, keyboard state, disconnected worker"]
    fn event_reads_thread_layout_and_keyboard_state() {
        use crate::com::key::event::to_key_event;
        use crate::com::service::TextService;
        use windows::Win32::Foundation::{LPARAM, WPARAM};
        use windows::Win32::UI::Input::KeyboardAndMouse::{
            ActivateKeyboardLayout, SetKeyboardState, VK_CAPITAL, VK_CONTROL, VK_LSHIFT,
        };
        use windows::Win32::UI::TextServices::ITfKeyEventSink;
        use windows::core::ComObject;

        let original_layout = unsafe { GetKeyboardLayout(0) };
        let mut original_state = [0; 256];
        unsafe { GetKeyboardState(&mut original_state) }.unwrap();
        // Windows 的布局激活可能影响整个会话；仅允许隔离环境运行。
        let us = preinstalled_layout("00000409");
        let de = preinstalled_layout("00000407");
        // 即使断言失败，也先恢复布局和按键状态。
        let result = std::panic::catch_unwind(|| {
            let service = ComObject::new(TextService::new());
            let sink: ITfKeyEventSink = service.to_interface();
            for (layout, expected) in [(us, '+'), (de, '*')] {
                unsafe { ActivateKeyboardLayout(layout, Default::default()) }.unwrap();
                let mut state = [0; 256];
                state[VK_SHIFT.0 as usize] = 0x80;
                state[VK_LSHIFT.0 as usize] = 0x80;
                unsafe { SetKeyboardState(&state) }.unwrap();
                for _ in 0..2 {
                    let event = to_key_event(0xBB, true);
                    assert_eq!(event.character, Some(expected));
                    assert!(event.modifiers.shift);
                    assert!(event.modifiers.english_mode);
                    // 未连接后台的独立 TSF 对象仍正确解析布局，但必须放行按键。
                    assert!(
                        !unsafe { sink.OnTestKeyDown(None, WPARAM(0xBB), LPARAM(0)) }
                            .unwrap()
                            .as_bool()
                    );
                }
                state[VK_SHIFT.0 as usize] = 0;
                state[VK_LSHIFT.0 as usize] = 0;
                state[VK_CAPITAL.0 as usize] = 1;
                state[VK_CONTROL.0 as usize] = 0x80;
                unsafe { SetKeyboardState(&state) }.unwrap();
                let event = to_key_event(0x31, false);
                assert!(!event.modifiers.shift);
                assert!(event.modifiers.caps);
                assert!(event.modifiers.ctrl);
                assert_eq!(event.virtual_key, 0x31);
                assert_eq!(to_key_event(0x41, false).character, Some('A'));
                assert!(
                    !unsafe { sink.OnTestKeyDown(None, WPARAM(0x31), LPARAM(0)) }
                        .unwrap()
                        .as_bool()
                );
            }
        });
        unsafe { ActivateKeyboardLayout(original_layout, Default::default()) }.unwrap();
        unsafe { SetKeyboardState(&original_state) }.unwrap();
        result.unwrap();
    }
}
