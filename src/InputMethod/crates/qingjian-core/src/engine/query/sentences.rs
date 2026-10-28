//! 整句组合候选及模型重排；保留少量不同完整路径供用户选择。
use qingjian_dictionary::SyllablePattern;

use super::super::{Engine, RESCORE_PATHS};
use super::{EnglishTail, leading_english};
use crate::parser::{self, Segmentation};
use crate::sentence::{self, Conversion};
use crate::{Candidate, CandidateKind, Learner};

impl Engine {
    /// 整句候选。没有英文尾段时是整段拼音的转换（[`Self::plain_sentence`]），排在开头的英文候选之后。
    /// 有英文尾段且英文读法胜出（`head_wins`）时，头段的转换加上那个词排第一（`woxiangxuehaorust` → 我想学好rust），
    /// 整段也能读成拼音的再把拼音读法的整句放在第二；英文读法输了就不出（`diaoyong` 不出 掉Yong），
    /// 免得把真正要的候选往后挤。
    /// `typos` 为假时词图里不加敲错边（整段一处编辑的纠错已经生效，不在纠正后的拼音上再猜第二处）。
    pub(in crate::engine) fn insert_sentence(
        &self,
        items: &mut Vec<Candidate>,
        segmentations: &[Segmentation],
        typos: bool,
        english_tail: Option<&EnglishTail>,
        head_wins: bool,
    ) {
        let Some(best) = segmentations.first() else {
            return;
        };
        let keys = self.composition.scope();
        let first_segmentation = |text: &str| parser::segment(text).ok()?.into_iter().next();
        match english_tail {
            Some(tail) if head_wins => {
                if let Some(mixed) = self.mixed_sentence(best, tail, typos) {
                    items.insert(0, mixed);
                }
                if tail.competes
                    && let Some(full) = first_segmentation(keys)
                    && let Some(plain) = self.plain_sentence(items, &full, typos)
                {
                    let position = items.len().min(1);
                    items.insert(position, plain);
                }
            }
            _ => {
                for plain in self.plain_sentences(items, best, typos).into_iter().rev() {
                    let mut position = leading_english(items);
                    // Respect a selected exact two-syllable word before generated combinations.
                    if self.chinese_only_candidates
                        && plain.kind == CandidateKind::Sentence
                        && best.syllables.len() == 2
                    {
                        let letters = best.joined("");
                        if let Some(offset) = items[position..].iter().position(|candidate| {
                            candidate.kind == CandidateKind::Chinese
                                && candidate.syllables.concat() == letters
                                && self.learner.choice_weight(&letters, &candidate.text) > 0
                        }) {
                            position += offset + 1;
                        }
                    }
                    items.insert(position, plain);
                }
            }
        }
    }

    /// 整段拼音的整句候选：最优切分至少两个音节、且最优路径不止一个词时才有（空格上屏的就是它）。
    /// 整段本身就是词库里的词时不重复；有音节没转成字的不算句子。
    /// 词级候选里已有同文本同读音的候选时不出（那条留在词级排序给它的位置），同文本不同读音的从 `items` 里去掉。
    pub(in crate::engine) fn plain_sentence(
        &self,
        items: &mut Vec<Candidate>,
        best: &Segmentation,
        typos: bool,
    ) -> Option<Candidate> {
        self.plain_sentences(items, best, typos).into_iter().next()
    }

    fn plain_sentences(
        &self,
        items: &mut Vec<Candidate>,
        best: &Segmentation,
        typos: bool,
    ) -> Vec<Candidate> {
        if best.syllables.len() < 2 {
            return Vec::new();
        }
        let mut paths = self.sentence_paths(&best.patterns(), typos, false, RESCORE_PATHS);
        let letters = best.joined("");
        let exact_word = items
            .iter()
            .any(|c| c.kind == CandidateKind::Chinese && c.syllables.concat() == letters);
        if paths.first().is_some_and(Conversion::altered) && exact_word {
            paths = self.sentence_paths(&best.patterns(), false, false, RESCORE_PATHS);
        }
        // 完整词已在词级列表中时，保持其原排序，不拿其他拼接句挤掉它。
        if paths.first().is_some_and(|p| {
            items
                .iter()
                .any(|c| c.text == p.text && c.syllables == p.syllables)
        }) {
            return Vec::new();
        }
        let complete = paths
            .first()
            .is_some_and(|p| p.syllables.len() == best.syllables.len());
        let mut result: Vec<Candidate> = Vec::new();
        for conversion in paths {
            if conversion.has_placeholder()
                || (!result.is_empty()
                    && (!complete || conversion.syllables.len() != best.syllables.len()))
            {
                continue;
            }
            let kind = if conversion.word_count() >= 2 {
                CandidateKind::Sentence
            } else if conversion.altered() && conversion.syllables.len() == best.syllables.len() {
                CandidateKind::Chinese
            } else {
                continue;
            };
            if result.iter().any(|c| c.text == conversion.text) {
                continue;
            }
            if let Some(index) = items.iter().position(|c| c.text == conversion.text) {
                if items[index].syllables == conversion.syllables {
                    continue;
                }
                items.remove(index);
            }
            result.push(Candidate {
                text: conversion.text,
                kind,
                syllables: conversion.syllables,
                reading: None,
                translation: None,
                aux_code: None,
            });
            if result.len() == 3 {
                break;
            }
        }
        result
    }

