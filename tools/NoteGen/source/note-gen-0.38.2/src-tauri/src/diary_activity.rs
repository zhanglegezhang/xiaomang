// Optional local shortcut metadata. Failure never makes a successful note save fail.
use super::*;
use serde::{Deserialize, Serialize};

const FILE: &str = "activity-v1.json";
#[derive(Default, Clone, Serialize, Deserialize)]
pub(super) struct Entry {
    pub path: String,
    pub folder: bool,
    pub edited: u64,
    pub opens: u64,
    pub last_open: u64,
}
#[derive(Serialize, Deserialize)]
struct Store { version: u32, entries: Vec<Entry> }
fn load(diary: &Diary) -> Result<Store> {
    let path = diary.root.join(FILE);
    if linked(&path) { return Err("日记统计文件不能是链接".into()) }
    if !path.exists() { return Ok(Store { version: 1, entries: vec![] }) }
    if fs::metadata(&path).map_err(err)?.len() > 8 * 1024 * 1024 { return Err("日记统计文件过大".into()) }
    let store: Store = serde_json::from_slice(&fs::read(path).map_err(err)?).map_err(err)?;
    if store.version != 1 || store.entries.len() > 20000 { return Err("日记统计格式无效，原文件已保留".into()) }
    for entry in &store.entries { diary.note_path(&entry.path, false)?; }
    Ok(store)
}
fn write(diary: &Diary, store: &Store) -> Result<()> {
    let path = diary.root.join(FILE);
    if linked(&path) { return Err("日记统计文件不能是链接".into()) }
    let bytes = serde_json::to_vec(store).map_err(err)?;
    if store.entries.len() > 20000 || bytes.len() > 8 * 1024 * 1024 { return Err("日记统计已达容量限制，原有统计已保留".into()) }
    atomic_write(&path, &bytes)
}
fn modified(diary: &Diary, path: &str) -> u64 {
    diary.content_path(path).ok().and_then(|p|fs::metadata(p).ok())
        .and_then(|m|m.modified().ok()).and_then(|t|t.duration_since(std::time::UNIX_EPOCH).ok())
        .map(|d|d.as_millis().min(u64::MAX as u128) as u64).unwrap_or(0)
}
pub(super) fn sync(diary: &Diary, tree: &[Value]) -> Result<()> {
    let old = load(diary)?;
    let mut by_path: BTreeMap<_,_> = old.entries.into_iter().map(|e|(e.path.clone(),e)).collect();
    let mut paths=vec![]; flatten(tree,&mut paths);
    fn complete(nodes: &[Value]) -> bool {
        nodes.iter().all(|node| node.get("warning").is_none() && complete(node["children"].as_array().unwrap()))
    }
    // At the exact display limit a child scan can be empty before it has a node
    // on which to attach a warning. Treat that boundary conservatively too.
    let full = paths.len() < 20000 && complete(tree);
    let mut entries = if full { BTreeMap::new() } else { by_path.clone() };
    for path in paths {
        let folder=diary.note_path(path,false)?.is_dir();
        let mut entry=by_path.remove(path).filter(|e|e.folder==folder).unwrap_or_default();
        entry.path=path.to_owned(); entry.folder=folder; entry.edited=modified(diary,path);
        if entries.contains_key(path) || entries.len() < 20000 { entries.insert(path.to_owned(), entry); }
    }
    write(diary,&Store { version:1,entries:entries.into_values().collect() })
}
pub(super) fn visit(diary: &Diary, path: &str) -> Result<()> {
    let target=diary.note_path(path,false)?;
    if !target.exists() || (!target.is_dir() && note_extension(&target).is_none()) { return Err("日记项目已不存在".into()) }
    let mut store=load(diary)?;
    if !store.entries.iter().any(|e|e.path==path) {
        store.entries.push(Entry { path:path.to_owned(),folder:target.is_dir(),edited:modified(diary,path),..Entry::default() });
    }
    let entry=store.entries.iter_mut().find(|e|e.path==path).unwrap();
    entry.opens=entry.opens.saturating_add(1); entry.last_open=stamp().min(u64::MAX as u128) as u64;
    write(diary,&store)
}
pub(super) fn saved(diary: &Diary, path: &str) -> Result<()> {
    let mut store=load(diary)?;
    if let Some(entry)=store.entries.iter_mut().find(|e|e.path==path) { entry.edited=modified(diary,path); }
    else { store.entries.push(Entry {path:path.to_owned(),folder:diary.note_path(path,false)?.is_dir(),edited:modified(diary,path),..Entry::default()}); }
    write(diary,&store)
}
pub(super) fn relocate(diary: &Diary, path: &str, next: &str) -> Result<()> {
    let mut store=load(diary)?;
    for entry in &mut store.entries {
        if entry.path==path || entry.path.starts_with(&(path.to_owned()+"/")) { entry.path=format!("{next}{}",&entry.path[path.len()..]); }
    }
    write(diary,&store)
}
pub(super) fn removed(diary: &Diary, path: &str) -> Result<()> {
    let mut store=load(diary)?;
    store.entries.retain(|e|e.path!=path && !e.path.starts_with(&(path.to_owned()+"/")));
    write(diary,&store)
}
pub(super) fn optional(result: Result<()>) { if let Err(error)=result { eprintln!("日记快捷记录更新失败：{error}"); } }

#[cfg(test)] mod tests {
    use super::*;
    #[test] fn usage_and_edits_survive_reopen_and_follow_paths() {
        let root=std::env::temp_dir().join(format!("diary-activity-{}",Uuid::new_v4()));
        let diary=Diary::open(root.clone()).unwrap();
        fs::create_dir(root.join("notes/目录")).unwrap();fs::write(root.join("notes/目录/A.txt"),"内容").unwrap();
        sync(&diary,&diary.tree().unwrap()).unwrap();
        assert_eq!(load(&diary).unwrap().entries.iter().find(|e|e.folder).unwrap().edited,0);
        visit(&diary,"目录/A.txt").unwrap();visit(&diary,"目录/A.txt").unwrap();visit(&diary,"目录").unwrap();
        diary.save("目录","目录内容",&digest("")).unwrap();
        diary.relocate("目录","新目录").unwrap();
        let entries=load(&Diary::open(root.clone()).unwrap()).unwrap().entries;
        assert_eq!(entries.iter().find(|e|e.path=="新目录/A.txt").unwrap().opens,2);
        assert!(entries.iter().find(|e|e.path=="新目录").unwrap().edited>0);
        diary.remove("新目录").unwrap();assert!(load(&diary).unwrap().entries.is_empty());
        fs::create_dir(root.join("notes/新目录")).unwrap();sync(&diary,&diary.tree().unwrap()).unwrap();
        assert_eq!(load(&diary).unwrap().entries[0].opens,0);
        assert!(visit(&diary,"../outside.txt").is_err());fs::remove_dir_all(root).unwrap();
    }
    #[test] fn corrupt_optional_stats_do_not_block_content_saves() {
        let root=std::env::temp_dir().join(format!("diary-activity-{}",Uuid::new_v4()));let diary=Diary::open(root.clone()).unwrap();
        fs::write(root.join("notes/A.md"),"旧").unwrap();fs::write(root.join(FILE),"corrupt").unwrap();
        diary.save("A.md","新",&digest("旧")).unwrap();assert_eq!(diary.text("A.md").unwrap(),"新");
        assert_eq!(fs::read_to_string(root.join(FILE)).unwrap(),"corrupt");fs::remove_dir_all(root).unwrap();
    }
}
