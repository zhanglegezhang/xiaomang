use std::path::PathBuf;

// Read once in response to an explicit paste. No clipboard polling or history.
#[cfg(windows)]
pub fn images() -> Result<Vec<PathBuf>, String> {
    use windows_sys::Win32::{System::DataExchange::{OpenClipboard, CloseClipboard, GetClipboardData}, UI::Shell::DragQueryFileW};
    struct Open;
    impl Drop for Open { fn drop(&mut self) { unsafe { CloseClipboard(); } } }
    unsafe {
        if OpenClipboard(std::ptr::null_mut()) == 0 { return Err("剪贴板正被其他程序使用，请重试粘贴".into()) }
        let _close = Open;
        let drop = GetClipboardData(15); // CF_HDROP
        if drop.is_null() { return Ok(vec![]) }
        let count = DragQueryFileW(drop, u32::MAX, std::ptr::null_mut(), 0);
        if count > 100 { return Err("一次最多粘贴 100 张图片".into()) }
        let mut paths = vec![];
        for index in 0..count {
            let length = DragQueryFileW(drop, index, std::ptr::null_mut(), 0);
            if length > 32767 { return Err("图片路径过长".into()) }
            let mut name = vec![0u16; length as usize + 1];
            if DragQueryFileW(drop, index, name.as_mut_ptr(), name.len() as u32) != length { return Err("无法读取剪贴板图片路径".into()) }
            let path = PathBuf::from(String::from_utf16(&name[..length as usize]).map_err(|e| e.to_string())?);
            let ext = path.extension().and_then(|e| e.to_str()).unwrap_or("").to_lowercase();
            if ["png", "jpg", "jpeg", "gif", "webp"].contains(&ext.as_str()) { paths.push(path) }
        }
        Ok(paths)
    }
}
#[cfg(not(windows))]
pub fn images() -> Result<Vec<PathBuf>, String> { Ok(vec![]) }
