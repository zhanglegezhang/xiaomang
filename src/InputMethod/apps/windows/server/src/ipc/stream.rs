//! Overlapped pipe I/O with a whole-frame deadline. No watchdog thread per read.
use std::{
    fs::File,
    io::{self, Read, Write},
    os::windows::io::AsRawHandle,
    time::{Duration, Instant},
};
use windows::Win32::{
    Foundation::{
        CloseHandle, ERROR_BROKEN_PIPE, ERROR_IO_PENDING, ERROR_NO_DATA, HANDLE, WAIT_OBJECT_0,
    },
    Storage::FileSystem::{ReadFile, WriteFile},
    System::{
        IO::{CancelIoEx, GetOverlappedResult, OVERLAPPED},
        Threading::{CreateEventW, WaitForSingleObject},
    },
};
pub struct BoundedStream {
    pub file: File,
    deadline: Instant,
    first: bool,
}
impl BoundedStream {
    pub fn new(file: File) -> Self {
        Self {
            file,
            deadline: Instant::now() + Duration::from_secs(5),
            first: true,
        }
    }
    pub fn read_frame(&mut self, initial: bool) {
        self.first = true;
        self.deadline = Instant::now() + Duration::from_secs(if initial { 5 } else { 600 });
    }
    pub fn write_frame(&mut self) {
        self.first = false;
        self.deadline = Instant::now() + Duration::from_secs(5);
    }
    pub fn idle(&self) -> bool {
        self.first
    }
    fn transfer(
        &mut self,
        start: impl FnOnce(HANDLE, *mut OVERLAPPED) -> windows::core::Result<()>,
    ) -> io::Result<usize> {
        let handle = HANDLE(self.file.as_raw_handle());
        let event = unsafe { CreateEventW(None, true, false, None) }.map_err(io::Error::other)?;
        let mut operation = OVERLAPPED {
            hEvent: event,
            ..Default::default()
        };
        let initial = start(handle, &mut operation);
        let mut bytes = 0;
        let pending = initial
            .as_ref()
            .is_err_and(|e| e.code() == windows::core::HRESULT::from_win32(ERROR_IO_PENDING.0));
        let remaining = self
            .deadline
            .saturating_duration_since(Instant::now())
            .as_millis()
            .min(u32::MAX as u128 - 1) as u32;
        let result = if pending && unsafe { WaitForSingleObject(event, remaining) } != WAIT_OBJECT_0
        {
            let _ = unsafe { CancelIoEx(handle, Some(&operation)) };
            let _ = unsafe { GetOverlappedResult(handle, &operation, &mut bytes, true) };
            Err(io::Error::new(
                io::ErrorKind::TimedOut,
                "input IPC frame deadline exceeded",
            ))
        } else if initial.is_ok() || pending {
            unsafe { GetOverlappedResult(handle, &operation, &mut bytes, false) }
                .map(|()| bytes as usize)
                .map_err(io::Error::other)
        } else {
            let error = initial.unwrap_err();
            if [ERROR_BROKEN_PIPE, ERROR_NO_DATA]
                .iter()
                .any(|code| error.code() == windows::core::HRESULT::from_win32(code.0))
            {
                Ok(0)
            } else {
                Err(io::Error::other(error))
            }
        };
        let _ = unsafe { CloseHandle(event) };
        result
    }
}
impl Read for BoundedStream {
    fn read(&mut self, bytes: &mut [u8]) -> io::Result<usize> {
        if bytes.is_empty() {
            return Ok(0);
        }
        let count = self.transfer(|handle, operation| unsafe {
            ReadFile(handle, Some(bytes), None, Some(operation))
        })?;
        if count > 0 && self.first {
            self.first = false;
            self.deadline = Instant::now() + Duration::from_secs(5);
        }
        Ok(count)
    }
}
impl Write for BoundedStream {
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
