#[cfg(windows)]
use tauri::Manager;

fn editing_command(name: &str) -> Option<&'static str> {
    match name.to_ascii_lowercase().as_str() {
        "undo" => Some("undo"), "cut" => Some("cut"), "copy" => Some("copy"),
        "paste" => Some("paste"), "selectall" => Some("selectAll"),
        "pasteandmatchstyle" | "pasteasplaintext" | "pasteplaintext" => Some("pastePlain"),
        _ => None,
    }
}

#[derive(Clone, serde::Serialize)]
struct EditingItem { id: i32, name: &'static str, enabled: bool }
#[derive(Clone, serde::Serialize)]
struct MenuRequest { request: u64, items: Vec<EditingItem> }

// COM objects stay on the WebView UI thread. Never put them in Send/Sync state.
#[cfg(windows)]
struct PendingMenu {
    request: MenuRequest,
    args: webview2_com::Microsoft::Web::WebView2::Win32::ICoreWebView2ContextMenuRequestedEventArgs,
    deferral: webview2_com::Microsoft::Web::WebView2::Win32::ICoreWebView2Deferral,
}
#[cfg(windows)]
impl Drop for PendingMenu {
    fn drop(&mut self) { if let Err(error)=unsafe { self.deferral.Complete() } { eprintln!("正文菜单结束失败：{error}"); } }
}
#[cfg(windows)]
thread_local! { static PENDING: std::cell::RefCell<Option<PendingMenu>> = const { std::cell::RefCell::new(None) }; }

#[cfg(windows)]
unsafe fn editing_items(items: &webview2_com::Microsoft::Web::WebView2::Win32::ICoreWebView2ContextMenuItemCollection) -> windows_core::Result<Vec<EditingItem>> {
    use webview2_com::take_pwstr;
    let mut count=0; items.Count(&mut count)?; let mut result=Vec::new();
    for index in 0..count {
        let item=items.GetValueAtIndex(index)?;
        let mut name=windows_core::PWSTR::null(); item.Name(&mut name)?;
        if let Some(name)=editing_command(&take_pwstr(name)) {
            let mut id=0; let mut enabled=windows_core::BOOL::default();
            item.CommandId(&mut id)?;item.IsEnabled(&mut enabled)?;
            result.push(EditingItem{id,name,enabled:enabled.as_bool()});
        }
    }
    Ok(result)
}

#[tauri::command]
pub async fn body_menu_command(window: tauri::WebviewWindow, request: u64, command: i32, native: bool) -> Result<(),String> {
    #[cfg(windows)] {
        let (sender,receiver)=std::sync::mpsc::channel();
        window.with_webview(move |_| {
            let result=PENDING.with(|state| {
                let mut state=state.borrow_mut();
                if state.as_ref().map(|p|p.request.request)!=Some(request) { return Ok(()) }
                let pending=state.take().unwrap();
                if command!=-1 && !pending.request.items.iter().any(|item|item.id==command&&item.enabled) { return Err("此编辑命令当前不可用".to_string()) }
                // WebView retains the original native target and clipboard formats.
                // Selecting its original command ID invokes the normal edit/paste path.
                unsafe { pending.args.SetHandled(!native).and_then(|_|pending.args.SetSelectedCommandId(command)).map_err(|e|e.to_string()) }
                // Drop completes the deferral after the chosen ID is set.
            });
            let _=sender.send(result);
        }).map_err(|e|e.to_string())?;
        return tauri::async_runtime::spawn_blocking(move ||receiver.recv_timeout(std::time::Duration::from_secs(5)).map_err(|e|e.to_string())?)
            .await.map_err(|e|e.to_string())?;
    }
    #[cfg(not(windows))] { let _=(window,request,command,native);Err("正文菜单需要 Windows WebView2".into()) }
}

// Names are unlocalized WebView2 command identifiers. Labels cover runtimes
// which expose a different identifier for Windows voice typing or sharing.
fn excluded(name: &str, label: &str) -> bool {
    let name = name.to_ascii_lowercase();
    let label = label.replace('&', "");
    let label = label.split('\t').next().unwrap_or("").trim().to_lowercase();
    matches!(name.as_str(), "share" | "sharing" | "voicetyping" | "voiceinput" | "dictation" | "emoji" | "emojis" | "emojipicker" | "emojipanel" | "insertemoji" | "moretools")
        || matches!(label.as_str(), "共享" | "分享" | "语音输入" | "語音輸入" | "共用" | "share" | "sharing" | "voice typing" | "voice input" | "dictation" | "表情符号" | "表情符號" | "emoji" | "emojis" | "emoji and symbols" | "emoji  symbols" | "更多工具" | "more tools")
}

