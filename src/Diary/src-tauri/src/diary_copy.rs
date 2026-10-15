// Duplicate an item inside the same diary without changing its source or attachments.
use super::*;

impl Diary {
    pub(super) fn duplicate(&self, relative:&str, name:&str) -> Result<Value> {
        let source=self.note_path(relative,false)?;
        let folder=source.is_dir();
        if !source.exists() || (!folder && note_extension(&source).is_none()) {return Err("项目不存在或不支持复制".into())}
        let mut name=name.to_owned();
        if !folder {
            let suffix=format!(".{}",note_extension(&source).unwrap());
            if !name.to_lowercase().ends_with(&suffix) {name.push_str(&suffix)}
        }
        safe_name(&name)?;
        let parent=source.parent().unwrap();
        let prefix=relative.rsplit_once('/').map(|(p,_)|p).unwrap_or("");
        let next=if prefix.is_empty(){name.clone()}else{format!("{prefix}/{name}")};
        let target=self.note_path(&next,false)?;
        if target.exists() {return Err("同名文件或目录已经存在，未覆盖".into())}
        let tree=self.tree()?;let mut all=vec![];flatten(&tree,&mut all);
        let subtree=all.iter().filter(|path|**path==relative||path.starts_with(&(relative.to_owned()+"/"))).count();
        if all.len()+subtree>20000 {return Err("复制后日记项目超过 20000 个，请拆分资料库".into())}
        let old=self.colors()?;let mut colors=old.clone();
        for (path,appearance) in &old {
            if path==relative || path.starts_with(&(relative.to_owned()+"/")) {
                colors.insert(next.clone()+&path[relative.len()..],appearance.clone());
            }
        }
        if colors.len()>20000 {return Err("图标颜色记录超过 20000 项".into())}
        let order=order_with_new(parent,&name)?;
        // Reserve the destination exclusively before writing; never overwrite an existing file.
        if folder {fs::create_dir(&target).map_err(err)?}
        else {OpenOptions::new().write(true).create_new(true).open(&target).map_err(err)?;}
        let result=(|| {
            if folder {copy_tree(&source,&target,&mut 0,&mut 0)?;}
            else {
                if fs::metadata(&source).map_err(err)?.len()>MAX_NOTE {return Err("日记超过 10 MB，不能复制".into())}
                fs::copy(&source,&target).map_err(err)?;
            }
            if colors!=old {self.write_colors(&colors)?;}
            if let Some(order)=order {if let Err(error)=commit_orders(&[(parent.to_path_buf(),Some(order))]) {
                if colors!=old {self.write_colors(&old).map_err(|rollback|format!("{error}；恢复图标设置失败：{rollback}"))?;}
                return Err(error)
            }}
            Ok(json!(next))
        })();
        if result.is_err() {
            if linked(&target) {return Err(format!("{}；副本路径被外部修改，未清理",result.unwrap_err()))}
            let cleanup=if folder {fs::remove_dir_all(&target)}else{fs::remove_file(&target)};
            if let Err(error)=cleanup {return Err(format!("{}；未完成副本保留在 {next}：{error}",result.unwrap_err()))}
        }
        result
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    fn fixture()->PathBuf {let root=std::env::temp_dir().join(format!("xiaomang-copy-test-{}",Uuid::new_v4()));Diary::open(root.clone()).unwrap();root}
    fn call(root:&Path,op:&str,args:Value)->Value {dispatch(root,op,args).unwrap()}
    fn create(root:&Path,parent:&str,name:&str,folder:bool,format:&str) {call(root,"create",json!({"parent":parent,"name":name,"folder":folder,"format":format}));}
    #[test] fn duplicate_txt_preserves_bytes_format_and_icon_without_changing_source() {
        let root=fixture();create(&root,"","文本",false,"txt");
        let bytes=b"\xef\xbb\xbf# plain\r\n**same**\r\n";fs::write(root.join("notes/文本.txt"),bytes).unwrap();
        call(&root,"set_color",json!({"path":"文本.txt","color":"#0066ff","solid":true}));
        let next=call(&root,"duplicate",json!({"path":"文本.txt","name":"文本 - 副本"}));assert_eq!(next,"文本 - 副本.txt");
        assert_eq!(fs::read(root.join("notes/文本 - 副本.txt")).unwrap(),bytes);assert_eq!(fs::read(root.join("notes/文本.txt")).unwrap(),bytes);
        let diary=Diary::open(root.clone()).unwrap();let colors=diary.colors().unwrap();assert_eq!(colors["文本.txt"],colors["文本 - 副本.txt"]);
        fs::remove_dir_all(root).unwrap();
    }
    #[test] fn duplicate_folder_preserves_nested_notes_description_order_and_asset_references() {
        let root=fixture();create(&root,"","目录",true,"md");create(&root,"目录","子",true,"md");
        for name in ["A","B"] {create(&root,"目录",name,false,"md");}
        create(&root,"目录/子","文本",false,"txt");
        fs::write(root.join("notes/目录/.xiaomang-folder.md"),"目录说明").unwrap();
        fs::write(root.join("notes/目录/A.md"),"![图](diary-asset:sample.png)").unwrap();fs::write(root.join("attachments/sample.png"),b"image").unwrap();
        call(&root,"reorder",json!({"path":"目录/B.md","direction":-1}));call(&root,"set_color",json!({"path":"目录/子/文本.txt","color":"#ff2020","solid":true}));
        call(&root,"duplicate",json!({"path":"目录","name":"目录 - 副本"}));
        for file in [".xiaomang-folder.md",".xiaomang-order.json","A.md","B.md","子/文本.txt"] {
            assert_eq!(fs::read(root.join("notes/目录").join(file)).unwrap(),fs::read(root.join("notes/目录 - 副本").join(file)).unwrap());
        }
        assert_eq!(fs::read(root.join("attachments/sample.png")).unwrap(),b"image");
        assert!(Diary::open(root.clone()).unwrap().colors().unwrap()["目录 - 副本/子/文本.txt"].solid());
        fs::remove_dir_all(root).unwrap();
    }
    #[test] fn duplicate_rejects_conflicts_paths_and_same_name_without_overwrite() {
        let root=fixture();create(&root,"","A",false,"md");create(&root,"","B",false,"md");fs::write(root.join("notes/B.md"),"保留").unwrap();
        for args in [json!({"path":"A.md","name":"B"}),json!({"path":"A.md","name":"A"}),json!({"path":"../A.md","name":"C"}),json!({"path":"A.md","name":"../C"})] {assert!(dispatch(&root,"duplicate",args).is_err());}
        assert_eq!(fs::read_to_string(root.join("notes/B.md")).unwrap(),"保留");assert!(!root.join("notes/C.md").exists());fs::remove_dir_all(root).unwrap();
    }
    #[test] fn failed_folder_copy_removes_partial_destination_and_keeps_source() {
        let root=fixture();create(&root,"","目录",true,"md");create(&root,"目录","A",false,"md");fs::write(root.join("notes/目录/.unsupported"),"blocked").unwrap();
        assert!(dispatch(&root,"duplicate",json!({"path":"目录","name":"副本"})).is_err());assert!(!root.join("notes/副本").exists());assert!(root.join("notes/目录/A.md").exists());
        assert_eq!(fs::read_to_string(root.join("notes/目录/.unsupported")).unwrap(),"blocked");fs::remove_dir_all(root).unwrap();
    }
}
