//! 单字读音的相对词频；由现有词库延迟建立，不改变分发文件格式。
use std::collections::HashMap;

use super::Dictionary;
use crate::Match;

impl Dictionary {
    /// 罕见单字读音相对该字最常见读音的 log 代价。词组和只有一个读音的字不扣分。
    pub fn reading_penalty(&self, hit: &Match<'_>) -> f64 {
        let mut chars = hit.text.chars();
        let Some(ch) = chars.next() else {
            return 0.0;
        };
        if chars.next().is_some() {
            return 0.0;
        }
        let frequencies = self.character_frequencies.get_or_init(|| {
            let mut frequencies = HashMap::<char, u32>::new();
            for entry in self.entries() {
                let mut chars = entry.text.chars();
                if let Some(ch) = chars.next()
                    && chars.next().is_none()
                {
                    frequencies
                        .entry(ch)
                        .and_modify(|n| *n = (*n).max(entry.frequency))
                        .or_insert(entry.frequency);
                }
            }
            frequencies
        });
        let maximum = frequencies
            .get(&ch)
            .copied()
            .unwrap_or(hit.frequency)
            .max(1);
        (f64::from(maximum) / f64::from(hit.frequency.max(1)))
            .ln()
            .max(0.0)
    }
}

#[cfg(test)]
mod tests {
    use super::Dictionary;

    #[test]
    fn reading_cost_survives_packing_and_does_not_penalize_phrases() {
        let dictionary =
            Dictionary::parse("说\tshuo\t1000\n说\tyue\t50\n说服\tshuo fu\t1\n月\tyue\t400\n")
                .unwrap();
        let costs: Vec<_> = dictionary
            .entries()
            .map(|hit| (hit.pinyin.to_owned(), dictionary.reading_penalty(&hit)))
            .collect();
        assert!(
            costs
                .iter()
                .any(|(p, c)| p == "yue" && (*c - 20.0_f64.ln()).abs() < 1e-9)
        );
        assert!(
            costs
                .iter()
                .filter(|(p, _)| p != "yue")
                .all(|(_, c)| *c == 0.0)
        );
        let dir =
            std::env::temp_dir().join(format!("qingjian-reading-test-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join("dict.qj");
        dictionary
            .write_qj(&path, &qingjian_format::Metadata::default())
            .unwrap();
        let packed = Dictionary::from_path(&path).unwrap();
        let restored: Vec<_> = packed
            .entries()
            .map(|hit| (hit.pinyin.to_owned(), packed.reading_penalty(&hit)))
            .collect();
        assert_eq!(costs, restored);
        drop(packed);
        std::fs::remove_file(path).unwrap();
        std::fs::remove_dir(dir).unwrap();
    }
}
