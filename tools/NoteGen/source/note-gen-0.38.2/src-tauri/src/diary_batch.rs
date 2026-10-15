// Batch commands run under diary::dispatch's storage lock, off the UI thread.
use super::*;
use std::collections::HashSet;

fn paths(diary: &Diary, args: &Value, collapse: bool) -> Result<Vec<String>> {
    let values = args["paths"].as_array().filter(|v| !v.is_empty() && v.len() <= 20000).ok_or("请选择 1–20000 个项目")?;
    let mut seen = HashSet::new();
    let mut paths = vec![];
    for value in values {
        let relative = value.as_str().ok_or("无效选择")?;
        let path = diary.note_path(relative, false)?;
        if !path.exists() || (!path.is_dir() && note_extension(&path).is_none()) { return Err(format!("项目不存在或不支持：{relative}")) }
        if seen.insert(relative.to_lowercase()) { paths.push(relative.to_owned()); }
    }
    if collapse { paths.retain(|path| {
        let parts:Vec<_>=path.split('/').collect();
        !(1..parts.len()).any(|length|seen.contains(&parts[..length].join("/").to_lowercase()))
    }); }
    Ok(paths)
}

impl Diary {
    pub(super) fn remove(&self, relative: &str) -> Result<String> {
        let path = self.note_path(relative,false)?;
        if !path.exists() { return Err("项目已不存在".into()) }
        let parent=path.parent().unwrap(); let old_order=read_order(parent)?;
        let next_order=old_order.as_ref().map(|o|o.iter().filter(|n|Some(n.as_str())!=path.file_name().and_then(|s|s.to_str())).cloned().collect());
        let mut colors = self.colors()?;
        let removed: Colors = colors.iter().filter(|(p,_)| *p == relative || p.starts_with(&(relative.to_owned()+"/"))).map(|(p,c)|(p.clone(),c.clone())).collect();
        colors.retain(|p,_| !removed.contains_key(p));
        let id = Uuid::new_v4().to_string(); let trash = self.root.join("trash").join(&id);
        fs::create_dir(&trash).map_err(err)?;
        let result = (|| {
            atomic_write(&trash.join("entry.json"), serde_json::to_string(&json!({"id":id,"path":relative,"time":stamp(),"colors":removed})).map_err(err)?.as_bytes())?;
            fs::rename(&path,trash.join("content")).map_err(err)?;
            if let Err(error)=commit_orders(&[(parent.to_path_buf(),next_order)]) {
                fs::rename(trash.join("content"),&path).map_err(|rollback|format!("{error}；恢复失败，内容保留在回收站 {id}：{rollback}"))?; return Err(error)
            }
            if !removed.is_empty() { if let Err(error)=self.write_colors(&colors) {
                let order_error=commit_orders(&[(parent.to_path_buf(),old_order)]).err();
                fs::rename(trash.join("content"),&path).map_err(|rollback|format!("{error}；恢复失败，内容保留在回收站 {id}：{rollback}"))?;
                return Err(format!("{error}{}",order_error.map(|e|format!("；{e}")).unwrap_or_default()))
            } }
            activity::optional(activity::removed(self,relative));
            Ok(id.clone())
        })();
        // Only discard an incomplete entry when its content has been restored.
        if result.is_err() && !trash.join("content").exists() { let _=fs::remove_dir_all(&trash); }
        result
    }
}

