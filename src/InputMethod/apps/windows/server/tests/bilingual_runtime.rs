//! 用实际发布数据验证中英释义保留、退休语言资源移除；显式指定运行目录。

use std::path::PathBuf;

use qingjian_core::{Language, Translator};
use qingjian_dictionary::{Dictionary, SyllablePattern};
use qingjian_translate::Glossary;
use qingjian_windows_server::assembly::glossary_file;

#[test]
#[ignore = "requires QINGJIAN_TEST_RUNTIME pointing to staged product data"]
fn staged_runtime_keeps_chinese_and_both_translation_directions() {
    let root = PathBuf::from(std::env::var_os("QINGJIAN_TEST_RUNTIME").unwrap());
    let dictionary = Dictionary::from_path(root.join("data/generated/dict.qj")).unwrap();
    let candidates = dictionary.lookup_exact(&[
        SyllablePattern::complete("ni"),
        SyllablePattern::complete("hao"),
    ]);
    assert!(candidates.iter().any(|candidate| candidate.text == "你好"));
    for (language, word) in [(Language::English, "你好"), (Language::Chinese, "hello")] {
        let path = glossary_file(&root, language).unwrap();
        let glossary = Glossary::from_path(language, path).unwrap();
        let translation = glossary.translate(word).unwrap();
        assert_eq!(translation.language, language);
        assert!(!translation.senses().is_empty());
    }
    for language in [Language::Japanese, Language::Spanish] {
        assert!(glossary_file(&root, language).is_none());
        for folder in ["assets/levels", "assets/sample"] {
            for prefix in ["levels", "glossary"] {
                assert!(
                    !root
                        .join(folder)
                        .join(format!("{prefix}-{}.tsv", language.code()))
                        .exists()
                );
            }
        }
    }
}