#[cfg(windows)]
unsafe fn filter(items: &webview2_com::Microsoft::Web::WebView2::Win32::ICoreWebView2ContextMenuItemCollection) -> windows_core::Result<bool> {
    use webview2_com::{take_pwstr, Microsoft::Web::WebView2::Win32::COREWEBVIEW2_CONTEXT_MENU_ITEM_KIND_SUBMENU};
    use windows_core::PWSTR;
    let mut count = 0;
    items.Count(&mut count)?;
    let mut changed = false;
    // Descending indexes stay valid when removing items from nested menus.
    for index in (0..count).rev() {
        let item = items.GetValueAtIndex(index)?;
        let mut name = PWSTR::null(); item.Name(&mut name)?;
        let name = take_pwstr(name);
        let mut label = PWSTR::null(); item.Label(&mut label)?;
        let label = take_pwstr(label);
        if excluded(&name, &label) {
            items.RemoveValueAtIndex(index)?;
            changed = true;
            continue;
        }
        let mut kind = Default::default(); item.Kind(&mut kind)?;
        if kind == COREWEBVIEW2_CONTEXT_MENU_ITEM_KIND_SUBMENU {
            let children = item.Children()?;
            if filter(&children)? {
                changed = true;
                let mut remaining = 0; children.Count(&mut remaining)?;
                if remaining == 0 { items.RemoveValueAtIndex(index)?; }
            }
        }
    }
    Ok(changed)
}

pub fn install(app: &tauri::App) -> tauri::Result<()> {
    #[cfg(windows)]
    {
    use tauri::Emitter;
    let window=app.get_webview_window("main").expect("日记主窗口缺失");
    let events=window.clone();
    window.with_webview(move |webview| {
        use webview2_com::{ContextMenuRequestedEventHandler, Microsoft::Web::WebView2::Win32::ICoreWebView2_11};
        use windows_core::Interface;
        let result = unsafe {
            (|| -> windows_core::Result<()> {
                let core = webview.controller().CoreWebView2()?.cast::<ICoreWebView2_11>()?;
                let handler = ContextMenuRequestedEventHandler::create(Box::new(move |_, args| {
                    if let Some(args) = args {
                        filter(&args.MenuItems()?)?;
                        let items=editing_items(&args.MenuItems()?)?;
                        let deferral=args.GetDeferral()?;
                        args.SetHandled(true)?;
                        static NEXT: std::sync::atomic::AtomicU64=std::sync::atomic::AtomicU64::new(1);
                        let request=MenuRequest{request:NEXT.fetch_add(1,std::sync::atomic::Ordering::Relaxed),items};
                        PENDING.with(|state|*state.borrow_mut()=Some(PendingMenu{request:request.clone(),args:args.clone(),deferral}));
                        if let Err(error)=events.emit("diary-body-menu",request) {
                            eprintln!("正文菜单显示失败：{error}");
                            args.SetHandled(false)?;PENDING.with(|state|{state.borrow_mut().take();});
                        }
                    }
                    Ok(())
                }));
                let mut token = 0;
                core.add_ContextMenuRequested(&handler, &mut token)
            })()
        };
        if let Err(error) = result { eprintln!("日记右键菜单过滤初始化失败：{error}"); }
    })?;
    window.on_window_event(|event|{if matches!(event,tauri::WindowEvent::CloseRequested{..}|tauri::WindowEvent::Destroyed){PENDING.with(|state|{state.borrow_mut().take();});}});
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn editing_commands_are_allowlisted_and_normalized() {
        assert_eq!(editing_command("copy"),Some("copy"));
        assert_eq!(editing_command("selectAll"),Some("selectAll"));
        assert_eq!(editing_command("pasteAndMatchStyle"),Some("pastePlain"));
        assert_eq!(editing_command("pasteAsPlainText"),Some("pastePlain"));
        for name in ["share","voiceTyping","print","saveAs","inspectElement",""] { assert_eq!(editing_command(name),None); }
    }
    #[test] fn excludes_target_commands_across_locales() {
        assert!(excluded("share", "Partager"));
        assert!(excluded("voiceTyping", "Saisie vocale"));
        assert!(excluded("unknown", "语音输入\tWin+H"));
        assert!(excluded("unknown", "共享"));
        assert!(excluded("unknown", "&Voice typing"));
        assert!(excluded("emoji", "Émojis"));
        assert!(excluded("unknown", "表情符号\tWin+."));
        assert!(excluded("unknown", "表情符號"));
        assert!(excluded("moreTools", "Outils supplémentaires"));
        assert!(excluded("unknown", "更多工具"));
        assert!(excluded("unknown", "More &tools"));
    }
    #[test] fn preserves_editing_and_other_commands() {
        for (name, label) in [("copy", "复制"), ("paste", "粘贴"), ("cut", "剪切"), ("selectAll", "全选"), ("print", "打印"), ("saveAs", "另存为"), ("copyLink", "复制共享链接")] {
            assert!(!excluded(name, label), "removed unrelated command {name}");
        }
    }
}
