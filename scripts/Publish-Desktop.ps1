[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$Dotnet='dotnet',
    [string[]]$BuildArguments=@()
)
$ErrorActionPreference='Stop'
$publishOutput=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $publishOutput){throw 'Publish output must be a new directory; never merge with stale runtime files.'}
& $Dotnet publish $Project -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:TargetLatestRuntimePatch=true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:UseSharedCompilation=false -p:NuGetAudit=false -m:1 -o $publishOutput @BuildArguments
if($LASTEXITCODE -ne 0){throw "Self-contained publish failed ($LASTEXITCODE)"}
& (Join-Path $PSScriptRoot 'Test-SelfContainedRuntime.ps1') -Directory $publishOutput -Entry 'ShareX.exe'
if(!$?){throw 'Self-contained publish validation failed'}
