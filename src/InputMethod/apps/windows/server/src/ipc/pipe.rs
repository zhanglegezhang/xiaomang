//! 命名管道传输：在 `\\.\pipe\qingjian` 上服务 DLL 客户端，字节模式，帧由协议 codec 切。
//!
//! 每个应用进程各开一条连接且失焦后连接仍在，所以不能串行服务（新聚焦的应用连不上会阻塞 UI 线程、
//! 触发 TSF 看门狗）：后台接受循环每来一个客户端就新建实例、起一条线程；[`Router`] 不跨线程，
//! 留在调用线程跑工人循环，各连接经通道把消息转给它串行处理。

use std::fs::File;
use std::io;
use std::os::windows::io::{AsRawHandle, FromRawHandle};
use std::sync::mpsc::{self, Receiver, RecvTimeoutError, Sender};
use std::sync::{
    Arc,
    atomic::{AtomicUsize, Ordering},
};
use std::thread;
use std::time::{Duration, Instant};

use windows::Win32::Foundation::{
    ERROR_ACCESS_DENIED, ERROR_BROKEN_PIPE, ERROR_NO_DATA, ERROR_PIPE_CONNECTED,
    ERROR_PIPE_NOT_CONNECTED, HANDLE,
};
use windows::Win32::Security::Authorization::{
    ConvertStringSecurityDescriptorToSecurityDescriptorW, SDDL_REVISION_1,
};
use windows::Win32::Security::{PSECURITY_DESCRIPTOR, SECURITY_ATTRIBUTES};
use windows::Win32::Storage::FileSystem::{FILE_FLAG_FIRST_PIPE_INSTANCE, PIPE_ACCESS_DUPLEX};
use windows::Win32::System::Pipes::{
    ConnectNamedPipe, CreateNamedPipeW, DisconnectNamedPipe, PIPE_READMODE_BYTE,
    PIPE_REJECT_REMOTE_CLIENTS, PIPE_TYPE_BYTE, PIPE_WAIT,
};
use windows::core::{HRESULT, HSTRING};

use qingjian_platform::protocol::{ClientMessage, ServerMessage, write_message};

use super::Work;
use crate::dispatch::Router;

use super::{
    limits::{self, Ownership},
    stream::BoundedStream,
};
pub use qingjian_platform::protocol::DEFAULT_PIPE_NAME;
pub use qingjian_platform::protocol::default_pipe_name;
use qingjian_platform::protocol::read_message_limited;
use windows::Win32::Foundation::{CloseHandle, ERROR_IO_PENDING};
use windows::Win32::Storage::FileSystem::FILE_FLAG_OVERLAPPED;
use windows::Win32::System::IO::{GetOverlappedResult, OVERLAPPED};
use windows::Win32::System::Pipes::GetNamedPipeClientProcessId;
use windows::Win32::System::Threading::{
    CreateEventW, GetProcessIdOfThread, OpenThread, THREAD_QUERY_LIMITED_INFORMATION,
};

const BUFFER_SIZE: u32 = 64 * 1024;

/// 管道的 SDDL：放行 Everyone / ALL APPLICATION PACKAGES / ALL RESTRICTED APPLICATION PACKAGES，
/// 完整性标 Low。任务栏搜索、设置这类 AppContainer 进程在默认 DACL 下连不上。
const PIPE_SDDL: &str = "D:(A;;GRGW;;;OW)(A;;GRGW;;;AC)(A;;GRGW;;;S-1-15-2-2)S:(ML;;NW;;;LW)";

/// 在命名管道上服务多个客户端。当前线程独占 [`Router`] 跑工人循环，正常不返回。
/// `work` 通道由调用方建（UI 线程也往里投状态条事件），这里拿一份发送端给各连接。
/// 第一个实例带 `FILE_FLAG_FIRST_PIPE_INSTANCE`：已有一个 Server 在跑就建不出来，直接报错退出（两个 Server
/// 会各画一条状态条、各持一份状态）。
pub fn serve_pipe(
    name: &str,
    router: &mut Router,
    sender: Sender<Work>,
    receiver: Receiver<Work>,
) -> io::Result<()> {
    let pipe = HSTRING::from(name);
    let first = match create_instance(&pipe, pipe_security_descriptor(), true) {
        Ok(first) => first,
        Err(error) if error.raw_os_error() == Some(ERROR_ACCESS_DENIED.0 as i32) => {
            return Err(io::Error::other(
                "已有一个 qingjian-server 在运行（命名管道被占），本进程退出",
            ));
        }
        Err(error) => return Err(error),
    };
    thread::spawn(move || accept_loop(&pipe, first, sender));
    tracing::info!(pipe = name, "命名管道监听中");
    // 按 Router 的节拍来 tick：在等本地整句模型就几十毫秒一次，否则一秒看一次配置文件。
    // 到点时间是绝对的，不随消息重新计时——前台进程里的 DLL 隔几百毫秒就问一次切模式（SyncMode），
    // 若每收一条消息就重等一秒，tick 永远到不了，热加载与模型接入都会停摆。
    let mut due = Instant::now() + router.next_tick();
    let mut ownership = Ownership::default();
    loop {
        let now = Instant::now();
        if now >= due {
            router.tick();
            due = Instant::now() + router.next_tick();
            continue;
        }
        match receiver.recv_timeout(due - now) {
            Ok(Work::Client(connection, pid, message, reply)) => {
                if ownership.authorize(connection, pid, &message) {
                    let _ = reply.send(router.handle(message));
                }
            }
            Ok(Work::Disconnected(connection)) => {
                for session in ownership.disconnected(connection) {
                    router.handle(ClientMessage::CloseSession { session });
                }
            }
            Ok(Work::Status(event)) => router.handle_status_event(event),
            Ok(Work::Shutdown) => {
                router.flush_learning();
                break;
            }
            Err(RecvTimeoutError::Timeout) => continue,
            Err(RecvTimeoutError::Disconnected) => break,
        }
        // 处理完消息节拍可能变短了（按键起了防抖）：到点时间只提前不推后
        due = due.min(Instant::now() + router.next_tick());
    }
    Ok(())
}

