//! Only recover a locally registered worker after a failed connection. Never wait on the input thread.
mod policy;

use std::os::windows::process::CommandExt;
use std::path::{Path, PathBuf};
use std::process::{Child, Command};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use windows::Win32::Foundation::{CloseHandle, HANDLE};
use windows::Win32::System::Threading::{
    CreateMutexW, OpenMutexW, ReleaseMutex, SYNCHRONIZATION_SYNCHRONIZE, WaitForSingleObject,
};
use windows::Win32::UI::WindowsAndMessaging::{MB_ICONWARNING, MB_OK, MessageBoxW};
use windows::core::HSTRING;
use windows_registry::{CURRENT_USER, Key};

use crate::com::log::log;
use policy::Budget;

const RECOVERY_KEY: &str = r"Software\Xiaomang\InputMethodRecovery";
static BUSY: AtomicBool = AtomicBool::new(false);
static NEXT_CHECK: AtomicU64 = AtomicU64::new(0);

fn now_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_millis() as u64
}

struct LaunchLock(HANDLE);
impl Drop for LaunchLock {
    fn drop(&mut self) {
        unsafe {
            let _ = ReleaseMutex(self.0);
            let _ = CloseHandle(self.0);
        }
    }
}

fn lock() -> Option<LaunchLock> {
    let pipe = qingjian_platform::protocol::default_pipe_name();
    let name = HSTRING::from(format!(
        "Local\\{}.RecoveryLaunch",
        pipe.rsplit('\\').next()?
    ));
    let handle = unsafe { CreateMutexW(None, false, &name) }.ok()?;
    let result = unsafe { WaitForSingleObject(handle, 0) };
    if result.0 == 0 || result.0 == 0x80 {
        Some(LaunchLock(handle))
    } else {
        unsafe {
            let _ = CloseHandle(handle);
        }
        None
    }
}

fn config() -> Result<Key, String> {
    CURRENT_USER
        .options()
        .read()
        .write()
        .open(RECOVERY_KEY)
        .map_err(|e| e.to_string())
}

fn validate_worker(path: &Path) -> bool {
    path.is_absolute()
        && !path.to_string_lossy().starts_with(r"\\")
        && path
            .file_name()
            .is_some_and(|name| name.eq_ignore_ascii_case("XiaomangInputWorker.exe"))
        && path.is_file()
        && path
            .parent()
            .is_some_and(|dir| dir.join("data/generated/dict.qj").is_file())
}

fn start_worker() -> Result<Option<Child>, String> {
    let Some(_lock) = lock() else { return Ok(None) };
    let key = config()?;
    if key.get_u32("Enabled").unwrap_or(0) != 1 {
        return Ok(None);
    }
    // An existing singleton may still be loading or flushing. Do not spawn a competitor.
    let pipe = qingjian_platform::protocol::default_pipe_name();
    let leaf = pipe.rsplit('\\').next().ok_or("输入会话无效")?;
    if let Ok(handle) = unsafe {
        OpenMutexW(
            SYNCHRONIZATION_SYNCHRONIZE,
            false,
            &HSTRING::from(format!("Local\\{leaf}.Singleton")),
        )
    } {
        unsafe {
            let _ = CloseHandle(handle);
        }
        return Ok(None);
    }
    let mut budget = Budget {
        last: key.get_u64("LastAttempt").unwrap_or(0),
        start: key.get_u64("WindowStart").unwrap_or(0),
        attempts: key.get_u32("Attempts").unwrap_or(0),
    };
    if !budget.take(now_ms()) {
        return Ok(None);
    }
    key.set_u64("LastAttempt", budget.last)
        .map_err(|e| e.to_string())?;
    key.set_u64("WindowStart", budget.start)
        .map_err(|e| e.to_string())?;
    key.set_u32("Attempts", budget.attempts)
        .map_err(|e| e.to_string())?;
    let path = PathBuf::from(key.get_string("WorkerPath").map_err(|_| "未找到后台位置")?);
    if !validate_worker(&path) {
        return Err("后台文件或词库不可用，请打开小蟒检查组件".into());
    }
    // The stop name is supplied by the main app, derived from this user and login session.
    let stop = key
        .get_string("StopEvent")
        .map_err(|_| "未找到后台控制信息")?;
    if !stop.starts_with(r"Local\XiaomangInputStop.") || stop.contains(['\r', '\n']) {
        return Err("后台控制信息无效".into());
    }
    log("输入后台失联，开始按需恢复");
    Command::new(&path)
        .current_dir(path.parent().unwrap())
        .args(["--resident", "--stop-event", &stop])
        .creation_flags(0x08000000)
        .spawn()
        .map(Some)
        .map_err(|e| format!("后台启动失败：{e}"))
}

fn notify_failure(reason: &str) {
    log(&format!("输入后台恢复失败：{reason}"));
    // Cross-process cooldown prevents a dialog per application. Explicit pauses are silent.
    let Some(guard) = lock() else { return };
    let Ok(key) = config() else { return };
    if key.get_u32("Enabled").unwrap_or(0) != 1 {
        return;
    }
    let now = now_ms();
    let previous = key.get_u64("LastNotice").unwrap_or(0);
    if now.saturating_sub(previous) < 300_000 {
        return;
    }
    if key.set_u64("LastNotice", now).is_err() {
        return;
    }
    drop(guard);
    // This runs on the recovery thread with no owner, so it does not disable the input application.
    let message = HSTRING::from(format!(
        "小蟒输入后台暂不可用。请打开小蟒后重试。\n{reason}"
    ));
    unsafe {
        MessageBoxW(
            None,
            &message,
            &HSTRING::from("小蟒输入法"),
            MB_OK | MB_ICONWARNING,
        );
    }
}

fn recover() {
    match start_worker() {
        Ok(Some(mut child)) => {
            for _ in 0..40 {
                if crate::client::pipe::connect_default().is_ok() {
                    log("输入后台按需恢复成功");
                    return;
                }
                if let Ok(Some(status)) = child.try_wait() {
                    notify_failure(&format!("后台启动后退出，代码：{status}"));
                    return;
                }
                std::thread::sleep(Duration::from_millis(100));
            }
            notify_failure("后台已启动，但尚未就绪");
        }
        Ok(None) => {}
        Err(error) => notify_failure(&error),
    }
}

pub(super) fn launch_server() -> bool {
    let now = now_ms();
    if now < NEXT_CHECK.load(Ordering::Relaxed) || BUSY.swap(true, Ordering::AcqRel) {
        return false;
    }
    NEXT_CHECK.store(now + 10_000, Ordering::Relaxed);
    crate::com::lock_module();
    if std::thread::Builder::new()
        .name("xiaomang-input-recovery".into())
        .spawn(|| {
            let _ = std::panic::catch_unwind(recover);
            BUSY.store(false, Ordering::Release);
            crate::com::unlock_module();
        })
        .is_err()
    {
        BUSY.store(false, Ordering::Release);
        crate::com::unlock_module();
        return false;
    }
    true
}

#[cfg(test)]
mod tests {
    use super::validate_worker;
    use std::path::Path;
    #[test]
    fn recovery_does_not_run_relative_network_or_missing_payloads() {
        assert!(!validate_worker(Path::new("XiaomangInputWorker.exe")));
        assert!(!validate_worker(Path::new(
            r"\\server\share\XiaomangInputWorker.exe"
        )));
        assert!(!validate_worker(Path::new(
            r"C:\definitely-missing-xiaomang\XiaomangInputWorker.exe"
        )));
    }
}
