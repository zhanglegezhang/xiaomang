//! 真命名管道的端到端测试（仅 Windows）：监听线程 + 文件句柄客户端，走完「开会话 → 敲 nihao → 收候选」。

#![cfg(windows)]

use std::fs::OpenOptions;
use std::path::PathBuf;
use std::thread;
use std::time::Duration;

use qingjian_core::Language;
use qingjian_platform::protocol::{
    ClientMessage, KeyEvent, PROTOCOL_VERSION, ServerMessage, SessionId,
};
use qingjian_windows_server::ipc::pipe::serve_pipe;
use qingjian_windows_server::ipc::{read_message, write_message};
use qingjian_windows_server::{AssemblySpec, Router, RouterConfig, assembly};

fn letter(c: char) -> KeyEvent {
    KeyEvent::new(c.to_ascii_uppercase() as u32, Some(c), Default::default())
}

/// 客户端重试打开管道，直到监听线程建好实例。
fn connect(name: &str) -> std::fs::File {
    for _ in 0..50 {
        match OpenOptions::new().read(true).write(true).open(name) {
            Ok(file) => return file,
            Err(_) => thread::sleep(Duration::from_millis(50)),
        }
    }
    panic!("连不上管道 {name}");
}

#[test]
fn named_pipe_round_trips_the_open_type_loop() {
    // Production sessions identify the real TSF thread; a made-up TID is now
    // correctly rejected by the server's kernel-backed client ownership check.
    let session = SessionId(u64::from(unsafe {
        windows::Win32::System::Threading::GetCurrentThreadId()
    }));
    let name = format!(r"\\.\pipe\qingjian-test-{}", std::process::id());

    // 监听线程服务完一个客户端后阻塞等下一个，随进程退出即可。
    let server_name = name.clone();
    thread::spawn(move || {
        let root = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../..");
        let engine = assembly::assemble(&AssemblySpec {
            glossary: Some((
                Language::English,
                root.join("assets/sample/glossary-en.tsv"),
            )),
            ..AssemblySpec::new(root.join("assets/sample/dict.tsv"))
        })
        .expect("assemble engine from sample data");
        let mut router = Router::new(engine, RouterConfig::default());
        let (work_tx, work_rx) = std::sync::mpsc::channel();
        let _ = serve_pipe(&server_name, &mut router, work_tx, work_rx);
    });

    let mut client = connect(&name);

    write_message(
        &mut client,
        &ClientMessage::OpenSession {
            session,
            app: None,
            protocol: PROTOCOL_VERSION,
        },
    )
    .unwrap();
    for c in "nihao".chars() {
        write_message(
            &mut client,
            &ClientMessage::Key {
                session,
                event: letter(c),
            },
        )
        .unwrap();
    }

    // 开会话先回一条 `SessionOpened`，之后五个按键各回一条 `KeyResult`。
    let mut last_frame = None;
    for _ in 0..6 {
        let message: ServerMessage = read_message(&mut client)
            .expect("read response")
            .expect("server closed early");
        if let ServerMessage::KeyResult { frame, .. } = message {
            last_frame = Some(frame);
        }
    }

    let frame = last_frame.expect("至少一条 KeyResult");
    let preedit: String = frame.preedit.iter().map(|s| s.text.as_str()).collect();
    assert_eq!(preedit, "ni'hao");
    let texts: Vec<&str> = frame
        .candidates
        .items
        .iter()
        .map(|c| c.text.as_str())
        .collect();
    assert!(
        texts.contains(&"你好"),
        "候选里应有「你好」，实际：{texts:?}"
    );
}

fn isolated_listener(suffix: &str) -> String {
    let name = format!(r"\\.\pipe\xm-bounded-{}-{suffix}", std::process::id());
    let server = name.clone();
    thread::spawn(move || {
        let root = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../..");
        let engine =
            assembly::assemble(&AssemblySpec::new(root.join("assets/sample/dict.tsv"))).unwrap();
        let mut router = Router::new(engine, RouterConfig::default());
        let (tx, rx) = std::sync::mpsc::channel();
        let _ = serve_pipe(&server, &mut router, tx, rx);
    });
    name
}
#[test]
fn invalid_identity_oversized_and_half_frames_do_not_break_next_connection() {
    use std::io::{Read, Write};
    use std::time::Instant;
    let name = isolated_listener("bad-frame");
    let closed = |file: &mut std::fs::File| {
        let mut byte = [0];
        assert!(!matches!(file.read(&mut byte),Ok(n) if n>0));
    };
    {
        let mut client = connect(&name);
        write_message(
            &mut client,
            &ClientMessage::OpenSession {
                session: SessionId(u64::MAX),
                app: None,
                protocol: PROTOCOL_VERSION,
            },
        )
        .unwrap();
        closed(&mut client);
    }
    {
        let mut client = connect(&name);
        client
            .write_all(&(256 * 1024u32 + 1).to_le_bytes())
            .unwrap();
        let timer = Instant::now();
        closed(&mut client);
        assert!(timer.elapsed() < Duration::from_secs(2));
    }
    {
        let mut client = connect(&name);
        client.write_all(&100u32.to_le_bytes()).unwrap();
        client.write_all(b"half").unwrap();
        let timer = Instant::now();
        closed(&mut client);
        assert!(timer.elapsed() < Duration::from_secs(8));
    }
    let mut client = connect(&name);
    let session = SessionId(u64::from(unsafe {
        windows::Win32::System::Threading::GetCurrentThreadId()
    }));
    write_message(
        &mut client,
        &ClientMessage::OpenSession {
            session,
            app: None,
            protocol: PROTOCOL_VERSION,
        },
    )
    .unwrap();
    assert!(
        matches!(read_message::<_,ServerMessage>(&mut client).unwrap(),Some(ServerMessage::SessionOpened{session:id,..}) if id==session)
    );
}
#[test]
fn unauthenticated_idle_connection_has_a_deadline() {
    use std::io::Read;
    use std::time::Instant;
    let name = isolated_listener("idle");
    let mut idle = connect(&name);
    let clock = Instant::now();
    let mut byte = [0];
    assert!(!matches!(idle.read(&mut byte),Ok(n) if n>0));
    assert!(clock.elapsed() < Duration::from_secs(8));
}
