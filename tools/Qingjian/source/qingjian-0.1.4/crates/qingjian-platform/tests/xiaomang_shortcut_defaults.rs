//! 小蟒快捷键默认值：首次写入、缺失配置回退和已有偏好保留。

use std::path::PathBuf;

use qingjian_platform::{Config, DEFAULT_PAGE_KEYS, Modifiers, PAGE_KEY_OPTIONS};

fn test_path(label: &str) -> PathBuf {
    std::env::temp_dir()
        .join(format!(
            "xiaomang-shortcut-{label}-{}-{}",
            std::process::id(),
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .unwrap()
                .as_nanos()
        ))
        .join("config.toml")
}

fn check_shortcuts(config: &Config) {
    assert_eq!(config.general.page_keys, "-=");
    assert_eq!(config.general.page_keys(), ('-', '='));
    assert_eq!(config.shortcut.mode.expression, 'v');
    assert_eq!(config.shortcut.mode.question, 'u');
    assert!(!config.shortcut.mode.question_mark);
    #[cfg(windows)]
    {
        assert_eq!(config.shortcut.translation, Modifiers::CONTROL);
        assert_eq!(config.shortcut.translation_second, Modifiers::SHIFT_CONTROL);
    }
    assert_eq!(config.shortcut.delete_candidate, Modifiers::SHIFT);
    assert_eq!(config.shortcut.translate_selection.key, 't');
    assert_eq!(
        config.shortcut.translate_selection.modifiers,
        "ctrl+alt".parse().unwrap()
    );
}

#[test]
fn absent_settings_use_the_approved_shortcuts() {
    check_shortcuts(&Config::default());
    assert_eq!(DEFAULT_PAGE_KEYS, ('-', '='));
    assert_eq!(PAGE_KEY_OPTIONS[0], "-=");
    let path = test_path("missing");
    check_shortcuts(&Config::load(&path).unwrap());
    assert!(!path.exists());
}

#[test]
fn first_run_template_and_its_reload_match_the_approved_shortcuts() {
    let path = test_path("first-run");
    assert!(Config::write_template_if_missing(&path).unwrap());
    check_shortcuts(&Config::load(&path).unwrap());
    let original = std::fs::read(&path).unwrap();
    assert!(!Config::write_template_if_missing(&path).unwrap());
    assert_eq!(std::fs::read(&path).unwrap(), original);
    std::fs::remove_dir_all(path.parent().unwrap()).unwrap();
}

#[test]
fn existing_shortcuts_are_not_replaced_or_rewritten() {
    let path = test_path("existing");
    std::fs::create_dir_all(path.parent().unwrap()).unwrap();
    let source = "# 保留用户偏好\n[general]\npage_keys = \"[]\"\npage_size = 4\n\
        [shortcut]\nexpression = \"i\"\nquestion = \"v\"\nquestion_mark = true\n\
        translation = \"ctrl+alt\"\ntranslation_second = \"alt\"\n\
        delete_candidate = \"shift\"\ntranslate_selection = \"shift+alt+x\"\n";
    std::fs::write(&path, source).unwrap();
    assert!(!Config::write_template_if_missing(&path).unwrap());
    let config = Config::load(&path).unwrap();
    assert_eq!(config.general.page_keys(), ('[', ']'));
    assert_eq!(config.general.page_size(), 4);
    assert_eq!(config.shortcut.mode.expression, 'i');
    assert_eq!(config.shortcut.mode.question, 'v');
    assert!(config.shortcut.mode.question_mark);
    assert_eq!(config.shortcut.translate_selection.key, 'x');
    assert_eq!(config.shortcut.translation_second, Modifiers::OPTION);
    assert_eq!(std::fs::read_to_string(&path).unwrap(), source);
    std::fs::remove_dir_all(path.parent().unwrap()).unwrap();
}
