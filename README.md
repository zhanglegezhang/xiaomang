# 小蟒同学（xiaomang）

面向 Windows 的桌面效率工具：截图、录屏、贴边便签、待办、剪切板、收藏夹、本地日记与输入辅助。

## 直接使用

**下载本仓库源码后，双击根目录的 `启动小蟒.cmd`。** 首次联网下载匹配的使用包，通过 SHA256 校验后解压运行；后续复用本地程序，不需要安装开发环境。

也可以从 [Releases](https://github.com/zhanglegezhang/xiaomang/releases) 下载使用包，完整解压后运行 `xiaomang.exe`。

- 系统要求：Windows 11 22H2 及以上，x64。部分功能需要 WebView2，缺少时按程序提示安装。
- 启动入口首次下载约 110 MB（104.8 MiB），解压约 275 MB，保存在 `%LOCALAPPDATA%\XiaomangSourceLauncher\v2.3-20261015-r2`。个人记录不会写入源码仓库。
- 从旧版本迁移时，先在旧程序导出个人数据，再在新版本导入。启动入口按版本使用独立目录，不会自动迁移旧目录中的记录；首次切换本版本会重新下载，后续复用。
- 下载或校验失败不执行程序，可重新运行重试。需要代理时，在 PowerShell 中运行 `./scripts/Start-Xiaomang.ps1 -Proxy http://你的代理地址:端口`。
- 首次使用密码向发布者索取；手册和版本日志在程序“关于”中。
- 录屏、语音模型和文本输入法通过“全局设置 → 注册”按需下载；入口不会自动注册这些组件。

## 继续开发

从 Code → Download ZIP 下载，或克隆本仓库。按 [开发指南](docs/DEVELOPMENT.md) 准备环境后，使用统一构建入口：

```powershell
./scripts/Build.ps1 -Component All -Test
```

**“启动小蟒”运行的是已发布程序，不会自动编译修改后的源码。** 开发修改应编译后运行 `artifacts/Desktop/xiaomang.exe`；开发构建所需运行环境和外部工具边界见开发指南。

| 目录 | 内容 |
| --- | --- |
| `src/Desktop` | 主程序、截图、便签、系统集成与原生注册组件 |
| `src/Diary` | 本地日记界面、编辑器、存储和检索 |
| `src/InputMethod` | 文本输入法引擎、后台、候选窗与设置 |
| `src/FileFavoritesMenu` | Windows 收藏菜单 |
| `resources` | 构建必需的图标和培训资源 |
| `scripts` | 下载启动与统一构建入口 |
| `docs` | 开发和验证说明 |
| `licenses` | 第三方许可材料 |

## 主要功能

- 截图、区域截图、录屏、贴图与拾色。
- 贴边便签：待办、剪切板、收藏夹、工具和日记。
- 本地日记：树形目录、TXT/Markdown、检索替换、图片与表格、自动保存、回收站、导入导出和备份。
- 全局语音输入、文本输入法和文本对比。

## 版本与对应源码

当前启动入口固定使用 [小蟒 2.3 / 2026-10-15 r2](https://github.com/zhanglegezhang/xiaomang/releases/tag/v2.3-20261015-r2)，不跟随 latest 自动更换版本。

- 使用包：`xiaomang2.3_2026-10-15_usage.zip`
- SHA256：`A7DA4CACAC2755B20B25A49A2A573FF64F6B90871A9C1F72D432EA0E47FE1390`
- 对应源码标签：`v2.3-20261015-r2`。产品仍为 2.3，r2 用于区分同日发布批次。
- 本次修复轻量包备份被空输入法数据阻断的问题；迁移已有输入法数据时，提前提示缺失组件。
- 更新主程序、便签、日记和内置说明材料的机器人 Logo 为透明圆角样式。

该标签包含完整维护源码、资源与构建入口，和同一 Release 的使用包对应，详细校验信息见 [发布对应关系](docs/RELEASE-v2.3-20261015-r2.json) 和 [验证说明](docs/VERIFICATION.md)。历史 r1 标签与使用包保留。GitHub 自动生成的 Source code 附件是源码，不是预编译程序。按需组件仍固定使用 [xiaomang_component v1.0](https://github.com/zhanglegezhang/xiaomang_component/releases/tag/v1.0)，本次没有重新发布该组件仓库；输入法图标源码已更新，v1.0 下载包中的图标仍以原组件为准。

## 开源来源与许可证

这是小蟒实际修改和维护的工程，基于 [ShareX](https://github.com/ShareX/ShareX)、[青简输入法](https://github.com/qingjian-team/qingjian)、[NoteGen](https://github.com/codexu/note-gen) 等项目。内部程序集和 crate 名称保留，以保持兼容。

仓库根 [LICENSE](LICENSE)、[NOTICE.txt](NOTICE.txt) 和各组件许可证说明版权、修改与分发条件。第三方依赖仍遵守各自许可。必要的上游声明和源码保留，不以更名删除原作者署名。

问题反馈：[Issues](https://github.com/zhanglegezhang/xiaomang/issues)。
