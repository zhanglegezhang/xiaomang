//! 每个登录会话只允许一个引擎；常驻模式只响应明确停止，兼容旧的父进程模式。
use std::sync::mpsc::Sender;

use qingjian_windows_server::ipc::Work;
use windows::Win32::Foundation::{
    CloseHandle, ERROR_ALREADY_EXISTS, GetLastError, HANDLE, WAIT_OBJECT_0,
};
use windows::Win32::System::Threading::{
    CreateEventW, CreateMutexW, OpenEventW, OpenProcess, PROCESS_SYNCHRONIZE,
    SYNCHRONIZATION_SYNCHRONIZE, WaitForMultipleObjects, WaitForSingleObject,
};
use windows::core::HSTRING;

pub struct Owner {
    parent: Option<HANDLE>,

    stop: HANDLE,

    singleton: HANDLE,
}

impl Owner {
    pub fn from_args() -> Option<Self> {
        let args: Vec<String> = std::env::args().collect();
        let (parent, event_name) =
            if args.len() == 4 && args[1] == "--resident" && args[2] == "--stop-event" {
                (None, &args[3])
            } else if args.len() == 5 && args[1] == "--parent-pid" && args[3] == "--stop-event" {
                let pid = args[2].parse::<u32>().ok()?;
                (
                    Some(unsafe { OpenProcess(PROCESS_SYNCHRONIZE, false, pid) }.ok()?),
                    &args[4],
                )
            } else {
                return None;
            };
        // 常驻后台也可创建事件，主程序在新进程调度前退出不会丢失控制句柄。
        let stop = match if parent.is_none() {
            unsafe { CreateEventW(None, true, false, &HSTRING::from(event_name)) }
        } else {
            unsafe {
                OpenEventW(
                    SYNCHRONIZATION_SYNCHRONIZE,
                    false,
                    &HSTRING::from(event_name),
                )
            }
        } {
            Ok(handle) => handle,
            Err(_) => {
                if let Some(parent) = parent {
                    let _ = unsafe { CloseHandle(parent) };
                }
                return None;
            }
        };
        // 名字来自当前会话管道，不由调用者指定，启动竞争也不能加载第二份引擎。
        let pipe = qingjian_platform::protocol::default_pipe_name();
        let name = HSTRING::from(format!("Local\\{}.Singleton", pipe.rsplit('\\').next()?));
        let singleton = unsafe { CreateMutexW(None, false, &name) };
        let already_exists = unsafe { GetLastError() } == ERROR_ALREADY_EXISTS;
        let singleton = match singleton {
            Ok(handle) => handle,
            Err(_) => {
                if let Some(parent) = parent {
                    let _ = unsafe { CloseHandle(parent) };
                }
                let _ = unsafe { CloseHandle(stop) };
                return None;
            }
        };
        let owner = Self {
            parent,
            stop,
            singleton,
        };
        if already_exists
            || parent
                .is_some_and(|handle| unsafe { WaitForSingleObject(handle, 0) } == WAIT_OBJECT_0)
        {
            return None;
        }
        Some(owner)
    }

    pub fn watch(&self, sender: Sender<Work>) {
        let mut handles = vec![self.stop.0 as usize];
        if let Some(parent) = self.parent {
            handles.push(parent.0 as usize);
        }
        // Owner 由 serve 持有至工人循环返回，句柄与互斥体覆盖停止后的落盘阶段。
        std::thread::spawn(move || {
            let handles: Vec<HANDLE> = handles
                .into_iter()
                .map(|value| HANDLE(value as *mut _))
                .collect();
            let _ = unsafe { WaitForMultipleObjects(&handles, false, u32::MAX) };
            let _ = sender.send(Work::Shutdown);
        });
    }
}

impl Drop for Owner {
    fn drop(&mut self) {
        if let Some(parent) = self.parent {
            let _ = unsafe { CloseHandle(parent) };
        }
        let _ = unsafe { CloseHandle(self.stop) };
        let _ = unsafe { CloseHandle(self.singleton) };
    }
}
