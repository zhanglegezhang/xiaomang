//! 故意不回包的后台不能永久卡住宿主输入线程。
#![cfg(windows)]
use qingjian_tsf::client::pipe::connect;
use std::fs::File;
use std::io::Read;
use std::os::windows::io::FromRawHandle;
use std::time::{Duration, Instant};
use windows::Win32::Storage::FileSystem::PIPE_ACCESS_DUPLEX;
use windows::Win32::System::Pipes::{
    ConnectNamedPipe, CreateNamedPipeW, PIPE_TYPE_BYTE, PIPE_WAIT,
};
use windows::core::HSTRING;

#[test]
fn stalled_worker_times_out_and_cancellation_releases_buffers() {
    let name = format!(r"\\.\pipe\xiaomang-stall-test-{}", std::process::id());
    let (ready, receive) = std::sync::mpsc::channel();
    let server_name = name.clone();
    let server = std::thread::spawn(move || {
        let handle = unsafe {
            CreateNamedPipeW(
                &HSTRING::from(server_name),
                PIPE_ACCESS_DUPLEX,
                PIPE_TYPE_BYTE | PIPE_WAIT,
                1,
                4096,
                4096,
                0,
                None,
            )
        };
        assert!(!handle.is_invalid());
        let _file = unsafe { File::from_raw_handle(handle.0) };
        ready.send(()).unwrap();
        let _ = unsafe { ConnectNamedPipe(handle, None) };
        std::thread::sleep(Duration::from_millis(1600));
    });
    receive.recv().unwrap();
    let mut client = connect(&name).unwrap();
    let mut buffer = [0; 4];
    let began = Instant::now();
    assert_eq!(
        client.read(&mut buffer).unwrap_err().kind(),
        std::io::ErrorKind::TimedOut
    );
    assert!(began.elapsed() < Duration::from_millis(1400));
    drop(client);
    server.join().unwrap();
}

#[test]
fn missing_worker_does_not_start_an_independent_engine() {
    let name = format!(r"\\.\pipe\xiaomang-missing-test-{}", std::process::id());
    let began = Instant::now();
    assert!(connect(&name).is_err());
    assert!(began.elapsed() < Duration::from_millis(500));
}
