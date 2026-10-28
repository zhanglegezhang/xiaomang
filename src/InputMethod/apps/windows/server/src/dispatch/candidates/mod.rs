//! 候选窗口输出：Router 只产出 [`Frame`] 与光标矩形，交给 [`CandidateSink`] 去画；帧没变就不重画。

mod action;
mod sink;

use qingjian_platform::protocol::{Frame, ScreenRect, SessionId};

pub use self::action::CandidateAction;
pub use self::sink::{CandidateSink, NoopSink, RenderSettings};
use super::Router;

impl Router {
    pub fn scroll_candidates(&mut self, session: SessionId, frame: Frame, pages: isize) {
        // 连续滚轮事件可能带着翻页前的帧；允许同一组句的页码变化，但拒绝切换会话或重新输入后的旧事件。
        let current = self.self_drawn_frame();
        if pages == 0
            || self.focused != Some(session)
            || self.last_shown.is_none()
            || current.preedit != frame.preedit
            || current.cursor != frame.cursor
            || current.preedit_mode != frame.preedit_mode
            || current.page_count <= 1
        {
            return;
        }
        self.pending_click = None;
        self.page(pages.clamp(-256, 256));
        let shown = self.self_drawn_frame();
        self.reconcile_candidates(&shown);
    }

    pub fn click_candidate(&mut self, session: SessionId, frame: Frame, index: usize) {
        if self.focused == Some(session)
            && self.sessions.get(&session).is_some_and(|s| s.protocol >= 8)
            && self
                .last_shown
                .as_ref()
                .is_some_and(|(shown, _)| *shown == frame)
            && index < frame.candidates.items.len()
        {
            self.pending_click = Some((session, frame, index));
        }
    }

    /// 空帧收窗口；非空且已知光标矩形就重绘；还没收到矩形（组句刚起）先不显示，免得在旧位置闪一下。
    pub(super) fn reconcile_candidates(&mut self, frame: &Frame) {
        if frame.is_empty() {
            self.engine.note_displayed(std::iter::empty());
            self.hide_candidate_window();
        } else if let Some(rect) = self.last_rect {
            let unchanged = matches!(&self.last_shown, Some((f, r)) if f == frame && *r == rect);
            if !unchanged {
                // 词汇记录的「看到轮次」按真正显示的页算，与 macOS 壳对齐。
                self.engine.note_displayed(frame.candidates.items.iter());
                if let Some(session) = self.focused {
                    self.candidates
                        .show_for_session(frame.clone(), rect, session);
                }
                self.last_shown = Some((frame.clone(), rect));
            }
        }
    }

    pub(super) fn hide_candidate_window(&mut self) {
        self.last_rect = None;
        self.last_shown = None;
        self.pending_click = None;
        self.candidates.hide();
    }

    pub(super) fn position_candidates(&mut self, session: SessionId, rect: ScreenRect) {
        if self.focused != Some(session) {
            return;
        }
        self.last_rect = Some(rect);
        // 自绘窗吃未降级的帧（降级只作用于发给 DLL 的那份）
        let frame = self.self_drawn_frame();
        self.reconcile_candidates(&frame);
    }
}
