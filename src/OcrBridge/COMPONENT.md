# 中英 OCR 按需组件

运行环境：Windows x64。主程序嵌入桥接 DLL，模型和推理依赖从 GitHub Release 按需下载。

唯一运行下载清单是 `../Desktop/ShareX/Assets/MixedOcrManifest.json`，固定 ZIP URL、长度、SHA256 和每个允许解压文件的 SHA256。`UpstreamManifest.json` 仅记录上游来源和原始校验值，不供客户端下载。更新评估脚本不会覆盖正式下载地址。

发布组件时，只收集清单中的运行 DLL、三个 ONNX 模型、keys.txt 和许可证/第三方声明。排除 ready、缓存、日志、测试程序和个人数据；ready 由安装器在校验成功后写入。ZIP 内根目录为 MixedOCR。

使用 项目发布脚本 Compress-Release.ps1 和 7-Zip 26.03 固定压缩参数制作 ZIP。发布前通过 7-Zip 完整性检查和 .NET 逐文件 SHA256 检查；发布到 zhanglegezhang/xiaomang_component 的 v1.0 Release，采用新的唯一附件名称，不覆盖旧附件。更新正式下载清单后验证真实下载、取消安装、组件复用、识别与失败回退。

2026-10-18 组件：xiaomang-ocr-ppocrv5-v1-win-x64.zip，29,854,102 字节；安装文件 51,722,649 字节。已有有效 v1 组件可复用，无需重新下载。发行使用包继续排除 ThirdParty/MixedOCR。
