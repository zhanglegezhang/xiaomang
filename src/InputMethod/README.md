# 小蟒文本输入法

基于青简 0.1.4 修改的维护分支，包括引擎、Windows TSF、独立后台和候选设置。小蟒按需下载安装对应组件；开发构建不会自动注册输入法。

crates 是共享引擎，apps/windows 是 Windows 程序，assets 保存公开词表与许可，tools 保留数据生成工具。Rust workspace 仍包含跨平台成员，保留它们以维持锁定依赖与完整工程；小蟒统一入口只构建 Windows 产品。

构建见 [开发指南](../../docs/DEVELOPMENT.md)，来源与修改见 [NOTICE](../../NOTICE.txt)，许可证见 [LICENSE](LICENSE)，数据来源见 assets 内各 README。
