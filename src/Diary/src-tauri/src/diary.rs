// Local-only storage for the NoteGen-derived Xiaomang diary. GPL-3.0-or-later.
use std::{collections::{BTreeMap, HashMap, HashSet, VecDeque}, fs::{self, OpenOptions}, io::Write, path::{Component, Path, PathBuf}, sync::{Arc, Mutex, OnceLock, atomic::{AtomicBool, Ordering}}};
use serde_json::{json, Value};
use sha2::{Digest, Sha256};
use base64::Engine;
use uuid::Uuid;
#[path = "diary_batch.rs"]
mod batch;
#[path = "diary_copy.rs"]
mod copies;
#[path = "diary_activity.rs"]
mod activity;
#[path = "diary_import.rs"]
mod backup_import;

const MAX_NOTE: u64 = 10 * 1024 * 1024;
const MAX_ASSET: u64 = 20 * 1024 * 1024;
const MAX_SEARCH_RESULTS: usize = 200;
const MAX_SEARCH_GAP: usize = 20;
const MAX_FUZZY_QUERY_CHARS: usize = 16;
static CHINESE_SEARCH: OnceLock<regex::Regex> = OnceLock::new();
// Reserved internal file: never exposed as a separate diary in the tree.
const FOLDER_CONTENT: &str = ".xiaomang-folder.md";
const ITEM_ORDER: &str = ".xiaomang-order.json";
const ICON_COLORS: [&str; 46] = [
    "#c74747", "#b96523", "#967519", "#368057", "#258387", "#3976b8",
    "#8f3030", "#884414", "#685312", "#24553a", "#1b5c61", "#284f7d",
    "#8755ad", "#b34f85", "#ce6473", "#8d6246", "#73808d", "#303841",
    "#a381c4", "#cb7aaa", "#729ac4", "#73a486", "#ce996c", "#acb5be",
    "#ef4444", "#f97316", "#eab308", "#16a34a", "#06b6d4", "#2563eb",
    "#7c3aed", "#d946ef", "#ec4899", "#a16207", "#64748b", "#111827",
    "#ff2020", "#ff7a00", "#ffcc00", "#00c853", "#00cfee", "#0066ff",
    "#8338ff", "#ed00e6", "#ff3b91", "#c07800",
];
// Legacy color strings stay readable and unchanged. Only solid selections need
// the structured value; storing both attributes together keeps existing atomic
// rename, trash and backup handling intact.
#[derive(Clone, Debug, PartialEq, serde::Serialize, serde::Deserialize)]
#[serde(untagged, deny_unknown_fields)]
enum IconAppearance { Outline(String), Styled { color: String, solid: bool } }
impl IconAppearance {
    fn color(&self) -> &str { match self { Self::Outline(color) | Self::Styled { color, .. } => color } }
    fn solid(&self) -> bool { matches!(self, Self::Styled { solid: true, .. }) }
    fn valid(&self) -> bool { ICON_COLORS.contains(&self.color()) || (self.color().is_empty() && self.solid()) }
}
impl From<&str> for IconAppearance { fn from(color: &str) -> Self { Self::Outline(color.to_owned()) } }
type Colors = BTreeMap<String, IconAppearance>;
static IO_LOCK: OnceLock<Mutex<()>> = OnceLock::new();
#[derive(Default)]
struct Searches {
    active: HashMap<PathBuf, (String, Arc<AtomicBool>)>,
    canceled: HashSet<(PathBuf, String)>,
    order: VecDeque<(PathBuf, String)>,
}
static SEARCHES: OnceLock<Mutex<Searches>> = OnceLock::new();
pub struct SearchPermit { root: PathBuf, canceled: Arc<AtomicBool> }
impl SearchPermit {
    fn check(&self) -> Result<()> { if self.canceled.load(Ordering::Acquire) { Err("搜索已取消".into()) } else { Ok(()) } }
}
impl Drop for SearchPermit {
    fn drop(&mut self) {
        if let Ok(mut searches) = SEARCHES.get_or_init(|| Mutex::new(Searches::default())).lock() {
            if searches.active.get(&self.root).is_some_and(|(_, flag)| Arc::ptr_eq(flag, &self.canceled)) { searches.active.remove(&self.root); }
        }
    }
}
// Register before spawning blocking work, so newer queries cancel queued work too.
pub fn begin_search(root: &Path, request_id: &str) -> Result<SearchPermit> {
    if request_id.is_empty() || request_id.len() > 128 { return Err("无效搜索请求".into()) }
    let mut searches = SEARCHES.get_or_init(|| Mutex::new(Searches::default())).lock().map_err(err)?;
    let flag = Arc::new(AtomicBool::new(searches.canceled.contains(&(root.to_owned(), request_id.to_owned()))));
    if let Some((_, old)) = searches.active.insert(root.to_owned(), (request_id.to_owned(), flag.clone())) { old.store(true, Ordering::Release); }
    Ok(SearchPermit { root: root.to_owned(), canceled: flag })
}
pub fn cancel_search(root: &Path, request_id: &str) -> Result<Value> {
    if request_id.is_empty() || request_id.len() > 128 { return Err("无效搜索请求".into()) }
    let mut searches = SEARCHES.get_or_init(|| Mutex::new(Searches::default())).lock().map_err(err)?;
    if let Some((id, flag)) = searches.active.get(root) { if id == request_id { flag.store(true, Ordering::Release); } }
    let key = (root.to_owned(), request_id.to_owned());
    if searches.canceled.insert(key.clone()) { searches.order.push_back(key); }
    while searches.order.len() > 1024 { if let Some(old) = searches.order.pop_front() { searches.canceled.remove(&old); } }
    Ok(json!(true))
}
pub fn dispatch_search(root: &Path, args: Value, permit: SearchPermit) -> Result<Value> {
    permit.check()?;
    let query = string(&args, "query")?.trim().to_lowercase();
    if query.is_empty() { return Ok(json!({"results":[],"limited":false,"skipped":0,"literalOnly":false})) }
    // Only tree metadata is protected. Atomic note replacement lets a search read
    // either complete revision while saves/open/delete continue independently.
    let (diary, paths) = {
        let lock = IO_LOCK.get_or_init(|| Mutex::new(()));
        let guard = loop {
            permit.check()?;
            match lock.try_lock() {
                Ok(guard) => break guard,
                Err(std::sync::TryLockError::Poisoned(_)) => return Err("日记存储锁不可用".into()),
                Err(std::sync::TryLockError::WouldBlock) => std::thread::sleep(std::time::Duration::from_millis(2)),
            }
        };
        let diary = Diary::open(root.to_owned())?;
        let tree = diary.tree()?;
        // The tree already validated these entries and captured their kind.
        // Actual reads still validate paths; do not stat every path again here.
        let mut paths = vec![]; flatten_search(&tree, &mut paths);
        drop(guard); (diary, paths)
    };
    // Bound skipped-character work for pasted Chinese passages. Count Unicode
    // characters, not UTF-8 bytes; other queries retain their literal meaning.
    let chinese_query = CHINESE_SEARCH.get_or_init(|| regex::Regex::new(r"^\p{Han}{2,}$").unwrap()).is_match(&query);
    let literal_only = chinese_query && query.chars().take(MAX_FUZZY_QUERY_CHARS + 1).count() > MAX_FUZZY_QUERY_CHARS;
    let fuzzy_query = if chinese_query && !literal_only {
        query.chars().collect::<Vec<_>>()
    } else { vec![] };
    let mut results = vec![]; let mut fuzzy_results = vec![];
    let mut skipped = 0; let mut limited = false; let mut fuzzy_full = false;
    for (path, folder) in paths {
        permit.check()?;
        let text = match diary.text(&path) { Ok(text) => text, Err(_) => { skipped += 1; continue } };
        let lower_path = path.to_lowercase();
        let exact_path = lower_path.contains(&query);
        let collect_fuzzy = !fuzzy_query.is_empty() && !exact_path && !fuzzy_full;
        let mut matches = vec![]; let mut fuzzy_matches = vec![];
        for (line, content) in text.lines().enumerate() {
            if line % 128 == 0 { permit.check()?; }
            let lower_content = content.to_lowercase();
            if lower_content.contains(&query) {
                matches.push(json!({"line":line+1,"snippet":content.chars().take(180).collect::<String>()}));
                if matches.len() == 3 { break }
            } else if collect_fuzzy && fuzzy_matches.len() < 3 && ordered_search_match(&lower_content, &fuzzy_query, &permit)? {
                fuzzy_matches.push(json!({"line":line+1,"snippet":content.chars().take(180).collect::<String>()}));
            }
        }
        if !matches.is_empty() || exact_path {
            if results.len() == MAX_SEARCH_RESULTS { limited = true; break }
            results.push(json!({"path":path,"folder":folder,"matches":matches}));
        } else if collect_fuzzy && (!fuzzy_matches.is_empty() || ordered_search_match(&lower_path, &fuzzy_query, &permit)?) {
            if fuzzy_results.len() == MAX_SEARCH_RESULTS { fuzzy_full = true; }
            else { fuzzy_results.push(json!({"path":path,"folder":folder,"matches":fuzzy_matches})); }
        }
    }
    permit.check()?;
    // Keep scanning for literal hits after collecting enough fuzzy candidates,
    // so earlier fuzzy hits cannot hide a literal hit near the end of the tree.
    let available = MAX_SEARCH_RESULTS - results.len();
    if fuzzy_results.len() > available || fuzzy_full { limited = true; }
    results.extend(fuzzy_results.into_iter().take(available));
    Ok(json!({"results":results,"limited":limited,"skipped":skipped,"literalOnly":literal_only}))
}

