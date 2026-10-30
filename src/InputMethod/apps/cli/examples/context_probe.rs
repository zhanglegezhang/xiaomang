//! 真实分发词库的候选与延迟对照：不加载个人数据，不上屏，不连接日常输入法。
use std::path::PathBuf;
use std::time::{Duration, Instant};

use qingjian_core::Engine;

use qingjian_dictionary::Dictionary;
use qingjian_lm::BigramModel;
use qingjian_neural::CharScorer;
use serde_json::json;

fn settled(e: &mut Engine, input: &str, context: &str) -> Vec<qingjian_core::Candidate> {
    e.clear();
    e.set_rescoring_context(Some(context.into()));
    e.set_input(input);
    e.query().unwrap();
    if e.rescoring_pending() && e.request_rescoring() {
        let t = Instant::now();
        while !e.poll_rescoring() {
            assert!(t.elapsed() < Duration::from_secs(10));
            std::thread::sleep(Duration::from_millis(1));
        }
    }
    e.query().unwrap().candidates.items
}
fn main() -> Result<(), Box<dyn std::error::Error>> {
    let root = PathBuf::from(std::env::args().nth(1).unwrap());
    let weight: f64 = std::env::args().nth(2).unwrap().parse()?;
    let make = || -> Result<Engine, Box<dyn std::error::Error>> {
        let mut e = Engine::new(Dictionary::from_path(root.join("data/generated/dict.qj"))?)
            .with_language_model(Box::new(BigramModel::from_path(
                &root.join("data/generated/lm.qj"),
            )?))
            .with_learner(Box::new(qingjian_learning::FrequencyLearner::default()))
            .with_async_sentence_scorer(
                Box::new(CharScorer::load(&root.join("data/model/model.qjm"))?),
                Some(0.5),
                None,
                None,
            );
        e.set_chinese_only_candidates(true);
        Ok(e)
    };
    let mut e = make()?;
    let mut rows = Vec::new();
    for context in [
        "",
        "请按照文档处理，",
        "我们正在讨论语言学中的",
        "这个案件的进度是",
        "今天开会时他说",
        "这是一位新同事的名字：",
    ] {
        for input in [
            "dicengcihui",
            "anjianyizhixing",
            "womenmingtianjian",
            "qingbangwokanxia",
            "woxihuanzhege",
            "jintianxiayule",
            "xietongkaifa",
            "ruanjianpeizhi",
            "shujukujiegou",
            "yonghushuju",
            "zhangweilai",
            "linmuran",
            "xinmoxing",
            "liangzitaijie",
            "zhongjianjianceng",
            "diyicishiyong",
            "zhegekeyiyou",
            "xinzengyigezhi",
            "liangzitai",
            "zhongjianjianceng",
            "huancunchi",
            "xianchengchi",
            "suoyinbiao",
            "neicuncao",
            "shujukujiansuo",
            "fenbushisuo",
            "wuxianshu",
            "hongheishu",
            "yonghuquan",
            "zhangsanlai",
            "limingqu",
            "wangweishuo",
            "moxingceng",
            "yuyiku",
            "ceshiji",
            "kaifaban",
            "jiekouceng",
            "shujuliu",
            "zhuangtaiji",
            "yunxingzhong",
            "wenjianming",
            "peizhixiang",
            "duquzhi",
            "morenxiang",
            "womenquba",
            "womenzouba",
            "tamenlaile",
            "nimenkanxia",
            "jintianhenre",
            "mingtianyouyu",
            "tianqihenhao",
            "yonghushuole",
            "chengxucuole",
            "zhegehaoyong",
            "nagekeyiyong",
            "huancunshixiao",
            "moxingcengshu",
            "suoyinbiaoming",
            "denglushibai",
            "wenjianlujing",
            "shujuleixing",
            "ceshitongguo",
            "yonghumingcheng",
            "zidonggengxin",
            "baocunchenggong",
            "quanxianbuzu",
            "wangluolianjie",
            "xianchenganzhuang",
            "jiekoucanshu",
            "xinzenggongneng",
            "zhangsanlaile",
            "liminghuijia",
            "wangweishuole",
            "zhaominglaile",
            "liuhuikaifa",
            "chengxuyunxing",
            "rengongzhineng",
            "jixuexuexi",
            "shenduxuexi",
            "duoyuyanshuru",
            "zhongwenshurufa",
            "yingwenfanyi",
            "gerenshezhi",
            "xuexijilu",
            "yonghuciku",
            "wenbenbianji",
            "jiancharizhi",
            "dakaicaidan",
            "guanbichuangkou",
            "chongxinjiazai",
            "qidongchengxu",
            "shanchuwenjian",
            "xinjianmulu",
            "huifumoren",
            "anquantuichu",
            "moxingcengji",
            "shujukuceng",
            "jiekoucengci",
            "huancunchidaxiao",
            "xianchengchishuliang",
        ] {
            let t = Instant::now();
            let items = settled(&mut e, input, context);
            rows.push(json!({"context":context,"input":input,"top":items.iter().take(3).map(|c|c.text.clone()).collect::<Vec<_>>(),"ms":t.elapsed().as_secs_f64()*1000.0}));
        }
    }
    let mut learning = Vec::new();
    for (input, target) in [
        ("dicengcihui", "低层次会"),
        ("dicengcihui", "底层词汇"),
        ("anjianyizhixing", "案件已执行"),
        ("nihao", "你好"),
    ] {
        let mut e = make()?;
        for n in 0..=10 {
            let items = settled(&mut e, input, "");
            let rank = items.iter().position(|c| c.text == target);
            learning.push(json!({"input":input,"target":target,"selections":n,"rank":rank.map(|r|r+1),"top":items[0].text,"weight":e.learner().weight(target)}));
            if n < 10 {
                if let Some(i) = rank {
                    e.commit(&items[i]);
                } else {
                    break;
                }
            }
        }
    }
    let mut persisted = Vec::new();
    for (idx, (input, target)) in [
        ("dicengcihui", "低层次会"),
        ("dicengcihui", "底层词汇"),
        ("anjianyizhixing", "案件已执行"),
    ]
    .iter()
    .enumerate()
    {
        let dir = PathBuf::from(format!(
            "build/sentence-score-20261030/production-learning-{}-{}",
            weight, idx
        ));
        std::fs::create_dir_all(&dir)?;
        let file = dir.join("user.tsv");
        let mut e = make()?.with_learner(Box::new(qingjian_learning::FrequencyLearner::from_path(
            &file,
        )?));
        let items = settled(&mut e, input, "");
        let c = items.iter().find(|c| c.text == *target).unwrap();
        e.commit(c);
        e.flush_learning();
        drop(e);
        let mut e = make()?.with_learner(Box::new(qingjian_learning::FrequencyLearner::from_path(
            &file,
        )?));
        let items = settled(&mut e, input, "");
        persisted.push(json!({"target":target,"top_after_reload":items[0].text,"choice_weight":e.learner().choice_weight(input,target)}));
    }
    println!(
        "{}",
        json!({"penalty":weight,"contexts":rows,"learning":learning,"persisted":persisted})
    );
    Ok(())
}
