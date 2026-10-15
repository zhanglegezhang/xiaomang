# 小蟒同学（xiaomang）

小蟒同学是一款面向 Windows 的桌面效率工具，整合截图、便签、本地日记和输入辅助等功能。

## 仓库状态

- 本仓库用于项目介绍、使用包发布，以及后续维护源码公开。
- **完整维护源码尚未上传**，当前仓库不能用于编译小蟒。
- GitHub 自动生成的 `Source code.zip` / `Source code.tar.gz` 仅打包仓库现有文件，不代表已提供完整源码。
- 使用包统一在 [Releases 发布页](https://github.com/zhanglegezhang/xiaomang/releases)提供；如暂无版本，请等待发布者上传。

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

完整维护源码及构建说明计划后续公开，届时会明确对应版本和获取方式。当前页面不作为已经提供完整对应源码的声明。

相关功能基于以下项目进行维护，分发时保留各组件原有许可证、版权及鸣谢：

- [ShareX](https://github.com/ShareX/ShareX)
- [青简输入法](https://github.com/qingjian-team/qingjian)
- [NoteGen](https://github.com/codexu/note-gen)

## 问题反馈

请通过 [Issues](https://github.com/zhanglegezhang/xiaomang/issues)提交问题，注明 Windows 版本、涉及功能和复现步骤。
