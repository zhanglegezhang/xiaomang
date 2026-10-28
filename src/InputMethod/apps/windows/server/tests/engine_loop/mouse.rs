use crate::support::{
    ClientMessage, Frame, PROTOCOL_VERSION, Router, RouterConfig, SESSION, ServerMessage,
    SessionId, function_key, press, rect, router_with, type_letters,
};

fn show(router: &mut Router) -> Frame {
    let (_, _, frame) = type_letters(router, "nihao");
    router.handle(ClientMessage::PositionCandidates {
        session: SESSION,
        rect: rect(),
    });
    frame
}

#[test]
fn mouse_commits_current_candidate_once() {
    let mut router = router_with(RouterConfig::default());
    let frame = show(&mut router);
    let expected = frame.candidates.items[0].text.clone();
    router.click_candidate(SESSION, frame.clone(), 0);
    match router.handle(ClientMessage::Poll { session: SESSION }) {
        Some(ServerMessage::CandidateCommitted { commit, .. }) => assert_eq!(commit, expected),
        other => panic!("expected mouse commit, got {other:?}"),
    }
    router.click_candidate(SESSION, frame, 0);
    assert!(matches!(
        router.handle(ClientMessage::Poll { session: SESSION }),
        Some(ServerMessage::Update { .. })
    ));
}

#[test]
fn horizontal_second_candidate_can_be_selected_and_typing_continues() {
    let mut router = router_with(RouterConfig {
        layout: qingjian_platform::LayoutMode::Horizontal,
        ..RouterConfig::default()
    });
    let frame = show(&mut router);
    let expected = frame.candidates.items[1].text.clone();
    router.click_candidate(SESSION, frame, 1);
    match router.handle(ClientMessage::Poll { session: SESSION }) {
        Some(ServerMessage::CandidateCommitted { commit, .. }) => assert_eq!(commit, expected),
        other => panic!("expected horizontal second candidate commit, got {other:?}"),
    }
    let (_, _, continued) = type_letters(&mut router, "a");
    assert!(!continued.preedit.is_empty());
}

#[test]
fn old_dll_wrong_session_blank_and_changed_frame_do_not_commit() {
    for case in 0..4 {
        let mut router = router_with(RouterConfig::default());
        if case == 0 {
            router.handle(ClientMessage::OpenSession {
                session: SESSION,
                app: None,
                protocol: 7,
            });
        }
        let frame = show(&mut router);
        router.click_candidate(
            if case == 1 { SessionId(99) } else { SESSION },
            frame,
            if case == 2 { 99 } else { 0 },
        );
        if case == 3 {
            press(&mut router, function_key(0x1b));
        }
        assert!(
            matches!(
                router.handle(ClientMessage::Poll { session: SESSION }),
                Some(ServerMessage::Update { .. })
            ),
            "case {case}"
        );
    }
    assert!(PROTOCOL_VERSION >= 8);
}

#[test]
fn clicking_on_second_page_uses_page_relative_index() {
    let mut router = router_with(RouterConfig {
        page_size: 1,
        ..RouterConfig::default()
    });
    let mut frame = show(&mut router);
    assert!(frame.page_count > 1);
    frame = press(&mut router, function_key(0x22)).2;
    assert_eq!(frame.page, 1);
    let expected = frame.candidates.items[0].text.clone();
    router.click_candidate(SESSION, frame, 0);
    match router.handle(ClientMessage::Poll { session: SESSION }) {
        Some(ServerMessage::CandidateCommitted { commit, .. }) => assert_eq!(commit, expected),
        other => panic!("expected second page commit, got {other:?}"),
    }
}

fn poll_frame(router: &mut Router) -> Frame {
    match router.handle(ClientMessage::Poll { session: SESSION }) {
        Some(ServerMessage::Update { frame, .. }) => frame,
        other => panic!("expected frame update, got {other:?}"),
    }
}

#[test]
fn wheel_pages_both_layouts_and_keeps_keyboard_and_click_selection() {
    use qingjian_platform::LayoutMode;
    for layout in [LayoutMode::Vertical, LayoutMode::Horizontal] {
        let mut router = router_with(RouterConfig {
            page_size: 1,
            layout,
            ..RouterConfig::default()
        });
        let first = show(&mut router);
        assert!(first.page_count > 1);
        router.scroll_candidates(SESSION, first.clone(), -1);
        assert_eq!(poll_frame(&mut router).page, 0);
        router.scroll_candidates(SESSION, first.clone(), 1);
        let second = poll_frame(&mut router);
        assert_eq!(second.page, 1);
        router.scroll_candidates(SESSION, second, -1);
        assert_eq!(poll_frame(&mut router).page, 0);
        let next = press(&mut router, function_key(0x22)).2;
        assert_eq!(next.page, 1);
        let expected = next.candidates.items[0].text.clone();
        router.click_candidate(SESSION, next, 0);
        assert!(
            matches!(router.handle(ClientMessage::Poll { session: SESSION }),
            Some(ServerMessage::CandidateCommitted { commit, .. }) if commit == expected)
        );
        assert!(!type_letters(&mut router, "a").2.preedit.is_empty());
    }
}

#[test]
fn wheel_bursts_do_not_lose_ticks_and_stop_at_boundaries() {
    let mut router = router_with(RouterConfig {
        page_size: 1,
        ..RouterConfig::default()
    });
    let first = show(&mut router);
    // UI 尚未来得及重绘时，连续消息仍携带第一帧。
    for _ in 0..3 {
        router.scroll_candidates(SESSION, first.clone(), 1);
    }
    assert_eq!(poll_frame(&mut router).page, 3.min(first.page_count - 1));
    router.scroll_candidates(SESSION, first.clone(), 256);
    assert_eq!(poll_frame(&mut router).page, first.page_count - 1);
    router.scroll_candidates(SESSION, first.clone(), 1);
    assert_eq!(poll_frame(&mut router).page, first.page_count - 1);
    router.scroll_candidates(SESSION, first, -256);
    assert_eq!(poll_frame(&mut router).page, 0);
}

#[test]
fn stale_hidden_and_wrong_session_wheel_events_are_ignored() {
    let mut router = router_with(RouterConfig {
        page_size: 1,
        ..RouterConfig::default()
    });
    let first = show(&mut router);
    router.scroll_candidates(SessionId(99), first.clone(), 1);
    assert_eq!(poll_frame(&mut router).page, 0);
    type_letters(&mut router, "a");
    router.scroll_candidates(SESSION, first.clone(), 1);
    assert_eq!(poll_frame(&mut router).page, 0);
    press(&mut router, function_key(0x1b));
    router.scroll_candidates(SESSION, first, 1);
    assert!(poll_frame(&mut router).is_empty());
}
