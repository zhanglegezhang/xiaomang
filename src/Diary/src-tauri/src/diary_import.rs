// A local backup is merged into a new namespace. Existing notes/history/trash
// are never replaced, and source backups are read-only throughout the operation.
use super::*;

fn mapped(prefix: &str, original: &str) -> String {
    if prefix.is_empty() {
        original.to_owned()
    } else {
        format!("{prefix}/{original}")
    }
}
fn note_mapping(nodes: &[Value], prefix: &str, mapping: &mut HashMap<String, String>) {
    for node in nodes {
        let relative = node["path"].as_str().unwrap();
        mapping.insert(digest(relative), digest(&mapped(prefix, relative)));
        note_mapping(node["children"].as_array().unwrap(), prefix, mapping);
    }
}
fn files(root: &Path) -> Result<Vec<PathBuf>> {
    let mut pending = vec![root.to_path_buf()];
    let mut result = vec![];
    while let Some(dir) = pending.pop() {
        for entry in fs::read_dir(dir).map_err(err)? {
            let path = entry.map_err(err)?.path();
            if linked(&path) {
                return Err("导入内容不能包含链接".into());
            }
            if path.is_dir() {
                pending.push(path)
            } else {
                result.push(path)
            }
        }
    }
    Ok(result)
}
pub(super) fn execute(diary: &Diary, source: &Path) -> Result<Value> {
    let source = source.canonicalize().map_err(err)?;
    let manifest = source.join("xiaomang-diary.json");
    if linked(&manifest) || fs::metadata(&manifest).map_err(err)?.len() > 16384 {
        return Err("无效日记备份清单".into());
    }
    let manifest: Value = serde_json::from_slice(&fs::read(manifest).map_err(err)?).map_err(err)?;
    if manifest["format"] != 1 || source.starts_with(&diary.root) {
        return Err("不是受支持的外部日记备份".into());
    }
    let imported_colors = read_colors(&source.join("item-colors.json"))?;
    let existing_colors = diary.colors()?;
    let mut checked = 0;
    for part in ["notes", "attachments", "history", "trash"] {
        if source.join(part).exists() {
            inspect_tree(
                &source.join(part),
                &mut 0,
                &mut checked,
                if part == "notes" { 60000 } else { 100000 },
                0,
            )?;
        } else if matches!(part, "notes" | "attachments") {
            return Err("备份缺少正文或附件目录".into());
        }
    }
    let transaction = Uuid::new_v4().to_string();
    let stage = diary.root.join(format!("import-{transaction}"));
    fs::create_dir(&stage).map_err(err)?;
    let mut installed: Vec<(PathBuf, String)> = vec![];
    let mut notes_target: Option<PathBuf> = None;
    let mut metadata_written = false;
    let parent = diary.root.join("notes");
    let old_order = read_order(&parent)?;
    let result = (|| {
        let mut bytes = 0;
        for part in ["notes", "attachments", "history", "trash"] {
            if source.join(part).exists() {
                copy_tree_bounded(
                    &source.join(part),
                    &stage.join(part),
                    &mut 0,
                    &mut bytes,
                    if part == "notes" { 60000 } else { 100000 },
                )?;
            } else {
                fs::create_dir(stage.join(part)).map_err(err)?;
            }
        }
        let mut imported_count = 0;
        let nodes = walk(&stage.join("notes"), "", 0, &mut imported_count)?;
        let direct = fs::read_dir(&parent).map_err(err)?.next().is_none()
            && (imported_count == 20000 || walk(&stage.join("notes"), "", 1, &mut 0).is_err());
        if !direct {
            let _ = walk(&stage.join("notes"), "", 1, &mut 0)?;
            diary.validate_addition("", true, imported_count + 1)?;
        }
        let prefix = if direct {
            String::new()
        } else {
            format!("导入-{}-{}", stamp(), &transaction[..8])
        };
        let mut mapping = HashMap::new();
        note_mapping(&nodes, &prefix, &mut mapping);
        let mut trash_moves = vec![];
        for entry in fs::read_dir(stage.join("trash")).map_err(err)? {
            let path = entry.map_err(err)?.path();
            if !path.is_dir() {
                return Err("无效备份回收站项目".into());
            }
            let metadata = path.join("entry.json");
            if fs::metadata(&metadata).map_err(err)?.len() > 16 * 1024 * 1024 {
                return Err("回收站元数据过大".into());
            }
            let mut value: Value =
                serde_json::from_slice(&fs::read(metadata).map_err(err)?).map_err(err)?;
            let original = string(&value, "path")?.to_owned();
            let _ = diary.note_path(&original, false)?;
            // Restored items retain entry.json but no longer contain content.
            let content = path.join("content");
            mapping.insert(digest(&original), digest(&mapped(&prefix, &original)));
            if content.is_dir() {
                let children = walk(&content, &original, 0, &mut 0)?;
                note_mapping(&children, &prefix, &mut mapping);
            }
            let id = Uuid::new_v4().to_string();
            value["id"] = json!(id);
            value["path"] = json!(mapped(&prefix, &original));
            if let Some(colors) = value.get("colors") {
                let colors: Colors = serde_json::from_value(colors.clone()).map_err(err)?;
                for (relative, color) in &colors {
                    let _ = diary.note_path(relative, false)?;
                    if !color.valid() {
                        return Err("无效回收站颜色".into());
                    }
                }
                value["colors"] = json!(colors
                    .into_iter()
                    .map(|(p, c)| (mapped(&prefix, &p), c))
                    .collect::<Colors>());
            }
            atomic_write(
                &path.join("entry.json"),
                &serde_json::to_vec(&value).map_err(err)?,
            )?;
            trash_moves.push((path, id));
        }
        let ready = stage.join("ready");
        for part in ["attachments", "history", "trash"] {
            fs::create_dir_all(ready.join(part)).map_err(err)?;
        }
        for entry in fs::read_dir(stage.join("attachments")).map_err(err)? {
            let path = entry.map_err(err)?.path();
            if !path.is_file() {
                return Err("无效附件目录".into());
            }
            let target = diary
                .root
                .join("attachments")
                .join(path.file_name().unwrap());
            if target.exists() {
                if linked(&target)
                    || fs::read(&target).map_err(err)? != fs::read(&path).map_err(err)?
                {
                    return Err("附件名称冲突，未导入日记".into());
                }
            } else {
                fs::rename(
                    &path,
                    ready.join("attachments").join(path.file_name().unwrap()),
                )
                .map_err(err)?;
            }
        }
        for (path, id) in trash_moves {
            fs::rename(path, ready.join("trash").join(id)).map_err(err)?;
        }
        let mut unmapped = 0;
        for entry in fs::read_dir(stage.join("history")).map_err(err)? {
            let path = entry.map_err(err)?.path();
            let name = path.file_name().unwrap().to_string_lossy().to_string();
            let destination = if let Some(hash) = mapping.get(&name) {
                ready.join("history").join(hash)
            } else {
                unmapped += 1;
                ready
                    .join("history")
                    .join(format!("unmapped-{transaction}"))
                    .join(&name)
            };
            fs::create_dir_all(destination.parent().unwrap()).map_err(err)?;
            fs::rename(path, destination).map_err(err)?;
        }
        let next_order = if direct {
            None
        } else {
            order_with_new(&parent, &prefix)?
        };
        // Prevalidate every destination before installing anything. Unique imported
        // history names avoid coalescing into an unrelated existing note's history.
        let mut operations = vec![];
        for path in files(&ready)? {
            let relative = path.strip_prefix(&ready).map_err(err)?;
            let mut destination = diary.root.join(relative);
            if destination.exists() {
                if relative.starts_with("history") {
                    let filename = destination.file_name().unwrap().to_string_lossy();
                    destination =
                        destination.with_file_name(format!("import-{transaction}-{filename}"));
                } else {
                    return Err("导入目标已存在，未覆盖任何文件".into());
                }
            }
            if linked(&destination) {
                return Err("导入目标不能是链接".into());
            }
            operations.push((path, destination));
        }
        // The source backup and staging copy remain available if an unexpected
        // I/O error interrupts rollback. No pre-existing attachment is removed.
        for (path, destination) in operations {
            let hash = format!("{:x}", Sha256::digest(fs::read(&path).map_err(err)?));
            fs::create_dir_all(destination.parent().unwrap()).map_err(err)?;
            fs::rename(path, &destination).map_err(err)?;
            installed.push((destination, hash));
        }
        let target = if direct {
            parent.clone()
        } else {
            parent.join(&prefix)
        };
        if direct {
            fs::remove_dir(&parent).map_err(err)?;
        }
        if let Err(error) = fs::rename(stage.join("notes"), &target) {
            if direct {
                let _ = fs::create_dir(&parent);
            }
            return Err(err(error));
        }
        notes_target = Some(target);
        metadata_written = true;
        if let Some(order) = next_order {
            commit_orders(&[(parent.clone(), Some(order))])?;
        }
        let mut colors = existing_colors.clone();
        colors.extend(
            imported_colors
                .into_iter()
                .map(|(p, c)| (mapped(&prefix, &p), c)),
        );
        diary.write_colors(&colors)?;
        let warning = if unmapped > 0 {
            format!("已保留 {unmapped} 项无法映射原路径的历史，位于 history/unmapped-{transaction}；正文与回收站已导入。")
        } else {
            String::new()
        };
        atomic_write(
            &diary.root.join("last-import-warning.txt"),
            warning.as_bytes(),
        )?;
        Ok(json!(prefix))
    })();
    if let Err(error) = result {
        let rollback = (|| -> Result<()> {
            if metadata_written {
                diary.write_colors(&existing_colors)?;
                commit_orders(&[(parent.clone(), old_order)])?;
            }
            if let Some(target) = notes_target {
                fs::rename(&target, stage.join("notes")).map_err(err)?;
                if target == parent {
                    fs::create_dir(&parent).map_err(err)?;
                }
            }
            for (path, hash) in installed.iter().rev() {
                if linked(path)
                    || format!("{:x}", Sha256::digest(fs::read(path).map_err(err)?)) != *hash
                {
                    return Err("导入文件出现额外变化，停止清理".into());
                }
                fs::remove_file(path).map_err(err)?;
            }
            Ok(())
        })();
        if let Err(rollback) = rollback {
            return Err(format!(
                "{error}；回滚未完成：{rollback}；导入现场已保留：{}",
                stage.display()
            ));
        }
        // Empty created directories are harmless; retain the staging copy for
        // diagnosis and retry instead of deleting evidence after an I/O failure.
        return Err(format!(
            "{error}；已撤回本次导入，源备份未修改；暂存副本：{}",
            stage.display()
        ));
    }
    if let Err(error) = fs::remove_dir_all(&stage) {
        let message = format!(
            "导入已完成；暂存清理失败，副本已保留：{error}；{}",
            stage.display()
        );
        let _ = atomic_write(
            &diary.root.join("last-import-warning.txt"),
            message.as_bytes(),
        );
    }
    result
}

