param(
    [string]$Source = "D:\xiaomangtongxue\tools\ShareX\app",
    [Parameter(Mandatory = $true)]
    [string]$Destination
)

$ErrorActionPreference = "Stop"
$sourcePath = [System.IO.Path]::GetFullPath($Source)
$destinationPath = [System.IO.Path]::GetFullPath($Destination)

if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
    throw "运行文件目录不存在：$sourcePath"
}
if (Test-Path -LiteralPath $destinationPath) {
    throw "目标目录已经存在。为避免误删文件，请换一个新的目录：$destinationPath"
}

New-Item -ItemType Directory -Path $destinationPath | Out-Null

# Restrict root copies to runtime file types and explicitly named support files.
$developmentPattern = '(?i)(^|[._\s-])(before|backup|bak|old|original|copy|dev|test)([._\s-]|$)|备份|副本'
$supportFiles = @('xiaomang.exe', 'createdump.exe',
    'recorder-devices-0.12.10-setup.exe', 'Portable', 'ShareX_File_Icon.ico',
    'ShareX.deps.json', 'ShareX.runtimeconfig.json', 'resources.pri')
$excludedFiles = @(
    "ApplicationConfig.json", "HotkeysConfig.json", "UploadersConfig.json", "History.db",
    "ShareX.ImageEditor.App.exe", "ShareX.ImageEditor.App.dll", "ShareX.ImageEditor.App.deps.json", "ShareX.ImageEditor.App.runtimeconfig.json",
    "ShareX_NativeMessagingHost.exe", "ShareX_NativeMessagingHost.dll", "ShareX_NativeMessagingHost.dll.config", "ShareX_NativeMessagingHost.deps.json", "ShareX_NativeMessagingHost.runtimeconfig.json",
    "host-manifest-chrome.json", "host-manifest-firefox.json"
)
$excludedRuntimePatterns = @(
    "Avalonia*", "av_libglesv2.dll", "CommunityToolkit.Mvvm.dll", "DirectML*.dll",
    "Microsoft.ML.OnnxRuntime.dll", "OpenTK*.dll", "glfw3.dll",
    "HarfBuzzSharp.dll", "libHarfBuzzSharp.dll", "MicroCom.Runtime.dll", "Tmds.DBus.Protocol.dll",
    "SkiaSharp*.dll", "libSkiaSharp.dll", "ShareX.ImageEditor.dll", "Vortice*.dll", "SharpGen*.dll",
    "exiftool.exe",
    # Upload is disabled in this desktop-only build. Keep the shared UploadersLib model assembly,
    # but omit protocol runtimes that are only loaded by FTP/SFTP upload actions.
    "BouncyCastle.Cryptography.dll", "FluentFTP.dll", "Renci.SshNet.dll"
)
Get-ChildItem -LiteralPath $sourcePath -File | Where-Object {
    $file = $_
    $matchesRemovedRuntime = $excludedRuntimePatterns | Where-Object { $file.Name -like $_ }
    ($_.Extension -eq '.dll' -or $supportFiles -contains $_.Name) -and
        $_.Name -notmatch $developmentPattern -and $excludedFiles -notcontains $_.Name -and -not $matchesRemovedRuntime
} | Copy-Item -Destination $destinationPath

# Fresh build output still names the generated apphost ShareX.exe. Ship only the product entry.
$entry = Join-Path $destinationPath 'xiaomang.exe'
if (!(Test-Path -LiteralPath $entry -PathType Leaf)) {
    $builtEntry = Join-Path $sourcePath 'ShareX.exe'
    if (!(Test-Path -LiteralPath $builtEntry -PathType Leaf)) { $builtEntry = Join-Path $sourcePath 'xiaomangtongxue.exe' }
    if (!(Test-Path -LiteralPath $builtEntry -PathType Leaf)) { throw 'Missing product launcher' }
    Copy-Item -LiteralPath $builtEntry -Destination $entry
}

