//! 多路径候选的读音、去重、上屏和个人转移回归。
use std::sync::{Arc, Mutex};

use super::WordLearner;
use crate::sentence::{LanguageModel, SentenceScorer, UserNgram};
use crate::{CandidateKind, Engine};
use qingjian_dictionary::Dictionary;

fn sample() -> Engine {
    Engine::new(Dictionary::parse("我\two\t900000\n想\txiang\t500000\n翔\txiang\t300000\n向\txiang\t200000\n去\tqu\t400000\n").unwrap())
}

#[test]
fn several_sentences_with_same_last_word_are_selectable_and_learned() {
    let shared = Arc::new(Mutex::new((Vec::new(), UserNgram::default())));
    let mut engine = sample().with_learner(Box::new(WordLearner {
        shared: Arc::clone(&shared),
        ..WordLearner::default()
    }));
    engine.set_input("woxiangqu");
    let query = engine.query().unwrap();
    let sentences: Vec<_> = query
        .candidates
        .items
        .iter()
        .filter(|c| c.kind == CandidateKind::Sentence)
        .collect();
    assert_eq!(sentences.len(), 3);
    assert_eq!(sentences[0].text, "我想去");
    let second = sentences.iter().find(|c| c.text == "我翔去").unwrap();
    assert_eq!(engine.commit(second), "我翔去");
    assert!(engine.composition().is_empty());
    let ngram = &shared.lock().unwrap().1;
    assert!(ngram.pair(Some("我"), "翔") > 0);
    assert!(ngram.pair(Some("翔"), "去") > 0);
    assert_eq!(ngram.pair(Some("我"), "想"), 0);
}

struct TextModel;
impl LanguageModel for TextModel {
    fn log_prob(&self, _: Option<&str>, word: &str) -> Option<f64> {
        Some(match word {
            "说" => -0.1,
            "月" => -1.0,
            _ => -0.5,
        })
    }
}
struct TextScorer;
impl SentenceScorer for TextScorer {
    fn score(&self, _: &str, texts: &[&str]) -> Vec<f64> {
        texts
            .iter()
            .map(|t| if t.ends_with('说') { -0.1 } else { -1.0 })
            .collect()
    }
}

#[test]
fn rare_reading_remains_available_but_text_model_cannot_borrow_its_common_reading() {
    let dictionary =
        Dictionary::parse("这\tzhe\t1000\n月\tyue\t500\n说\tyue\t50\n说\tshuo\t1000\n").unwrap();
    let mut engine = Engine::new(dictionary)
        .with_language_model(Box::new(TextModel))
        .with_sentence_scorer(Box::new(TextScorer), None, None, None);
    engine.set_input("zheyue");
    let query = engine.query().unwrap();
    assert_eq!(query.candidates.items[0].text, "这月");
    assert!(
        query
            .candidates
            .items
            .iter()
            .any(|c| c.text == "这说" && c.syllables == ["zhe", "yue"])
    );
    engine.set_input("shuo");
    assert_eq!(engine.query().unwrap().candidates.items[0].text, "说");
}

#[test]
fn full_word_and_prefix_commit_keep_their_existing_meaning() {
    let mut engine = sample();
    engine.set_input("woxiangqu");
    let query = engine.query().unwrap();
    let texts: std::collections::HashSet<_> =
        query.candidates.items.iter().map(|c| &c.text).collect();
    assert_eq!(texts.len(), query.candidates.items.len());
    let prefix = query
        .candidates
        .items
        .iter()
        .find(|c| c.text == "我")
        .unwrap();
    assert_eq!(engine.commit(prefix), "我");
    assert_eq!(engine.composition().text(), "xiangqu");
    engine.set_input("xiang");
    assert_eq!(engine.query().unwrap().candidates.items[0].text, "想");
}

#[test]
fn complete_long_prefix_is_retained_and_secondary_commit_records_its_words() {
    let dictionary = Dictionary::parse("就是\tjiu shi\t458089\n九十九\tjiu shi jiu\t442\n九月\tjiu yue\t5870\n就\tjiu\t800000\n九\tjiu\t10000\n月\tyue\t182437\n说\tshuo\t799734\n说\tyue\t39987\n越\tyue\t100000\n约\tyue\t100000\n").unwrap();
    let shared = Arc::new(Mutex::new((Vec::new(), UserNgram::default())));
    let mut engine = Engine::new(dictionary).with_learner(Box::new(WordLearner {
        shared: Arc::clone(&shared),
        ..WordLearner::default()
    }));
    engine.set_input("jiushijiuyue");
    let query = engine.query().unwrap();
    let position = query
        .candidates
        .items
        .iter()
        .position(|c| c.text == "九十九月")
        .unwrap();
    assert!(position < 3);
    let chosen = &query.candidates.items[position];
    assert_eq!(engine.commit(chosen), "九十九月");
    assert!(engine.composition().is_empty());
    assert!(shared.lock().unwrap().1.pair(Some("九十九"), "月") > 0);
}
