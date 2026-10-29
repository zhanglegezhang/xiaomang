# 文本输入法组件更新

从小蟒 3.12 起，在“全局设置 → 注册 → 文本输入”选择“检查并更新”。更新通道为 https://github.com/zhanglegezhang/xiaomang_component/releases/download/text-input-channel/latest.json 。

程序先下载并验证压缩包和逐文件 SHA256，下载期间旧后台继续工作；验证通过后暂停监控和自动恢复，正常保存退出后台，备份并替换组件，再验证系统注册和后台启动。系统 DLL 变化时需要 Windows 管理员授权。失败时恢复旧组件，恢复失败会保留备份并提示，不能显示升级成功。

更新不覆盖用户配置和学习记录，不重置快捷键、排布或中英文偏好。原本暂停的后台不会因为检查更新被启用。已经打开的其他应用可能仍使用旧 DLL，需重新打开该应用后才能载入新 DLL。

本次组件版本为 3.11.1，系统注册版本目录仍沿用兼容身份；以发行清单和文件校验值判断版本，不以目录名判断新旧。

验证：清单规则 12 项、更新事务与真实旧包升级 21 项通过；独立目录内旧、新后台各完成 30 轮候选上屏、数字与快捷键协议测试。未在另一台物理电脑验证，不等同于所有系统输入框及管理员安装环境覆盖。

开发者可在仓库根目录运行：

```powershell
dotnet run --project verification/input-component-catalog/Check.csproj
dotnet run --project verification/input-component-update/Check.csproj
```

两个命令仅使用生成的测试数据；真实旧发行包升级用例需要额外提供历史发行包和新发行清单，不包含在源码下载中。
