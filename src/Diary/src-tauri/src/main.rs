#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
mod diary;
mod window_state;
mod clipboard_files;
mod context_menu;
mod window_icon;
use std::sync::Mutex;
use tauri::{Emitter, Manager};
use std::path::{Path, PathBuf};
struct OpenConflict(Mutex<Option<serde_json::Value>>);
struct OpenRequest(Mutex<Option<String>>);
fn open_argument(args: &[String]) -> Option<String> {
    args.windows(2).find(|pair|pair[0]=="--open-note").map(|pair|pair[1].clone())
}
fn personal_data_lock(root: &Path) -> std::io::Result<std::fs::File> {
    std::fs::create_dir_all(root)?;
    let mut options = std::fs::OpenOptions::new();
    options.read(true).write(true).create(true).truncate(false);
    #[cfg(windows)] { use std::os::windows::fs::OpenOptionsExt; options.share_mode(0); }
    options.open(root.join(".personal-transfer.lock"))
}

fn root_argument(args: &[String], cwd: &Path) -> PathBuf {
    let path = args.windows(2).find(|pair| pair[0] == "--data-dir").map(|pair| PathBuf::from(&pair[1]))
        .unwrap_or_else(|| std::env::var_os("APPDATA").map(PathBuf::from).unwrap_or_else(std::env::temp_dir).join("XiaomangLocalDiary"));
    if path.is_absolute() { path } else { cwd.join(path) }
}
fn same_root(first: &Path, second: &Path) -> bool {
    let first = first.canonicalize().unwrap_or_else(|_| first.to_path_buf());
    let second = second.canonicalize().unwrap_or_else(|_| second.to_path_buf());
    #[cfg(windows)] { first.to_string_lossy().eq_ignore_ascii_case(&second.to_string_lossy()) }
    #[cfg(not(windows))] { first == second }
}

#[tauri::command]
async fn diary_command(state: tauri::State<'_, Mutex<diary::Diary>>, conflict: tauri::State<'_, OpenConflict>, pending: tauri::State<'_, OpenRequest>, op: String, args: serde_json::Value) -> Result<serde_json::Value, String> {
    if op=="take_open_request" { return Ok(serde_json::json!(pending.0.lock().map_err(|_|"日记入口状态不可用")?.take())); }
    let app = state.inner();
    // Blocking disk operations run away from the webview/UI thread.
    let request = (op, args);
    let root = app.lock().map_err(|_| "日记服务不可用")?.root.clone();
    if request.0 == "search_cancel" {
        return diary::cancel_search(&root, request.1["requestId"].as_str().ok_or("缺少搜索请求标识")?);
    }
    if request.0 == "search" {
        let id = request.1["requestId"].as_str().ok_or("缺少搜索请求标识")?;
        let permit = diary::begin_search(&root, id)?;
        return tauri::async_runtime::spawn_blocking(move || diary::dispatch_search(&root, request.1, permit)).await.map_err(|e| e.to_string())?;
    }
    let initializing = request.0 == "init";
    let mut result = tauri::async_runtime::spawn_blocking(move || diary::dispatch(&root, &request.0, request.1))
        .await.map_err(|e| e.to_string())??;
    if initializing { result["dataConflict"] = conflict.0.lock().map_err(|_|"资料库状态不可用")?.take().unwrap_or(serde_json::Value::Null); }
    Ok(result)
}

fn main() {
    let initial_request=open_argument(&std::env::args().collect::<Vec<_>>());
    let mut args = std::env::args().skip(1);
    let root = match args.next().as_deref() {
        Some("--data-dir") => args.next().map(std::path::PathBuf::from),
        _ => None,
    }.unwrap_or_else(|| {
        let base = std::env::var_os("APPDATA").map(std::path::PathBuf::from).unwrap_or_else(std::env::temp_dir);
        base.join("XiaomangLocalDiary")
    });
    let active_root = root.clone();
    tauri::Builder::default()
        .plugin(tauri_plugin_single_instance::init(move |app, args, cwd| {
            let requested = root_argument(&args, Path::new(&cwd));
            if !same_root(&active_root, &requested) {
                let payload = serde_json::json!({"current": active_root, "requested": requested});
                if let Ok(mut conflict) = app.state::<OpenConflict>().0.lock() { *conflict = Some(payload.clone()); }
                let _ = app.emit("diary-data-conflict", payload);
            } else if let Some(path)=open_argument(&args) {
                if let Ok(mut request)=app.state::<OpenRequest>().0.lock() { *request=Some(path); }
                let _=app.emit("diary-open-request", ());
            }
            if let Some(window) = app.get_webview_window("main") {
                let _ = window.show(); let _ = window.unminimize(); let _ = window.set_focus();
            }
        }))
        .plugin(tauri_plugin_dialog::init())
        .manage(OpenConflict(Mutex::new(None)))
        .manage(OpenRequest(Mutex::new(initial_request)))
        .setup(move |app| {
            let guard = personal_data_lock(&root)?;
            app.manage(guard);
            app.manage(Mutex::new(diary::Diary::open(root).map_err(std::io::Error::other)?));
            context_menu::install(app)?; window_icon::install(app)?; window_state::install(app)?; Ok(())
        })
        .invoke_handler(tauri::generate_handler![diary_command, context_menu::body_menu_command])
        .run(tauri::generate_context!())
        .expect("无法启动本地日记窗口");
}

#[cfg(test)] mod instance_tests {
    use super::*;
    #[cfg(windows)] #[test] fn personal_transfer_lock_is_exclusive_until_close() {
        let root=std::env::temp_dir().join(format!("xiaomang-diary-lock-{}",uuid::Uuid::new_v4()));
        let first=personal_data_lock(&root).unwrap(); assert!(personal_data_lock(&root).is_err());
        drop(first); drop(personal_data_lock(&root).unwrap()); std::fs::remove_dir_all(root).unwrap();
    }
    #[test] fn roots_are_compared_after_canonicalization() {
        let root=std::env::temp_dir().join(format!("xiaomang-instance-{}",uuid::Uuid::new_v4()));
        std::fs::create_dir_all(root.join("sub")).unwrap();
        assert!(same_root(&root,&root.join("sub/..")));
        assert!(!same_root(&root,&root.join("sub")));
        std::fs::remove_dir_all(root).unwrap();
    }
    #[test] fn later_launch_resolves_the_requested_directory() {
        let root=std::env::temp_dir();
        assert_eq!(root_argument(&["diary.exe".into(),"--data-dir".into(),"second".into()],&root),root.join("second"));
    }
}
