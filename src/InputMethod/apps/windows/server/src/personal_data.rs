// Personal backup codec. It never loads .env, logs, dictionaries or the resident engine.
use std::path::Path;
use qingjian_platform::Config;
use toml_edit::DocumentMut;
use std::io::Write;
const SECTIONS: &[&str] = &["general", "custom_phrases", "shortcut", "fuzzy", "dictionaries", "aux_code", "apps", "status_bar", "model"];

fn portable(source: &str) -> Result<DocumentMut, String> {
    let config: Config = toml::from_str(source).map_err(|e| format!("输入法配置无效：{e}"))?;
    let mut doc: DocumentMut = toml::to_string(&config).map_err(|e| e.to_string())?.parse::<DocumentMut>().map_err(|e| e.to_string())?;
    let keys: Vec<String> = doc.iter().map(|(key, _)| key.to_owned()).collect();
    for key in keys { if !SECTIONS.contains(&key.as_str()) { doc.remove(&key); } }
    // These languages have been removed from the maintained product.
    if let Some(general) = doc.get_mut("general").and_then(|item| item.as_table_mut()) {
        if matches!(general.get("learning_language").and_then(|item| item.as_str()), Some("ja" | "es")) {
            general.insert("learning_language", toml_edit::value("en"));
        }
    }
    Ok(doc)
}

pub fn try_run(args: &[String]) -> bool {
    if args.get(1).map(String::as_str) != Some("--personal-data-config") { return false; }
    let result = (|| -> Result<(), String> {
        let mode = args.get(2).map(String::as_str).ok_or("缺少操作")?;
        let input = args.get(3).ok_or("缺少配置路径")?;
        let source = match std::fs::read_to_string(input) { Ok(value) => value, Err(e) if e.kind() == std::io::ErrorKind::NotFound && mode == "export" => String::new(), Err(e) => return Err(e.to_string()) };
        let selected = portable(&source)?;
        let (document, output) = match mode {
            "export" if args.len() == 5 => (selected, args[4].as_str()),
            "merge" if args.len() == 6 => {
                let current = match std::fs::read_to_string(&args[4]) { Ok(value) => value, Err(e) if e.kind() == std::io::ErrorKind::NotFound => String::new(), Err(e) => return Err(e.to_string()) };
                let mut target: DocumentMut = current.parse::<DocumentMut>().map_err(|e| e.to_string())?;
                for key in SECTIONS { if let Some(item) = selected.get(key) { target.insert(key, item.clone()); } }
                (target, args[5].as_str())
            },
            _ => return Err("不支持的配置操作".into()),
        };
        let output = Path::new(output);
        if !output.is_absolute() || output == Path::new(input) { return Err("输出必须是独立的绝对路径".into()); }
        let mut file = std::fs::OpenOptions::new().write(true).create_new(true).open(output).map_err(|e| e.to_string())?;
        file.write_all(document.to_string().as_bytes()).map_err(|e| e.to_string())?;
        file.sync_all().map_err(|e| e.to_string())
    })();
    if let Err(error) = result { eprintln!("{error}"); std::process::exit(2); }
    true
}

pub fn session_lock(root: &Path) -> std::io::Result<std::fs::File> {
    std::fs::create_dir_all(root)?;
    let mut options = std::fs::OpenOptions::new();
    options.read(true).write(true).create(true).truncate(false);
    #[cfg(windows)] { use std::os::windows::fs::OpenOptionsExt; options.share_mode(0); }
    options.open(root.join(".personal-transfer.lock"))
}

#[cfg(test)] mod tests {
    use super::*;
    #[test] fn portable_keeps_phrases_and_appearance_but_excludes_cloud_and_unknown_secrets() {
        let doc = portable("[general]\npage_size=7\nlearning_language='ja'\nsecret='not-exported'\n[predict]\nenabled=true\n[[custom_phrases]]\ncode='xm'\ntext='小蟒\\n短语'\nposition=1\n").unwrap();
        let text=doc.to_string(); assert!(text.contains("小蟒")); assert!(text.contains("page_size = 7")); assert!(text.contains("learning_language = \"en\"")); assert!(!text.contains("predict")); assert!(!text.contains("secret"));
    }
    #[cfg(windows)]
    #[test] fn session_lock_blocks_a_second_writer_and_releases_normally() {
        let root = std::env::temp_dir().join(format!("xiaomang-transfer-lock-{}", std::process::id()));
        let first = session_lock(&root).unwrap();
        assert!(session_lock(&root).is_err()); drop(first);
        drop(session_lock(&root).unwrap()); std::fs::remove_dir_all(root).unwrap();
    }
}