pub(super) fn execute(diary: &Diary, op: &str, args: &Value) -> Result<Value> {
    let paths = paths(diary,args,op != "batch_color")?;
    if op == "batch_export" { return export(diary,&paths, string(args,"destination")?) }
    if op == "batch_color" {
        let color=string(args,"color")?;
        if !color.is_empty() && !ICON_COLORS.contains(&color) { return Err("无效图标颜色".into()) }
        let solid=args["solid"].as_bool().ok_or("无效图标样式")?;
        let mut colors=diary.colors()?;
        for path in paths {
            if color.is_empty() && !solid { colors.remove(&path); }
            else { colors.insert(path,if solid {IconAppearance::Styled{color:color.into(),solid}} else {color.into()}); }
        }
        if colors.len()>20000 {return Err("图标颜色记录超过 20000 项".into())}
        diary.write_colors(&colors)?; return Ok(json!(true))
    }
    // Validate the entire request before changing any item.
    let mut targets=vec![];
    let _=diary.colors()?;
    let mut names=HashSet::new();
    for path in &paths {
        let source=diary.note_path(path,false)?; let _=read_order(source.parent().unwrap())?;
        if op == "batch_move" {
            let parent=string(args,"parent")?; let destination=diary.note_path(parent,true)?;
            if !destination.is_dir() {return Err("目标目录不存在".into())}
            let lower=path.to_lowercase();
            if source.is_dir() && (parent.to_lowercase()==lower || parent.to_lowercase().starts_with(&(lower+"/"))) {return Err("不能移动到自身或子目录".into())}
            let name=source.file_name().unwrap().to_string_lossy();
            if !names.insert(name.to_lowercase()) {return Err(format!("选择中有同名项目：{name}，请分开移动"))}
            let next=if parent.is_empty(){name.to_string()}else{format!("{parent}/{name}")};
            if next != *path && destination.join(name.as_ref()).exists(){return Err(format!("目标已有同名项目：{name}，未移动任何项目"))}
            let _=read_order(&destination)?;
            if source.is_dir(){let _=walk(&source,&next,next.split('/').count(),&mut 0)?;}
            targets.push(next);
        }
    }
    let mut completed=vec![]; let mut failed=vec![];
    for (i,path) in paths.iter().enumerate() {
        let result=if op=="batch_move" {diary.relocate(path,&targets[i]).map(|_|json!({"path":path,"next":targets[i]}))}
            else {diary.remove(path).map(|id|json!({"path":path,"trashId":id}))};
        match result {
            Ok(item)=>completed.push(item),
            Err(error)=>{failed.push(json!({"path":path,"error":error}));failed.extend(paths[i+1..].iter().map(|path|json!({"path":path,"error":"前一项失败，本项未执行"})));break}
        }
    }
    Ok(json!({"completed":completed,"failed":failed}))
}

fn export(diary: &Diary, selected: &[String], destination: &str) -> Result<Value> {
    let parent=PathBuf::from(destination);
    if !parent.is_absolute() || linked(&parent) {return Err("请选择有效的外部目录".into())}
    let parent=parent.canonicalize().map_err(err)?;
    if !parent.is_dir() || parent.starts_with(&diary.root) {return Err("请选择日记数据目录之外的导出位置".into())}
    let id=Uuid::new_v4().to_string();
    let stage=parent.join(format!(".小蟒日记导出-{id}"));
    let target=parent.join(format!("小蟒日记导出-{}-{}",stamp(),&id[..8]));
    fs::create_dir(&stage).map_err(err)?;
    let result=(|| {
        let tree=diary.tree()?; let mut all=vec![];flatten(&tree,&mut all);
        let mut files=0usize;let mut bytes=0u64;let mut assets=HashSet::new();
        let pattern=regex::Regex::new(r"diary-asset:([A-Za-z0-9._-]+)").map_err(err)?;
        for path in all.into_iter().filter(|path|selected.iter().any(|p|*path==p || path.starts_with(&(p.clone()+"/")))) {
            let source=diary.note_path(path,false)?;
            let relative=if source.is_dir(){
                fs::create_dir_all(stage.join("notes").join(path)).map_err(err)?;
                let content=diary.content_path(path)?; if !content.exists(){continue}
                format!("{path}/{FOLDER_CONTENT}")
            }else{path.to_owned()};
            let mut content=diary.text(path)?;
            if !relative.to_lowercase().ends_with(".txt") {
                let refs:Vec<String>=pattern.captures_iter(&content).map(|m|m[1].to_owned()).collect();
                for name in refs {
                    safe_name(&name)?;let asset=diary.root.join("attachments").join(&name);
                    if linked(&asset) || !asset.is_file() || fs::metadata(&asset).map_err(err)?.len()>MAX_ASSET {return Err(format!("图片附件不存在或无效：{name}"))}
                    if assets.insert(name.clone()) {
                        bytes+=fs::metadata(&asset).map_err(err)?.len();files+=1;
                        if bytes>1024*1024*1024 || files>20000 {return Err("导出超过 1 GB 或 20000 个文件".into())}
                        fs::create_dir_all(stage.join("attachments")).map_err(err)?;
                        fs::copy(asset,stage.join("attachments").join(&name)).map_err(err)?;
                    }
                    let prefix="../".repeat(relative.split('/').count());
                    let reference=format!("diary-asset:{name}");
                    content=pattern.replace_all(&content,|capture:&regex::Captures|if capture[0]==reference {format!("{prefix}attachments/{name}")}else{capture[0].to_owned()}).into_owned();
                }
            }
            files+=1;bytes+=content.len() as u64;
            if bytes>1024*1024*1024 || files>20000 {return Err("导出超过 1 GB 或 20000 个文件".into())}
            let output=stage.join("notes").join(relative);
            fs::create_dir_all(output.parent().unwrap()).map_err(err)?;
            atomic_write(&output,content.as_bytes())?;
        }
        atomic_write(&stage.join("xiaomang-export.json"), b"{\"format\":1,\"kind\":\"xiaomang-diary-export\"}")?;
        fs::rename(&stage,&target).map_err(err)?;
        Ok(json!({"destination":target,"files":files}))
    })();
    if result.is_err() {if let Err(cleanup)=fs::remove_dir_all(&stage){return Err(format!("{}；临时导出目录未清理：{}：{cleanup}",result.unwrap_err(),stage.display()))}}
    result
}

