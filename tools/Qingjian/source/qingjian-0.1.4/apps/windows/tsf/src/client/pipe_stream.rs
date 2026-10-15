//! 异步管道带超时，后台挂起时不把宿主界面的输入线程永久卡住。
use std::fs::File;
use std::io::{self, Read, Write};
use std::os::windows::io::AsRawHandle;

use windows::Win32::Foundation::{
    CloseHandle, ERROR_BROKEN_PIPE, ERROR_IO_PENDING, HANDLE, WAIT_OBJECT_0,
};
use windows::Win32::Storage::FileSystem::{ReadFile, WriteFile};
use windows::Win32::System::IO::{CancelIoEx, GetOverlappedResult, OVERLAPPED};
use windows::Win32::System::Threading::{CreateEventW, WaitForSingleObject};

pub struct PipeStream(pub(super) File);

impl PipeStream {
    fn transfer(
        &mut self,
        start: impl FnOnce(HANDLE, *mut OVERLAPPED) -> windows::core::Result<()>,
    ) -> io::Result<usize> {
        let handle = HANDLE(self.0.as_raw_handle());
        let event = unsafe { CreateEventW(None, true, false, None) }.map_err(io::Error::other)?;
        let mut overlapped = OVERLAPPED {
            hEvent: event,
            ..Default::default()
        };
        let initial = start(handle, &mut overlapped);
        let mut bytes = 0;
        let pending = initial
            .as_ref()
            .is_err_and(|e| e.code() == windows::core::HRESULT::from_win32(ERROR_IO_PENDING.0));
        let result = if pending && unsafe { WaitForSingleObject(event, 700) } != WAIT_OBJECT_0 {
            // 取消后必须等内核释放缓冲区，才能销毁栈上的 OVERLAPPED。
            let _ = unsafe { CancelIoEx(handle, Some(&overlapped)) };
            let _ = unsafe { GetOverlappedResult(handle, &overlapped, &mut bytes, true) };
            Err(io::Error::new(
                io::ErrorKind::TimedOut,
                "input worker did not respond",
            ))
        } else if initial.is_ok() || pending {
            unsafe { GetOverlappedResult(handle, &overlapped, &mut bytes, false) }
                .map(|()| bytes as usize)
                .map_err(io::Error::other)
        } else {
            let error = initial.unwrap_err();
            if error.code() == windows::core::HRESULT::from_win32(ERROR_BROKEN_PIPE.0) {
                Ok(0)
            } else {
                Err(io::Error::other(error))
            }
        };
        let _ = unsafe { CloseHandle(event) };
        result
    }
}

impl Read for PipeStream {
    fn read(&mut self, bytes: &mut [u8]) -> io::Result<usize> {
        if bytes.is_empty() {
            return Ok(0);
        }
        self.transfer(|handle, operation| unsafe {
            ReadFile(handle, Some(bytes), None, Some(operation))
        })
    }
}

impl Write for PipeStream {
    fn write(&mut self, bytes: &[u8]) -> io::Result<usize> {
        if bytes.is_empty() {
            return Ok(0);
        }
        self.transfer(|handle, operation| unsafe {
            WriteFile(handle, Some(bytes), None, Some(operation))
        })
    }
    fn flush(&mut self) -> io::Result<()> {
        Ok(())
    }
}
