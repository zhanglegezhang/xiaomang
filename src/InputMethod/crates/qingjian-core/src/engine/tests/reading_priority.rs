//! 原读音可完整组句时不改音；无完整路径仍保留键盘纠错，已开启的模糊音与英文尾段继续可用。

use qingjian_dictionary::{Dictionary, WordList};

use crate::{CandidateKind, Engine, FuzzyRules};

const TAIL: &str =
    "他\tta\t1000\n她\tta\t900\n妈\tma\t1000\n骂\tma\t2000\n的\tde\t100000\n到\tdao\t100000000\n";

#[test]
fn complete_original_readings_survive_a_more_frequent_keyboard_substitution() {
    let dictionary = Dictionary::parse(&format!("曹\tcao\t1\n草\tcao\t2\n{TAIL}")).unwrap();
    let mut engine = Engine::new(dictionary);
    for input in ["caotamade", "cao'ta'ma'de", "caota"] {
        engine.set_input(input);
        let query = engine.query().unwrap();
        let sentences: Vec<_> = query
            .candidates
            .items
            .iter()
            .filter(|candidate| candidate.kind == CandidateKind::Sentence)
            .collect();
        assert!(!sentences.is_empty(), "{input}");
        assert!(
            sentences
                .iter()
                .all(|candidate| candidate.syllables[0] == "cao"),
            "{input}: {sentences:?}"
        );
    }
}

#[test]
fn missing_original_path_still_allows_keyboard_typo_recovery() {
    let mut engine = Engine::new(Dictionary::parse(TAIL).unwrap());
    engine.set_input("cao'ta'ma'de");
    let query = engine.query().unwrap();
    let first = &query.candidates.items[0];
    assert_eq!(first.syllables[0], "dao");
    assert_eq!(first.syllables.len(), 4);
    assert_eq!(engine.commit(first), first.text);
    assert!(engine.composition().is_empty());
}

#[test]
fn enabled_fuzzy_readings_remain_available_before_keyboard_typos() {
    let mut engine = Engine::new(Dictionary::parse(&format!("超\tchao\t1\n{TAIL}")).unwrap());
    engine.set_fuzzy(FuzzyRules {
        c_ch: true,
        ..FuzzyRules::default()
    });
    engine.set_input("cao'ta'ma'de");
    let query = engine.query().unwrap();
    assert_eq!(query.candidates.items[0].syllables[0], "chao");
    assert!(!query.candidates.items.iter().any(|candidate| {
        candidate
            .syllables
            .first()
            .is_some_and(|syllable| syllable == "dao")
    }));
}

#[test]
fn mixed_english_tail_keeps_the_original_chinese_reading() {
    let mut engine = Engine::new(Dictionary::parse(&format!("曹\tcao\t1\n{TAIL}")).unwrap())
        .with_english(WordList::parse("vim\tvim\t5000\n").unwrap());
    engine.set_input("caotavim");
    let query = engine.query().unwrap();
    let mixed = query
        .candidates
        .items
        .iter()
        .find(|candidate| {
            candidate.kind == CandidateKind::Sentence && candidate.text.ends_with("vim")
        })
        .unwrap();
    assert_eq!(mixed.syllables[0], "cao");
}

#[test]
fn an_unusual_but_complete_original_phrase_is_not_replaced_by_a_common_typo() {
    let dictionary =
        Dictionary::parse("美感\tmei gan\t300000\n洗\txi\t100000\n没关系\tmei guan xi\t800000\n")
            .unwrap();
    let mut engine = Engine::new(dictionary);
    engine.set_input("meiganxi");
    let query = engine.query().unwrap();
    assert_eq!(query.candidates.items[0].text, "美感洗");
    assert!(
        query
            .candidates
            .items
            .iter()
            .take(3)
            .all(|candidate| candidate.text != "没关系")
    );
}
