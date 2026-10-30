//! 词图上的最优路径：bigram Viterbi + 束搜索。
//!
//! 状态只按前一个词分（束宽内），个人三元要的前二词取前驱节点的回指（它那条最优路径上的前一个词）：
//! 不扩状态，代价是三元上下文是近似的，个人数据量下够用。

use qingjian_dictionary::{Dictionary, Match, SyllablePattern};

use super::{
    ABBREVIATED_SPAN_CANDIDATES, BEAM_WIDTH, Context, Conversion, LanguageModel,
    MAX_WORD_SYLLABLES, MIN_PARTIAL_LETTERS, Personal, SPAN_CANDIDATES, SentenceWord, SpanCache,
    SpanWord, fallback_log_prob, transition_log_prob,
};
use crate::ranking::weight_bonus;

/// Only consider unseen single-character tails when exact multi-character words
/// cover the input; never break an existing three-or-more-character word.
fn tail_guard(dictionaries: &[&Dictionary], positions: &[Vec<SyllablePattern<'_>>]) -> bool {
    if positions.len() < 4
        || positions
            .iter()
            .any(|p| p.first().is_none_or(|s| !s.complete))
    {
        return false;
    }
    let keys: Vec<_> = positions.iter().map(|p| p[0].text).collect();
    let n = keys.len();
    let exact = |start: usize, end: usize| {
        dictionaries.iter().any(|d| {
            d.lookup(&keys[start..end], false).iter().any(|h| {
                h.syllables().count() == end - start && h.text.chars().count() == end - start
            })
        })
    };
    for start in 0..n {
        for end in start + 3..=n {
            if exact(start, end) {
                return false;
            }
        }
    }
    let mut reachable = vec![false; n + 1];
    reachable[0] = true;
    for start in 0..n {
        if reachable[start] && start + 2 <= n && exact(start, start + 2) {
            reachable[start + 2] = true;
        }
    }
    reachable[n]
}

/// 词库里没有的孤立音节（罕见音节没有单字）按这个 log 概率兜底，让路径总能走通。
const UNKNOWN_LOG_PROB: f64 = -30.0;

/// 一条部分路径的末尾节点。
struct Node {
    /// 这个词从第几个音节开始。
    start: usize,

    /// 词。
    text: String,

    /// 词的音节。
    syllables: Vec<String>,

    /// 到此为止的累计得分。
    score: f64,

    /// 累计得分里静态模型的部分（见 `Conversion::static_score`）。
    static_score: f64,

    /// 前驱在 `nodes[start]` 里的下标；`start == 0` 时无意义。
    back: usize,

    /// 是占位音节。
    placeholder: bool,

    /// 到此为止路径上模糊音 / 敲错变体的代价之和（已从 `score` 里扣掉，另记一份给调用方判断路径是不是原样）。
    penalty: f64,
}

/// 把音节序列转成最可能的词序列。`positions` 每个位置是若干写法（第一种是敲的，其余是模糊音 / 敲错变体），
/// `cost(位置, 命中的音节)` 是那个位置命中这种写法要扣的分（敲的原样 0），
/// `weight` 是用户选择次数，`personal` 是个人 n-gram 与插值参数（没有个人数据就传 [`Personal::NONE`]），`cache` 是格子候选的缓存
/// （调用方保证它与词库、`weight`、`personal`、`cost` 一致，这些一变就清）。
///
/// 简拼位置（`w x q`）按前缀取词：每个格子的候选会多得多，由语言模型在路径上分辨。
/// 全拼句子末尾的前缀太短时不算它（多半是没打完的音节）；前面已有简拼的句子里末尾单字母就是一个音节。
pub fn convert(
    dictionaries: &[&Dictionary],
    positions: &[Vec<SyllablePattern<'_>>],
    model: &dyn LanguageModel,
    personal: Personal<'_>,
    weight: impl Fn(&str) -> u32,
    cost: impl Fn(usize, &str) -> f64,
    cache: &mut SpanCache,
) -> Option<Conversion> {
    convert_with(
        dictionaries,
        positions,
        false,
        model,
        personal,
        weight,
        cost,
        cache,
    )
}

/// 同 [`convert`]，但全拼句子末尾的单字母也当一个音节读（`huo z…` → 或者）：
/// 给「整段拼音读法」与别的读法比分用，比分要两边覆盖同样多的字母。
pub fn convert_whole(
    dictionaries: &[&Dictionary],
    positions: &[Vec<SyllablePattern<'_>>],
    model: &dyn LanguageModel,
    personal: Personal<'_>,
    weight: impl Fn(&str) -> u32,
    cost: impl Fn(usize, &str) -> f64,
    cache: &mut SpanCache,
) -> Option<Conversion> {
    convert_with(
        dictionaries,
        positions,
        true,
        model,
        personal,
        weight,
        cost,
        cache,
    )
}

/// [`convert`] 与 [`convert_whole`] 的共同实现，`keep_partial` 选哪种；只要最优的一条。
#[allow(clippy::too_many_arguments)]
pub fn convert_with(
    dictionaries: &[&Dictionary],
    positions: &[Vec<SyllablePattern<'_>>],
    keep_partial: bool,
    model: &dyn LanguageModel,
    personal: Personal<'_>,
    weight: impl Fn(&str) -> u32,
    cost: impl Fn(usize, &str) -> f64,
    cache: &mut SpanCache,
) -> Option<Conversion> {
    convert_paths(
        dictionaries,
        positions,
        keep_partial,
        1,
        model,
        personal,
        weight,
        cost,
        cache,
    )
    .into_iter()
    .next()
}

/// 得分最高的前 `k` 条路径（最多束宽条，按得分降序，文本相同的只留一条）：给重打分用。
#[allow(clippy::too_many_arguments)]
pub fn convert_paths(
    dictionaries: &[&Dictionary],
    positions: &[Vec<SyllablePattern<'_>>],
    keep_partial: bool,
    k: usize,
    model: &dyn LanguageModel,
    personal: Personal<'_>,
    weight: impl Fn(&str) -> u32,
    cost: impl Fn(usize, &str) -> f64,
    cache: &mut SpanCache,
) -> Vec<Conversion> {
    convert_paths_with_prefix(
        dictionaries,
        positions,
        keep_partial,
        k,
        model,
        personal,
        weight,
        cost,
        cache,
        None,
    )
}

/// 固定一个完整词前缀，完成其余音节；复用词图缓存，不扩大原束宽。
#[allow(clippy::too_many_arguments)]
pub fn convert_paths_with_prefix(
    dictionaries: &[&Dictionary],
    positions: &[Vec<SyllablePattern<'_>>],
    keep_partial: bool,
    k: usize,
    model: &dyn LanguageModel,
    personal: Personal<'_>,
    weight: impl Fn(&str) -> u32,
    cost: impl Fn(usize, &str) -> f64,
    cache: &mut SpanCache,
    prefix: Option<&SpanWord>,
) -> Vec<Conversion> {
    let Some((last, head)) = positions.split_last() else {
        return Vec::new();
    };
    let Some(&last) = last.first() else {
        return Vec::new();
    };
    let abbreviated_head = head.iter().any(|p| p.first().is_none_or(|t| !t.complete));
    let positions = if keep_partial
        || last.complete
        || abbreviated_head
        || last.text.len() >= MIN_PARTIAL_LETTERS
    {
        positions
    } else {
        head
    };
    let n = positions.len();
    if n == 0 || k == 0 {
        return Vec::new();
    }
    let total: f64 = dictionaries
        .iter()
        .map(|d| d.total_frequency() as f64)
        .sum::<f64>()
        .max(1.0);
    let log_total = total.ln();
    let conservative_tail = prefix.is_none() && tail_guard(dictionaries, positions);

    // nodes[i]：覆盖前 i 个音节、以某个词结尾的部分路径；nodes[0] 是虚拟起点
    let mut nodes: Vec<Vec<Node>> = (0..=n).map(|_| Vec::new()).collect();
    nodes[0].push(Node {
        start: 0,
        text: String::new(),
        syllables: Vec::new(),
        score: 0.0,
        static_score: 0.0,
        back: 0,
        placeholder: false,
        penalty: 0.0,
    });
    if let Some(prefix) = prefix {
        let end = prefix.syllables.len();
        if end == 0 || end >= n {
            return Vec::new();
        }
        let fallback = fallback_log_prob(prefix.frequency, log_total);
        let static_score = model.log_prob(None, &prefix.text).unwrap_or(fallback);
        nodes[end].push(Node {
            start: 0,
            text: prefix.text.clone(),
            syllables: prefix.syllables.clone(),
            score: transition_log_prob(model, personal, Context::START, &prefix.text, fallback)
                + weight_bonus(weight(&prefix.text))
                - prefix.penalty,
            static_score,
            back: 0,
            placeholder: false,
            penalty: prefix.penalty,
        });
    }
    for start in 0..n {
        if prefix.is_some() && start == 0 {
            continue;
        }
        prune(&mut nodes[start]);
        if nodes[start].is_empty() {
            continue;
        }
        let mut any = false;
        for end in start + 1..=n.min(start + MAX_WORD_SYLLABLES) {
            let span = &positions[start..end];
            let hits = cache.get_or_insert_with(SpanCache::key(span), || {
                span_candidates(dictionaries, span, start, personal, &weight, &cost)
            });
            if hits.is_empty() {
                continue;
            }
            any = true;
            for hit in hits.iter() {
                let bonus = weight_bonus(weight(&hit.text));
                let fallback = fallback_log_prob(hit.frequency, log_total);
                for (score, back) in predecessors(
                    &nodes,
                    start,
                    &hit.text,
                    model,
                    personal,
                    fallback,
                    k.min(3),
                ) {
                    let previous = &nodes[start][back];
                    let penalty = previous.penalty + hit.penalty;
                    let static_probability =
                        model.log_prob((start > 0).then_some(previous.text.as_str()), &hit.text);
                    // 词级/神经模型只看汉字，不能借常用读音的热度抬高罕见读音。
                    // 无词级概率时 fallback 已包含读音词频，不重复扣。
                    let reading_cost = if static_probability.is_some() {
                        hit.reading_penalty
                    } else {
                        0.0
                    };
                    let static_step = static_probability.unwrap_or(fallback);
                    let static_score = previous.static_score + static_step;
                    let tail_cost = if conservative_tail && end == n {
                        model.transition_penalty(
                            (start > 0).then_some(previous.text.as_str()),
                            &hit.text,
                        )
                    } else {
                        0.0
                    };
                    nodes[end].push(Node {
                        start,
                        text: hit.text.clone(),
                        syllables: hit.syllables.clone(),
                        score: score + bonus - hit.penalty - reading_cost - tail_cost,
                        static_score,
                        back,
                        placeholder: false,
                        penalty,
                    });
                }
            }
        }
        // 这个音节连单字都查不到：用音节本身占位，别让整句断掉
        if !any {
            let text = positions[start][0].text;
            let (score, back) = best_predecessor(
                &nodes,
                start,
                text,
                &NoModel,
                Personal::NONE,
                UNKNOWN_LOG_PROB,
            );
            let penalty = nodes[start][back].penalty;
            let static_score = nodes[start][back].static_score + UNKNOWN_LOG_PROB;
            nodes[start + 1].push(Node {
                start,
                text: text.to_owned(),
                syllables: vec![text.to_owned()],
                score,
                static_score,
                back,
                placeholder: true,
                penalty,
            });
        }
    }
    prune(&mut nodes[n]);
    let mut paths: Vec<Conversion> = Vec::with_capacity(k.min(nodes[n].len()));
    for index in 0..nodes[n].len() {
        if paths.len() >= k {
            break;
        }
        let conversion = backtrack(&nodes, n, index);
        if !paths.iter().any(|p| p.text == conversion.text) {
            paths.push(conversion);
        }
    }
    paths
}

/// 从 `nodes[position][index]` 回溯出整条路径。
fn backtrack(nodes: &[Vec<Node>], mut position: usize, mut index: usize) -> Conversion {
    let score = nodes[position][index].score;
    let static_score = nodes[position][index].static_score;
    let penalty = nodes[position][index].penalty;
    let mut words: Vec<SentenceWord> = Vec::new();
    while position > 0 {
        let node = &nodes[position][index];
        words.push(SentenceWord {
            text: node.text.clone(),
            syllables: node.syllables.clone(),
            placeholder: node.placeholder,
        });
        position = node.start;
        index = node.back;
    }
    words.reverse();
    let mut text = String::new();
    let mut syllables = Vec::new();
    for word in &words {
        text.push_str(&word.text);
        syllables.extend(word.syllables.iter().cloned());
    }
    Conversion {
        text,
        syllables,
        words,
        score,
        static_score,
        penalty,
    }
}

/// 占位音节不问语言模型。
struct NoModel;

impl LanguageModel for NoModel {
    fn log_prob(&self, _previous: Option<&str>, _word: &str) -> Option<f64> {
        None
    }
}

/// 一个格子里的候选词：所有词库的精确命中，按词频（加用户选择次数与个人出现次数，替代写法命中的按代价打折）取前几个。
/// 个人次数只在这里保证用户常用的同音词进得了格子，不进路径打分（那是 n-gram 的事）；
/// 打折让敲错变体命中的词只在原样命中不够多时才进格子，而常用词（关系）即使打折也留得住。
/// 格子里有简拼位置时命中的是一大片不同读音的词，多留一些让语言模型去挑。
fn span_candidates(
    dictionaries: &[&Dictionary],
    span: &[Vec<SyllablePattern<'_>>],
    start: usize,
    personal: Personal<'_>,
    weight: &impl Fn(&str) -> u32,
    cost: &impl Fn(usize, &str) -> f64,
) -> Vec<SpanWord> {
    let alternatives = span.iter().any(|p| p.len() > 1);
    let penalty_of = |m: &Match<'_>| {
        if !alternatives {
            return 0.0;
        }
        m.syllables()
            .enumerate()
            .map(|(index, syllable)| cost(start + index, syllable))
            .sum::<f64>()
    };
    // 得分先算好再排：单字母简拼的格子能命中几千条，比较器里每次查两张表会让排序占掉十几毫秒
    let mut scored: Vec<(f64, f64, f64, Match<'_>)> = dictionaries
        .iter()
        .flat_map(|d| {
            d.lookup_exact_alt(span)
                .into_iter()
                .map(|m| (m, d.reading_penalty(&m)))
        })
        .map(|(m, reading_penalty)| {
            let seen = weight(m.text) + personal.count(m.text);
            let penalty = penalty_of(&m);
            let score = f64::from(m.frequency) * (1.0 + f64::from(seen)) * (-penalty).exp();
            (score, penalty, reading_penalty, m)
        })
        .collect();
    scored.sort_by(|a, b| b.0.partial_cmp(&a.0).unwrap_or(std::cmp::Ordering::Equal));
    scored.dedup_by(|a, b| a.3.text == b.3.text);
    let abbreviated = span.iter().any(|p| p.iter().any(|t| !t.complete));
    scored.truncate(if abbreviated {
        ABBREVIATED_SPAN_CANDIDATES
    } else {
        SPAN_CANDIDATES
    });
    scored
        .into_iter()
        .map(|(_, penalty, reading_penalty, hit)| SpanWord {
            text: hit.text.to_owned(),
            syllables: hit.syllables().map(str::to_owned).collect(),
            frequency: hit.frequency,
            penalty,
            reading_penalty,
        })
        .collect()
}

/// 在 `nodes[start]` 的前驱里挑让 `word` 得分最高的那条，返回 (累计得分, 前驱下标)。
/// 转移概率先问静态模型（不认识就用词库兜底值），再与个人 n-gram 插值；前二词是前驱自己的前驱（回指）。
fn best_predecessor(
    nodes: &[Vec<Node>],
    start: usize,
    word: &str,
    model: &dyn LanguageModel,
    personal: Personal<'_>,
    fallback: f64,
) -> (f64, usize) {
    let mut best = (f64::NEG_INFINITY, 0);
    for (index, previous) in nodes[start].iter().enumerate() {
        let context = if start == 0 {
            Context::START
        } else {
            Context {
                previous: Some(previous.text.as_str()),
                earlier: (previous.start > 0)
                    .then(|| nodes[previous.start][previous.back].text.as_str()),
            }
        };
        let score = previous.score + transition_log_prob(model, personal, context, word, fallback);
        if score > best.0 {
            best = (score, index);
        }
    }
    best
}

/// 多候选保留到达同一末词的不同前缀；束宽仍限制为原来的八条。
fn predecessors(
    nodes: &[Vec<Node>],
    start: usize,
    word: &str,
    model: &dyn LanguageModel,
    personal: Personal<'_>,
    fallback: f64,
    limit: usize,
) -> Vec<(f64, usize)> {
    if limit <= 1 {
        return vec![best_predecessor(
            nodes, start, word, model, personal, fallback,
        )];
    }
    let mut paths: Vec<_> = nodes[start]
        .iter()
        .enumerate()
        .map(|(index, previous)| {
            let context = if start == 0 {
                Context::START
            } else {
                Context {
                    previous: Some(previous.text.as_str()),
                    earlier: (previous.start > 0)
                        .then(|| nodes[previous.start][previous.back].text.as_str()),
                }
            };
            (
                previous.score + transition_log_prob(model, personal, context, word, fallback),
                index,
            )
        })
        .collect();
    paths.sort_by(|a, b| b.0.total_cmp(&a.0));
    paths.truncate(limit);
    paths
}

/// 按得分降序只留束宽条。
fn prune(nodes: &mut Vec<Node>) {
    nodes.sort_by(|a, b| {
        b.score
            .partial_cmp(&a.score)
            .unwrap_or(std::cmp::Ordering::Equal)
    });
    nodes.truncate(BEAM_WIDTH);
}

#[cfg(test)]
mod tests;
