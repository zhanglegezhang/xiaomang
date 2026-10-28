//! 真实分发词库的候选与延迟对照：不加载个人数据，不上屏，不连接日常输入法。
use std::path::PathBuf;
use std::time::{Duration, Instant};

use qingjian_core::Engine;
use qingjian_dictionary::Dictionary;
use qingjian_lm::BigramModel;
use qingjian_neural::CharScorer;
use serde_json::json;

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let root = PathBuf::from(
        std::env::args()
            .nth(1)
            .ok_or("runtime directory required")?,
    );
    let mode = std::env::args().nth(2).unwrap_or_else(|| "lexical".into());
    let load = Instant::now();
    let dict = Dictionary::from_path(root.join("data/generated/dict.qj"))?;
    let model = BigramModel::from_path(&root.join("data/generated/lm.qj"))?;
    let mut engine = Engine::new(dict).with_language_model(Box::new(model));
    engine.set_chinese_only_candidates(true);
    let dictionary_ms = load.elapsed().as_secs_f64() * 1000.0;
    let neural = Instant::now();
    if mode == "async" {
        engine = engine.with_async_sentence_scorer(
            Box::new(CharScorer::load(&root.join("data/model/model.qjm"))?),
            None,
            None,
            None,
        );
    }
    let model_ms = neural.elapsed().as_secs_f64() * 1000.0;
    let inputs = [
        "jiushijiuyue",
        "jiushijiu",
        "yue",
        "woxiangquchifan",
        "kaifazhe",
        "yinyue",
        "yinhang",
        "chongqing",
        "shuofu",
        "jiushi",
        "nihao",
    ];
    let mut quality = Vec::new();
    let mut cold = Vec::new();
    let mut background = Vec::new();
    for input in inputs {
        engine.clear();
        engine.set_input(input);
        let start = Instant::now();
        let first = engine.query()?;
        cold.push(start.elapsed().as_secs_f64() * 1000.0);
        let initial: Vec<_> = first
            .candidates
            .items
            .iter()
            .take(6)
            .map(|c| c.text.clone())
            .collect();
        let rescore = Instant::now();
        if engine.rescoring_pending() && engine.request_rescoring() {
            while !engine.poll_rescoring() {
                if rescore.elapsed() > Duration::from_secs(15) {
                    return Err("rescore timed out".into());
                }
                std::thread::sleep(Duration::from_millis(1));
            }
        }
        let final_query = engine.query()?;
        background.push(rescore.elapsed().as_secs_f64() * 1000.0);
        quality.push(json!({"input":input,"initial":initial,"final":final_query.candidates.items.iter().take(8).map(|c|json!({"text":c.text,"syllables":c.syllables,"kind":format!("{:?}",c.kind)})).collect::<Vec<_>>() }));
    }
    let mut keys = Vec::new();
    for _ in 0..20 {
        for input in inputs {
            engine.clear();
            for ch in input.chars() {
                engine.push(ch);
                let start = Instant::now();
                let _query = engine.query()?;
                keys.push(start.elapsed().as_secs_f64() * 1000.0);
            }
        }
    }
    keys.sort_by(f64::total_cmp);
    println!(
        "{}",
        json!({"mode":mode,"dictionary_load_ms":dictionary_ms,"model_load_ms":model_ms,"cold_query_ms":cold,"background_ms":background,"keystrokes":keys.len(),"key_median_ms":keys[keys.len()/2],"key_p95_ms":keys[keys.len()*95/100],"key_max_ms":keys[keys.len()-1],"quality":quality})
    );
    Ok(())
}
