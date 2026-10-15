[CmdletBinding()]
param(
 [ValidateSet('Desktop','Diary','InputMethod','Native','All')][string]$Component='All',
 [string]$Toolchain=$env:LLVM_MINGW_HOME,
 [string]$WindowsSdkBin=$env:WINDOWS_SDK_BIN,
 [switch]$Test,
 [switch]$Offline
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$env:CARGO_BUILD_JOBS){$env:CARGO_BUILD_JOBS='1'}
function Invoke-Checked([string]$Command,[string[]]$Arguments) {
 & $Command @Arguments
 if($LASTEXITCODE -ne 0){throw "$Command failed ($LASTEXITCODE)"}
}
function Require-Command([string]$Name) {
 if(!(Get-Command $Name -ErrorAction SilentlyContinue)){throw "Missing $Name. See docs/DEVELOPMENT.md."}
}
function Build-Native {
 if(!$Toolchain -or !$WindowsSdkBin){throw 'Set LLVM_MINGW_HOME and WINDOWS_SDK_BIN, or pass -Toolchain and -WindowsSdkBin.'}
 & (Join-Path $root 'src/Desktop/VoiceInput/Build.ps1') -Toolchain $Toolchain
 & (Join-Path $root 'src/Desktop/KeyboardInput/build.ps1') -Toolchain $Toolchain
 & (Join-Path $root 'src/FileFavoritesMenu/build.ps1') -Toolchain $Toolchain -WindowsSdkBin $WindowsSdkBin
}
function Build-Diary {
 Require-Command 'pnpm'; Require-Command 'cargo'
 Push-Location (Join-Path $root 'src/Diary')
 try {
  $install=@('install','--frozen-lockfile','--ignore-scripts'); if($Offline){$install+='--offline'}
  Invoke-Checked 'pnpm' $install
  $env:NEXT_TELEMETRY_DISABLED='1'
  Invoke-Checked 'pnpm' @('run','build')
  $common=@('--manifest-path','src-tauri/Cargo.toml','--locked'); if($Offline){$common+='--offline'}
  Invoke-Checked 'cargo' (@('build')+$common+@('--release','--bin','xiaomang-diary','--features','tauri/custom-protocol'))
  $target=if($env:CARGO_TARGET_DIR){$env:CARGO_TARGET_DIR}else{Join-Path (Get-Location) 'src-tauri/target'}
  $out=Join-Path $root 'artifacts/Diary'; New-Item -ItemType Directory -Force -Path $out | Out-Null
  Copy-Item -LiteralPath (Join-Path $target 'release/xiaomang-diary.exe') -Destination $out
  Copy-Item -LiteralPath 'LICENSE' -Destination $out
  if($Test){Invoke-Checked 'cargo' (@('test')+$common+@('--bin','xiaomang-diary'))}
 } finally {Pop-Location}
}
function Build-InputMethod {
 Require-Command 'cargo'
 Push-Location (Join-Path $root 'src/InputMethod')
 try {
  $env:QINGJIAN_UIACCESS='0'
  $common=@('--release','--locked'); if($Offline){$common+='--offline'}
  Invoke-Checked 'cargo' (@('build')+$common+@('-p','qingjian-windows-server','-p','qingjian-windows-tsf','-p','qingjian-windows-settings'))
  $vcvars=Join-Path $env:VSINSTALLDIR 'VC/Auxiliary/Build/vcvarsall.bat'
  if(!(Test-Path -LiteralPath $vcvars)){throw 'Run in a Visual Studio x64 developer shell with x86 C++ build tools installed.'}
  $saved=@{}; Get-ChildItem Env: | ForEach-Object {$saved[$_.Name]=$_.Value}
  try {
   $lines=& cmd /d /s /c "`"$vcvars`" x64_x86 >nul && set"
   if($LASTEXITCODE){throw 'Could not initialize the x86 C++ environment.'}
   foreach($line in $lines){if($line -match '^([^=]+)=(.*)$'){[Environment]::SetEnvironmentVariable($Matches[1],$Matches[2],'Process')}}
   Invoke-Checked 'cargo' (@('build')+$common+@('-p','qingjian-windows-tsf','--target','i686-pc-windows-msvc'))
  } finally {
   Get-ChildItem Env: | Where-Object {!$saved.ContainsKey($_.Name)} | ForEach-Object {[Environment]::SetEnvironmentVariable($_.Name,$null,'Process')}
   foreach($key in $saved.Keys){[Environment]::SetEnvironmentVariable($key,$saved[$key],'Process')}
  }
  if($Test){Invoke-Checked 'cargo' (@('test')+$common+@('-p','qingjian-core','-p','qingjian-platform','--lib'))}
 } finally {Pop-Location}
}
function Build-Desktop {
 Require-Command 'dotnet'
 if(!(Test-Path -LiteralPath (Join-Path $root 'src/Desktop/VoiceInput/bin/VoiceInputSetup.exe'))){Build-Native}
 $project=Join-Path $root 'src/Desktop/ShareX/ShareX.csproj'
 $out=Join-Path $root 'artifacts/Desktop'
 Invoke-Checked 'dotnet' @('restore',$project,'-p:Platform=x64','-m:1')
 Invoke-Checked 'dotnet' @('build',$project,'-c','Release','-p:Platform=x64','-m:1','-p:UseSharedCompilation=false','--no-restore',"-p:OutputPath=$out/")
 Move-Item -LiteralPath (Join-Path $out 'ShareX.exe') -Destination (Join-Path $out 'xiaomang.exe') -Force
}
switch($Component){
 'Native' {Build-Native}
 'Diary' {Build-Diary}
 'InputMethod' {Build-InputMethod}
 'Desktop' {Build-Desktop}
 'All' {Build-Native; Build-Diary; Build-InputMethod; Build-Desktop}
}
Write-Host "Build completed: $Component"
