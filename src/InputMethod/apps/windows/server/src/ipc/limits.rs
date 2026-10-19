//! 管道连接的会话归属及资源上限，重连后旧连接不能清理新会话。
use qingjian_platform::protocol::{ClientMessage, SessionId};
use std::collections::HashMap;

pub const MAX_CONNECTIONS: usize = 128;
pub const MAX_SESSIONS: usize = 512;
pub const MAX_SESSIONS_PER_CONNECTION: usize = 16;
pub const MAX_FRAME: u32 = 256 * 1024;

#[derive(Default)]
pub struct Ownership {
    sessions: HashMap<SessionId, (u64, u32)>,
}
pub fn session(message: &ClientMessage) -> SessionId {
    match message {
        ClientMessage::OpenSession { session, .. }
        | ClientMessage::Key { session, .. }
        | ClientMessage::Commit { session }
        | ClientMessage::Poll { session }
        | ClientMessage::Surrounding { session, .. }
        | ClientMessage::Privacy { session, .. }
        | ClientMessage::Selection { session, .. }
        | ClientMessage::PositionCandidates { session, .. }
        | ClientMessage::HideCandidates { session }
        | ClientMessage::ModeChanged { session, .. }
        | ClientMessage::SyncMode { session }
        | ClientMessage::ImeSwitched { session }
        | ClientMessage::Indicator { session, .. }
        | ClientMessage::CloseSession { session } => *session,
    }
}
impl Ownership {
    pub fn authorize(&mut self, connection: u64, pid: u32, message: &ClientMessage) -> bool {
        let id = session(message);
        if matches!(
            message,
            ClientMessage::ImeSwitched { .. } | ClientMessage::Indicator { .. }
        ) {
            return true;
        }
        if matches!(message, ClientMessage::OpenSession { .. }) {
            if self
                .sessions
                .get(&id)
                .is_some_and(|(_, owner)| *owner != pid)
            {
                return false;
            }
            if !self.sessions.contains_key(&id)
                && (self.sessions.len() >= MAX_SESSIONS
                    || self
                        .sessions
                        .values()
                        .filter(|(c, _)| *c == connection)
                        .count()
                        >= MAX_SESSIONS_PER_CONNECTION)
            {
                return false;
            }
            self.sessions.insert(id, (connection, pid));
            return true;
        }
        if self.sessions.get(&id) != Some(&(connection, pid)) {
            return false;
        }
        if matches!(message, ClientMessage::CloseSession { .. }) {
            self.sessions.remove(&id);
        }
        true
    }
    pub fn disconnected(&mut self, connection: u64) -> Vec<SessionId> {
        let ids = self
            .sessions
            .iter()
            .filter_map(|(id, (owner, _))| (*owner == connection).then_some(*id))
            .collect::<Vec<_>>();
        for id in &ids {
            self.sessions.remove(id);
        }
        ids
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    fn open(id: u64) -> ClientMessage {
        ClientMessage::OpenSession {
            session: SessionId(id),
            app: None,
            protocol: 0,
        }
    }
    #[test]
    fn ownership_cleanup_cannot_remove_a_reconnected_or_foreign_session() {
        let mut owner = Ownership::default();
        assert!(owner.authorize(1, 10, &open(1)));
        assert!(!owner.authorize(2, 20, &open(1)));
        assert!(!owner.authorize(
            2,
            10,
            &ClientMessage::Poll {
                session: SessionId(1)
            }
        ));
        assert!(owner.authorize(2, 10, &open(1)));
        assert!(owner.disconnected(1).is_empty());
        assert!(owner.authorize(3, 20, &open(2)));
        assert_eq!(owner.disconnected(2), vec![SessionId(1)]);
        assert!(owner.authorize(
            3,
            20,
            &ClientMessage::Poll {
                session: SessionId(2)
            }
        ));
    }
    #[test]
    fn per_connection_and_global_session_budgets_are_bounded() {
        let mut owner = Ownership::default();
        for id in 0..MAX_SESSIONS_PER_CONNECTION {
            assert!(owner.authorize(1, 10, &open(id as u64)));
        }
        assert!(!owner.authorize(1, 10, &open(99)));
        let mut owner = Ownership::default();
        for id in 0..MAX_SESSIONS {
            assert!(owner.authorize(id as u64, 10, &open(id as u64)));
        }
        assert!(!owner.authorize(9999, 10, &open(9999)));
    }
}