/// 先在建好的第一个实例上等客户端。客户端抢先连上又关闭，只丢弃该实例，不能停止整个监听。
fn accept_loop(name: &HSTRING, first: File, sender: Sender<Work>) {
    let descriptor = pipe_security_descriptor();
    let mut instance = Ok(first);
    let active = Arc::new(AtomicUsize::new(0));
    let mut next = 0u64;
    loop {
        let stream = match instance.and_then(wait_client) {
            Ok(stream) => stream,
            Err(error) => {
                if transient_client_disconnect(&error) {
                    instance = create_instance(name, descriptor, false);
                    continue;
                }
                tracing::error!(%error, "建管道实例失败，停止接受");
                break;
            }
        };
        if active.load(Ordering::Acquire) >= limits::MAX_CONNECTIONS {
            let _ = unsafe { DisconnectNamedPipe(HANDLE(stream.as_raw_handle())) };
        } else {
            active.fetch_add(1, Ordering::AcqRel);
            next = next.wrapping_add(1);
            let sender = sender.clone();
            let active = active.clone();
            thread::spawn(move || {
                struct Slot(Arc<AtomicUsize>);
                impl Drop for Slot {
                    fn drop(&mut self) {
                        self.0.fetch_sub(1, Ordering::AcqRel);
                    }
                }
                let _slot = Slot(active);
                serve_connection(stream, sender, next);
            });
        }
        instance = create_instance(name, descriptor, false);
    }
}

fn transient_client_disconnect(error: &io::Error) -> bool {
    error.raw_os_error().is_some_and(|code| {
        [
            ERROR_BROKEN_PIPE.0,
            ERROR_NO_DATA.0,
            ERROR_PIPE_NOT_CONNECTED.0,
        ]
        .contains(&(code as u32))
    })
}

/// 转换失败返回 null，退回默认 DACL。描述符建一次、随进程存活。
fn pipe_security_descriptor() -> PSECURITY_DESCRIPTOR {
    let mut descriptor = PSECURITY_DESCRIPTOR::default();
    let converted = unsafe {
        ConvertStringSecurityDescriptorToSecurityDescriptorW(
            &HSTRING::from(PIPE_SDDL),
            SDDL_REVISION_1,
            &mut descriptor,
            None,
        )
    };
    if let Err(error) = converted {
        tracing::warn!(%error, "构建管道安全描述符失败，退回默认 DACL（UWP 应用可能连不上）");
        return PSECURITY_DESCRIPTOR::default();
    }
    descriptor
}

/// 建一个实例。句柄交给 `File` 管：对端关闭时 std 把 `ERROR_BROKEN_PIPE` 当 EOF。
fn create_instance(
    name: &HSTRING,
    descriptor: PSECURITY_DESCRIPTOR,
    first: bool,
) -> io::Result<File> {
    let attributes = SECURITY_ATTRIBUTES {
        nLength: size_of::<SECURITY_ATTRIBUTES>() as u32,
        lpSecurityDescriptor: descriptor.0,
        bInheritHandle: false.into(),
    };
    let attributes = (!descriptor.0.is_null()).then_some(&raw const attributes);
    let mut open_mode = PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED;
    if first {
        open_mode |= FILE_FLAG_FIRST_PIPE_INSTANCE;
    }
    let handle = unsafe {
        CreateNamedPipeW(
            name,
            open_mode,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
            limits::MAX_CONNECTIONS as u32 + 1,
            BUFFER_SIZE,
            BUFFER_SIZE,
            0,
            attributes,
        )
    };
    if handle.is_invalid() {
        return Err(io::Error::last_os_error());
    }
    Ok(unsafe { File::from_raw_handle(handle.0) })
}