function Copy-RuntimeTree([string]$From, [string]$To) {
    New-Item -ItemType Directory -Path $To -Force | Out-Null
    foreach ($entry in Get-ChildItem -LiteralPath $From) {
        if ($entry.Name -match $developmentPattern -or $entry.Extension -in @('.pdb', '.xml')) { continue }
        $target = Join-Path $To $entry.Name
        if ($entry.PSIsContainer) { Copy-RuntimeTree $entry.FullName $target }
        else { Copy-Item -LiteralPath $entry.FullName -Destination $target }
    }
}

$runtimeFolders = @("runtimes", "Licenses", "ThirdParty\SevenZip", "ThirdParty\LocalDiary", "ThirdParty\FileFavoritesMenu")
foreach ($folderName in $runtimeFolders) {
    $folder = Join-Path $sourcePath $folderName
    if (Test-Path -LiteralPath $folder -PathType Container) {
        Copy-RuntimeTree $folder (Join-Path $destinationPath $folderName)
    }
}

# Everything must build its index on the recipient's computer. Never distribute this
# machine's index, preferences or run history.
$everythingSource = Join-Path $sourcePath "ThirdParty\Everything"
if (Test-Path -LiteralPath $everythingSource -PathType Container) {
    $everythingDestination = Join-Path $destinationPath "ThirdParty\Everything"
    New-Item -ItemType Directory -Path $everythingDestination -Force | Out-Null
    $everythingRuntimeFiles = @("everything.exe", "Everything.lng", "LICENSE-Everything.txt")
    foreach ($fileName in $everythingRuntimeFiles) {
        $filePath = Join-Path $everythingSource $fileName
        if (Test-Path -LiteralPath $filePath -PathType Leaf) {
            Copy-Item -LiteralPath $filePath -Destination (Join-Path $everythingDestination $fileName)
        }
    }
}

# Fresh publish uses culture folders at its root; installed copies use Languages/.
foreach ($culture in @('zh-CN', 'zh-TW', 'zh-Hans', 'zh-Hant')) {
    $languageSource = Join-Path $sourcePath ('Languages/' + $culture)
    if (!(Test-Path -LiteralPath $languageSource -PathType Container)) {
        $languageSource = Join-Path $sourcePath $culture
    }
    if (Test-Path -LiteralPath $languageSource -PathType Container) {
        Copy-RuntimeTree $languageSource (Join-Path $destinationPath ('Languages/' + $culture))
    }
}

$forbiddenNames = @(
    'ShareX.exe', 'xiaomangtongxue.exe',
    "Backup", "Logs", "log", "Screenshots", "daily_news", "DailyNewsWebView", "TranslationWebView",
    "便签日志", "剪切板归档", "ShareX", "xiaomangtongxue",
    "ApplicationConfig.json", "HotkeysConfig.json", "UploadersConfig.json", "History.db",
    "Everything.db", "Everything.ini", "Run History.csv"
)
$leaks = Get-ChildItem -LiteralPath $destinationPath -Recurse -Force | Where-Object {
    $forbiddenNames -contains $_.Name -or $_.Extension -in @(".pdb", ".xml") -or $_.Name -match $developmentPattern
}
if ($leaks) {
    throw "发布目录检查失败，发现不允许打包的文件：$($leaks[0].FullName)"
}

$fileCount = (Get-ChildItem -LiteralPath $destinationPath -File -Recurse).Count
$size = (Get-ChildItem -LiteralPath $destinationPath -File -Recurse | Measure-Object Length -Sum).Sum
Write-Host "干净发布目录已准备完成：$destinationPath"
Write-Host "文件数：$fileCount；大小：$([math]::Round($size / 1MB, 2)) MB"
Write-Host "此脚本不会创建压缩包，也不会复制任何用户配置、历史、缓存、截图、备份、Everything 索引或调试文件。"
