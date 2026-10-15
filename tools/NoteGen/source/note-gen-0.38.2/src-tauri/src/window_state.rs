use serde::{Deserialize, Serialize};
use std::{fs, path::PathBuf, sync::{Arc, Mutex, mpsc, atomic::{AtomicU64, Ordering}}, time::Duration};
use tauri::{Manager, LogicalSize, WindowEvent};

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct WindowState { width: f64, height: f64, maximized: bool }
impl Default for WindowState {
    fn default() -> Self { Self { width: 1280.0, height: 820.0, maximized: false } }
}
impl WindowState {
    fn valid(&self) -> bool {
        self.width.is_finite() && self.height.is_finite() && self.width >= 200.0 && self.height >= 200.0
            && self.width <= 10000.0 && self.height <= 10000.0
    }
    fn fit(&mut self, width: f64, height: f64) {
        self.width = self.width.clamp(800.0_f64.min(width), width);
        self.height = self.height.clamp(540.0_f64.min(height), height);
    }
}
pub fn install(app: &tauri::App) -> tauri::Result<()> {
    let window = app.get_webview_window("main").expect("日记主窗口缺失");
    // --data-dir is also used for isolated acceptance tests; keep their window
    // preferences separate from the user's normal diary window.
    let arguments: Vec<String> = std::env::args().collect();
    let path = arguments.windows(2).find(|pair| pair[0] == "--data-dir")
        .map(|pair| PathBuf::from(&pair[1])).unwrap_or(app.path().app_config_dir()?).join("window-state.json");
    let mut saved = fs::read(&path).ok().and_then(|data| serde_json::from_slice::<WindowState>(&data).ok())
        .filter(WindowState::valid).unwrap_or_default();
    if let Some(monitor) = window.current_monitor()? {
        let area = monitor.work_area();
        let scale = monitor.scale_factor();
        let width = (area.size.width as f64 / scale - 24.0).max(200.0);
        let height = (area.size.height as f64 / scale - 64.0).max(200.0);
        saved.fit(width, height);
        window.set_min_size(Some(LogicalSize::new(800.0_f64.min(width), 540.0_f64.min(height))))?;
    }
    window.set_size(LogicalSize::new(saved.width, saved.height))?;
    window.center()?;
    if saved.maximized { window.maximize()?; }
    let state = Arc::new(Mutex::new(saved));
    let (sender, receiver) = mpsc::channel::<()>();
    let worker_path = path.clone();
    let worker_state = state.clone();
    let worker_window = window.clone();
    let generation = Arc::new(AtomicU64::new(0));
    let worker_generation = generation.clone();
    // Debounce resize events away from the UI thread. A corrupt or missing file falls back to defaults.
    std::thread::spawn(move || {
        while receiver.recv().is_ok() {
            loop {
                match receiver.recv_timeout(Duration::from_millis(250)) {
                    Ok(()) => (),
                    Err(_) => break,
                }
            }
            // Read under the same lock as close: a queued resize cannot overwrite
            // a newer close snapshot. Closing always flushes synchronously.
            let version = worker_generation.load(Ordering::SeqCst);
            let Ok(mut next) = worker_state.lock().map(|s| s.clone()) else { continue; };
            // Do not hold the shared lock across window API calls: they may need
            // the UI thread, which can concurrently be saving the close snapshot.
            observe(&worker_window, &mut next);
            if let Ok(mut latest) = worker_state.lock() {
                if worker_generation.load(Ordering::SeqCst) != version { continue; }
                *latest = next;
                if let Err(error) = persist(&worker_path, &latest) { eprintln!("日记窗口大小保存失败：{error}"); }
            }
        }
    });
    let observed = window.clone();
    window.on_window_event(move |event| {
        if !matches!(event, WindowEvent::Resized(_) | WindowEvent::Moved(_) | WindowEvent::CloseRequested { .. }) { return; }
        if matches!(event, WindowEvent::CloseRequested { .. }) {
            generation.fetch_add(1, Ordering::SeqCst);
            let Ok(mut next) = state.lock().map(|s| s.clone()) else { return; };
            observe(&observed, &mut next);
            let Ok(mut latest) = state.lock() else { return; };
            *latest = next.clone();
            if let Err(error) = persist(&path, &next) { eprintln!("日记窗口大小保存失败：{error}"); }
        } else { let _ = sender.send(()); }
    });
    window.show()?;
    Ok(())
}
fn observe(window: &tauri::WebviewWindow, next: &mut WindowState) {
    if window.is_minimized().unwrap_or(true) { return; }
    next.maximized = window.is_maximized().unwrap_or(next.maximized);
    if !next.maximized {
        if let (Ok(size), Ok(scale)) = (window.inner_size(), window.scale_factor()) {
            let logical = size.to_logical::<f64>(scale);
            if logical.width >= 200.0 && logical.height >= 200.0 { next.width = logical.width; next.height = logical.height; }
        }
    }
}
fn persist(path: &PathBuf, state: &WindowState) -> Result<(), String> {
    fs::create_dir_all(path.parent().unwrap()).map_err(|e| e.to_string())?;
    crate::diary::atomic_write(path, &serde_json::to_vec(state).map_err(|e| e.to_string())?)
}
#[cfg(test)] mod tests {
    use super::*;
    #[test] fn restored_size_fits_changed_screen() { let mut s = WindowState { width: 1800.0, height: 1000.0, maximized: true }; s.fit(960.0, 680.0); assert_eq!((s.width,s.height,s.maximized),(960.0,680.0,true)); }
    #[test] fn small_screen_does_not_leave_window_offscreen() { let mut s=WindowState::default();s.fit(640.0,400.0);assert_eq!((s.width,s.height),(640.0,400.0)); }
    #[test] fn normal_size_roundtrips() { let s=WindowState {width: 910.0,height:620.0,maximized:false};let restored:WindowState=serde_json::from_slice(&serde_json::to_vec(&s).unwrap()).unwrap();assert_eq!(restored.width,910.0);assert!(restored.valid()); }
    #[test] fn corrupt_size_is_rejected() { assert!(!WindowState {width:-1.0,..WindowState::default()}.valid());assert!(serde_json::from_str::<WindowState>("corrupt").is_err()); }
}
