use std::sync::mpsc::Sender;

use qingjian_platform::protocol::{ClientMessage, ServerMessage};

use crate::dispatch::{CandidateAction, StatusEvent};

/// 工人线程（独占 [`crate::dispatch::Router`]）的一件活：DLL 的一条消息，或状态条上的一次操作。
pub enum Work {
    /// 某条连接收到的消息 + 回结果的通道（`None` 表示不用回话）。
    Client(u64, u32, ClientMessage, Sender<Option<ServerMessage>>),
    Disconnected(u64),

    /// UI 线程发来的状态条操作。
    Status(StatusEvent),
    Candidate(
        qingjian_platform::protocol::SessionId,
        qingjian_platform::protocol::Frame,
        CandidateAction,
    ),

    /// 所属小蟒退出或请求停止。
    Shutdown,
}
