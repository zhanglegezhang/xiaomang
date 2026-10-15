# 小蟒同学（xiaomang）

小蟒同学是一款面向 Windows 的桌面效率工具，整合截图、便签、本地日记和输入辅助等功能。

## 下载与源码

- 直接使用：到 [Releases 发布页](https://github.com/zhanglegezhang/xiaomang/releases)下载使用包，完整解压后运行 `xiaomang.exe`。
- 继续开发：本仓库 Code 页面提供修改后的完整维护源码与构建资源；可克隆或使用 Code → Download ZIP。
- 本次使用包对应源码标签为 `v2.3-20261015-r1`。Release 中自动生成的 Source code 附件是源码，需要自行构建，不包含可运行使用包。
## 主要功能

- 截图、区域截图、录屏、贴图与拾色。
- 右侧便签：待办、剪切板、收藏夹、常用工具和日记。
- 本地日记：树形目录、TXT / Markdown 编辑、检索替换、图片与表格、自动保存、回收站、导入导出和备份。
- 语音输入、文本输入法，以及文本对比等辅助工具。

## 使用方式

1. 在 Releases 中下载名称标注为“使用包”的附件；不要将 GitHub 自动生成的源码压缩包当作可用程序。
2. 完整解压到可写目录，双击 `xiaomang.exe`；不要在压缩包中运行，也不要只复制 EXE。
3. 首次使用密码请向发布者索取。已有用户数据与配置不会随仓库改名发生迁移。
4. 功能说明见程序中的“关于 → 使用手册”；版本变化见“关于 → 版本日志”。

系统要求：Windows 11 22H2（10.0.22621）及以上，x64。部分界面需要 Microsoft Edge WebView2 运行环境，缺少时按程序提示安装。

## 按需组件

使用包不预装 FFmpeg、SenseVoiceSmall 语音模型和文本输入法整套运行组件。可在“全局设置 → 注册”中按需下载、校验和安装，已有有效组件会复用；首次下载需要联网，注册时按系统提示授予管理员权限。

- **录屏组件**：用于录屏、视频转 GIF 等功能。
- **语音输入**：注册语音系统组件并准备模型后，按住 Ctrl + Alt 说话，松开提交；无需切换到文本输入法。
- **文本输入**：注册文本输入组件后，按 Win + 空格切换到小蟒输入法；与语音组件独立。

组件发布地址：[xiaomang_component / v1.0](https://github.com/zhanglegezhang/xiaomang_component/releases/tag/v1.0)。

## 源码与第三方项目

本仓库已提供修改后的维护源码，构建说明见下方。许可证和修改说明见 [LICENSE](LICENSE) 与 [NOTICE.txt](NOTICE.txt)。

相关功能基于以下项目进行维护，分发时保留各组件原有许可证、版权及鸣谢：

- [ShareX](https://github.com/ShareX/ShareX)
- [青简输入法](https://github.com/qingjian-team/qingjian)
- [NoteGen](https://github.com/codexu/note-gen)

## 问题反馈

请通过 [Issues](https://github.com/zhanglegezhang/xiaomang/issues)提交问题，注明 Windows 版本、涉及功能和复现步骤。


## 对应源码与构建说明

这是小蟒 2.3 的维护源码，包含修改后的 ShareX、青简输入法、NoteGen 本地日记，以及小蟒的原生集成组件。下载源码后需要准备环境并编译；可直接运行的使用包将作为 GitHub Release 附件单独提供。

本源码快照的版本标签为 `v2.3-20261015-r1`，标签固定后不移动；后续功能修改应使用新的提交和版本标签。

## 与使用包的对应关系

- 使用包附件名：`xiaomang2.3_2026-10-15_usage.zip`。
- ZIP SHA256：`3F690AF5B13ECEC464FFC166A8220D06139793706047AE01499013CE469E6CEE`。
- 主程序集 `ShareX.dll` SHA256：`0E9DCBC9D29647F6D79A8DC4C4583DC1738928E9CC98A7BE7DD1A1033A32222A`。
- 本地日记 `xiaomang-diary.exe` SHA256：`8214269CCBE1B4AA1E4C036DCAFC38E168DC2D4B5F9A8583039DEAB4EFB2A872`。

本次补充源码发布说明，并移除未被当前产品启用的日语、西班牙语静态释义表；应用业务代码和产品默认值保持对应构建的版本。重新构建时，路径、编译标识、工具链和签名差异可能导致二进制哈希不同，以上哈希用于识别已发布的原始附件。

## 目录和许可证

| 目录 | 内容 |
| --- | --- |
| `tools/ShareX/source/ShareX-21.0.0` | .NET主程序、类库、语音和文本注册组件 |
| `tools/NoteGen/source/note-gen-0.38.2` | 日记前端与Rust桌面程序 |
| `tools/Qingjian/source/qingjian-0.1.4` | 文本输入法引擎、TSF服务、后台与设置界面 |
| `tools/FileFavoritesMenu` | Windows收藏菜单源码、身份清单和构建脚本 |
| `tools/ShareX/assets` | 主程序实际嵌入的图标资源 |
| `deliverables/新手使用培训` | 主程序实际嵌入的培训资源，编译时需要 |
| `tools/ShareX/release-licenses` | 第三方许可和修改说明 |

许可证与版权说明见根 `LICENSE`、`NOTICE.txt` 及各项目原始许可文件。第三方库和资源继续使用自己的许可证。本目录不包含个人记录、配置、密钥、签名私钥、聊天历史、依赖缓存或旧使用包。

## 构建环境

Windows x64；程序目标为 Windows 11 22H2（10.0.22621）及以上。

- .NET SDK 9.0.317（或兼容的9.0 SDK），主程序使用NuGet恢复工程中固定的依赖版本。
- Node.js 22或更新的受支持版本；pnpm 11.25.0（本次验证版本）；前端依赖使用 `pnpm-lock.yaml`。
- Rust 1.96 MSVC工具链；日记与输入法各自使用 `Cargo.lock`。
- Visual Studio C++ Build Tools、Windows SDK，准备x64及x86构建环境。
- LLVM-MinGW（原构建为20261006 UCRT），用于语音/输入法注册工具及收藏菜单。
- Windows SDK的 `makeappx.exe`、`makepri.exe`，用于收藏菜单身份包和图标资源。

依赖首次准备需要网络。无需将Node、Rust、.NET SDK、node_modules、Cargo/NuGet缓存提交到源码仓库。保留的原有 `build.ps1` 是维护环境的便利脚本，部分默认路径要求本机工具布局；包含维护者个人工具路径的日记便利脚本和使用包整理脚本未纳入此副本，构建采用下述标准命令；其他机器可使用下述命令，或在调用原生注册脚本时显式传入 `-Toolchain`。

## 日记

在仓库根目录打开已准备MSVC环境的PowerShell：

```powershell
$repo = (Get-Location).Path
Set-Location tools/NoteGen/source/note-gen-0.38.2
pnpm install --frozen-lockfile
pnpm run build
cargo build --manifest-path src-tauri/Cargo.toml --release --locked --bin xiaomang-diary --features tauri/custom-protocol
cargo test --manifest-path src-tauri/Cargo.toml --locked --bin xiaomang-diary
Set-Location $repo
```

先构建静态前端，桌面程序再将它嵌入EXE。未指定 `CARGO_TARGET_DIR` 时，EXE位于 `src-tauri/target/release`。将EXE、该项目LICENSE和NOTICE，以及对应VC运行库放入 `tools/NoteGen/runtime`，供主程序构建复制。VC运行库按微软许可从官方运行组件获取。

## 文本输入法

```powershell
$repo = (Get-Location).Path
Set-Location tools/Qingjian/source/qingjian-0.1.4
$env:QINGJIAN_UIACCESS = '0'
cargo build --release --locked -p qingjian-windows-server -p qingjian-windows-tsf -p qingjian-windows-settings
cargo build --release --locked -p qingjian-windows-tsf --target i686-pc-windows-msvc
cargo test --locked -p qingjian-core -p qingjian-platform --lib
Set-Location $repo
```

x86构建前安装 `i686-pc-windows-msvc` 目标并准备对应MSVC工具。`QINGJIAN_UIACCESS=0` 沿用本维护分支的未签名构建方式；不导出维护者签名私钥。设置界面的Windows App Runtime由其锁定依赖准备，首次构建可能下载。

输入法运行所需公开词表、资源来源及数据生成方法见该项目 `assets`、`tools` 和 `docs`；本仓库保留资源源码，个人学习词库不包含。独立已发布组件通过主程序注册页按固定版本下载。

## 原生注册工具

将 `$toolchain` 设置为已安装LLVM-MinGW的目录，然后执行：

```powershell
$toolchain = 'C:/DeveloperTools/llvm-mingw'
& tools/ShareX/source/ShareX-21.0.0/VoiceInput/Build.ps1 -Toolchain $toolchain
& tools/ShareX/source/ShareX-21.0.0/KeyboardInput/build.ps1 -Toolchain $toolchain
```

此路径为示例，应替换为实际工具目录。构建两个架构的TSF组件和安装工具，不调用注册EXE即可完成编译。测试构建不自动注册到Windows，也不替换系统输入法。

## Windows收藏菜单

原脚本 `tools/FileFavoritesMenu/build.ps1` 说明完整流程；其中工具路径可对应安装布局准备。也可在PowerShell中直接编译：

```powershell
$compiler = Join-Path $toolchain 'bin/x86_64-w64-mingw32-clang++.exe'
New-Item -ItemType Directory -Force tools/FileFavoritesMenu/bundle | Out-Null
& $compiler -std=c++17 -O2 -Werror -shared -static-libgcc -static-libstdc++ -o tools/FileFavoritesMenu/bundle/XiaomangFileFavoritesMenu.dll tools/FileFavoritesMenu/source/Menu.cpp tools/FileFavoritesMenu/source/Menu.def -lole32 -luuid
```

`AppxManifest.xml`、图标生成及PRI索引的流程保留在原构建脚本中；根据已安装Windows SDK指定 `makeappx` 和 `makepri`，将生成资源放入 `bundle`。编译DLL不需要签名私钥。`bundle/XiaomangMenuPublisher.cer` 是主工程引用的公开证书，不含私钥；保留它可完成主工程复制步骤。要实际注册新编译的身份包，须用自己的证书签名并替换对应公钥证书，不能用原公钥证书给新包签名，也不提供维护者私钥。

## .NET主程序

```powershell
dotnet restore tools/ShareX/source/ShareX-21.0.0/ShareX/ShareX.csproj -p:Platform=x64
dotnet build tools/ShareX/source/ShareX-21.0.0/ShareX/ShareX.csproj -c Release -p:Platform=x64 --no-restore
```

必须保留项目引用的 `Defaults`、`Assets`、两个机器人PNG和培训PPT；不以个人配置替代审核后的产品默认值。`Libs/ImageListView.dll` 是上游工程引用的公开第三方构建依赖，不是小蟒使用包。

构建输出启动器名为 `ShareX.exe`；制作小蟒程序目录时复制为 `xiaomang.exe`，保留 `ShareX.dll`、deps/runtimeconfig和其他必需运行文件。Everything、7-Zip是外部工具，源码目录不预装其程序。制作完整使用包时按各自许可准备匹配的运行文件；FFmpeg、语音模型、文本输入法通过注册页按需下载，不放进普通使用包。

输入法的三个x64产物分别为 `XiaomangInputWorker.exe`、`qingjian_tsf.dll` 和 `qingjian-settings.exe`；x86产物为 `i686-pc-windows-msvc/release/qingjian_tsf.dll`。可重新构建这些组件用于开发，但普通使用包仍按按需组件分发规则制作。

原生组件和日记产物准备好后再构建主程序，才能将已生成辅助程序复制到输出。干净编译与新机器真实操作验证是两件事；验证记录会明确说明其边界。

## 本次验证（2026-10-15）

从本目录复制独立验证副本后进行构建，未使用日常程序目录里的旧产品程序集代替本次编译。开发依赖从本机现有缓存恢复；没有验证全新物理电脑首次联网下载依赖的过程。

- 主程序及6个类库：Release x64构建通过，0警告、0错误；105项编辑器和便签界面断言通过。
- 日记：锁定依赖安装、静态前端及Rust桌面程序构建通过；68项后端测试和9项搜索前端检查通过。
- 文本输入法：x64后台、设置程序、TSF DLL及x86 TSF DLL构建通过；Core与Platform共349项单元测试通过。
- 语音/文本注册组件和Windows收藏菜单DLL、身份包：构建通过；未执行Windows注册或导入测试证书。
- 对应关系：核对了使用包内7个核心程序集的原编译文件和匹配的PDB记录，902个源码文档与本目录副本一致；日记相关源码也与原已验证构建记录一致。
- 清单检查：未混入个人配置、记录、密钥、开发备份、依赖缓存或旧使用包；单文件均小于GitHub普通Git的100 MB限制。

以上不代表已在另一台物理电脑完成实际安装和操作测试。未签名输入法沿用 `QINGJIAN_UIACCESS=0`，候选窗在部分系统界面中可能被遮挡；本次仅整理对应源码和说明，没有改变应用功能、默认值或输入法注册。

源码资源精简：移除了 assets/glossary 下的 glossary-ja.tsv 与 glossary-es.tsv，共18,703,354字节。保留中英文词表、生成工具及其原始来源和许可说明；现有小蟒配置已将停用语言迁移为英文。此前本地未发布标签保留，公开发布使用上述 r1 标签。
