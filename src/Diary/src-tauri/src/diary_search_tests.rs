use super::*;

struct Fixture(PathBuf);
impl Fixture {
    fn new() -> Self {
        let path = std::env::temp_dir().join(format!("xiaomang-chinese-search-test-{}", Uuid::new_v4()));
        Diary::open(path.clone()).unwrap(); Self(path)
    }
    fn write(&self, path: &str, text: &str) { fs::write(self.0.join("notes").join(path), text).unwrap(); }
    fn search(&self, query: &str) -> Value { dispatch(&self.0, "search", json!({"query":query})).unwrap() }
}
impl Drop for Fixture { fn drop(&mut self) { let _ = fs::remove_dir_all(&self.0); } }

#[test]
fn ordered_matching_preserves_order_repetition_and_total_unicode_gap() {
    let data = Fixture::new(); let permit = begin_search(&data.0, "bounds").unwrap();
    for (line, query, expected) in [
        ("经常用圆规画图", "常规", true), ("圆规经常使用", "常规", false),
        ("哈", "哈哈", false), ("哈啊哈", "哈哈", true),
        ("经常使用圆规做设置", "常规设置", true), ("常规设", "常规设置", false),
        ("常", "常", false), ("常规", "", false),
    ] { assert_eq!(ordered_search_match(line, &query.chars().collect::<Vec<_>>(), &permit).unwrap(), expected, "{line} / {query}"); }
    for gap in [20, 21] {
        let text = format!("常{}规", "😀".repeat(gap));
        assert_eq!(ordered_search_match(&text, &['常','规'], &permit).unwrap(), gap == 20);
    }
    for gap in [10, 11] {
        let text = format!("常{}规{}设", "甲".repeat(10), "乙".repeat(gap));
        assert_eq!(ordered_search_match(&text, &['常','规','设'], &permit).unwrap(), gap == 10);
    }
}

#[test]
fn ordered_matching_agrees_with_independent_greedy_window_oracle() {
    fn oracle(text: &[char], query: &[char]) -> bool {
        for start in 0..text.len() {
            if text[start] != query[0] { continue; }
            let mut next = 1;
            for end in start+1..text.len() {
                if text[end] == query[next] {
                    next += 1;
                    if next == query.len() {
                        if end-start+1-query.len() <= MAX_SEARCH_GAP { return true; }
                        break;
                    }
                }
            }
        }
        false
    }
    let data = Fixture::new(); let permit = begin_search(&data.0, "oracle").unwrap();
    let mut seed = 719_u64;
    let mut random = || { seed = seed.wrapping_mul(6364136223846793005).wrapping_add(1); (seed >> 32) as usize };
    for _ in 0..2000 {
        let text: Vec<char> = (0..48+random()%49).map(|_| ['常','规','设','甲','乙','😀'][random()%6]).collect();
        let query: Vec<char> = (0..2+random()%7).map(|_| ['常','规','设'][random()%3]).collect();
        assert_eq!(ordered_search_match(&text.iter().collect::<String>(), &query, &permit).unwrap(), oracle(&text, &query));
    }
}

#[test]
fn search_covers_txt_md_names_folder_pages_and_prefers_literal_snippets() {
    let data = Fixture::new();
    data.write("a.txt", "经常用圆规画图\n经常用圆规画图\n经常用圆规画图\n经常用圆规画图");
    data.write("常用规章.md", "不含目标的正文");
    fs::create_dir(data.0.join("notes/资料")).unwrap();
    data.write("资料/.xiaomang-folder.md", "平常使用圆规");
    data.write("z.md", "经常用圆规画图\n常规\n常规\n常规\n常规");
    let found = data.search(" 常规 "); let rows = found["results"].as_array().unwrap();
    assert_eq!(rows.len(), 4); assert_eq!(rows[0]["path"], "z.md");
    assert_eq!(rows[0]["matches"][0]["line"], 2);
    assert_eq!(rows[0]["matches"].as_array().unwrap().len(), 3);
    assert_eq!(rows.iter().find(|r| r["path"] == "a.txt").unwrap()["matches"].as_array().unwrap().len(), 3);
    assert_eq!(rows.iter().find(|r| r["path"] == "资料").unwrap()["folder"], true);
    assert_eq!(found["limited"], false);
}