    /// 跑一次整句转换：主词库 + 用户词（含模糊音与敲错写法，命中的按代价扣分），静态语言模型与个人 n-gram 插值，用户选择次数加分。
    /// `typos` 为假时不加敲错边。
    pub(in crate::engine) fn convert_sentence(
        &self,
        patterns: &[qingjian_dictionary::SyllablePattern<'_>],
        typos: bool,
    ) -> Option<Conversion> {
        self.convert_sentence_with(patterns, typos, false)
    }

    /// 同 [`Self::convert_sentence`]，`whole` 为真时末尾单字母也读（[`sentence::convert_whole`]），只给比分用。
    /// 接了神经重打分器时取前 [`RESCORE_PATHS`] 条路径，按「路径分 + λ·(神经分 − 静态分)」重排（[`Self::rescore_paths`]）：
    /// 神经分替换的是静态二元模型那部分判断，个人 n-gram 插值、用户加分、敲错代价原样保留，尺度也不变（纠错代价等常数照旧适用）。
    /// 返回重排后的第一条（`score` 换成重排后的分，好与别的读法比）；只有一条路径或模型还没给分时原样返回。
    pub(in crate::engine) fn convert_sentence_with(
        &self,
        patterns: &[qingjian_dictionary::SyllablePattern<'_>],
        typos: bool,
        whole: bool,
    ) -> Option<Conversion> {
        self.sentence_paths(patterns, typos, whole, 1)
            .into_iter()
            .next()
    }

    pub(in crate::engine) fn sentence_paths(
        &self,
        patterns: &[SyllablePattern<'_>],
        typos: bool,
        whole: bool,
        limit: usize,
    ) -> Vec<Conversion> {
        // 完整拼音先只按原读音与已开启的模糊音组句；能覆盖全部音节时，不再把合法音节当作误敲替换。
        // 简拼和未完成音节仍沿用原有纠错；没有完整路径时才扩展键盘敲错边。
        if typos && patterns.iter().all(|pattern| pattern.complete) {
            let original = self.sentence_paths(patterns, false, whole, limit);
            let complete = |path: &Conversion| {
                !path.has_placeholder() && path.syllables.len() == patterns.len()
            };
            if original.iter().any(complete) {
                return original.into_iter().filter(complete).collect();
            }
        }
        let dictionaries = self.all_dictionaries();
        let expanded = self.expand_positions(patterns, typos);
        let k = if self.has_sentence_scorer() {
            RESCORE_PATHS
        } else {
            limit
        };
        let mut paths = sentence::convert_paths(
            &dictionaries,
            &expanded.positions(),
            whole,
            k,
            &*self.language_model,
            self.personal(),
            |text| self.learner.weight(text),
            |index, syllable| expanded.cost(index, syllable),
            &mut self.span_cache.borrow_mut(),
        );
        // 与最优路径差得太远的不参与：那种差距多半是个人 n-gram 拉开的
        if paths.len() > 1 {
            let floor = paths[0].score - self.neural_margin;
            paths.retain(|p| p.score >= floor);
            self.rescore_paths(&mut paths);
        }
        // 保留较长完整词前缀的最佳完成，避免多个相同短前缀占满候选。
        if limit > 1 && patterns.len() >= 3 && patterns.iter().all(|p| p.complete) {
            for end in (2..patterns.len()).rev() {
                let positions = patterns[..end].iter().map(|p| vec![*p]).collect::<Vec<_>>();
                let mut hits = self.lookup_exact_all(&positions);
                hits.sort_by_key(|hit| std::cmp::Reverse(hit.frequency));
                let Some(hit) = hits.first() else {
                    continue;
                };
                let prefix = sentence::SpanWord {
                    text: hit.text.to_owned(),
                    syllables: hit.syllables().map(str::to_owned).collect(),
                    frequency: hit.frequency,
                    penalty: 0.0,
                    reading_penalty: 0.0,
                };
                let anchored = sentence::convert_paths_with_prefix(
                    &dictionaries,
                    &expanded.positions(),
                    whole,
                    1,
                    &*self.language_model,
                    self.personal(),
                    |text| self.learner.weight(text),
                    |index, syllable| expanded.cost(index, syllable),
                    &mut self.span_cache.borrow_mut(),
                    Some(&prefix),
                )
                .into_iter()
                .next();
                if let Some(path) =
                    anchored.filter(|p| !p.has_placeholder() && p.syllables.len() == patterns.len())
                    && !paths.iter().take(3).any(|p| p.text == path.text)
                {
                    paths.retain(|p| p.text != path.text);
                    paths.insert(paths.len().min(1), path);
                }
                break;
            }
        }
        paths.truncate(limit);
        paths
    }
}