// Latest starts dominate older candidates for the same prefix. Reverse iteration
// prevents a repeated query character from consuming one source position twice.
fn ordered_search_match(line: &str, query: &[char], permit: &SearchPermit) -> Result<bool> {
    permit.check()?;
    if query.len() < 2 || !line.contains(query[0]) || !line.contains(query[query.len()-1]) { return Ok(false); }
    let mut starts = vec![None; query.len()];
    for (position, character) in line.chars().enumerate() {
        if position % 256 == 0 { permit.check()?; }
        for index in (0..query.len()).rev() {
            // Long pasted keywords must remain cancelable within a long line.
            if query.len() >= 1024 && index % 1024 == 0 { permit.check()?; }
            if character != query[index] { continue; }
            let start = if index == 0 { Some(position) } else { starts[index-1] };
            if let Some(start) = start {
                if position - start - index <= MAX_SEARCH_GAP {
                    starts[index] = Some(start);
                    if index == query.len()-1 { return Ok(true); }
                }
            }
        }
    }
    Ok(false)
}
type Result<T> = std::result::Result<T, String>;
pub struct Diary { pub root: PathBuf }
fn err(e: impl std::fmt::Display) -> String { e.to_string() }
fn digest(text: &str) -> String { format!("{:x}", Sha256::digest(text.as_bytes())) }
fn stamp() -> u128 { std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default().as_millis() }
fn note_extension(path: &Path) -> Option<&'static str> {
    match path.extension()?.to_str()?.to_ascii_lowercase().as_str() { "md"=>Some("md"), "txt"=>Some("txt"), _=>None }
}
fn detected_encoding(bytes: &[u8]) -> Option<&'static str> {
    if bytes.starts_with(&[0xff,0xfe]) { Some("utf16le") }
    else if bytes.starts_with(&[0xfe,0xff]) { Some("utf16be") }
    else if std::str::from_utf8(bytes).is_ok() && !bytes.contains(&0) { Some("utf8") }
    else { None }
}
fn decode_text(bytes: &[u8], encoding: &str) -> Result<String> {
    let text = match encoding {
        "utf8" => std::str::from_utf8(bytes).map_err(|_|"不是有效 UTF-8，请选择正确编码")?.to_owned(),
        "utf16le" | "utf16be" => {
            let bytes=if bytes.starts_with(&[0xff,0xfe]) || bytes.starts_with(&[0xfe,0xff]) {&bytes[2..]} else {bytes};
            if bytes.len()%2!=0 {return Err("UTF-16 文件长度无效，原文件未修改".into())}
            let words:Vec<u16>=bytes.chunks_exact(2).map(|b|if encoding=="utf16le" {u16::from_le_bytes([b[0],b[1]])}else{u16::from_be_bytes([b[0],b[1]])}).collect();
            String::from_utf16(&words).map_err(|_|"UTF-16 内容无效，原文件未修改")?
        },
        "gbk" => {
            #[cfg(windows)] {
                #[link(name="kernel32")] extern "system" {fn MultiByteToWideChar(cp:u32, flags:u32, bytes:*const u8, count:i32, wide:*mut u16, capacity:i32)->i32;}
                if bytes.is_empty(){String::new()} else {unsafe {
                    let count=MultiByteToWideChar(936,8,bytes.as_ptr(),bytes.len() as i32,std::ptr::null_mut(),0);
                    if count==0 {return Err("不是有效 GBK，请选择正确编码".into())}
                    let mut wide=vec![0u16;count as usize];
                    if MultiByteToWideChar(936,8,bytes.as_ptr(),bytes.len() as i32,wide.as_mut_ptr(),count)!=count {return Err("GBK 解码失败".into())}
                    String::from_utf16(&wide).map_err(err)?
                }}
            }
            #[cfg(not(windows))] {return Err("GBK 导入仅支持 Windows".into())}
        },
        _ => return Err("不支持的文本编码".into()),
    };
    if text.contains('\0') {return Err("文件包含空字符，请检查编码或选择文本文件".into())}
    Ok(text.trim_start_matches('\u{feff}').to_owned())
}
fn linked(path: &Path) -> bool {
    let Ok(meta) = fs::symlink_metadata(path) else { return false };
    #[cfg(windows)] { use std::os::windows::fs::MetadataExt; meta.file_attributes() & 0x400 != 0 }
    #[cfg(not(windows))] { meta.file_type().is_symlink() }
}
fn safe_name(value: &str) -> Result<()> {
    let stem = value.split('.').next().unwrap_or("").to_uppercase();
    if value.is_empty() || value.len() > 200 || value.ends_with(['.', ' ']) || value.starts_with('.')
        || value.chars().any(|c| c.is_control() || "<>:\"/\\|?*".contains(c))
        || ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"].contains(&stem.as_str()) {
        return Err("名称不能为空，不能包含路径、系统保留名或特殊符号".into());
    } Ok(())
}
impl Diary {
    pub fn open(root: PathBuf) -> Result<Self> {
        if !root.is_absolute() { return Err("数据目录必须是绝对路径".into()) }
        fs::create_dir_all(&root).map_err(err)?;
        let root = root.canonicalize().map_err(err)?;
        for part in ["notes", "attachments", "history", "trash", "backups"] {
            let path = root.join(part);
            if linked(&path) { return Err("数据子目录不能是符号链接或目录联接".into()) }
            fs::create_dir_all(path).map_err(err)?;
        }
        Ok(Self { root })
    }
    fn note_path(&self, relative: &str, root_allowed: bool) -> Result<PathBuf> {
        if relative.is_empty() && root_allowed { return Ok(self.root.join("notes")) }
        if relative.is_empty() || relative.contains('\\') { return Err("无效的日记路径".into()) }
        let mut path = self.root.join("notes");
        for component in Path::new(relative).components() {
            let Component::Normal(name) = component else { return Err("不允许访问日记目录之外的文件".into()) };
            safe_name(&name.to_string_lossy())?;
            path.push(name);
            if linked(&path) { return Err("不支持符号链接或目录联接".into()) }
        }
        Ok(path)
    }
    fn content_path(&self, relative: &str) -> Result<PathBuf> {
        let path = self.note_path(relative, false)?;
        if path.is_dir() {
            let content = path.join(FOLDER_CONTENT);
            if linked(&content) || (content.exists() && !content.is_file()) { return Err("无效的目录说明文件".into()) }
            return Ok(content);
        }
        if note_extension(&path).is_none() { return Err("只支持 MD、TXT 笔记或目录说明".into()) }
        Ok(path)
    }
    fn text(&self, relative: &str) -> Result<String> {
        let path = self.content_path(relative)?;
        if path.file_name().and_then(|s| s.to_str()) == Some(FOLDER_CONTENT) && !path.exists() { return Ok(String::new()) }
        if fs::metadata(&path).map_err(err)?.len() > MAX_NOTE { return Err("日记超过 10 MB，请拆分文件后打开".into()) }
        let text = fs::read_to_string(path).map_err(err)?;
        Ok(text.trim_start_matches('\u{feff}').to_owned())
    }
    fn tree(&self) -> Result<Vec<Value>> {
        self.tree_mode(false)
    }
    fn tree_mode(&self, recover: bool) -> Result<Vec<Value>> {
        let mut count = 0;
        let mut tree = walk_inner(&self.root.join("notes"), "", 0, &mut count, recover)?;
        let colors = self.colors().unwrap_or_default();
        fn paint(nodes: &mut [Value], colors: &Colors) {
            for node in nodes {
                if let Some(appearance) = colors.get(node["path"].as_str().unwrap()) {
                    if !appearance.color().is_empty() { node["color"] = json!(appearance.color()); }
                    node["solid"] = json!(appearance.solid());
                }
                paint(node["children"].as_array_mut().unwrap(), colors);
            }
        }
        paint(&mut tree, &colors); Ok(tree)
    }
    fn validate_addition(&self, parent: &str, folder: bool, added: usize) -> Result<()> {
        let depth = if parent.is_empty() { 0 } else { parent.split('/').count() };
        if folder && depth + 1 > 64 { return Err("新建后目录将超过 64 层，未写入任何项目".into()) }
        let tree = self.tree()?; let mut all = vec![]; flatten(&tree, &mut all);
        if all.len().saturating_add(added) > 20000 { return Err("新建/导入后项目将超过 20000 个，请先整理资料库；原件未修改".into()) }
        Ok(())
    }
    fn colors(&self) -> Result<Colors> { read_colors(&self.root.join("item-colors.json")) }
    fn storage_warning(&self) -> String {
        let mut warnings=vec![];
        if let Err(error)=self.colors() {warnings.push(format!("{error}；图标暂用默认外观，正文可继续编辑；修复或重置前不会覆盖损坏记录。"));}
        let path=self.root.join("last-import-warning.txt");
        if !linked(&path) && fs::metadata(&path).is_ok_and(|m|m.len()<16384) {
            if let Ok(text)=fs::read_to_string(path) {if !text.is_empty(){warnings.push(text)}}
        }
        warnings.join("\n")
    }
    fn write_colors(&self, colors: &Colors) -> Result<()> {
        let path = self.root.join("item-colors.json");
        if path.exists() { read_colors(&path)?; }
        if linked(&path) { return Err("图标颜色文件不能是链接".into()) }
        atomic_write(&path, &serde_json::to_vec(colors).map_err(err)?)
    }
    fn relocate(&self, relative: &str, next: &str) -> Result<Value> {
        let source = self.note_path(relative, false)?;
        let target = self.note_path(next, false)?;
        if !source.exists() { return Err("文件或目录已不存在，请刷新后重试".into()) }
        if source.is_file() && (note_extension(&source).is_none() || note_extension(&source)!=note_extension(&target)) { return Err("重命名和移动须保留 MD/TXT 原格式".into()) }
        if relative == next { return Ok(json!(relative)) }
        if source.is_dir() && next.to_lowercase().starts_with(&(relative.to_lowercase() + "/")) { return Err("不能移动到自身或子目录".into()) }
        if !target.parent().unwrap().is_dir() { return Err("目标目录不存在".into()) }
        if target.exists() && source.canonicalize().map_err(err)? != target.canonicalize().map_err(err)? { return Err("目标已有同名文件或目录，未覆盖".into()) }
        // Keep the existing tree limits valid after moving an entire subtree.
        if source.is_dir() {
            let depth = next.split('/').count();
            let _ = walk(&source, next, depth, &mut 0)?;
        }
        let old = self.colors()?;
        let colors = remap_colors(&old, relative, next);
        let source_parent = source.parent().unwrap();
        let target_parent = target.parent().unwrap();
        let source_order = read_order(source_parent)?;
        let target_order = read_order(target_parent)?;
        let old_name = source.file_name().unwrap().to_string_lossy().to_string();
        let new_name = target.file_name().unwrap().to_string_lossy().to_string();
        let mut changes = vec![];
        if source_parent == target_parent {
            if let Some(order) = &source_order { changes.push((source_parent.to_path_buf(), Some(order.iter().map(|n| if n == &old_name {new_name.clone()} else {n.clone()}).collect()))); }
        } else {
            if let Some(order) = &source_order { changes.push((source_parent.to_path_buf(), Some(order.iter().filter(|n|*n != &old_name).cloned().collect()))); }
            if let Some(order) = &target_order { let mut order=order.clone(); order.retain(|n|n != &new_name); order.push(new_name); changes.push((target_parent.to_path_buf(),Some(order))); }
        }
        fs::rename(&source, &target).map_err(err)?;
        if let Err(error) = commit_orders(&changes) {
            return match fs::rename(&target,&source) { Ok(())=>Err(error), Err(rollback)=>Err(format!("{error}；项目现位于 {next}，恢复原位置失败：{rollback}")) };
        }
        if colors != old {
            if let Err(error) = self.write_colors(&colors) {
                let rollback_order = commit_orders(&[(source_parent.to_path_buf(),source_order), (target_parent.to_path_buf(),target_order)]);
                return match fs::rename(&target, &source) {
                    Ok(()) => Err(format!("颜色保存失败，操作已撤回：{error}{}",rollback_order.err().map(|e|format!("；{e}")).unwrap_or_default())),
                    Err(rollback) => Err(format!("项目现位于 {next}；颜色保存失败：{error}；恢复原位置失败：{rollback}")),
                };
            }
        }
        activity::optional(activity::relocate(self, relative, next));
        Ok(json!(next))
    }
    fn save(&self, relative: &str, content: &str, revision: &str) -> Result<String> {
        if content.len() as u64 > MAX_NOTE { return Err("日记超过 10 MB，尚未保存".into()) }
        let old = self.text(relative)?;
        if digest(&old) != revision { return Err("文件已被其他程序修改。当前内容仍在编辑器中，请导出副本后重新打开，避免覆盖".into()) }
        confirm_content_assets(self, content)?;
        if old == content { return Ok(revision.to_string()) }
        let path = self.content_path(relative)?;
        let history = self.root.join("history").join(digest(relative));
        fs::create_dir_all(&history).map_err(err)?;
        // Preserve a pre-edit snapshot at most once per five minutes. Never prune notes/history.
        let recent = fs::read_dir(&history).map_err(err)?.filter_map(|e| e.ok()).any(|e|
            e.metadata().ok().and_then(|m| m.modified().ok()).and_then(|t| t.elapsed().ok()).is_some_and(|age| age.as_secs() < 300));
        if !recent { atomic_write(&history.join(format!("{}-{}.{}", stamp(), Uuid::new_v4(),note_extension(&path).unwrap_or("md"))), old.as_bytes())?; }
        atomic_write(&path, content.as_bytes())?;
        activity::optional(activity::saved(self, relative));
        Ok(digest(content))
    }
}
// Paste files stay outside attachments until the editor confirms insertion.
// UUID names are owned exclusively by this transaction; abort never touches an
// existing attachment. Saving references also confirms, covering UI/save races.
fn pending_root(diary: &Diary) -> Result<PathBuf> {
    let path = diary.root.join("asset-pending");
    if linked(&path) { return Err("图片暂存目录不能是链接".into()) }
    Ok(path)
}
fn batch_path(diary: &Diary, batch_id: &str) -> Result<PathBuf> {
    Uuid::parse_str(batch_id).map_err(|_| "无效图片批次")?;
    let path = pending_root(diary)?.join(batch_id);
    if linked(&path) { return Err("图片批次不能是链接".into()) }
    Ok(path)
}
fn valid_asset_id(id: &str) -> bool {
    Path::new(id).file_stem().and_then(|s|s.to_str()).is_some_and(|stem| Uuid::parse_str(stem).is_ok())
        && Path::new(id).extension().and_then(|s|s.to_str()).is_some_and(|ext| ["png","jpg","jpeg","gif","webp"].contains(&ext))
        && !id.contains(['/', '\\'])
}
fn batch_ids(path: &Path) -> Result<Vec<String>> {
    let manifest = path.join("manifest.json");
    if linked(&manifest) || fs::metadata(&manifest).map_err(err)?.len() > 8 * 1024 * 1024 { return Err("无效图片批次记录".into()) }
    let ids: Vec<String> = serde_json::from_slice(&fs::read(manifest).map_err(err)?).map_err(err)?;
    if ids.iter().any(|id| !valid_asset_id(id)) || ids.iter().collect::<HashSet<_>>().len() != ids.len() { return Err("无效图片批次记录".into()) }
    Ok(ids)
}
fn begin_assets(diary: &Diary, items: impl Iterator<Item = Result<(String, Vec<u8>)>>) -> Result<Value> {
    let batch_id = Uuid::new_v4().to_string(); let dir = batch_path(diary, &batch_id)?;
    fs::create_dir_all(pending_root(diary)?).map_err(err)?; fs::create_dir(&dir).map_err(err)?;
    let result = (|| {
        let mut ids = vec![];
        for item in items {
            let (extension,bytes) = item?;
            let id = format!("{}.{}",Uuid::new_v4(),extension);
            atomic_write(&dir.join(&id), &bytes)?; ids.push(id);
        }
        atomic_write(&dir.join("manifest.json"), &serde_json::to_vec(&ids).map_err(err)?)?;
        if ids.is_empty() { fs::remove_file(dir.join("manifest.json")).map_err(err)?; fs::remove_dir(&dir).map_err(err)?; return Ok(json!({"batchId":"","ids":[]})) }
        Ok(json!({"batchId":batch_id,"ids":ids}))
    })();
    if result.is_err() { let _ = fs::remove_dir_all(&dir); } result
}
fn confirm_assets(diary: &Diary, batch_id: &str) -> Result<()> {
    if batch_id.is_empty() { return Ok(()) }
    let dir = batch_path(diary, batch_id)?;
    if !dir.exists() { return Ok(()) } // idempotent after confirmation/abort
    if !dir.join("manifest.json").exists() && fs::read_dir(&dir).map_err(err)?.next().is_none() { return Ok(()) }
    if dir.join("committed").is_file() { return Ok(()) }
    let ids = batch_ids(&dir)?; let mut moved: Vec<(PathBuf,PathBuf)> = vec![];
    let result = (|| {
        for id in &ids {
            let source = dir.join(id); let target = diary.root.join("attachments").join(id);
            if linked(&source) || linked(&target) || !source.is_file() || fs::metadata(&source).map_err(err)?.len() > MAX_ASSET { return Err("图片暂存文件不可用，正文与原有图片已保留".into()) }
            if target.exists() { return Err("图片名称冲突，原有图片未修改".into()) }
        }
        for id in &ids {
            let source = dir.join(id); let target = diary.root.join("attachments").join(id);
            fs::rename(&source, &target).map_err(err)?; moved.push((source,target));
        }
        atomic_write(&dir.join("committed"), b"committed")?;
        Ok(())
    })();
    if let Err(error) = result {
        let mut rollback = vec![];
        for (source,target) in moved.into_iter().rev() { if let Err(e) = fs::rename(target,source) { rollback.push(e.to_string()); } }
        return Err(if rollback.is_empty() {error} else {format!("{error}；本批图片仍保留，恢复暂存失败：{}",rollback.join("；"))});
    }
    // All files are committed. A stale marker is harmless; never abort committed
    // files. Remove the manifest before deleting the now-empty transaction dir.
    let _ = fs::remove_file(dir.join("manifest.json"));
    let _ = fs::remove_file(dir.join("committed"));
    let _ = fs::remove_dir(&dir); Ok(())
}
fn abort_assets(diary: &Diary, batch_id: &str) -> Result<()> {
    if batch_id.is_empty() { return Ok(()) }
    let dir = batch_path(diary,batch_id)?; if !dir.exists() { return Ok(()) }
    if !dir.join("manifest.json").exists() && fs::read_dir(&dir).map_err(err)?.next().is_none() { return Ok(()) }
    if dir.join("committed").is_file() { return Ok(()) }
    let ids = batch_ids(&dir)?;
    for id in ids { let path = dir.join(id); if linked(&path) { return Err("图片暂存文件不能是链接".into()) } if path.exists() { fs::remove_file(path).map_err(err)?; } }
    fs::remove_file(dir.join("manifest.json")).map_err(err)?; fs::remove_dir(dir).map_err(err)?; Ok(())
}
fn pending_asset(diary: &Diary, id: &str) -> Result<Option<(String,PathBuf)>> {
    if !valid_asset_id(id) { return Ok(None) }
    let root = pending_root(diary)?; if !root.exists() { return Ok(None) }
    for entry in fs::read_dir(root).map_err(err)? {
        let entry = entry.map_err(err)?; if linked(&entry.path()) || !entry.path().is_dir() { continue }
        let batch_id = entry.file_name().to_string_lossy().to_string(); if Uuid::parse_str(&batch_id).is_err() { continue }
        let path = entry.path().join(id);
        if path.exists() && batch_ids(&entry.path())?.iter().any(|known| known == id) { return Ok(Some((batch_id,path))) }
    }
    Ok(None)
}
fn confirm_content_assets(diary: &Diary, content: &str) -> Result<()> {
    let pattern = regex::Regex::new(r"diary-asset:([0-9a-fA-F-]+\.(?:png|jpg|jpeg|gif|webp))").map_err(err)?;
    let mut batches = HashSet::new();
    for capture in pattern.captures_iter(content) { if let Some((batch,_)) = pending_asset(diary,&capture[1])? { batches.insert(batch); } }
    for batch in batches { confirm_assets(diary,&batch)?; } Ok(())
}
fn decode_asset(args: &Value) -> Result<(String,Vec<u8>)> {
    let extension = string(args,"extension")?.to_lowercase();
    if !["png","jpg","jpeg","gif","webp"].contains(&extension.as_str()) { return Err("不支持的图片类型".into()) }
    let encoded = string(args,"data")?;
    if encoded.len() as u64 > (MAX_ASSET+2)/3*4 { return Err("图片超过 20 MB，未插入".into()) }
    let bytes = base64::engine::general_purpose::STANDARD.decode(encoded).map_err(err)?;
    if bytes.is_empty() || bytes.len() as u64 > MAX_ASSET { return Err("图片为空或超过 20 MB，未插入".into()) }
    let valid = match extension.as_str() {
        "png"=>bytes.starts_with(b"\x89PNG\r\n\x1a\n"), "jpg"|"jpeg"=>bytes.starts_with(b"\xff\xd8\xff"),
        "gif"=>bytes.starts_with(b"GIF87a")||bytes.starts_with(b"GIF89a"), "webp"=>bytes.starts_with(b"RIFF")&&bytes.get(8..12)==Some(b"WEBP"), _=>false,
    };
    if !valid { return Err("剪贴板内容不是有效图片，未插入".into()) } Ok((extension,bytes))
}
fn read_order(dir: &Path) -> Result<Option<Vec<String>>> {
    let path=dir.join(ITEM_ORDER);
    if linked(&path) { return Err("排序记录不能是链接".into()) }
    if !path.exists() { return Ok(None) }
    if fs::metadata(&path).map_err(err)?.len()>4*1024*1024 { return Err("排序记录过大".into()) }
    let order:Vec<String>=serde_json::from_slice(&fs::read(path).map_err(err)?).map_err(|e|format!("排序记录无法读取，原文件已保留：{e}"))?;
    let mut names=std::collections::HashSet::new();
    if order.len()>20000 { return Err("排序记录超过 20000 项".into()) }
    for name in &order { safe_name(name)?; if !names.insert(name.to_lowercase()) { return Err("排序记录包含重复项目".into()) } }
    Ok(Some(order))
}
fn write_order(dir:&Path, order:&Option<Vec<String>>) -> Result<()> {
    let path=dir.join(ITEM_ORDER);
    if linked(&path) { return Err("排序记录不能是链接".into()) }
    match order { Some(names)=>atomic_write(&path,&serde_json::to_vec(names).map_err(err)?), None=>{if path.exists(){fs::remove_file(path).map_err(err)?} Ok(())} }
}
fn commit_orders(changes:&[(PathBuf,Option<Vec<String>>)]) -> Result<()> {
    let original:Vec<_>=changes.iter().map(|(dir,_)|Ok((dir.clone(),read_order(dir)?))).collect::<Result<_>>()?;
    for (i,(dir,order)) in changes.iter().enumerate() {
        if order == &original[i].1 { continue }
        if let Err(error)=write_order(dir,order) {
            let mut rollback=vec![];
            for j in (0..i).rev() { if original[j].1 != changes[j].1 { if let Err(e)=write_order(&original[j].0,&original[j].1){rollback.push(e)} } }
            return Err(format!("排序保存失败，操作未完成：{error}{}",if rollback.is_empty(){String::new()}else{format!("；恢复排序失败：{}",rollback.join("；"))}));
        }
    }
    Ok(())
}
fn order_with_new(dir:&Path,name:&str) -> Result<Option<Vec<String>>> {
    if read_order(dir)?.is_none() {return Ok(None)}
    let mut names:Vec<String>=walk(dir,"",0,&mut 0)?.iter().map(|n|n["name"].as_str().unwrap().to_owned()).filter(|n|n!=name).collect();
    names.push(name.to_owned());Ok(Some(names))
}
fn read_colors(path: &Path) -> Result<Colors> {
    if linked(path) { return Err("图标颜色文件不能是链接".into()) }
    if !path.exists() { return Ok(Colors::new()) }
    if fs::metadata(path).map_err(err)?.len() > 16 * 1024 * 1024 { return Err("图标颜色记录过大".into()) }
    let colors: Colors = serde_json::from_slice(&fs::read(path).map_err(err)?).map_err(|e| format!("图标颜色记录无法读取，原文件已保留：{e}"))?;
    if colors.len() > 20000 { return Err("图标颜色记录超过 20000 项".into()) }
    for (relative, color) in &colors {
        if relative.is_empty() || relative.contains('\\') || !color.valid() { return Err("无效图标颜色记录".into()) }
        for part in Path::new(relative).components() {
            let Component::Normal(name) = part else { return Err("无效图标颜色路径".into()) };
            safe_name(&name.to_string_lossy())?;
        }
    }
    Ok(colors)
}
fn remap_colors(colors: &Colors, source: &str, destination: &str) -> Colors {
    colors.iter().map(|(path, color)| {
        let next = if path == source || path.starts_with(&(source.to_owned() + "/")) { format!("{destination}{}", &path[source.len()..]) } else { path.clone() };
        (next, color.clone())
    }).collect()
}
fn walk(dir: &Path, relative: &str, depth: usize, count: &mut usize) -> Result<Vec<Value>> {
    walk_inner(dir, relative, depth, count, false)
}
fn walk_inner(dir: &Path, relative: &str, depth: usize, count: &mut usize, recover: bool) -> Result<Vec<Value>> {
    if depth > 64 { return Err("目录层级超过 64 层".into()) }
    let mut values: Vec<Value> = vec![];
    for entry in fs::read_dir(dir).map_err(err)? {
        let entry = entry.map_err(err)?; let path = entry.path();
        if linked(&path) { continue }
        let name = entry.file_name().to_string_lossy().into_owned();
        if safe_name(&name).is_err() { continue }
        let is_dir = path.is_dir();
        if !is_dir && note_extension(&path).is_none() { continue }
        if *count >= 20000 {
            if !recover { return Err("日记目录项目超过 20000 个，请拆分资料库".into()) }
            if let Some(last) = values.last_mut() { last["warning"] = json!("资料库超过 20000 项，部分项目未显示，请整理资料库") }
            break;
        }
        *count += 1;
        let child = if relative.is_empty() { name.clone() } else { format!("{relative}/{name}") };
        let truncated = recover && is_dir && depth >= 64;
        let children = if is_dir && !truncated { walk_inner(&path, &child, depth+1, count, recover)? } else { vec![] };
        let mut value = json!({"name": name, "path": child, "folder": is_dir, "children": children});
        if truncated { value["warning"] = json!("目录超过 64 层，深层内容未展开；文件仍保留，可将此目录移到较浅位置") }
        values.push(value);
    }
    values.sort_by(|a,b| b["folder"].as_bool().cmp(&a["folder"].as_bool()).then_with(|| a["name"].as_str().cmp(&b["name"].as_str())));
    if let Some(order)=read_order(dir)? {
        let ranks:std::collections::HashMap<_,_>=order.iter().enumerate().map(|(i,n)|(n.as_str(),i)).collect();
        values.sort_by_key(|node|ranks.get(node["name"].as_str().unwrap()).copied().unwrap_or(usize::MAX));
    }
    Ok(values)
}
pub(crate) fn atomic_write(path: &Path, bytes: &[u8]) -> Result<()> {
    let temp = path.with_file_name(format!(".write-{}", Uuid::new_v4()));
    let result = (|| {
        let mut file = OpenOptions::new().create_new(true).write(true).open(&temp).map_err(err)?;
        file.write_all(bytes).map_err(err)?; file.sync_all().map_err(err)?; drop(file);
        #[cfg(windows)] {
            use std::os::windows::ffi::OsStrExt;
            use windows_sys::Win32::Storage::FileSystem::{MoveFileExW,MOVEFILE_REPLACE_EXISTING,MOVEFILE_WRITE_THROUGH};
            let from: Vec<u16> = temp.as_os_str().encode_wide().chain(Some(0)).collect();
            let to: Vec<u16> = path.as_os_str().encode_wide().chain(Some(0)).collect();
            if unsafe { MoveFileExW(from.as_ptr(), to.as_ptr(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) } == 0 { return Err(err(std::io::Error::last_os_error())) }
        }
        #[cfg(not(windows))] { fs::rename(&temp, path).map_err(err)?; }
        Ok(())
    })();
    if result.is_err() { let _ = fs::remove_file(&temp); }
    result
}
fn string<'a>(args: &'a Value, key: &str) -> Result<&'a str> { args[key].as_str().ok_or_else(|| format!("缺少参数 {key}")) }
fn flatten<'a>(tree: &'a [Value], paths: &mut Vec<&'a str>) { for node in tree { paths.push(node["path"].as_str().unwrap()); if node["folder"] == true { flatten(node["children"].as_array().unwrap(), paths) } } }
fn flatten_search(tree: &[Value], paths: &mut Vec<(String, bool)>) {
    for node in tree {
        let folder = node["folder"] == true;
        paths.push((node["path"].as_str().unwrap().to_owned(), folder));
        if folder { flatten_search(node["children"].as_array().unwrap(), paths); }
    }
}
fn import_images(diary: &Diary, source: &Path, text: &str, created: &mut Vec<PathBuf>) -> Result<String> {
    let parent = source.parent().ok_or("无效导入位置")?.canonicalize().map_err(err)?;
    // Parent paths are accepted only inside our marked batch-export layout.
    // A marker never permits arbitrary filesystem reads outside attachments.
    let export_assets = parent.ancestors().find_map(|root| {
        let marker = root.join("xiaomang-export.json");
        if linked(&marker) || linked(&root.join("attachments")) { return None }
        let value: Value = serde_json::from_slice(&fs::read(marker).ok()?).ok()?;
        if value["format"] != 1 || value["kind"] != "xiaomang-diary-export" { return None }
        let notes = root.join("notes").canonicalize().ok()?;
        if !source.canonicalize().ok()?.starts_with(notes) { return None }
        root.join("attachments").canonicalize().ok()
    });
    let pattern = regex::Regex::new(r"!\[([^\]]*)\]\(([^)\r\n]+)\)").map_err(err)?;
    let titled = regex::Regex::new(r#"^(.*?)\s+("(?:\\.|[^"])*")$"#).map_err(err)?;
    // Resolve full, collapsed and shortcut references into individual local
    // image candidates. Definitions stay intact for ordinary Markdown links.
    let label = |value: &str| value.split_whitespace().collect::<Vec<_>>().join(" ").to_lowercase();
    let definition = regex::Regex::new(r#"(?m)^ {0,3}\[([^\]\r\n]+)\]:[ \t]*(<[^>\r\n]+>|[^\s\r\n]+)(?:[ \t]+("[^"\r\n]*"|'[^'\r\n]*'|\([^\r\n]*\)))?[ \t]*\r?$"#).map_err(err)?;
    let mut definitions = HashMap::new();
    let mut fenced = Vec::new();
    let mut fence: Option<(char,usize,usize)> = None;let mut offset=0;
    for line in text.split_inclusive('\n') {
        let trimmed=line.trim_start_matches(' ');let spaces=line.len()-trimmed.len();
        let first=trimmed.chars().next().unwrap_or(' ');
        let count=trimmed.chars().take_while(|c|*c==first).count();
        if spaces<=3 && (first=='`'||first=='~') && count>=3 {
            if let Some((mark,length,start))=fence {if mark==first && count>=length && trimmed[count..].trim().is_empty(){fenced.push(start..offset+line.len());fence=None;}}
            else {fence=Some((first,count,offset));}
        }
        offset+=line.len();
    }
    if let Some((_,_,start))=fence {fenced.push(start..text.len());}
    let ticks=regex::Regex::new(r"`+").map_err(err)?;
    let mut endings:HashMap<usize,VecDeque<std::ops::Range<usize>>>=HashMap::new();
    for run in ticks.find_iter(text) {
        if !fenced.iter().any(|r|r.contains(&run.start())) {endings.entry(run.len()).or_default().push_back(run.range());}
    }
    let mut inline=Vec::new();let mut until=0;
    for run in ticks.find_iter(text) {
        if run.start()<until || fenced.iter().any(|r|r.contains(&run.start())){continue}
        let escaped=text[..run.start()].bytes().rev().take_while(|b|*b==b'\\').count()%2==1;
        if escaped{continue}
        if let Some(runs)=endings.get_mut(&run.len()) {
            while runs.front().is_some_and(|r|r.start<=run.start()){runs.pop_front();}
            if let Some(end)=runs.front() {until=end.end;inline.push(run.start()..end.end);}
        }
    }
    let mut excluded=fenced;excluded.extend(inline);let mut offset=0;
    for line in text.split_inclusive('\n') {if line.starts_with("    ")||line.starts_with('\t'){excluded.push(offset..offset+line.len());}offset+=line.len();}
    excluded.sort_by_key(|range|range.start);
    let mut merged:Vec<std::ops::Range<usize>>=Vec::new();
    for range in excluded {if let Some(last)=merged.last_mut().filter(|last|last.end>=range.start){last.end=last.end.max(range.end);}else{merged.push(range);}}
    let in_code=|at:usize| {let index=merged.partition_point(|range|range.start<=at);index>0 && merged[index-1].contains(&at)};
    let escaped=|at:usize|text[..at].bytes().rev().take_while(|b|*b==b'\\').count()%2==1;
    for capture in definition.captures_iter(text) {
        if in_code(capture.get(0).unwrap().start()){continue}
        let title=capture.get(3).map(|m|m.as_str()).unwrap_or("");
        let title=if title.starts_with('"'){title.to_owned()}else if title.len()>=2 {format!("\"{}\"",title[1..title.len()-1].replace('"',"\\\""))}else{String::new()};
        definitions.entry(label(&capture[1])).or_insert((capture[2].to_owned(),title));
    }
    let mut candidates=Vec::new();
    for capture in pattern.captures_iter(text) {
        let range=capture.get(0).unwrap().range();if in_code(range.start)||escaped(range.start){continue}
        let reference=capture[2].trim();let title_match=titled.captures(reference);
        let (url,title)=match &title_match {Some(parts)=>(parts[1].to_owned(),parts[2].to_owned()),None=>(reference.to_owned(),String::new())};
        candidates.push((range,capture[1].to_owned(),url,title));
    }
    let references=regex::Regex::new(r"!\[([^\]\r\n]*)\](?:\[([^\]\r\n]*)\])?").map_err(err)?;
    for capture in references.captures_iter(text) {
        let range=capture.get(0).unwrap().range();
        if in_code(range.start) || escaped(range.start) || text[range.end..].starts_with('('){continue}
        let key=capture.get(2).filter(|m|!m.as_str().is_empty()).map(|m|m.as_str()).unwrap_or(&capture[1]);
        if let Some((url,title))=definitions.get(&label(key)){candidates.push((range,capture[1].to_owned(),url.clone(),title.clone()));}
    }
    candidates.sort_by_key(|(range,_,_,_)|range.start);
    let mut replacements = vec![];
    let mut imported:HashMap<PathBuf,String>=HashMap::new();
    let mut resolved:HashMap<String,String>=HashMap::new();
    for (range,alt,url,title) in candidates {
        let raw = url.trim_start_matches('<').trim_end_matches('>');
        if raw.starts_with("diary-asset:") || raw.contains("://") { continue }
        let suffix = if title.is_empty() { String::new() } else { format!(" {title}") };
        if let Some(id)=resolved.get(raw){replacements.push((range,format!("![{alt}](diary-asset:{id}{suffix})")));continue}
        let mut decoded=vec![];let bytes=raw.as_bytes();let mut index=0;
        while index<bytes.len() {
            if bytes[index]==b'%' && index+2<bytes.len() {
                if let Ok(hex)=std::str::from_utf8(&bytes[index+1..index+3]) {
                    if let Ok(byte)=u8::from_str_radix(hex,16) {decoded.push(byte);index+=3;continue}
                }
            }
            decoded.push(bytes[index]);index+=1;
        }
        let Ok(relative)=String::from_utf8(decoded) else {continue};let local=Path::new(&relative);
        if local.is_absolute() || local.components().any(|p| !matches!(p, Component::Normal(_) | Component::ParentDir)) {continue}
        let has_parent = local.components().any(|p| matches!(p, Component::ParentDir));
        if has_parent && export_assets.is_none() { continue }
        let file=parent.join(local); if !file.is_file() || linked(&file) {continue}
        let canonical=file.canonicalize().map_err(err)?;
        if has_parent { if !canonical.starts_with(export_assets.as_ref().unwrap()) { continue } }
        else if !canonical.starts_with(&parent) { continue }
        let extension=file.extension().and_then(|s|s.to_str()).unwrap_or("").to_lowercase();
        if !["png","jpg","jpeg","gif","webp"].contains(&extension.as_str()) || fs::metadata(&file).map_err(err)?.len()>MAX_ASSET {continue}
        if let Some(id)=imported.get(&canonical) {replacements.push((range,format!("![{alt}](diary-asset:{id}{suffix})")));resolved.insert(raw.to_owned(),id.clone());continue}
        let id=format!("{}.{}",Uuid::new_v4(),extension);let target=diary.root.join("attachments").join(&id);
        let mut output=OpenOptions::new().write(true).create_new(true).open(&target).map_err(err)?;
        created.push(target);
        let mut input=fs::File::open(file).map_err(err)?;
        let bytes=std::io::copy(&mut std::io::Read::take(&mut input,MAX_ASSET+1),&mut output).map_err(err)?;
        if bytes>MAX_ASSET {return Err("图片在读取时超过 20 MB，导入未完成，原文件未修改".into())}
        replacements.push((range,format!("![{alt}](diary-asset:{id}{suffix})")));
        resolved.insert(raw.to_owned(),id.clone());
        imported.insert(canonical,id);
    }
    let final_size=replacements.iter().fold(text.len(),|size,(range,replacement)|size-(range.end-range.start)+replacement.len());
    if final_size as u64 > MAX_NOTE {return Err("转换图片链接后超过 10 MB，请拆分后再导入；原文件未修改".into())}
    let mut content=String::with_capacity(final_size);let mut cursor=0;
    for (range,replacement) in replacements {content.push_str(&text[cursor..range.start]);content.push_str(&replacement);cursor=range.end;}
    content.push_str(&text[cursor..]);Ok(content)
}
fn inspect_tree(source: &Path, count: &mut usize, bytes: &mut u64, limit: usize, depth: usize) -> Result<()> {
    if depth > 128 { return Err("导入/备份目录层级过深".into()) }
    if linked(source) { return Err("导入/备份目录不能包含链接".into()) }
    for entry in fs::read_dir(source).map_err(err)? {
        let entry=entry.map_err(err)?; let path=entry.path();
        if linked(&path) { return Err("导入/备份目录不能包含链接".into()) }
        let name=entry.file_name().to_string_lossy().to_string();
        if ![FOLDER_CONTENT,ITEM_ORDER].contains(&name.as_str()) || !path.is_file() { safe_name(&name)?; }
        *count=count.saturating_add(1); if *count > limit { return Err(format!("此类备份内容超过 {limit} 项，原件未修改")) }
        if path.is_dir() { inspect_tree(&path,count,bytes,limit,depth+1)?; }
        else {
            let size=entry.metadata().map_err(err)?.len(); *bytes=bytes.saturating_add(size);
            if size > MAX_ASSET || *bytes > 1024*1024*1024 { return Err("导入/备份超过大小限制（单文件 20 MB、总计 1 GB）".into()) }
        }
    }
    Ok(())
}
fn copy_tree(source: &Path, destination: &Path, count: &mut usize, bytes: &mut u64) -> Result<()> {
    copy_tree_bounded(source,destination,count,bytes,20000)
}
fn copy_tree_bounded(source: &Path, destination: &Path, count: &mut usize, bytes: &mut u64, limit: usize) -> Result<()> {
    if linked(source) { return Err("导入/备份目录不能包含链接".into()) }
    fs::create_dir_all(destination).map_err(err)?;
    for entry in fs::read_dir(source).map_err(err)? {
        let entry = entry.map_err(err)?; let path = entry.path();
        if linked(&path) { return Err("导入/备份目录不能包含链接".into()) }
        let name = entry.file_name().to_string_lossy().into_owned();
        if ![FOLDER_CONTENT,ITEM_ORDER].contains(&name.as_str()) || !path.is_file() { safe_name(&name)?; }
        *count += 1; if *count > limit { return Err(format!("此类备份内容超过 {limit} 项")) }
        let target = destination.join(name);
        if path.is_dir() { copy_tree_bounded(&path, &target, count, bytes, limit)?; } else {
            let size = entry.metadata().map_err(err)?.len(); *bytes += size;
            if size > MAX_ASSET || *bytes > 1024 * 1024 * 1024 { return Err("导入/备份超过大小限制（单文件 20 MB、总计 1 GB）".into()) }
            fs::copy(path, target).map_err(err)?;
        }
    } Ok(())
}
fn encoded_url_segment(segment: &str) -> String {
    segment.chars().map(|character| if !character.is_ascii() || character.is_ascii_alphanumeric() || "-._~".contains(character) {
        character.to_string()
    } else { format!("%{:02X}", character as u8) }).collect()
}
pub fn dispatch(root: &Path, op: &str, args: Value) -> Result<Value> {
    if op == "search_cancel" { return cancel_search(root, string(&args, "requestId")?) }
    if op == "search" {
        let id = args["requestId"].as_str().map(str::to_owned).unwrap_or_else(|| Uuid::new_v4().to_string());
        let permit = begin_search(root, &id)?;
        return dispatch_search(root, args, permit);
    }
    let _guard = IO_LOCK.get_or_init(|| Mutex::new(())).lock().map_err(|_| "日记存储锁不可用")?;
    let diary = Diary::open(root.to_path_buf())?;
    match op {
        "init" => { let tree=diary.tree_mode(true)?; activity::optional(activity::sync(&diary,&tree)); Ok(json!({"root": diary.root, "tree": tree, "storageWarning": diary.storage_warning()})) },
        "tree" => { let tree=diary.tree_mode(true)?; activity::optional(activity::sync(&diary,&tree)); Ok(json!(tree)) },
        "visit" => { activity::visit(&diary,string(&args,"path")?)?; Ok(json!(true)) },
        "reorder" => {
            let relative=string(&args,"path")?;
            let direction=args["direction"].as_i64().filter(|n|*n == -1 || *n == 1).ok_or("无效排序方向")?;
            let path=diary.note_path(relative,false)?; let parent=path.parent().unwrap();
            let siblings=walk(parent,"",0,&mut 0)?;
            let mut names:Vec<String>=siblings.iter().map(|n|n["name"].as_str().unwrap().to_owned()).collect();
            let index=names.iter().position(|n|Some(n.as_str())==path.file_name().and_then(|s|s.to_str())).ok_or("项目已不存在，请刷新后重试")?;
            let next=index as i64+direction;
            if next<0 || next>=names.len() as i64 {return Ok(json!(false))}
            names.swap(index,next as usize);
            commit_orders(&[(parent.to_path_buf(),Some(names))])?;
            Ok(json!(true))
        }
        "read" => { let content = diary.text(string(&args, "path")?)?; Ok(json!({"revision": digest(&content), "content": content})) }
        "save" => Ok(json!({"revision": diary.save(string(&args,"path")?,string(&args,"content")?,string(&args,"revision")?)?})),
        "create" => {
            let parent = string(&args,"parent")?; let mut name = string(&args,"name")?.to_owned();
            let folder = args["folder"].as_bool().unwrap_or(false);
            if !folder {
                let format=args["format"].as_str().unwrap_or("md");
                if !["md","txt"].contains(&format) {return Err("请选择 MD 或 TXT".into())}
                let suffix=format!(".{format}");
                if !name.to_lowercase().ends_with(&suffix) {name.push_str(&suffix)}
            }
            safe_name(&name)?; let parent_path = diary.note_path(parent, true)?;
            if !parent_path.is_dir() { return Err("父目录不存在".into()) }
            diary.validate_addition(parent, folder, 1)?;
            let path = parent_path.join(&name);
            if path.exists() { return Err("同名文件或目录已经存在".into()) }
            let next_order=order_with_new(&parent_path,&name)?;
            if folder { fs::create_dir(&path).map_err(err)?; } else { OpenOptions::new().create_new(true).write(true).open(&path).map_err(err)?; }
            if let Some(order)=next_order {if let Err(error)=commit_orders(&[(parent_path,Some(order))]) {if folder {fs::remove_dir(&path).map_err(err)?}else{fs::remove_file(&path).map_err(err)?}return Err(error)}}
            Ok(json!(if parent.is_empty() { name } else { format!("{parent}/{name}") }))
        }
        "duplicate" => diary.duplicate(string(&args,"path")?,string(&args,"name")?),
        "rename" => {
            let relative = string(&args,"path")?; let name = string(&args,"name")?; safe_name(name)?;
            let source = diary.note_path(relative,false)?;
            if source.is_file() && (note_extension(&source).is_none() || note_extension(&source)!=note_extension(Path::new(name))) { return Err("重命名须保留原文件格式".into()) }
            let parent = relative.rsplit_once('/').map(|(p,_)| p).unwrap_or("");
            let next = if parent.is_empty() { name.to_string() } else { format!("{parent}/{name}") };
            diary.relocate(relative, &next)
        }
        "move" => {
            let relative = string(&args, "path")?; let parent = string(&args, "parent")?;
            let source = diary.note_path(relative, false)?;
            let destination = diary.note_path(parent, true)?;
            if !destination.is_dir() { return Err("目标目录不存在".into()) }
            if source.is_dir() && (relative.eq_ignore_ascii_case(parent) || parent.to_lowercase().starts_with(&(relative.to_lowercase() + "/"))) { return Err("不能移动到自身或子目录".into()) }
            let name = source.file_name().unwrap().to_string_lossy();
            let next = if parent.is_empty() { name.to_string() } else { format!("{parent}/{name}") };
            diary.relocate(relative, &next)
        }
        "set_color" => {
            let relative = string(&args, "path")?; let color = string(&args, "color")?;
            if !diary.note_path(relative, false)?.exists() { return Err("文件或目录已不存在".into()) }
            if !color.is_empty() && !ICON_COLORS.contains(&color) { return Err("请选择支持的图标颜色".into()) }
            let solid = match args.get("solid") { None => false, Some(value) => value.as_bool().ok_or("请选择支持的图标样式")? };
            let mut colors = diary.colors()?;
            if color.is_empty() && !solid { colors.remove(relative); }
            else { colors.insert(relative.to_string(), if solid { IconAppearance::Styled { color: color.to_string(), solid } } else { color.into() }); }
            diary.write_colors(&colors)?; Ok(json!(true))
        }
        "batch_move" | "batch_delete" | "batch_color" | "batch_export" => batch::execute(&diary,op,&args),
        "delete" => { diary.remove(string(&args,"path")?)?; Ok(json!(true)) }
        "trash" => {
            let mut items = vec![];
            for entry in fs::read_dir(diary.root.join("trash")).map_err(err)? {
                let path=entry.map_err(err)?.path(); if linked(&path) || !path.join("content").exists() {continue}
                if let Ok(data)=fs::read_to_string(path.join("entry.json")) { if let Ok(value)=serde_json::from_str::<Value>(&data) { items.push(value) } }
            }
            items.sort_by(|a,b| b["time"].as_u64().cmp(&a["time"].as_u64())); Ok(json!(items))
        }
        "restore" => {
            let id=string(&args,"id")?; Uuid::parse_str(id).map_err(err)?;
            let trash=diary.root.join("trash").join(id); if linked(&trash) {return Err("无效回收站路径".into())}
            let entry:Value=serde_json::from_str(&fs::read_to_string(trash.join("entry.json")).map_err(err)?).map_err(err)?;
            let path=diary.note_path(string(&entry,"path")?,false)?;
            if path.exists() {return Err("原位置已有同名文件，请先重命名该文件".into())}
            fs::create_dir_all(path.parent().unwrap()).map_err(err)?;
            let mut colors=diary.colors()?;
            let restored:Colors=serde_json::from_value(entry.get("colors").cloned().unwrap_or(json!({}))).map_err(err)?;
            for (relative,color) in &restored { let _=diary.note_path(relative,false)?; if !color.valid(){return Err("无效回收站颜色".into())} }
            colors.extend(restored.clone());
            let next_order=order_with_new(path.parent().unwrap(),&path.file_name().unwrap().to_string_lossy())?;
            fs::rename(trash.join("content"),&path).map_err(err)?;
            let old_order=read_order(path.parent().unwrap())?;
            if let Some(order)=next_order {if let Err(error)=commit_orders(&[(path.parent().unwrap().to_path_buf(),Some(order))]){fs::rename(&path,trash.join("content")).map_err(err)?;return Err(error)}}
            if !restored.is_empty() { if let Err(error)=diary.write_colors(&colors) { commit_orders(&[(path.parent().unwrap().to_path_buf(),old_order)])?;fs::rename(&path,trash.join("content")).map_err(err)?;return Err(error) } }
            Ok(json!(true))
        }
        "asset" => {
            let source=PathBuf::from(string(&args,"source")?);
            let extension=source.extension().and_then(|s|s.to_str()).unwrap_or("").to_lowercase();
            if !["png","jpg","jpeg","gif","webp"].contains(&extension.as_str()) {return Err("仅支持 PNG/JPG/GIF/WebP 图片".into())}
            if linked(&source) || fs::metadata(&source).map_err(err)?.len()>MAX_ASSET {return Err("图片过大或路径不受支持".into())}
            let id=format!("{}.{}",Uuid::new_v4(),extension); fs::copy(&source,diary.root.join("attachments").join(&id)).map_err(err)?;
            Ok(json!(id))
        }
        "asset_batch_begin" => {
            let items = args["items"].as_array().ok_or("无效图片批次")?;
            begin_assets(&diary, items.iter().map(decode_asset))
        }
        "asset_batch_confirm" => { confirm_assets(&diary,string(&args,"batchId")?)?; Ok(json!(true)) }
        "asset_batch_abort" => { abort_assets(&diary,string(&args,"batchId")?)?; Ok(json!(true)) }
        "clipboard_image_batch_begin" => {
            let paths = crate::clipboard_files::images()?;
            begin_assets(&diary, paths.into_iter().map(|source| {
                let metadata = fs::metadata(&source).map_err(err)?;
                if linked(&source) || !metadata.is_file() || metadata.len() == 0 || metadata.len() > MAX_ASSET { return Err("图片为空、超过 20 MB 或路径不受支持".into()) }
                let extension = source.extension().and_then(|ext|ext.to_str()).ok_or("无效图片类型")?.to_lowercase();
                if !["png","jpg","jpeg","gif","webp"].contains(&extension.as_str()) { return Err("不支持的图片类型".into()) }
                Ok((extension,fs::read(source).map_err(err)?))
            }))
        }
        "clipboard_image_files" => {
            let paths = crate::clipboard_files::images()?;
            // Validate the whole batch before creating any attachments.
            for source in &paths {
                let metadata = fs::metadata(source).map_err(err)?;
                if linked(source) || !metadata.is_file() || metadata.len() == 0 || metadata.len() > MAX_ASSET { return Err("图片为空、超过 20 MB 或路径不受支持".into()) }
            }
            let mut ids = vec![];
            for source in paths {
                let extension = source.extension().and_then(|e| e.to_str()).unwrap().to_lowercase();
                let id = format!("{}.{}", Uuid::new_v4(), extension);
                atomic_write(&diary.root.join("attachments").join(&id), &fs::read(source).map_err(err)?)?;
                ids.push(id);
            }
            Ok(json!(ids))
        }
        "asset_bytes" => {
            let extension = string(&args, "extension")?.to_lowercase();
            if !["png", "jpg", "jpeg", "gif", "webp"].contains(&extension.as_str()) { return Err("不支持的图片类型".into()) }
            let encoded = string(&args, "data")?;
            if encoded.len() as u64 > (MAX_ASSET + 2) / 3 * 4 { return Err("图片超过 20 MB，未插入".into()) }
            let bytes = base64::engine::general_purpose::STANDARD.decode(encoded).map_err(err)?;
            if bytes.is_empty() || bytes.len() as u64 > MAX_ASSET { return Err("图片为空或超过 20 MB，未插入".into()) }
            let image = match extension.as_str() {
                "png" => bytes.starts_with(b"\x89PNG\r\n\x1a\n"),
                "jpg" | "jpeg" => bytes.starts_with(b"\xff\xd8\xff"),
                "gif" => bytes.starts_with(b"GIF87a") || bytes.starts_with(b"GIF89a"),
                "webp" => bytes.starts_with(b"RIFF") && bytes.get(8..12) == Some(b"WEBP"),
                _ => false,
            };
            if !image { return Err("剪贴板内容不是有效图片，未插入".into()) }
            let id = format!("{}.{}", Uuid::new_v4(), extension);
            atomic_write(&diary.root.join("attachments").join(&id), &bytes)?;
            Ok(json!(id))
        }
        "asset_data" => {
            let id=string(&args,"id")?; safe_name(id)?;
            let mut path=diary.root.join("attachments").join(id);
            if !path.exists() { path=pending_asset(&diary,id)?.map(|(_,path)|path).ok_or("图片不存在")?; }
            if linked(&path) || fs::metadata(&path).map_err(err)?.len()>MAX_ASSET {return Err("无效图片".into())}
            let ext=path.extension().and_then(|s|s.to_str()).unwrap_or("");
            let mime=match ext {"jpg"|"jpeg"=>"image/jpeg","png"=>"image/png","gif"=>"image/gif","webp"=>"image/webp",_=>return Err("不支持的图片类型".into())};
            Ok(json!(format!("data:{mime};base64,{}",base64::engine::general_purpose::STANDARD.encode(fs::read(path).map_err(err)?))))
        }
        "import_encoding" => {
            let source=PathBuf::from(string(&args,"source")?);
            if linked(&source) || !source.is_file() || fs::metadata(&source).map_err(err)?.len()>MAX_NOTE || note_extension(&source).is_none() {return Err("请选择不超过 10 MB 的 MD/TXT 文件".into())}
            Ok(json!({"encoding":detected_encoding(&fs::read(source).map_err(err)?)}))
        }
        "import" => {
            let source=PathBuf::from(string(&args,"source")?); if linked(&source) || fs::metadata(&source).map_err(err)?.len()>MAX_NOTE {return Err("导入文件过大或路径不受支持".into())}
            let extension=note_extension(&source).ok_or("只支持 MD 或 TXT 文件")?;
            let bytes=fs::read(&source).map_err(err)?;
            let encoding=args["encoding"].as_str().or_else(||detected_encoding(&bytes)).ok_or("无法确定编码，请选择编码后导入，原文件未修改")?;
            let text=decode_text(&bytes,encoding)?;
            if text.len() as u64 > MAX_NOTE { return Err("转为 UTF-8 后超过 10 MB，请拆分后再导入；原文件未修改".into()) }
            let parent=diary.note_path(string(&args,"parent")?,true)?;
            diary.validate_addition(string(&args,"parent")?, false, 1)?;
            let name=source.file_stem().unwrap().to_string_lossy(); safe_name(&name)?;
            let mut target=parent.join(format!("{name}.{extension}")); if target.exists(){target=parent.join(format!("{name}-{}.{extension}",stamp()))}
            let next_order=order_with_new(&parent,&target.file_name().unwrap().to_string_lossy())?;
            let mut created=vec![];
            let result=(|| {
                let content=if extension=="md" {import_images(&diary,&source,&text,&mut created)?} else {text};
                if content.len() as u64 > MAX_NOTE {return Err("转换图片链接后超过 10 MB，请拆分后再导入；原文件未修改".into())}
                atomic_write(&target,content.as_bytes())?;
                if let Some(order)=next_order {if let Err(error)=commit_orders(&[(parent,Some(order))]) {fs::remove_file(&target).map_err(err)?;return Err(error)}}
                Ok(())
            })();
            if let Err(error)=result {for file in created {let _=fs::remove_file(file);}return Err(error)}
            Ok(json!(target.strip_prefix(diary.root.join("notes")).map_err(err)?.to_string_lossy().replace('\\',"/")))
        }
        "export" => {
            let content=string(&args,"content")?; let destination=PathBuf::from(string(&args,"destination")?);
            let format=args["format"].as_str().unwrap_or("md");
            if !destination.is_absolute() || linked(&destination) || note_extension(&destination)!=Some(format) {return Err("请导出为与笔记相同的 MD/TXT 格式".into())}
            if destination.starts_with(&diary.root) {return Err("请导出到日记数据目录之外，避免覆盖原件".into())}
            let parent=destination.parent().ok_or("无效导出位置")?.canonicalize().map_err(err)?;
            if parent.starts_with(&diary.root) {return Err("请选择日记数据目录之外的导出位置".into())}
            if format=="txt" {atomic_write(&destination,content.as_bytes())?;return Ok(json!(true))}
            let asset_dir=format!("{}.assets",destination.file_stem().unwrap().to_string_lossy());
            let asset_path=parent.join(&asset_dir);if linked(&asset_path){return Err("导出图片目录不能是链接".into())}
            let mut exported=content.to_string();
            for entry in fs::read_dir(diary.root.join("attachments")).map_err(err)? {
                let entry=entry.map_err(err)?;let name=entry.file_name().to_string_lossy().into_owned();
                let reference=format!("diary-asset:{name}");
                if exported.contains(&reference) {
                    if linked(&entry.path()){return Err("无效图片附件".into())}
                    fs::create_dir_all(&asset_path).map_err(err)?;
                    let target=asset_path.join(&name); if linked(&target){return Err("导出图片不能是链接".into())}
                    if target.exists() && fs::read(&target).map_err(err)?!=fs::read(entry.path()).map_err(err)? {return Err("导出图片已存在且内容不同，原文件已保留".into())}
                    fs::copy(entry.path(),target).map_err(err)?;
                    exported=exported.replace(&reference,&format!("{}/{name}",encoded_url_segment(&asset_dir)));
                }
            }
            atomic_write(&destination,exported.as_bytes())?; Ok(json!(true))
        }
        "backup" => {
            let parent=PathBuf::from(string(&args,"destination")?); let parent=parent.canonicalize().map_err(err)?;
            if parent.starts_with(&diary.root) {return Err("请选择日记数据目录之外的备份位置".into())}
            // The 20,000 user-visible items do not share a quota with auxiliary
            // images/history/trash or each folder's internal metadata files.
            diary.tree()?;
            let mut checked_bytes=0;
            for dir in ["notes","attachments","history","trash"] {
                inspect_tree(&diary.root.join(dir),&mut 0,&mut checked_bytes,if dir=="notes" {60000} else {100000},0)?;
            }
            let target=parent.join(format!("小蟒日记备份-{}-{}",stamp(),&Uuid::new_v4().to_string()[..8]));
            fs::create_dir(&target).map_err(err)?;
            let mut bytes=0;
            let result=(|| { for dir in ["notes","attachments","history","trash"] { copy_tree_bounded(&diary.root.join(dir),&target.join(dir),&mut 0,&mut bytes,if dir=="notes" {60000} else {100000})?; } let colors=diary.colors()?; atomic_write(&target.join("item-colors.json"),&serde_json::to_vec(&colors).map_err(err)?)?; atomic_write(&target.join("xiaomang-diary.json"),b"{\"format\":1}")?; Ok(json!(target)) })();
            if result.is_err() {let _=fs::remove_dir_all(&target);} result
        }
        "import_backup" => backup_import::execute(&diary, &PathBuf::from(string(&args,"source")?)),
        "storage_warning" => Ok(json!(diary.storage_warning())),

        _ => Err("未知的本地日记操作".into()),
    }
}

#[cfg(test)]
#[path = "diary_search_tests.rs"]
mod search_tests;

#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn import_checks_decoded_utf8_size_before_writing() {
        let root=fixture();let outside=fixture();let source=outside.join("中文.txt");
        let mut bytes=Vec::with_capacity(8*1024*1024);
        for _ in 0..4*1024*1024 {bytes.extend_from_slice(&[0x2d,0x4e]);}
        fs::write(&source,&bytes).unwrap();
        let error=dispatch(&root,"import",json!({"source":source,"parent":"","encoding":"utf16le"})).unwrap_err();
        assert!(error.contains("UTF-8"));assert_eq!(fs::read_dir(root.join("notes")).unwrap().count(),0);
        assert_eq!(fs::metadata(&source).unwrap().len(),8*1024*1024);
        fs::remove_dir_all(root).unwrap();fs::remove_dir_all(outside).unwrap();
    }
    #[test] fn import_reference_images_preserves_definitions_and_sandbox() {
        let root=fixture();let outside=fixture();fs::create_dir(outside.join("images")).unwrap();
        fs::write(outside.join("images/logo.png"),b"fixture-image").unwrap();
        let source=outside.join("references.md");
        let text="![One][LOGO]\n![logo][]\n![logo]\n![remote][web]\n![bad][escape]\n`![literal][logo]`\n\\![escaped][logo]\n```md\n![code][logo]\n```\n\n[logo]: images/logo.png \"title\"\n[web]: https://example.invalid/image.png\n[escape]: ../private.png\n";
        fs::write(&source,text).unwrap();
        let path=call(&root,"import",json!({"source":source,"parent":""}));
        let read=call(&root,"read",json!({"path":path}));let imported=read["content"].as_str().unwrap();
        assert_eq!(imported.matches("diary-asset:").count(),3);
        assert!(imported.contains("[logo]: images/logo.png"));assert!(imported.contains("![remote][web]"));assert!(imported.contains("![bad][escape]"));assert!(imported.contains("![code][logo]"));assert!(imported.contains("`![literal][logo]`"));assert!(imported.contains("\\![escaped][logo]"));
        assert_eq!(fs::read_dir(root.join("attachments")).unwrap().count(),1);
        assert_eq!(fs::read_to_string(&source).unwrap(),text);
        fs::remove_dir_all(root).unwrap();fs::remove_dir_all(outside).unwrap();
    }
    #[test] fn oversized_image_rewrite_rolls_back_only_new_attachments() {
        let root=fixture();let outside=fixture();fs::write(root.join("attachments/existing.png"),b"keep").unwrap();
        fs::write(outside.join("a.png"),b"fixture-image").unwrap();let source=outside.join("many.md");
        fs::write(&source,"![a](a.png) ".repeat(240000)).unwrap();
        let error=dispatch(&root,"import",json!({"source":source,"parent":""})).unwrap_err();assert!(error.contains("10 MB"));
        assert_eq!(fs::read_dir(root.join("notes")).unwrap().count(),0);assert_eq!(fs::read_dir(root.join("attachments")).unwrap().count(),1);
        assert_eq!(fs::read(root.join("attachments/existing.png")).unwrap(),b"keep");
        fs::remove_dir_all(root).unwrap();fs::remove_dir_all(outside).unwrap();
    }
    #[test] fn new_folder_depth_is_checked_before_write_and_old_overflow_is_readable() {
        let root=fixture(); let mut parent=String::new();
        for _ in 0..64 {parent=if parent.is_empty(){"d".into()}else{format!("{parent}/d")};fs::create_dir(root.join("notes").join(&parent)).unwrap();}
        assert!(dispatch(&root,"create",json!({"parent":parent,"name":"blocked","folder":true})).is_err());
        assert!(!root.join("notes").join(&parent).join("blocked").exists());
        fs::create_dir(root.join("notes").join(&parent).join("legacy-overflow")).unwrap();
        let view=call(&root,"init",json!({}));
        assert!(view["tree"].to_string().contains("warning"));
        assert!(root.join("notes").join(&parent).join("legacy-overflow").exists());
        fs::remove_dir_all(root).unwrap();
    }
    #[test] fn new_note_count_is_checked_before_write_and_overflow_tree_is_recoverable() {
        let root=fixture();
        for i in 0..20000 {fs::write(root.join("notes").join(format!("{i}.txt")),"").unwrap();}
        assert!(dispatch(&root,"create",json!({"parent":"","name":"blocked","format":"txt"})).is_err());
        assert!(!root.join("notes/blocked.txt").exists());
        fs::write(root.join("notes/legacy-overflow.txt"),"keep").unwrap();
        let view=call(&root,"init",json!({}));assert_eq!(view["tree"].as_array().unwrap().len(),20000);
        assert!(view["tree"].as_array().unwrap().iter().any(|node|node.get("warning").is_some()));
        assert_eq!(fs::read_to_string(root.join("notes/legacy-overflow.txt")).unwrap(),"keep");
        fs::remove_dir_all(root).unwrap();
    }
    #[test] fn exported_special_names_roundtrip_images_without_url_fragment_loss() {
        let root=fixture();let outside=fixture();
        fs::write(root.join("attachments/sample.png"),b"image-data").unwrap();
        let destination=outside.join("日记#100%.md");
        call(&root,"export",json!({"destination":destination,"content":"![图](diary-asset:sample.png)"}));
        let exported=fs::read_to_string(&destination).unwrap();
        assert!(exported.contains("%23")&&exported.contains("%25"));
        let path=call(&root,"import",json!({"source":destination,"parent":""}));
        assert!(call(&root,"read",json!({"path":path}))["content"].as_str().unwrap().contains("diary-asset:"));
        assert_eq!(fs::read(root.join("attachments/sample.png")).unwrap(),b"image-data");
        fs::remove_dir_all(root).unwrap();fs::remove_dir_all(outside).unwrap();
    }
    #[test] fn batch_export_images_can_be_reimported_but_cannot_escape_marked_assets() {
        let root=fixture();let outside=fixture();
        call(&root,"create",json!({"parent":"","name":"目录","folder":true}));
        call(&root,"create",json!({"parent":"目录","name":"日记"}));
        fs::write(root.join("notes/目录/日记.md"),"![图](diary-asset:sample.png)").unwrap();
        fs::write(root.join("attachments/sample.png"),b"image-data").unwrap();
        let exported=call(&root,"batch_export",json!({"paths":["目录"],"destination":outside}));
        let exported_root=PathBuf::from(exported["destination"].as_str().unwrap());
        let source=exported_root.join("notes/目录/日记.md");
        let imported=call(&root,"import",json!({"source":source,"parent":""}));
        assert!(call(&root,"read",json!({"path":imported}))["content"].as_str().unwrap().contains("diary-asset:"));
        fs::write(exported_root.join("private.png"),b"private").unwrap();
        fs::write(&source,"![private](../../private.png)").unwrap();
        let before=fs::read_dir(root.join("attachments")).unwrap().count();
        let imported=call(&root,"import",json!({"source":source,"parent":""}));
        assert!(!call(&root,"read",json!({"path":imported}))["content"].as_str().unwrap().contains("diary-asset:"));
        assert_eq!(fs::read_dir(root.join("attachments")).unwrap().count(),before);
        fs::remove_dir_all(root).unwrap();fs::remove_dir_all(outside).unwrap();
    }
    #[test] fn txt_lifecycle_and_markdown_coexist_without_conversion() {
        let root=fixture();
        call(&root,"create",json!({"parent":"","name":"同名","format":"txt"}));
        call(&root,"create",json!({"parent":"","name":"同名"}));
        assert_eq!(names(&root,""),["同名.md","同名.txt"]);
        let content="# 纯文本\r\n**不要格式化** ![图](diary-asset:no.png)\r\n中文\t测试";
        let read=call(&root,"read",json!({"path":"同名.txt"}));
        call(&root,"save",json!({"path":"同名.txt","content":content,"revision":read["revision"]}));
        assert_eq!(fs::read_to_string(root.join("notes/同名.txt")).unwrap(),content);
        assert!(fs::read_dir(root.join("history").join(digest("同名.txt"))).unwrap().all(|e|note_extension(&e.unwrap().path())==Some("txt")));
        assert_eq!(call(&root,"search",json!({"query":"不要格式化"}))["results"][0]["path"],"同名.txt");
        assert!(dispatch(&root,"rename",json!({"path":"同名.txt","name":"转换.md"})).is_err());
        call(&root,"rename",json!({"path":"同名.txt","name":"新名.TXT"}));
        call(&root,"create",json!({"parent":"","name":"目录","folder":true}));
        call(&root,"move",json!({"path":"新名.TXT","parent":"目录"}));
        call(&root,"delete",json!({"path":"目录/新名.TXT"}));
        let trash=call(&root,"trash",json!({}));call(&root,"restore",json!({"id":trash[0]["id"]}));
        assert_eq!(call(&root,"read",json!({"path":"目录/新名.TXT"}))["content"],content);
        let outside=fixture();let output=outside.join("导出.txt");
        call(&root,"export",json!({"destination":output,"content":content,"format":"txt"}));
        assert_eq!(fs::read_to_string(&output).unwrap(),content);assert!(!outside.join("导出.assets").exists());
        let imported=call(&root,"import",json!({"source":output,"parent":""}));assert_eq!(imported,"导出.txt");
        assert_eq!(call(&root,"read",json!({"path":imported}))["content"],content);
        let backup=call(&root,"backup",json!({"destination":outside}));let target=fixture();
        let restored=call(&target,"import_backup",json!({"source":backup}));
        assert_eq!(fs::read_to_string(target.join("notes").join(restored.as_str().unwrap()).join("目录/新名.TXT")).unwrap(),content);
        fs::remove_dir_all(root).unwrap();fs::remove_dir_all(outside).unwrap();fs::remove_dir_all(target).unwrap();
    }
    #[test] fn txt_encoding_detection_is_strict_and_import_preserves_original() {
        let root=fixture();let outside=fixture();let source=outside.join("中文.txt");
        fs::write(&source,[0xd6,0xd0,0xce,0xc4]).unwrap();
        assert!(call(&root,"import_encoding",json!({"source":source}))["encoding"].is_null());
        assert!(dispatch(&root,"import",json!({"source":source,"parent":""})).is_err());
        assert!(names(&root,"").is_empty());
        assert!(dispatch(&root,"import",json!({"source":source,"parent":"","encoding":"utf8"})).is_err());
        #[cfg(windows)] {let imported=call(&root,"import",json!({"source":source,"parent":"","encoding":"gbk"}));assert_eq!(call(&root,"read",json!({"path":imported}))["content"],"中文");}
        assert_eq!(fs::read(&source).unwrap(),[0xd6,0xd0,0xce,0xc4]);
        for (label,bom,little) in [("LE",[0xff,0xfe],true),("BE",[0xfe,0xff],false)] {
            let source=outside.join(format!("{label}.txt"));let mut bytes=bom.to_vec();
            for word in "中文\r\n标题".encode_utf16(){bytes.extend(if little {word.to_le_bytes()}else{word.to_be_bytes()})}
            fs::write(&source,&bytes).unwrap();let imported=call(&root,"import",json!({"source":source,"parent":""}));
            assert_eq!(call(&root,"read",json!({"path":imported}))["content"],"中文\r\n标题");assert_eq!(fs::read(&source).unwrap(),bytes);
        }
        assert!(decode_text(&[0xff,0xfe,0x00],"utf16le").is_err());
        assert!(decode_text(&[0xff,0xfe,0x00,0xd8],"utf16le").is_err());
        assert!(decode_text(b"binary\0content","utf8").is_err());assert!(decode_text(b"hi","guess").is_err());
        fs::remove_dir_all(root).unwrap();fs::remove_dir_all(outside).unwrap();
    }
    #[test] fn txt_conflicts_are_preserved_and_unsupported_formats_rejected() {
        let root=fixture();call(&root,"create",json!({"parent":"","name":"外改","format":"txt"}));
        let read=call(&root,"read",json!({"path":"外改.txt"}));fs::write(root.join("notes/外改.txt"),"外部修改").unwrap();
        assert!(dispatch(&root,"save",json!({"path":"外改.txt","content":"编辑器内容","revision":read["revision"]})).is_err());
        assert_eq!(fs::read_to_string(root.join("notes/外改.txt")).unwrap(),"外部修改");
        assert!(dispatch(&root,"create",json!({"parent":"","name":"坏格式","format":"rtf"})).is_err());
        assert!(dispatch(&root,"read",json!({"path":"../outside.txt"})).is_err());
        fs::remove_dir_all(root).unwrap();
    }
    fn names(root:&Path,parent:&str)->Vec<String> {
        let tree=call(root,"tree",json!({}));let nodes=if parent.is_empty(){&tree}else{&tree.as_array().unwrap().iter().find(|n|n["path"]==parent).unwrap()["children"]};
        nodes.as_array().unwrap().iter().map(|n|n["name"].as_str().unwrap().to_owned()).collect()
    }
    #[test] fn mixed_order_boundaries_persistence_and_new_items() {
        let root=fixture();
        for (name,folder) in [("目录",true),("A",false),("B",false)] {call(&root,"create",json!({"parent":"","name":name,"folder":folder}));}
        assert_eq!(names(&root,""),["目录","A.md","B.md"]);
        assert_eq!(call(&root,"reorder",json!({"path":"目录","direction":-1})),false);
        assert!(!root.join("notes").join(ITEM_ORDER).exists());
        call(&root,"reorder",json!({"path":"A.md","direction":-1}));
        assert_eq!(names(&root,""),["A.md","目录","B.md"]);
        assert_eq!(call(&root,"reorder",json!({"path":"B.md","direction":1})),false);
        call(&root,"create",json!({"parent":"","name":"Z","folder":false}));call(&root,"create",json!({"parent":"","name":"C","folder":true}));
        assert_eq!(names(&root,""),["A.md","目录","B.md","Z.md","C"]);
        call(&root,"rename",json!({"path":"A.md","name":"改名.md"}));
        assert_eq!(names(&root,""),["改名.md","目录","B.md","Z.md","C"]);
        assert!(dispatch(&root,"reorder",json!({"path":"../private.md","direction":1})).is_err());
        assert!(dispatch(&root,"reorder",json!({"path":"改名.md","direction":2})).is_err());
        fs::remove_dir_all(root).unwrap();
    }
    #[test] fn order_follows_move_trash_and_backup() {
        let root=fixture();let target=fixture();let outside=fixture();
        call(&root,"create",json!({"parent":"","name":"目录","folder":true}));
        for name in ["A","B"] {call(&root,"create",json!({"parent":"目录","name":name,"folder":false}));}
        call(&root,"reorder",json!({"path":"目录/B.md","direction":-1}));
        call(&root,"create",json!({"parent":"","name":"C","folder":false}));
        call(&root,"reorder",json!({"path":"C.md","direction":-1}));
        call(&root,"move",json!({"path":"C.md","parent":"目录"}));
        assert_eq!(names(&root,"目录"),["B.md","A.md","C.md"]);
        call(&root,"delete",json!({"path":"目录/B.md"}));
        call(&root,"create",json!({"parent":"目录","name":"B","folder":false}));
        assert_eq!(names(&root,"目录"),["A.md","C.md","B.md"]);
        call(&root,"reorder",json!({"path":"目录/B.md","direction":-1}));
        call(&root,"delete",json!({"path":"目录"}));
        let trash=call(&root,"trash",json!({}));let entry=trash.as_array().unwrap().iter().find(|e|e["path"]=="目录").unwrap();
        call(&root,"restore",json!({"id":entry["id"]}));assert_eq!(names(&root,"目录"),["A.md","B.md","C.md"]);
        let backup=call(&root,"backup",json!({"destination":outside}));let imported=call(&target,"import_backup",json!({"source":backup}));
        let tree=call(&target,"tree",json!({}));let folder=&tree[0]["children"][0];assert_eq!(folder["path"],format!("{}/目录",imported.as_str().unwrap()));
        assert_eq!(folder["children"].as_array().unwrap().iter().map(|n|n["name"].as_str().unwrap()).collect::<Vec<_>>(),["A.md","B.md","C.md"]);
        for dir in [root,target,outside]{fs::remove_dir_all(dir).unwrap();}
    }
    #[test] fn corrupt_order_blocks_mutation_and_preserves_original() {
        let root=fixture();call(&root,"create",json!({"parent":"","name":"A","folder":false}));
        let path=root.join("notes").join(ITEM_ORDER);fs::write(&path,b"broken").unwrap();
        for (op,args) in [("reorder",json!({"path":"A.md","direction":1})),("rename",json!({"path":"A.md","name":"B.md"})),("delete",json!({"path":"A.md"}))] {assert!(dispatch(&root,op,args).is_err());}
        assert!(root.join("notes/A.md").exists());assert_eq!(fs::read(&path).unwrap(),b"broken");fs::remove_dir_all(root).unwrap();
    }
    #[cfg(windows)]
    #[test] fn readonly_order_failure_keeps_tree_and_files() {
        let root=fixture();for name in ["A","B"]{call(&root,"create",json!({"parent":"","name":name,"folder":false}));}
        call(&root,"reorder",json!({"path":"B.md","direction":-1}));let path=root.join("notes").join(ITEM_ORDER);let original=fs::read(&path).unwrap();
        let mut permissions=fs::metadata(&path).unwrap().permissions();permissions.set_readonly(true);fs::set_permissions(&path,permissions).unwrap();
        assert!(dispatch(&root,"reorder",json!({"path":"B.md","direction":1})).is_err());
        assert!(dispatch(&root,"rename",json!({"path":"B.md","name":"C.md"})).is_err());assert!(root.join("notes/B.md").exists());assert!(!root.join("notes/C.md").exists());
        assert!(dispatch(&root,"delete",json!({"path":"B.md"})).is_err());assert!(root.join("notes/B.md").exists());
        assert!(dispatch(&root,"create",json!({"parent":"","name":"C","folder":true})).is_err());assert!(!root.join("notes/C").exists());
        assert_eq!(fs::read(&path).unwrap(),original);assert_eq!(names(&root,""),["B.md","A.md"]);
        let mut permissions=fs::metadata(&path).unwrap().permissions();permissions.set_readonly(false);fs::set_permissions(&path,permissions).unwrap();fs::remove_dir_all(root).unwrap();
    }
    #[cfg(windows)]
    #[test] fn move_order_failure_rolls_back_both_parents() {
        let root=fixture();
        for folder in ["来源","目标"] {call(&root,"create",json!({"parent":"","name":folder,"folder":true}));for name in ["A","B"]{call(&root,"create",json!({"parent":folder,"name":name,"folder":false}));}call(&root,"reorder",json!({"path":format!("{folder}/B.md"),"direction":-1}));}
        call(&root,"rename",json!({"path":"来源/B.md","name":"C.md"}));
        let order=root.join("notes/目标").join(ITEM_ORDER);let original=fs::read(&order).unwrap();let mut permissions=fs::metadata(&order).unwrap().permissions();permissions.set_readonly(true);fs::set_permissions(&order,permissions).unwrap();
        assert!(dispatch(&root,"move",json!({"path":"来源/C.md","parent":"目标"})).is_err());
        assert!(root.join("notes/来源/C.md").exists());assert!(!root.join("notes/目标/C.md").exists());assert_eq!(names(&root,"来源"),["C.md","A.md"]);assert_eq!(fs::read(&order).unwrap(),original);
        let mut permissions=fs::metadata(&order).unwrap().permissions();permissions.set_readonly(false);fs::set_permissions(&order,permissions).unwrap();fs::remove_dir_all(root).unwrap();
    }
    #[test] fn folder_page_lifecycle_and_conflicts() {
        let root=fixture();
        call(&root,"create",json!({"parent":"","name":"工作","folder":true}));
        call(&root,"create",json!({"parent":"工作","name":"子目录","folder":true}));
        let empty=call(&root,"read",json!({"path":"工作"}));
        assert_eq!(empty["content"],"");
        assert!(!root.join("notes/工作").join(FOLDER_CONTENT).exists());
        call(&root,"save",json!({"path":"工作","content":"","revision":empty["revision"]}));
        assert!(!root.join("notes/工作").join(FOLDER_CONTENT).exists());
        call(&root,"save",json!({"path":"工作","content":"本周计划\n![图](diary-asset:fixture.png)","revision":empty["revision"]}));
        assert!(dispatch(&root,"save",json!({"path":"工作","content":"覆盖","revision":empty["revision"]})).is_err());
        let subtree=call(&root,"read",json!({"path":"工作/子目录"}));
        call(&root,"save",json!({"path":"工作/子目录","content":"子目录说明","revision":subtree["revision"]}));
        let tree=call(&root,"tree",json!({}));assert_eq!(tree[0]["children"].as_array().unwrap().len(),1);
        let found=call(&root,"search",json!({"query":"本周计划"}));assert_eq!(found["results"][0]["path"],"工作");assert_eq!(found["results"][0]["folder"],true);
        call(&root,"rename",json!({"path":"工作","name":"规划"}));
        call(&root,"create",json!({"parent":"","name":"目标","folder":true}));
        call(&root,"move",json!({"path":"规划","parent":"目标"}));
        assert!(call(&root,"read",json!({"path":"目标/规划"}))["content"].as_str().unwrap().contains("本周计划"));
        call(&root,"delete",json!({"path":"目标/规划"}));
        let trash=call(&root,"trash",json!({}));call(&root,"restore",json!({"id":trash[0]["id"]}));
        assert_eq!(call(&root,"read",json!({"path":"目标/规划/子目录"}))["content"],"子目录说明");
        assert!(dispatch(&root,"read",json!({"path":"目标/规划/.xiaomang-folder.md"})).is_err());
        fs::remove_dir_all(root).unwrap();
    }
    #[test] fn folder_pages_backup_import_and_legacy_backup() {
        let root=fixture();let target=fixture();let outside=fixture();
        call(&root,"create",json!({"parent":"","name":"资料","folder":true}));
        call(&root,"create",json!({"parent":"资料","name":"日记","folder":false}));
        fs::write(root.join("attachments/fixture.png"),b"image").unwrap();
        let r=call(&root,"read",json!({"path":"资料"}));
        call(&root,"save",json!({"path":"资料","content":"说明 ![图](diary-asset:fixture.png)","revision":r["revision"]}));
        let backup=call(&root,"backup",json!({"destination":outside}));
        let imported=call(&target,"import_backup",json!({"source":backup}));
        let folder=format!("{}/资料",imported.as_str().unwrap());
        assert_eq!(call(&target,"read",json!({"path":folder}))["content"],"说明 ![图](diary-asset:fixture.png)");
        assert_eq!(fs::read(target.join("attachments/fixture.png")).unwrap(),b"image");
        // A backup made before this feature has no reserved file and remains valid.
        fs::remove_file(PathBuf::from(backup.as_str().unwrap()).join("notes/资料").join(FOLDER_CONTENT)).unwrap();
        let legacy=fixture();let imported=call(&legacy,"import_backup",json!({"source":backup}));
        assert_eq!(call(&legacy,"read",json!({"path":format!("{}/资料",imported.as_str().unwrap())}))["content"],"");
        assert!(legacy.join("notes").join(imported.as_str().unwrap()).join("资料/日记.md").exists());
        for dir in [root,target,outside,legacy] {fs::remove_dir_all(dir).unwrap();}
    }
    #[test] fn invalid_folder_content_is_preserved() {
        let root=fixture();call(&root,"create",json!({"parent":"","name":"资料","folder":true}));
        let path=root.join("notes/资料").join(FOLDER_CONTENT);
        fs::write(&path,[0xff,0xfe]).unwrap();
        assert!(dispatch(&root,"read",json!({"path":"资料"})).is_err());
        assert!(dispatch(&root,"save",json!({"path":"资料","content":"覆盖","revision":digest("")})).is_err());
        assert_eq!(fs::read(&path).unwrap(),[0xff,0xfe]);
        fs::remove_dir_all(root).unwrap();
    }
    #[test] fn expanded_palette_survives_reopen_and_preserves_existing_colors() {
        let root=fixture();
        call(&root,"create",json!({"parent":"","name":"旧颜色","folder":false}));
        call(&root,"create",json!({"parent":"","name":"新颜色","folder":false}));
        call(&root,"set_color",json!({"path":"旧颜色.md","color":"#c74747"}));
        for color in ICON_COLORS {
            call(&root,"set_color",json!({"path":"新颜色.md","color":color}));
            let reopened=Diary::open(root.clone()).unwrap();
            assert_eq!(reopened.colors().unwrap()["新颜色.md"].color(),color);
            assert_eq!(reopened.colors().unwrap()["旧颜色.md"].color(),"#c74747");
            assert!(reopened.tree().unwrap().iter().any(|node|node["path"]=="新颜色.md"&&node["color"]==color));
        }
        fs::remove_dir_all(root).unwrap();
    }
    #[test] fn vivid_colors_and_solid_style_survive_reopen_without_migrating_legacy_values() {
        let root=fixture();call(&root,"create",json!({"parent":"","name":"旧颜色","folder":false}));
        call(&root,"create",json!({"parent":"","name":"新颜色","folder":false}));
        call(&root,"set_color",json!({"path":"旧颜色.md","color":"#c74747"}));
        for color in &ICON_COLORS[24..] { for solid in [false,true] {
            call(&root,"set_color",json!({"path":"新颜色.md","color":color,"solid":solid}));
            let reopened=Diary::open(root.clone()).unwrap();let colors=reopened.colors().unwrap();
            assert_eq!(colors["新颜色.md"].color(),*color);assert_eq!(colors["新颜色.md"].solid(),solid);
            assert_eq!(colors["旧颜色.md"],IconAppearance::Outline("#c74747".into()));
            let tree=reopened.tree().unwrap();let node=tree.iter().find(|n|n["path"]=="新颜色.md").unwrap();
            assert_eq!(node["color"],*color);assert_eq!(node["solid"],solid);
            let disk:Value=serde_json::from_slice(&fs::read(root.join("item-colors.json")).unwrap()).unwrap();
            assert_eq!(disk["旧颜色.md"],"#c74747");
        }}
        call(&root,"set_color",json!({"path":"新颜色.md","color":""}));
        assert!(!Diary::open(root.clone()).unwrap().colors().unwrap().contains_key("新颜色.md"));fs::remove_dir_all(root).unwrap();
    }
    #[test] fn solid_style_follows_folder_relocation_trash_and_backup_without_inheritance() {
        let root=fixture();let outside=fixture();let target=fixture();
        call(&root,"create",json!({"parent":"","name":"资料","folder":true}));
        call(&root,"create",json!({"parent":"资料","name":"日记","folder":false}));
        call(&root,"create",json!({"parent":"","name":"目标","folder":true}));
        call(&root,"set_color",json!({"path":"资料","color":"#2563eb","solid":true}));
        let tree=call(&root,"tree",json!({}));let folder=tree.as_array().unwrap().iter().find(|n|n["path"]=="资料").unwrap();
        assert_eq!(folder["solid"],true);assert!(folder["children"][0].get("solid").is_none());
        call(&root,"set_color",json!({"path":"资料/日记.md","color":"#ef4444","solid":true}));
        call(&root,"rename",json!({"path":"资料","name":"记录"}));call(&root,"move",json!({"path":"记录","parent":"目标"}));
        let expected=Diary::open(root.clone()).unwrap().colors().unwrap();
        assert!(expected["目标/记录"].solid());assert!(expected["目标/记录/日记.md"].solid());
        call(&root,"delete",json!({"path":"目标/记录"}));let trash=call(&root,"trash",json!({}));
        call(&root,"restore",json!({"id":trash[0]["id"]}));assert_eq!(Diary::open(root.clone()).unwrap().colors().unwrap(),expected);
        let backup=call(&root,"backup",json!({"destination":outside}));let imported=call(&target,"import_backup",json!({"source":backup}));
        let imported_colors=Diary::open(target.clone()).unwrap().colors().unwrap();
        for (path,appearance) in expected { assert_eq!(imported_colors[&format!("{}/{path}",imported.as_str().unwrap())],appearance); }
        for path in [root,outside,target] {fs::remove_dir_all(path).unwrap();}
    }
    #[cfg(windows)]
    #[test] fn invalid_or_failed_style_updates_preserve_existing_metadata() {
        let root=fixture();call(&root,"create",json!({"parent":"","name":"保留","folder":false}));
        call(&root,"set_color",json!({"path":"保留.md","color":"#2563eb","solid":true}));
        let path=root.join("item-colors.json");let before=fs::read(&path).unwrap();
        assert!(dispatch(&root,"set_color",json!({"path":"保留.md","color":"#ef4444","solid":"yes"})).is_err());
        assert_eq!(fs::read(&path).unwrap(),before);
        let mut permissions=fs::metadata(&path).unwrap().permissions();permissions.set_readonly(true);fs::set_permissions(&path,permissions).unwrap();
        assert!(dispatch(&root,"set_color",json!({"path":"保留.md","color":"#ef4444","solid":false})).is_err());
        assert_eq!(fs::read(&path).unwrap(),before);
        let mut permissions=fs::metadata(&path).unwrap().permissions();permissions.set_readonly(false);fs::set_permissions(&path,permissions).unwrap();
        fs::write(&path,serde_json::to_vec(&json!({"保留.md":{"color":"#2563eb","solid":true,"extra":"invalid"}})).unwrap()).unwrap();
        assert!(Diary::open(root.clone()).unwrap().tree().is_ok());
        assert!(!Diary::open(root.clone()).unwrap().storage_warning().is_empty());fs::remove_dir_all(root).unwrap();
    }
    #[test] fn solid_toggle_can_keep_default_color_and_reset_both_attributes() {
        let root=fixture();let outside=fixture();let target=fixture();
        call(&root,"create",json!({"parent":"","name":"默认","folder":false}));
        call(&root,"set_color",json!({"path":"默认.md","color":"","solid":true}));
        let reopened=Diary::open(root.clone()).unwrap();assert!(reopened.colors().unwrap()["默认.md"].solid());
        let tree=reopened.tree().unwrap();assert_eq!(tree[0]["solid"],true);assert!(tree[0].get("color").is_none());
        let backup=call(&root,"backup",json!({"destination":outside}));let imported=call(&target,"import_backup",json!({"source":backup}));
        assert!(Diary::open(target.clone()).unwrap().colors().unwrap()[&format!("{}/默认.md",imported.as_str().unwrap())].solid());
        call(&root,"delete",json!({"path":"默认.md"}));let trash=call(&root,"trash",json!({}));call(&root,"restore",json!({"id":trash[0]["id"]}));
        call(&root,"set_color",json!({"path":"默认.md","color":"","solid":false}));assert!(Diary::open(root.clone()).unwrap().colors().unwrap().is_empty());
        for path in [root,outside,target] {fs::remove_dir_all(path).unwrap();}
    }
    #[test] fn icon_colors_follow_rename_move_and_reset_without_inheritance() {
        let root=fixture();call(&root,"create",json!({"parent":"","name":"资料","folder":true}));
        call(&root,"create",json!({"parent":"资料","name":"日记","folder":false}));
        call(&root,"create",json!({"parent":"","name":"目标","folder":true}));
        call(&root,"set_color",json!({"path":"资料","color":"#3976b8"}));
        let tree=call(&root,"tree",json!({}));let folder=tree.as_array().unwrap().iter().find(|n|n["path"]=="资料").unwrap();
        assert_eq!(folder["color"],"#3976b8");assert!(folder["children"][0].get("color").is_none());
        call(&root,"set_color",json!({"path":"资料/日记.md","color":"#c74747"}));
        assert_eq!(call(&root,"rename",json!({"path":"资料","name":"记录"})),"记录");
        assert_eq!(call(&root,"move",json!({"path":"记录","parent":"目标"})),"目标/记录");
        assert_eq!(Diary::open(root.clone()).unwrap().colors().unwrap(),Colors::from([("目标/记录".into(),"#3976b8".into()),("目标/记录/日记.md".into(),"#c74747".into())]));
        call(&root,"move",json!({"path":"目标/记录/日记.md","parent":""}));
        call(&root,"set_color",json!({"path":"日记.md","color":""}));
        assert!(!Diary::open(root.clone()).unwrap().colors().unwrap().contains_key("日记.md"));
        fs::remove_dir_all(root).unwrap();
    }
    #[test] fn moving_folder_preserves_markdown_images_and_saved_revision() {
        let root=fixture();call(&root,"create",json!({"parent":"","name":"目录","folder":true}));call(&root,"create",json!({"parent":"","name":"目标","folder":true}));
        call(&root,"create",json!({"parent":"目录","name":"图片日记","folder":false}));
        let before=call(&root,"read",json!({"path":"目录/图片日记.md"}));
        let content="未丢失正文\n![图片](diary-asset:keep.png)";
        let saved=call(&root,"save",json!({"path":"目录/图片日记.md","content":content,"revision":before["revision"]}));fs::write(root.join("attachments/keep.png"),b"unchanged-image").unwrap();
        call(&root,"move",json!({"path":"目录","parent":"目标"}));
        let after=call(&root,"read",json!({"path":"目标/目录/图片日记.md"}));assert_eq!(after["content"],content);assert_eq!(after["revision"],saved["revision"]);
        assert_eq!(fs::read(root.join("attachments/keep.png")).unwrap(),b"unchanged-image");assert!(root.join("history").join(digest("目录/图片日记.md")).exists());
        fs::remove_dir_all(root).unwrap();
    }
    #[test] fn move_rejects_conflicts_descendants_and_external_paths_without_mutation() {
        let root=fixture();for name in ["目录","目标"] {call(&root,"create",json!({"parent":"","name":name,"folder":true}));}
        call(&root,"create",json!({"parent":"目录","name":"子目录","folder":true}));
        for parent in ["目录","目录/子目录","../外部","C:/外部","不存在"] {assert!(dispatch(&root,"move",json!({"path":"目录","parent":parent})).is_err());}
        for parent in ["","目标"] {call(&root,"create",json!({"parent":parent,"name":"同名","folder":false}));}
        fs::write(root.join("notes/同名.md"),"源内容").unwrap();fs::write(root.join("notes/目标/同名.md"),"目标内容").unwrap();
        assert!(dispatch(&root,"move",json!({"path":"同名.md","parent":"目标"})).is_err());
        assert_eq!(fs::read_to_string(root.join("notes/同名.md")).unwrap(),"源内容");assert_eq!(fs::read_to_string(root.join("notes/目标/同名.md")).unwrap(),"目标内容");
        assert_eq!(call(&root,"move",json!({"path":"同名.md","parent":""})),"同名.md");
        assert_eq!(call(&root,"rename",json!({"path":"同名.md","name":"同名.md"})),"同名.md");
        assert!(dispatch(&root,"rename",json!({"path":"同名.md","name":"CON.md"})).is_err());
        assert!(root.join("notes/目录/子目录").is_dir());fs::remove_dir_all(root).unwrap();
    }
    #[test] fn colors_survive_backup_import_and_trash_without_leaking_to_recreated_names() {
        let root=fixture();let outside=fixture();let target=fixture();
        call(&root,"create",json!({"parent":"","name":"日记","folder":false}));call(&root,"set_color",json!({"path":"日记.md","color":"#368057"}));
        let backup=call(&root,"backup",json!({"destination":outside}));let imported=call(&target,"import_backup",json!({"source":backup}));
        assert_eq!(Diary::open(target.clone()).unwrap().colors().unwrap()[&format!("{}/日记.md",imported.as_str().unwrap())].color(),"#368057");
        call(&root,"delete",json!({"path":"日记.md"}));assert!(Diary::open(root.clone()).unwrap().colors().unwrap().is_empty());
        call(&root,"create",json!({"parent":"","name":"日记","folder":false}));assert!(call(&root,"tree",json!({}))[0].get("color").is_none());
        call(&root,"delete",json!({"path":"日记.md"}));let trash=call(&root,"trash",json!({}));let colored=trash.as_array().unwrap().iter().find(|e|e["colors"]["日记.md"]=="#368057").unwrap();
        call(&root,"restore",json!({"id":colored["id"]}));assert_eq!(Diary::open(root.clone()).unwrap().colors().unwrap()["日记.md"].color(),"#368057");
        for path in [root,outside,target] {fs::remove_dir_all(path).unwrap();}
    }
    #[test] fn corrupt_color_metadata_blocks_changes_and_preserves_original() {
        let root=fixture();call(&root,"create",json!({"parent":"","name":"保留","folder":false}));
        assert!(dispatch(&root,"set_color",json!({"path":"保留.md","color":"red;display:none"})).is_err());
        fs::write(root.join("item-colors.json"),b"broken-metadata").unwrap();
        assert!(dispatch(&root,"rename",json!({"path":"保留.md","name":"新名字.md"})).is_err());assert!(root.join("notes/保留.md").exists());assert!(!root.join("notes/新名字.md").exists());
        assert_eq!(fs::read(root.join("item-colors.json")).unwrap(),b"broken-metadata");fs::remove_dir_all(root).unwrap();
    }
    #[cfg(windows)]
    #[test] fn failed_color_write_rolls_back_rename_and_move() {
        let root=fixture();call(&root,"create",json!({"parent":"","name":"保留","folder":false}));call(&root,"create",json!({"parent":"","name":"目标","folder":true}));call(&root,"set_color",json!({"path":"保留.md","color":"#3976b8"}));
        let metadata=root.join("item-colors.json");let before=fs::read(&metadata).unwrap();let mut permissions=fs::metadata(&metadata).unwrap().permissions();permissions.set_readonly(true);fs::set_permissions(&metadata,permissions).unwrap();
        for (op,args) in [("rename",json!({"path":"保留.md","name":"新名字.md"})),("move",json!({"path":"保留.md","parent":"目标"}))] {
            assert!(dispatch(&root,op,args).unwrap_err().contains("操作已撤回"));assert!(root.join("notes/保留.md").exists());assert_eq!(fs::read(&metadata).unwrap(),before);
        }
        let mut permissions=fs::metadata(&metadata).unwrap().permissions();permissions.set_readonly(false);fs::set_permissions(&metadata,permissions).unwrap();fs::remove_dir_all(root).unwrap();
    }
    #[test] fn clipboard_image_survives_save_reopen_and_export() {
        let root = fixture(); let outside = fixture();
        let data = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==";
        let id = call(&root, "asset_bytes", json!({"extension":"png", "data":data}));
        let path = call(&root, "create", json!({"parent":"", "name":"粘贴图片", "folder":false}));
        let initial = call(&root, "read", json!({"path":path}));
        let content = format!("图片前\n![图片](diary-asset:{})\n图片后", id.as_str().unwrap());
        call(&root, "save", json!({"path":path,"content":content,"revision":initial["revision"]}));
        assert_eq!(call(&root,"read",json!({"path":path}))["content"],content);
        assert_eq!(call(&root,"asset_data",json!({"id":id})),format!("data:image/png;base64,{data}"));
        call(&root,"export",json!({"content":content,"destination":outside.join("图片.md")}));
        assert!(outside.join("图片.assets").join(id.as_str().unwrap()).exists());
        for path in [root,outside] {fs::remove_dir_all(path).unwrap();}
    }
    #[test] fn invalid_clipboard_images_leave_attachment_directory_clean() {
        let root = fixture();
        for args in [json!({"extension":"png","data":""}),json!({"extension":"exe","data":"AAAA"}),json!({"extension":"png","data":"!!!!"}),json!({"extension":"png","data":"bm90IGFuIGltYWdl"})] {
            assert!(dispatch(&root,"asset_bytes",args).is_err());
        }
        let too_large = "A".repeat(((MAX_ASSET+2)/3*4+4) as usize);
        assert!(dispatch(&root,"asset_bytes",json!({"extension":"png","data":too_large})).is_err());
        assert_eq!(fs::read_dir(root.join("attachments")).unwrap().count(),0);
        fs::remove_dir_all(root).unwrap();
    }
    fn fixture() -> PathBuf { let path=std::env::temp_dir().join(format!("xiaomang-diary-test-{}",Uuid::new_v4()));Diary::open(path.clone()).unwrap();path }
    fn call(root:&Path,op:&str,args:Value)->Value {dispatch(root,op,args).unwrap()}
    #[test] fn chinese_save_search_conflict_and_reopen() {
        let root=fixture();call(&root,"create",json!({"parent":"","name":"学习","folder":true}));
        let path=call(&root,"create",json!({"parent":"学习","name":"今天","folder":false}));
        let initial=call(&root,"read",json!({"path":path}));
        call(&root,"save",json!({"path":path,"content":"今天测试中文检索\n第二行","revision":initial["revision"]}));
        assert!(dispatch(&root,"save",json!({"path":path,"content":"不能覆盖","revision":initial["revision"]})).is_err());
        assert_eq!(call(&root,"search",json!({"query":"中文检索"}))["results"].as_array().unwrap().len(),1);
        assert_eq!(call(&root,"read",json!({"path":path}))["content"],"今天测试中文检索\n第二行");
        fs::remove_dir_all(root).unwrap();
    }
    #[test] fn traversal_reserved_names_delete_restore() {
        let root=fixture();
        for path in ["../private.md","C:/private.md","x/../../private.md","a\\b.md","CON.md"] {assert!(dispatch(&root,"read",json!({"path":path})).is_err());}
        let path=call(&root,"create",json!({"parent":"","name":"日记","folder":false}));
        call(&root,"delete",json!({"path":path}));let trash=call(&root,"trash",json!({}));
        call(&root,"restore",json!({"id":trash[0]["id"]}));assert!(root.join("notes/日记.md").exists());
        fs::remove_dir_all(root).unwrap();
    }
    #[test] fn backup_import_preserves_existing_and_note_bytes() {
        let root=fixture();let target=fixture();let outside=fixture();
        call(&root,"create",json!({"parent":"","name":"历史","folder":false}));
        let r=call(&root,"read",json!({"path":"历史.md"}));call(&root,"save",json!({"path":"历史.md","content":"永久记录","revision":r["revision"]}));
        let backup=call(&root,"backup",json!({"destination":outside}));
        call(&target,"create",json!({"parent":"","name":"保留","folder":false}));
        let imported=call(&target,"import_backup",json!({"source":backup}));
        assert!(target.join("notes/保留.md").exists());assert_eq!(fs::read_to_string(target.join("notes").join(imported.as_str().unwrap()).join("历史.md")).unwrap(),"永久记录");
        for path in [root,target,outside] {fs::remove_dir_all(path).unwrap();}
    }
    #[test] fn rename_folder_search_titles_and_reject_root_export() {
        let root=fixture();call(&root,"create",json!({"parent":"","name":"工作","folder":true}));
        call(&root,"create",json!({"parent":"工作","name":"晨间","folder":false}));
        call(&root,"rename",json!({"path":"工作","name":"生活"}));
        assert_eq!(call(&root,"search",json!({"query":"晨间"}))["results"][0]["path"],"生活/晨间.md");
        assert!(dispatch(&root,"export",json!({"content":"危险覆盖","destination":root.join("notes/生活/晨间.md")})).is_err());
        fs::remove_dir_all(root).unwrap();
    }
    #[test] fn oversized_and_non_utf8_fail_without_mutation() {
        let root=fixture();let path=root.join("notes/大文件.md");fs::write(&path,vec![b'x';MAX_NOTE as usize+1]).unwrap();
        assert!(dispatch(&root,"read",json!({"path":"大文件.md"})).is_err());
        fs::write(root.join("notes/旧编码.md"),[0xff,0xfe,0x42]).unwrap();
        assert_eq!(call(&root,"search",json!({"query":"文字"}))["skipped"],2);
        assert_eq!(fs::metadata(path).unwrap().len(),MAX_NOTE+1);
        fs::remove_dir_all(root).unwrap();
    }
    #[test] fn exports_local_images_with_note() {
        let root=fixture();let outside=fixture();let image=root.join("attachments/test.png");fs::write(&image,b"image-fixture").unwrap();
        let destination=outside.join("导出.md");call(&root,"export",json!({"content":"![测试](diary-asset:test.png)","destination":destination}));
        assert_eq!(fs::read_to_string(destination).unwrap(),"![测试](导出.assets/test.png)");
        assert_eq!(fs::read(outside.join("导出.assets/test.png")).unwrap(),b"image-fixture");
        let imported=call(&root,"import",json!({"source":outside.join("导出.md"),"parent":""}));
        let note=call(&root,"read",json!({"path":imported}));assert!(note["content"].as_str().unwrap().contains("diary-asset:"));
        for path in [root,outside] {fs::remove_dir_all(path).unwrap();}
    }
    #[test] fn imports_percent_and_unicode_image_names_without_panicking() {
        let root=fixture();let outside=fixture();
        fs::write(outside.join("%中文.png"),b"unicode-image").unwrap();
        fs::write(outside.join("空 格.png"),b"space-image").unwrap();
        fs::write(outside.join("图片.md"),"![中文](%中文.png)\n![空格](空%20格.png)").unwrap();
        let path=call(&root,"import",json!({"source":outside.join("图片.md"),"parent":""}));
        let content=call(&root,"read",json!({"path":path}));
        assert_eq!(content["content"].as_str().unwrap().matches("diary-asset:").count(),2);
        for path in [root,outside] {fs::remove_dir_all(path).unwrap();}
    }
    #[test] fn resized_image_title_survives_export_import_without_resampling() {
        let root=fixture();let outside=fixture();
        fs::write(root.join("attachments/test.png"),b"original-image-bytes").unwrap();
        let destination=outside.join("图片 大小.md");
        call(&root,"export",json!({"content":"![图片](diary-asset:test.png \"说明 [xm-width=320]\")","destination":destination}));
        let imported=call(&root,"import",json!({"source":destination,"parent":""}));
        let note=call(&root,"read",json!({"path":imported}));
        let content=note["content"].as_str().unwrap();
        assert!(content.contains("diary-asset:"));assert!(content.contains("说明 [xm-width=320]"));
        assert_eq!(fs::read(root.join("attachments/test.png")).unwrap(),b"original-image-bytes");
        assert_eq!(fs::read_dir(root.join("attachments")).unwrap().count(),2);
        for path in [root,outside] {fs::remove_dir_all(path).unwrap();}
    }
}