#[cfg(test)]
mod tests {
    use super::*;
    fn fixture() -> PathBuf {let root=std::env::temp_dir().join(format!("xiaomang-batch-test-{}",Uuid::new_v4()));Diary::open(root.clone()).unwrap();root}
    fn call(root:&Path,op:&str,args:Value)->Value {dispatch(root,op,args).unwrap()}
    fn create(root:&Path,name:&str,folder:bool,parent:&str){call(root,"create",json!({"parent":parent,"name":name,"folder":folder}));}
    #[test] fn move_collapses_parent_child_and_remaps_metadata() {
        let root=fixture();create(&root,"源",true,"");create(&root,"子",false,"源");create(&root,"目标",true,"");
        call(&root,"set_color",json!({"path":"源/子.md","color":"#0066ff","solid":true}));
        let result=call(&root,"batch_move",json!({"paths":["源/子.md","源","源"],"parent":"目标"}));
        assert_eq!(result["completed"].as_array().unwrap().len(),1);assert_eq!(result["completed"][0]["next"],"目标/源");
        assert!(root.join("notes/目标/源/子.md").exists());assert!(Diary::open(root.clone()).unwrap().colors().unwrap().contains_key("目标/源/子.md"));
        fs::remove_dir_all(root).unwrap();
    }
    #[test] fn move_preflights_all_conflicts_and_rejects_self_descendants() {
        let root=fixture();create(&root,"A",false,"");create(&root,"B",false,"");create(&root,"目标",true,"");create(&root,"B",false,"目标");
        assert!(dispatch(&root,"batch_move",json!({"paths":["A.md","B.md"],"parent":"目标"})).is_err());assert!(root.join("notes/A.md").exists());assert!(!root.join("notes/目标/A.md").exists());
        create(&root,"子",true,"目标");assert!(dispatch(&root,"batch_move",json!({"paths":["目标"],"parent":"目标/子"})).is_err());
        assert!(dispatch(&root,"batch_delete",json!({"paths":["A.md","../外部"]})).is_err());assert!(root.join("notes/A.md").exists());
        fs::remove_dir_all(root).unwrap();
    }
    #[test] fn deletion_collapses_children_and_restores_color_content() {
        let root=fixture();create(&root,"目录",true,"");create(&root,"日记",false,"目录");create(&root,"独立",false,"");
        fs::write(root.join("notes/目录/日记.md"),"正文").unwrap();call(&root,"set_color",json!({"path":"目录/日记.md","color":"#ff2020","solid":true}));
        let result=call(&root,"batch_delete",json!({"paths":["目录/日记.md","目录","独立.md"]}));assert_eq!(result["completed"].as_array().unwrap().len(),2);
        for entry in result["completed"].as_array().unwrap(){call(&root,"restore",json!({"id":entry["trashId"]}));}
        assert_eq!(fs::read_to_string(root.join("notes/目录/日记.md")).unwrap(),"正文");assert!(Diary::open(root.clone()).unwrap().colors().unwrap()["目录/日记.md"].solid());
        fs::remove_dir_all(root).unwrap();
    }
    #[test] fn color_applies_to_explicit_parent_and_child_atomically() {
        let root=fixture();create(&root,"目录",true,"");create(&root,"子",false,"目录");create(&root,"其他",false,"");
        let args=json!({"paths":["目录","目录/子.md"],"color":"#0066ff","solid":true});call(&root,"batch_color",args);
        let colors=Diary::open(root.clone()).unwrap().colors().unwrap();assert_eq!(colors.len(),2);assert!(colors.values().all(|c|c.solid()));
        assert!(dispatch(&root,"batch_color",json!({"paths":["其他.md","消失.md"],"color":"#ff2020","solid":true})).is_err());assert_eq!(colors,Diary::open(root.clone()).unwrap().colors().unwrap());
        call(&root,"batch_color",json!({"paths":["目录","目录/子.md"],"color":"","solid":false}));assert!(Diary::open(root.clone()).unwrap().colors().unwrap().is_empty());fs::remove_dir_all(root).unwrap();
    }
    #[test] fn export_keeps_full_tree_txt_bytes_folder_notes_and_shared_images() {
        let root=fixture();let outside=std::env::temp_dir().join(format!("xiaomang-batch-export-{}",Uuid::new_v4()));fs::create_dir(&outside).unwrap();
        create(&root,"目录",true,"");create(&root,"空",true,"目录");create(&root,"笔记",false,"目录");
        call(&root,"create",json!({"parent":"目录","name":"原文","format":"txt"}));
        let txt="甲 乙\r\n**原样**\t尾\n";fs::write(root.join("notes/目录/原文.txt"),txt).unwrap();
        fs::write(root.join("attachments/shared.png"),b"image").unwrap();
        fs::write(root.join("notes/目录/笔记.md"),"![图](diary-asset:shared.png \"[xm-width=300]\")").unwrap();
        fs::write(root.join("notes/目录").join(FOLDER_CONTENT),"目录说明 ![图](diary-asset:shared.png)").unwrap();
        let output=call(&root,"batch_export",json!({"paths":["目录","目录/笔记.md"],"destination":outside}));let target=PathBuf::from(output["destination"].as_str().unwrap());
        assert_eq!(output["files"],4);assert_eq!(fs::read_to_string(target.join("notes/目录/原文.txt")).unwrap(),txt);assert!(target.join("notes/目录/空").is_dir());
        assert!(fs::read_to_string(target.join("notes/目录/笔记.md")).unwrap().contains("../../attachments/shared.png \"[xm-width=300]\""));
        assert!(target.join("notes/目录").join(FOLDER_CONTENT).is_file());assert_eq!(fs::read(target.join("attachments/shared.png")).unwrap(),b"image");
        let second=call(&root,"batch_export",json!({"paths":["目录"],"destination":outside}));assert_ne!(output["destination"],second["destination"]);
        assert_eq!(fs::read_to_string(root.join("notes/目录/原文.txt")).unwrap(),txt);fs::remove_dir_all(root).unwrap();fs::remove_dir_all(outside).unwrap();
    }
    #[test] fn export_missing_asset_cleans_stage_and_rejects_internal_destination() {
        let root=fixture();create(&root,"坏",false,"");fs::write(root.join("notes/坏.md"),"![图](diary-asset:missing.png)").unwrap();
        let outside=std::env::temp_dir().join(format!("xiaomang-batch-export-{}",Uuid::new_v4()));fs::create_dir(&outside).unwrap();
        assert!(dispatch(&root,"batch_export",json!({"paths":["坏.md"],"destination":outside})).is_err());assert_eq!(fs::read_dir(&outside).unwrap().count(),0);
        assert!(dispatch(&root,"batch_export",json!({"paths":["坏.md"],"destination":root.join("notes")})).is_err());assert!(root.join("notes/坏.md").exists());
        fs::remove_dir_all(root).unwrap();fs::remove_dir_all(outside).unwrap();
    }
}
