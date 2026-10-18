# 发布压缩约定

所有发布压缩包统一使用 7-Zip 26.03，保持 ZIP 格式与 Deflate 压缩。
固定参数：`-tzip -mm=Deflate -mx=9 -mfb=258 -mpass=15 -mmt=off -mcu=on -scsUTF-8`。

先准备并筛查成品目录，再运行：

```powershell
./scripts/Compress-Release.ps1 -Payload <成品目录> -Archive <输出ZIP路径> -SevenZipPath <7z.exe路径>
```

不使用 .NET ZipFile 或 Compress-Archive 替代，不覆盖已有压缩包。工具版本或压缩参数更改需事先确认。
压缩脚本执行完整性测试；发布前还应逐文件检查内容、许可、隐私和排除项，验证 Windows/.NET 解压及更新程序兼容性。
每次制作新的可分发使用包或同步 GitHub 发布，均先将产品小版本号加 1，包含仅重新打包。一次发布的构建、校验和失败重试共用同一版本，历史发布保留；日常内部修改不自动加号。

2026-10-18：中英 OCR 组件不预装到使用包。组件固定为 xiaomang_component/v1.0 的 xiaomang-ocr-ppocrv5-v1-win-x64.zip，约29MiB，安装约49MiB；下载校验值见 ShareX/Assets/MixedOcrManifest.json。

