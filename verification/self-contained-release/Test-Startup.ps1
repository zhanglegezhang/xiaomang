param([Parameter(Mandatory=$true)][string]$Directory,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
$Directory=[IO.Path]::GetFullPath($Directory);$Output=[IO.Path]::GetFullPath($Output);New-Item -ItemType Directory -Force $Output | Out-Null
. (Join-Path $PSScriptRoot 'ProcessGuard.ps1')
$entry=Join-Path $Directory 'xiaomang.exe';$rows=@()
for($index=0;$index -lt 3;$index++){
    $snapshot=Join-Path $Output "startup-$index.json";$trace=Join-Path $Output "host-$index.txt"
    @{Personal=(Join-Path $Directory 'xiaomangtongxue');Input=(Join-Path $Output 'input');Media=(Join-Path $Output 'media');Hotkeys=(Join-Path $Directory 'xiaomangtongxue/HotkeysConfig.json');Files=@()}|ConvertTo-Json -Depth 5|Set-Content $snapshot -Encoding utf8
    $empty=Join-Path $Output 'empty-dotnet';New-Item -ItemType Directory -Force $empty|Out-Null
    $info=[Diagnostics.ProcessStartInfo]::new($entry);$info.UseShellExecute=$false;$info.WorkingDirectory=$Directory;$info.WindowStyle='Hidden';$info.ArgumentList.Add('-silent');$info.ArgumentList.Add('-NoHotkeys')
    $info.Environment['DOTNET_ROOT']=$empty;$info.Environment['DOTNET_ROOT_X64']=$empty;$info.Environment['DOTNET_MULTILEVEL_LOOKUP']='0';$info.Environment['COREHOST_TRACE']='1';$info.Environment['COREHOST_TRACEFILE']=$trace;$info.Environment['DOTNET_DISABLE_GUI_ERRORS']='1';$info.Environment['XIAOMANG_UPGRADE_CHECK']=$snapshot
    $clock=[Diagnostics.Stopwatch]::StartNew();$process=[Diagnostics.Process]::Start($info)
    try{
        while(!(Test-Path ($snapshot+'.result'))){if($process.HasExited){throw 'Self-contained startup exited'};if($clock.Elapsed.TotalSeconds -gt 30){throw 'Startup/data-load receipt timeout'};Start-Sleep -Milliseconds 25}
        $elapsed=$clock.Elapsed.TotalMilliseconds;$receipt=Get-Content ($snapshot+'.result') -Raw|ConvertFrom-Json
        if(!$receipt.Success -or $receipt.Version -ne '3.4' -or !$process.Responding){throw 'Startup did not load 3.4 successfully'}
        $modules=@($process.Modules|Where-Object {$_.ModuleName -in @('coreclr.dll','hostfxr.dll','hostpolicy.dll')}|ForEach-Object {$_.FileName})
        if($modules.Count -ne 3 -or @($modules|Where-Object {!$_.StartsWith($Directory+'\',[StringComparison]::OrdinalIgnoreCase)}).Count){throw 'Startup loaded nonlocal runtime'}
        $rows+=@{Iteration=$index;ReadyMs=$elapsed;Version=$receipt.Version;RuntimeModules=$modules}
        Write-Output "PASS isolated startup $index loaded data and local runtime: $([Math]::Round($elapsed)) ms"
    }finally{Close-TestApp $entry}
}
@{Passed=$true;Rows=$rows;Scope='Empty DOTNET_ROOT and DOTNET_ROOT_X64; host trace and loaded-module confirmation; existing user authorization reused locally; not a clean VM. First process after copy, then warm OS-cache starts.'}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $Output 'results.json') -Encoding utf8
