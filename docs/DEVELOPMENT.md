# 小蟒开发指南

源码按产品功能组织：`src/Desktop` 是桌面主程序及截图、便签等类库，`src/Diary` 是本地日记，`src/InputMethod` 是文本输入法，`src/FileFavoritesMenu` 是 Windows 收藏菜单。`resources` 保存编译必需的图标和培训资源，`licenses` 保存第三方声明。

内部 ShareX 程序集名和 qingjian crate 名保留，以减少配置、组件和编译兼容性变化。它们是经过小蟒修改的维护源码；来源和许可证见根目录 NOTICE.txt。部分跨平台文件仍属于 Rust workspace，不能直接删掉导致依赖解析失败。

## 准备环境

Windows 11 22H2 或更高版本，x64。安装 .NET SDK 9.0、Node.js 22、pnpm 11.25.0、Rust 1.96 MSVC，以及 Visual Studio C++ Build Tools 的 x64/x86 工具和 Windows SDK。Rust 需要 `i686-pc-windows-msvc` 目标。原生注册工具需要 LLVM-MinGW UCRT；已验证版本为 20261006。

打开 Visual Studio **x64 开发者 PowerShell**，在仓库根目录执行。工具必须在 PATH 中；不需要使用维护者的磁盘路径。

```powershell
rustup target add i686-pc-windows-msvc
$env:LLVM_MINGW_HOME = 'C:/DeveloperTools/llvm-mingw'
$env:WINDOWS_SDK_BIN = 'C:/Program Files (x86)/Windows Kits/10/bin/10.0.26100.0/x64'
./scripts/Build.ps1 -Component All -Test
```

这两个目录是示例，请填写实际安装位置。`WINDOWS_SDK_BIN` 应同时包含 `makeappx.exe` 和 `makepri.exe`。脚本不会安装系统组件、注册输入法或更改日常程序。首次恢复依赖需要联网；`-Offline` 用于已具备 Rust/pnpm 缓存的机器，.NET 按本机 NuGet 配置恢复。

## 分模块开发

```powershell
./scripts/Build.ps1 -Component Native
./scripts/Build.ps1 -Component Diary -Test
./scripts/Build.ps1 -Component InputMethod -Test
./scripts/Build.ps1 -Component Desktop
```

- 桌面入口：`src/Desktop/ShareX/ShareX.csproj`，可用 Visual Studio 打开；输出为 `artifacts/Desktop/xiaomang.exe`。保留旁边 DLL 和运行配置，不能只复制 EXE。
- 日记：`src/Diary/src` 是界面，`src/Diary/src-tauri/src` 是本地存储和检索等逻辑。编译程序复制至 `artifacts/Diary`，随后主程序构建会带入此程序。
- 输入法：`src/InputMethod/crates` 是引擎，`apps/windows` 是 TSF、后台和设置界面。输出位于 Cargo target 的 `release` 和 `i686-pc-windows-msvc/release`。它是按需组件，不自动附加到桌面产物；测试注册需要另行执行注册工具。
- 原生收藏菜单：生成 DLL 和未签名的身份包。实际注册自己编译的身份包需使用自己的证书签名；仓库只有公开证书，没有维护者私钥。

`artifacts/Desktop` 是开发构建，运行需要 .NET Desktop Runtime 9；它不是完整使用包，不附带 Everything、7-Zip 等外部工具。日记需要 WebView2，某些原生程序需要 VC 运行库。要体验已发布的完整程序，请使用根目录“启动小蟒”；它不运行你刚修改的源码。要验证代码修改，请运行对应开发输出。

## 开发验证与数据

`-Test` 执行日记后端与输入法核心测试；前端构建会进行类型检查。截图、多屏、全局快捷键、剪贴板及系统输入法注册仍需人工验证。启动开发产物前保存日常程序中的工作，避免不同版本争用全局快捷键。

不要提交 `artifacts`、node_modules、target、bin、obj、个人记录、配置、密钥或证书私钥。Defaults 是经过审核的产品偏好，不从日常设置自动导出。依赖锁文件、构建资源、许可证和第三方声明必须保留。

历史发布标签 `v2.3-20261015-r1` 保留原始对应源码。本次 `v2.3-20261015-r2` 包含新的备份预检查修复和圆角图标资源，使用同一标签 Release 下的使用包验证实际产品。构建入口为减少受限环境中的子进程问题，串行构建 .NET 工程并关闭共享编译服务；这只影响开发构建耗时，不改变产品运行方式。

当前发布为 `v2.5-20261016`。桌面程序集从本标签源码重新构建；日记及原生组件源码未变，并与上一发布的运行文件逐项核验一致。对应关系见 `RELEASE-v2.5-20261016.json`。历史段落保留原日期与版本。

当前发布为 v2.6-20261016；对应构建与使用包校验值见 RELEASE-v2.6-20261016.json。

当前发布为 v2.7-20261017；对应构建与使用包校验值见 RELEASE-v2.7-20261017.json。

当前发布 v2.8-20261017，源码及使用包对应关系见 RELEASE-v2.8-20261017.json。

当前发布 v2.9-20261017，源码及使用包对应关系见 RELEASE-v2.9-20261017.json。

2.9 重新压缩发布：v2.9-20261017-r2。运行程序未修改；压缩规则及入口见 PACKAGING.md。

当前发布 v2.9-20261018：主程序和 OCR 桥接工程从本标签源码构建。src/OcrBridge 位于桌面中央包管理目录之外，固定 RapidOcrNet 4.2.0。识别运行组件不进入使用包，通过固定校验的 GitHub 组件附件按需下载。已有日记和原生组件复用已核验运行文件。


当前发布 v2.10-20261018；产品小版本由2.9增加为2.10，源码和使用包对应关系见 RELEASE-v2.10-20261018.json。保留本机已验证的截图操作调整。


当前发布 v2.11-20261018；产品小版本从2.10增加为2.11，源码与使用包对应关系见 RELEASE-v2.11-20261018.json。日常内部修改不自动增加发布版本。


当前发布 v2.12-20261019；升级工具维护于 https://github.com/zhanglegezhang/xiaomang_upgrade 。2.12 启动确认协议源码见 src/Desktop/ShareX/PersonalData/UpgradeStartupCheck.cs。


当前发布 v3.1-20261020：数据整理维护源码在 resources/Tools；版本、使用包及校验对应关系见 RELEASE-v3.1-20261020.json。
