//! 线上帧格式：4 字节小端长度前缀 + JSON 消息体。Server 与 TSF DLL 两端共用这套读写，
//! 所以放在平台层而不是某个 app 里。传输本身（命名管道等）在 app 侧，这里只切帧。

use std::io::{self, Read, Write};

use serde::Serialize;
use serde::de::DeserializeOwned;

/// 缺省命名管道名。Server 在这上面监听，DLL 用同名连上。放这里让两端共享同一个字面量。
pub const DEFAULT_PIPE_NAME: &str = r"\\.\pipe\xiaomang-input-v1";

/// 各登录会话独立，避免快速用户切换时把按键交给另一个用户的后台。
pub fn default_pipe_name() -> String {
    #[cfg(windows)]
    {
        #[link(name = "kernel32")]
        unsafe extern "system" {
            fn ProcessIdToSessionId(process: u32, session: *mut u32) -> i32;
        }
        let mut session = 0;
        if unsafe { ProcessIdToSessionId(std::process::id(), &mut session) } != 0 {
            return format!("{DEFAULT_PIPE_NAME}-{session}");
        }
    }
    DEFAULT_PIPE_NAME.to_owned()
}

/// 单帧上限，挡住坏长度前缀导致的巨量分配。
const MAX_FRAME: u32 = 16 * 1024 * 1024;

/// 编解码错误。
#[derive(Debug, thiserror::Error)]
pub enum CodecError {
    #[error("io: {0}")]
    Io(#[from] io::Error),

    #[error("json: {0}")]
    Json(#[from] serde_json::Error),

    #[error("frame too large: {0} bytes")]
    TooLarge(usize),
}

/// 写一帧：长度前缀 + JSON，然后 flush。
pub fn write_message<W: Write, T: Serialize>(writer: &mut W, value: &T) -> Result<(), CodecError> {
    let body = serde_json::to_vec(value)?;
    let len = u32::try_from(body.len()).map_err(|_| CodecError::TooLarge(body.len()))?;
    if len > MAX_FRAME {
        return Err(CodecError::TooLarge(body.len()));
    }
    writer.write_all(&len.to_le_bytes())?;
    writer.write_all(&body)?;
    writer.flush()?;
    Ok(())
}

/// 读一帧。对端在帧边界干净关闭（读长度前缀时就是 EOF）返回 `Ok(None)`；
/// 读到一半断开算错误（`UnexpectedEof`）。
pub fn read_message<R: Read, T: DeserializeOwned>(reader: &mut R) -> Result<Option<T>, CodecError> {
    read_message_limited(reader, MAX_FRAME)
}

/// A transport may choose a smaller budget without changing the wire protocol.
pub fn read_message_limited<R: Read, T: DeserializeOwned>(
    reader: &mut R,
    limit: u32,
) -> Result<Option<T>, CodecError> {
    let mut len_buf = [0u8; 4];
    if !read_fully_or_eof(reader, &mut len_buf)? {
        return Ok(None);
    }
    let len = u32::from_le_bytes(len_buf);
    if len > limit.min(MAX_FRAME) {
        return Err(CodecError::TooLarge(len as usize));
    }
    let mut body = vec![0u8; len as usize];
    reader.read_exact(&mut body)?;
    Ok(Some(serde_json::from_slice(&body)?))
}

/// 填满 `buf`。开头就 EOF（一个字节没读到）返回 `Ok(false)`（干净关闭）；
/// 读到一半 EOF 返回 `UnexpectedEof`；填满返回 `Ok(true)`。
fn read_fully_or_eof<R: Read>(reader: &mut R, buf: &mut [u8]) -> io::Result<bool> {
    let mut filled = 0;
    while filled < buf.len() {
        match reader.read(&mut buf[filled..])? {
            0 if filled == 0 => return Ok(false),
            0 => {
                return Err(io::Error::new(
                    io::ErrorKind::UnexpectedEof,
                    "connection closed mid-frame",
                ));
            }
            read => filled += read,
        }
    }
    Ok(true)
}