/// 阻塞等一个客户端连上这个实例。
fn wait_client(stream: File) -> io::Result<File> {
    let handle = HANDLE(stream.as_raw_handle());
    let event = unsafe { CreateEventW(None, true, false, None) }.map_err(io::Error::other)?;
    let mut operation = OVERLAPPED {
        hEvent: event,
        ..Default::default()
    };
    let connected = unsafe { ConnectNamedPipe(handle, Some(&mut operation)) };
    let result = match connected {
        Ok(()) => Ok(()),
        Err(error) if error.code() == HRESULT::from_win32(ERROR_PIPE_CONNECTED.0) => Ok(()),
        Err(error) if error.code() == HRESULT::from_win32(ERROR_IO_PENDING.0) => {
            let mut bytes = 0;
            unsafe { GetOverlappedResult(handle, &operation, &mut bytes, true) }
                .map_err(io::Error::other)
        }
        Err(error) => Err(io::Error::from_raw_os_error(error.code().0 & 0xffff)),
    };
    let _ = unsafe { CloseHandle(event) };
    result?;
    Ok(stream)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn thread_identity_is_checked_against_the_kernel_process() {
        let thread = unsafe { windows::Win32::System::Threading::GetCurrentThreadId() };
        assert!(thread_belongs_to(
            std::process::id(),
            qingjian_platform::protocol::SessionId(u64::from(thread))
        ));
        assert!(!thread_belongs_to(
            std::process::id().wrapping_add(1),
            qingjian_platform::protocol::SessionId(u64::from(thread))
        ));
        assert!(!thread_belongs_to(
            std::process::id(),
            qingjian_platform::protocol::SessionId(u64::MAX)
        ));
    }

    #[test]
    fn a_short_lived_client_must_not_stop_the_listener() {
        for code in [ERROR_BROKEN_PIPE, ERROR_NO_DATA, ERROR_PIPE_NOT_CONNECTED] {
            assert!(transient_client_disconnect(&io::Error::from_raw_os_error(
                code.0 as i32
            )));
        }
        assert!(!transient_client_disconnect(&io::Error::from_raw_os_error(
            ERROR_ACCESS_DENIED.0 as i32
        )));
        assert!(!transient_client_disconnect(&io::Error::other(
            "unexpected"
        )));
    }
}

/// 服务一条连接：读消息 → 转给工人线程 → 写回，直到对端在帧边界关闭或出错。
fn thread_belongs_to(pid: u32, id: qingjian_platform::protocol::SessionId) -> bool {
    let Ok(thread) = u32::try_from(id.0) else {
        return false;
    };
    let Ok(handle) = (unsafe { OpenThread(THREAD_QUERY_LIMITED_INFORMATION, false, thread) })
    else {
        return false;
    };
    let owner = unsafe { GetProcessIdOfThread(handle) };
    let _ = unsafe { CloseHandle(handle) };
    owner == pid && pid != 0
}
fn serve_connection(stream: File, sender: Sender<Work>, connection: u64) {
    let mut pid = 0;
    if unsafe { GetNamedPipeClientProcessId(HANDLE(stream.as_raw_handle()), &mut pid) }.is_err()
        || pid == 0
    {
        return;
    }
    let mut stream = BoundedStream::new(stream);
    let mut initial = true;
    let mut authorized = std::collections::HashSet::new();
    let (reply_sender, reply_receiver) = mpsc::channel::<Option<ServerMessage>>();
    let mut window = Instant::now();
    let mut requests = 0;
    loop {
        stream.read_frame(initial);
        let message = match read_message_limited::<_, ClientMessage>(&mut stream, limits::MAX_FRAME)
        {
            Ok(Some(message)) => message,
            Ok(None) => break,
            // Authenticated idle legacy DLLs stay connected to preserve the next
            // key. Slot/session limits still bound these retained resources.
            Err(qingjian_platform::protocol::CodecError::Io(ref error))
                if !initial && stream.idle() && error.kind() == io::ErrorKind::TimedOut =>
            {
                continue;
            }
            Err(error) => {
                tracing::warn!(%error,"客户端帧无效或超时");
                break;
            }
        };
        initial = false;
        if window.elapsed() >= Duration::from_secs(1) {
            window = Instant::now();
            requests = 0;
        }
        requests += 1;
        if requests > 1024 {
            break;
        }
        let id = limits::session(&message);
        if !authorized.contains(&id) {
            if !thread_belongs_to(pid, id) {
                break;
            }
            if authorized.len() >= limits::MAX_SESSIONS_PER_CONNECTION {
                break;
            }
            authorized.insert(id);
        }
        let valid = match &message {
            ClientMessage::OpenSession { app, .. } => {
                app.as_ref().is_none_or(|name| name.len() <= 512)
            }
            ClientMessage::Surrounding { text, .. } => text.len() <= 4096,
            ClientMessage::Selection { text, .. } => text.len() <= 128 * 1024,
            _ => true,
        };
        if !valid {
            break;
        }
        if sender
            .send(Work::Client(connection, pid, message, reply_sender.clone()))
            .is_err()
        {
            break;
        }
        match reply_receiver.recv_timeout(Duration::from_secs(5)) {
            Ok(Some(response)) => {
                stream.write_frame();
                if write_message(&mut stream, &response).is_err() {
                    break;
                }
            }
            Ok(None) => {}
            Err(_) => break,
        }
    }
    let _ = sender.send(Work::Disconnected(connection));
    let _ = unsafe { DisconnectNamedPipe(HANDLE(stream.file.as_raw_handle())) };
    tracing::debug!("客户端断开");
}