#[cfg(test)]
mod tests {
    use super::*;
    fn fixture() -> PathBuf {
        let root = std::env::temp_dir().join(format!("xm-medium-{}", Uuid::new_v4()));
        Diary::open(root.clone()).unwrap();
        root
    }
    fn call(root: &Path, op: &str, args: Value) -> Value {
        dispatch(root, op, args).unwrap()
    }
    fn make(root: &Path, name: &str) {
        call(
            root,
            "create",
            json!({"parent":"","name":name,"folder":false}),
        );
    }
    #[test]
    fn backup_merge_restores_history_trash_and_keeps_existing_and_source() {
        let source = fixture();
        let target = fixture();
        let outside = fixture();
        make(&source, "正文");
        make(&source, "删除");
        make(&target, "本机");
        let history = source.join("history").join(digest("正文.md"));
        fs::create_dir_all(&history).unwrap();
        fs::write(history.join("snapshot.md"), "历史正文").unwrap();
        call(&source, "delete", json!({"path":"删除.md"}));
        let backup = call(&source, "backup", json!({"destination":outside}));
        let backup = PathBuf::from(backup.as_str().unwrap());
        let original = fs::read(
            backup
                .join("history")
                .join(digest("正文.md"))
                .join("snapshot.md"),
        )
        .unwrap();
        let name = call(&target, "import_backup", json!({"source":backup}));
        let prefix = name.as_str().unwrap();
        assert!(target.join("notes/本机.md").exists());
        assert!(target.join("notes").join(prefix).join("正文.md").exists());
        assert_eq!(
            fs::read(
                target
                    .join("history")
                    .join(digest(&format!("{prefix}/正文.md")))
                    .join("snapshot.md")
            )
            .unwrap(),
            original
        );
        let trash = call(&target, "trash", json!({}));
        assert_eq!(trash.as_array().unwrap().len(), 1);
        assert_eq!(trash[0]["path"], format!("{prefix}/删除.md"));
        call(&target, "restore", json!({"id":trash[0]["id"]}));
        assert!(target.join("notes").join(prefix).join("删除.md").exists());
        assert_eq!(
            fs::read(
                backup
                    .join("history")
                    .join(digest("正文.md"))
                    .join("snapshot.md")
            )
            .unwrap(),
            original
        );
        for path in [source, target, outside] {
            fs::remove_dir_all(path).unwrap();
        }
    }
    #[test]
    fn unmatched_history_is_preserved_and_visible_as_warning() {
        let source = fixture();
        let target = fixture();
        let outside = fixture();
        make(&source, "正文");
        let unknown = source.join("history/unknown-origin");
        fs::create_dir_all(&unknown).unwrap();
        fs::write(unknown.join("old.md"), "不可丢弃").unwrap();
        let backup = call(&source, "backup", json!({"destination":outside}));
        call(&target, "import_backup", json!({"source":backup}));
        assert!(call(&target, "storage_warning", json!({}))
            .as_str()
            .unwrap()
            .contains("无法映射"));
        let all = files(&target.join("history")).unwrap();
        assert!(all
            .iter()
            .any(|p| fs::read_to_string(p).unwrap() == "不可丢弃"));
        for path in [source, target, outside] {
            fs::remove_dir_all(path).unwrap();
        }
    }
    #[test]
    fn attachment_conflict_is_rejected_before_any_commit() {
        let source = fixture();
        let target = fixture();
        let outside = fixture();
        make(&source, "正文");
        make(&target, "本机");
        fs::write(source.join("attachments/a.png"), b"new-a").unwrap();
        fs::write(source.join("attachments/z.png"), b"new-z").unwrap();
        fs::write(target.join("attachments/z.png"), b"existing-z").unwrap();
        let backup = call(&source, "backup", json!({"destination":outside}));
        assert!(dispatch(&target, "import_backup", json!({"source":backup})).is_err());
        assert!(!target.join("attachments/a.png").exists());
        assert_eq!(
            fs::read(target.join("attachments/z.png")).unwrap(),
            b"existing-z"
        );
        assert_eq!(
            call(&target, "tree", json!({})).as_array().unwrap().len(),
            1
        );
        for path in [source, target, outside] {
            fs::remove_dir_all(path).unwrap();
        }
    }
    #[test]
    fn corrupt_colors_do_not_block_notes_or_allow_overwrite() {
        let root = fixture();
        make(&root, "正文");
        let path = root.join("item-colors.json");
        fs::write(&path, b"broken metadata").unwrap();
        let view = call(&root, "init", json!({}));
        assert!(view["storageWarning"]
            .as_str()
            .unwrap()
            .contains("默认外观"));
        assert_eq!(view["tree"].as_array().unwrap().len(), 1);
        let note = call(&root, "read", json!({"path":"正文.md"}));
        call(
            &root,
            "save",
            json!({"path":"正文.md","content":"继续编辑","revision":note["revision"]}),
        );
        assert!(dispatch(
            &root,
            "set_color",
            json!({"path":"正文.md","color":"#c74747"})
        )
        .is_err());
        assert_eq!(fs::read(&path).unwrap(), b"broken metadata");
        assert_eq!(
            call(&root, "read", json!({"path":"正文.md"}))["content"],
            "继续编辑"
        );
        fs::remove_dir_all(root).unwrap();
    }
}
