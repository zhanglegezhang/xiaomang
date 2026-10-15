param(
    [string]$Source = (Join-Path $PSScriptRoot 'source/qingjian-0.1.4'),
    [ValidateSet('Test', 'Build', 'Build32', 'Format', 'Clippy')][string]$Action = 'Build',
    [string]$Log = (Join-Path $PSScriptRoot 'build.log')
)
$ErrorActionPreference = 'Stop'
$taskVcScript = Join-Path $PSScriptRoot ('vs-buildtools/VC/Auxiliary/Build/' + $(if ($Action -eq 'Build32') { 'vcvarsamd64_x86.bat' } else { 'vcvars64.bat' }))
if (-not (Test-Path -LiteralPath $taskVcScript)) { throw 'MSVC build tools are not ready' }
$taskVcEnvironment = & $env:ComSpec /d /s /c "`"`"$taskVcScript`" >nul && set`""
if ($LASTEXITCODE -ne 0) { throw 'Unable to initialize MSVC' }
foreach ($taskLine in $taskVcEnvironment) {
    if ($taskLine -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], 'Process') }
}
$env:PATH = (Join-Path $PSScriptRoot 'rust-1.96.0-msvc/bin') + ';' + $env:PATH
$env:CARGO_HOME = Join-Path $PSScriptRoot 'cargo-home'
$env:CARGO_TARGET_DIR = Join-Path $PSScriptRoot 'upstream/qingjian-0.1.4/target'
$env:CARGO_BUILD_JOBS = '1'
$env:CARGO_PROFILE_DEV_DEBUG = '0'
$env:CARGO_PROFILE_TEST_DEBUG = '0'
$env:QINGJIAN_UIACCESS = '0'
# 仅沿用本机已启用的代理，不修改系统网络设置。
$taskProxy = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
if ($taskProxy.ProxyEnable -and $taskProxy.ProxyServer -match '^127\.0\.0\.1:\d+$') {
    $env:CARGO_HTTP_PROXY = 'http://' + $taskProxy.ProxyServer
    $env:HTTPS_PROXY = $env:CARGO_HTTP_PROXY
    $env:HTTP_PROXY = $env:CARGO_HTTP_PROXY
}
$env:CARGO_HTTP_MULTIPLEXING = 'false'
Push-Location $Source
try {
    switch ($Action) {
        Test { cargo test --locked -p qingjian-core -p qingjian-platform -p qingjian-windows-server -p qingjian-windows-tsf 2>&1 | Tee-Object -FilePath $Log }
        Build { cargo build --release --locked -p qingjian-windows-server -p qingjian-windows-tsf -p qingjian-windows-settings 2>&1 | Tee-Object -FilePath $Log }
        Build32 { cargo build --release --locked -p qingjian-windows-tsf --target i686-pc-windows-msvc 2>&1 | Tee-Object -FilePath $Log }
        Format { cargo fmt --all -- --check 2>&1 | Tee-Object -FilePath $Log }
        Clippy { cargo clippy --locked -p qingjian-core -p qingjian-platform -p qingjian-windows-server -p qingjian-windows-tsf -p qingjian-windows-settings --all-targets -- -D warnings 2>&1 | Tee-Object -FilePath $Log }
    }
    if ($LASTEXITCODE -ne 0) { throw "Cargo $Action failed: $LASTEXITCODE" }
} finally { Pop-Location }
