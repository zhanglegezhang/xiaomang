//! 就地清理已退休的学习语言值与旧模板注释，保留其余配置格式和内容。

use std::path::Path;

use toml_edit::{DocumentMut, Value};

use crate::error::ConfigError;

const CURRENT_COMMENT: &str =
    "# 学习语言（en 英语 / off 不显示译文）：中文候选旁显示英文释义；英文候选的中文释义单独保留";

pub(super) fn clean_source(
    path: &Path,
    source: &str,
    language: &str,
) -> Result<Option<String>, ConfigError> {
    // 迁移完成后仍会频繁热加载配置，不再重复构建可编辑文档。
    if !matches!(language.trim().to_ascii_lowercase().as_str(), "ja" | "es")
        && !(source.contains("# 学习语言（")
            && (source.contains("ja 日语") || source.contains("es 西班牙语")))
    {
        return Ok(None);
    }
    let mut document: DocumentMut = source.parse().map_err(|source| ConfigError::Edit {
        path: path.to_owned(),
        source: Box::new(source),
    })?;
    let Some(general) = document
        .get_mut("general")
        .and_then(|item| item.as_table_like_mut())
    else {
        return Ok(None);
    };
    let mut changed = false;
    if let Some(value) = general
        .get_mut("learning_language")
        .and_then(|item| item.as_value_mut())
        && value
            .as_str()
            .is_some_and(|code| matches!(code.trim().to_ascii_lowercase().as_str(), "ja" | "es"))
    {
        let decor = value.decor().clone();
        *value = Value::from("en");
        *value.decor_mut() = decor;
        changed = true;
    }
    if let Some(mut key) = general.key_mut("learning_language") {
        let prefix = key
            .leaf_decor()
            .prefix()
            .and_then(|raw| raw.as_str())
            .unwrap_or("");
        let mut updated = String::new();
        for line in prefix.split_inclusive('\n') {
            let text = line.trim();
            if text.starts_with("# 学习语言（")
                && (text.contains("ja 日语") || text.contains("es 西班牙语"))
            {
                updated.push_str(CURRENT_COMMENT);
                if line.ends_with("\r\n") {
                    updated.push_str("\r\n");
                } else if line.ends_with('\n') {
                    updated.push('\n');
                }
                changed = true;
            } else {
                updated.push_str(line);
            }
        }
        if updated != prefix {
            key.leaf_decor_mut().set_prefix(updated);
        }
    }
    Ok(changed.then(|| document.to_string()))
}

#[cfg(test)]
mod tests {
    use super::clean_source;
    use std::path::Path;

    #[test]
    fn removes_old_template_comment_even_when_current_value_is_already_valid() {
        for language in ["en", "off"] {
            let source = format!(
                "[general]\n# my preference\n# 学习语言（en 英语 / ja 日语 / es 西班牙语 / off 不显示译文）：候选旁显示哪种语言的译文\nlearning_language = \"{language}\" # keep this note\npage_size = 5\n[predict]\nmodel = \"my-model\"\n"
            );
            let cleaned = clean_source(Path::new("config.toml"), &source, language)
                .unwrap()
                .unwrap();
            assert!(!cleaned.contains("日语"));
            assert!(!cleaned.contains("西班牙语"));
            assert!(cleaned.contains(&format!(
                "learning_language = \"{language}\" # keep this note"
            )));
            assert!(cleaned.contains("# my preference"));
            assert!(cleaned.contains("model = \"my-model\""));
            assert!(
                clean_source(Path::new("config.toml"), &cleaned, language)
                    .unwrap()
                    .is_none()
            );
        }
    }

    #[test]
    fn never_edits_comment_like_lines_inside_a_multiline_value() {
        let source = "[general]\nlearning_language = \"en\"\n[predict]\nprompt = '''\n# 学习语言（ja 日语 / es 西班牙语）\n'''\n";
        assert!(
            clean_source(Path::new("config.toml"), source, "en")
                .unwrap()
                .is_none()
        );
    }

    #[test]
    fn migrates_inline_and_dotted_config_values() {
        for source in [
            "general = { learning_language = 'es', page_size = 5 }\n",
            "general.learning_language = 'ja'\ngeneral.page_size = 5\n",
        ] {
            let cleaned = clean_source(Path::new("config.toml"), source, "ja")
                .unwrap()
                .unwrap();
            let config: super::super::Config = toml::from_str(&cleaned).unwrap();
            assert_eq!(config.general.learning_language, "en");
            assert_eq!(config.general.page_size, 5);
        }
    }
}