#[test]
fn search_keeps_line_boundaries_literal_english_numbers_and_no_pinyin() {
    let data = Fixture::new();
    data.write("a.txt", "常\n规\nA---B\n1---2\n常---A\n经常用圆规画图");
    data.write("b.md", "常规\nAB\n12\n常A\n𠀀甲规");
    for query in ["AB", "12", "常A"] {
        let found = data.search(query);
        assert_eq!(found["results"].as_array().unwrap().len(), 1);
        assert_eq!(found["results"][0]["path"], "b.md");
    }
    assert_eq!(data.search("changgui")["results"].as_array().unwrap().len(), 0);
    assert_eq!(data.search("𠀀规")["results"].as_array().unwrap().len(), 1);
    assert_eq!(data.search(" ")["results"].as_array().unwrap().len(), 0);
    data.write("a.txt", "常\n规"); data.write("b.md", "无结果");
    assert_eq!(data.search("常规")["results"].as_array().unwrap().len(), 0);
    assert_eq!(data.search("常")["results"].as_array().unwrap().len(), 1);
}

#[test]
fn search_result_limit_counts_combined_hits_and_preserves_late_literal() {
    let data = Fixture::new();
    for i in 0..199 { data.write(&format!("note-{i:03}.txt"), "经常用圆规画图"); }
    let first = data.search("常规");
    assert_eq!(first["results"].as_array().unwrap().len(), 199); assert_eq!(first["limited"], false);
    data.write("note-199.txt", "经常用圆规画图");
    assert_eq!(data.search("常规")["limited"], false);
    for i in 200..240 { data.write(&format!("note-{i:03}.txt"), "经常用圆规画图"); }
    data.write("zzz.txt", "常规");
    let found = data.search("常规"); let rows = found["results"].as_array().unwrap();
    assert_eq!(rows.len(), 200); assert_eq!(rows[0]["path"], "zzz.txt"); assert_eq!(found["limited"], true);
    let paths: HashSet<_> = rows.iter().map(|r| r["path"].as_str().unwrap()).collect(); assert_eq!(paths.len(), 200);
    for i in 0..200 { data.write(&format!("note-{i:03}.txt"), "常规"); }
    let exact = data.search("常规");
    assert_eq!(exact["results"].as_array().unwrap().len(), 200); assert_eq!(exact["limited"], true);
}

#[test]
fn chinese_keyword_boundary_keeps_sixteen_character_fuzzy_search_and_long_literals() {
    for count in [16usize, 17, 4096] {
        let data = Fixture::new();
        let query = format!("{}规", "常".repeat(count-1));
        data.write("a.txt", &format!("{}甲规", "常".repeat(count-1)));
        data.write("z.md", &format!("前文{query}后文"));
        let found = data.search(&format!("  {query}  "));
        assert_eq!(found["literalOnly"], count > 16);
        let rows = found["results"].as_array().unwrap();
        assert_eq!(rows.len(), if count == 16 { 2 } else { 1 });
        assert_eq!(rows[0]["path"], "z.md");
        assert_eq!(rows[0]["matches"][0]["line"], 1);
    }
}

