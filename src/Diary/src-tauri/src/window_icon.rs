pub fn image() -> tauri::image::Image<'static> {
    tauri::image::Image::new(include_bytes!(concat!(env!("OUT_DIR"), "/diary-window-icon.rgba")), 256, 256)
}

// Tauri sets ICON_SMALL only. Share its owned high-resolution handle with
// ICON_BIG so Windows does not fall back to a scaled executable/class icon.
pub fn install(app: &tauri::App) -> tauri::Result<()> {
    #[cfg(windows)]
    {
        use tauri::Manager;
        use std::ffi::c_void;
        #[link(name = "user32")]
        unsafe extern "system" {
            fn SendMessageW(window: *mut c_void, message: u32, wparam: usize, lparam: isize) -> isize;
        }
        let window = app.get_webview_window("main").expect("日记主窗口缺失");
        let handle = window.hwnd()?.0 as *mut c_void;
        // The window retains ownership of ICON_SMALL for its full lifetime.
        unsafe {
            let icon = SendMessageW(handle, 0x007f, 0, 0); // WM_GETICON, ICON_SMALL
            if icon != 0 { SendMessageW(handle, 0x0080, 1, icon); } // WM_SETICON, ICON_BIG
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    #[test]
    fn embedded_window_icon_is_full_resolution() {
        let image = super::image();
        assert_eq!((image.width(), image.height()), (256, 256));
        assert_eq!(image.rgba().len(), 256 * 256 * 4);
        assert!(image.rgba().chunks_exact(4).any(|pixel| pixel[3] != 0));
    }
}
