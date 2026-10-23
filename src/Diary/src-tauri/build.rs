fn main() {
    println!("cargo:rerun-if-changed=icons/icon.ico");
    println!("cargo:rerun-if-changed=icons/icon.png");
    // Tauri's ICO decoder uses the first frame, which may be only 16px.
    // Select the full-resolution frame explicitly, independently of ICO ordering.
    let directory = ico::IconDir::read(std::fs::File::open("icons/icon.ico").expect("Missing diary icon"))
        .expect("Invalid diary ICO");
    let entry = directory.entries().iter().find(|entry| entry.width() == 256 && entry.height() == 256)
        .expect("Diary icon must contain a 256x256 frame; refusing a blurry release");
    let image = entry.decode().expect("Cannot decode 256px diary icon");
    assert_eq!(image.rgba_data().len(), 256 * 256 * 4);
    let output = std::path::PathBuf::from(std::env::var_os("OUT_DIR").expect("Missing build output"));
    std::fs::write(output.join("diary-window-icon.rgba"), image.rgba_data()).expect("Cannot embed diary icon");
    tauri_build::build()
}