#[test]
fn long_literal_search_still_covers_titles_folder_pages_and_uses_unicode_character_count() {
    for count in [16usize, 17] {
        let data = Fixture::new();
        let query = format!("{}规", "𠀀".repeat(count-1));
        data.write("a.txt", &format!("{}甲规", "𠀀".repeat(count-1)));
        data.write(&format!("{query}.md"), "不含关键词的正文");
        fs::create_dir(data.0.join("notes/目录")).unwrap();
        data.write("目录/.xiaomang-folder.md", &query);
        let found = data.search(&query);
        assert_eq!(found["literalOnly"], count > 16);
        let rows = found["results"].as_array().unwrap();
        assert_eq!(rows.len(), if count == 16 { 3 } else { 2 });
        assert!(rows.iter().any(|row| row["path"] == format!("{query}.md")));
        assert!(rows.iter().any(|row| row["folder"] == true));
    }
    let data = Fixture::new();
    for query in ["", "常", "常规", "abcdefghijklmnopqrstuvwxyz", "常规abcdefghijklmnop", "12345678901234567"] {
        assert_eq!(data.search(query)["literalOnly"], false, "{query}");
    }
}

#[test]
fn unreadable_notes_are_skipped_and_cancelled_searches_do_not_return_results() {
    let data = Fixture::new(); data.write("a.txt", "经常用圆规画图");
    fs::write(data.0.join("notes/b.txt"), [0xff, 0xff]).unwrap();
    let found = data.search("常规");
    assert_eq!(found["skipped"], 1); assert_eq!(found["results"].as_array().unwrap().len(), 1);
    let permit = begin_search(&data.0, "cancelled").unwrap(); cancel_search(&data.0, "cancelled").unwrap();
    assert_eq!(dispatch_search(&data.0, json!({"query":"常规"}), permit).unwrap_err(), "搜索已取消");
}

#[test]
fn long_line_and_long_keyword_matching_can_be_cancelled_mid_scan() {
    let data = Fixture::new(); let permit = begin_search(&data.0, "long").unwrap();
    let flag = permit.canceled.clone();
    let query: Vec<_> = format!("{}规", "常".repeat(4096)).chars().collect();
    let text = format!("{}{}规", "常".repeat(1_000_000), "甲".repeat(21));
    let started = std::time::Instant::now();
    let cancel = std::thread::spawn(move || { std::thread::sleep(std::time::Duration::from_millis(20)); flag.store(true, Ordering::Release); });
    assert_eq!(ordered_search_match(&text, &query, &permit).unwrap_err(), "搜索已取消");
    cancel.join().unwrap();
    assert!(started.elapsed() < std::time::Duration::from_secs(2), "Old search did not yield promptly");
}

#[test]
fn chinese_scan_allows_read_save_and_is_replaced_by_newer_search() {
    let data = Fixture::new();
    let body = format!("{}{}规", "常".repeat(500_000), "甲".repeat(21));
    for i in 0..32 { data.write(&format!("n{i:02}.txt"), &body); }
    let root = data.0.clone(); let permit = begin_search(&root, "slow-chinese").unwrap();
    let worker = std::thread::spawn(move || dispatch_search(&root, json!({"query":format!("{}规", "常".repeat(15))}), permit));
    std::thread::sleep(std::time::Duration::from_millis(80));
    assert!(!worker.is_finished(), "Stress search finished before the concurrent edit");
    let started = std::time::Instant::now();
    let opened = dispatch(&data.0, "read", json!({"path":"n00.txt"})).unwrap();
    dispatch(&data.0, "save", json!({"path":"n00.txt","content":"完成保存","revision":opened["revision"]})).unwrap();
    let save_ms = started.elapsed().as_millis();
    assert!(save_ms < 1500, "Read/save blocked for {save_ms} ms");
    let latest = begin_search(&data.0, "new-chinese").unwrap();
    let canceled = std::time::Instant::now();
    assert_eq!(worker.join().unwrap().unwrap_err(), "搜索已取消");
    let cancel_ms = canceled.elapsed().as_millis(); assert!(cancel_ms < 1500);
    let result = dispatch_search(&data.0, json!({"query":"完成保存"}), latest).unwrap();
    assert_eq!(result["results"].as_array().unwrap().len(), 1);
    assert_eq!(result["results"][0]["path"], "n00.txt");
    eprintln!("CHINESE_CONCURRENT read_save_ms={save_ms} old_cancel_ms={cancel_ms}");
}
