param([Parameter(Mandatory=$true)][string]$Payload,[Parameter(Mandatory=$true)][string]$Old32,[Parameter(Mandatory=$true)][string]$Old33,[Parameter(Mandatory=$true)][string]$Output,[string]$DailyEntry,[string]$OldFrameworkRoot,[ValidateSet('3.2','3.3')][string[]]$Versions=@('3.2','3.3'))
$ErrorActionPreference='Stop'
$Payload=[IO.Path]::GetFullPath($Payload);$Output=[IO.Path]::GetFullPath($Output)
if(Get-ChildItem -LiteralPath $Payload -Recurse -File | Where-Object {$_.Name -in @('ApplicationConfig.json','HotkeysConfig.json','UploadersConfig.json','first-use.bin')}){throw 'Update payload must be a clean package without synthetic or personal configurations.'}
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $PSScriptRoot 'ProcessGuard.ps1')
$daily=if($DailyEntry){[IO.Path]::GetFullPath($DailyEntry)}else{Join-Path $workspace 'tools/ShareX/app/xiaomang.exe'}
if(!(Test-Path -LiteralPath $daily)){throw 'Supply -DailyEntry with the existing product executable; tests close and restore it normally.'}
$rows=[Collections.Generic.List[object]]::new();$checks=0
function Assert($ok,$name){if(!$ok){throw $name};$script:checks++;Write-Output "PASS $name"}
function Start-Fixture([string]$Entry,[string]$Trace,[bool]$Isolate,[string]$Snapshot=''){
    $info=[Diagnostics.ProcessStartInfo]::new($Entry);$info.WorkingDirectory=Split-Path $Entry;$info.UseShellExecute=$false;$info.WindowStyle='Hidden'
    $info.ArgumentList.Add('-silent');$info.ArgumentList.Add('-NoHotkeys')
    if($Isolate){$empty=Join-Path $Output 'empty-dotnet';New-Item -ItemType Directory -Force $empty | Out-Null;$info.Environment['DOTNET_ROOT']=$empty;$info.Environment['DOTNET_ROOT_X64']=$empty;$info.Environment['DOTNET_MULTILEVEL_LOOKUP']='0'}
    elseif($OldFrameworkRoot){$info.Environment['DOTNET_ROOT']=$OldFrameworkRoot;$info.Environment['DOTNET_ROOT_X64']=$OldFrameworkRoot}
    if($Snapshot){$info.Environment['XIAOMANG_UPGRADE_CHECK']=$Snapshot}
    $info.Environment['COREHOST_TRACE']='1';$info.Environment['COREHOST_TRACEFILE']=$Trace;$info.Environment['DOTNET_DISABLE_GUI_ERRORS']='1'
    $info.Environment['HTTP_PROXY']='http://127.0.0.1:9';$info.Environment['HTTPS_PROXY']='http://127.0.0.1:9';$info.Environment['ALL_PROXY']='http://127.0.0.1:9'
    return [Diagnostics.Process]::Start($info)
}
New-Item -ItemType Directory -Force $Output | Out-Null
Close-TestApp $daily
try{
    foreach($version in $Versions){
        $case=Join-Path $Output ('from-'+$version);if(Test-Path $case){throw 'Use a fresh test output'};New-Item -ItemType Directory $case | Out-Null
        $origin=if($version -eq '3.2'){$Old32}else{$Old33};$app=Join-Path $case 'app';New-Item -ItemType Directory $app | Out-Null;Copy-Item (Join-Path $origin '*') $app -Recurse
        $entry=Join-Path $app 'xiaomang.exe';$personal=Join-Path $app 'xiaomangtongxue';New-Item -ItemType Directory (Join-Path $personal 'data') -Force | Out-Null
        $text='升级验收合成待办 '+$version
        @{Language='SimplifiedChinese';StartupPreferenceInitialized=$true;AutoCheckUpdate=$false;ShowTray=$true;SilentRun=$true;DisableHotkeys=$true;DailyNews=@{Enabled=$false};TodoSettings=@{Items=@(@{Id=[guid]::NewGuid().ToString();Text=$text;CreatedAt='2026-10-23T10:00:00'});FavoriteItems=@(@{Id=[guid]::NewGuid().ToString();Text='合成收藏';CreatedAt='2026-10-23T10:00:00'});ClipboardItems=@();WidgetWidth=420;ActiveModule='Todo'}} | ConvertTo-Json -Depth 15 | Set-Content (Join-Path $personal 'ApplicationConfig.json') -Encoding utf8
        [IO.File]::WriteAllText((Join-Path $personal 'data/keep.txt'),'synthetic private data, must remain intact')
        $keepHash=(Get-FileHash (Join-Path $personal 'data/keep.txt')).Hash
        # The broken 3.3 app-local hostfxr searches its own shared directory.
        # Supply frameworks only to this disposable old-version fixture, then
        # remove the junction before updating and testing self-contained startup.
        $frameworkLink=Join-Path $app 'shared'
        if($version -eq '3.3' -and $OldFrameworkRoot){New-Item -ItemType Junction -Path $frameworkLink -Target (Join-Path $OldFrameworkRoot 'shared')|Out-Null}
        $clock=[Diagnostics.Stopwatch]::StartNew();$old=Start-Fixture $entry (Join-Path $case 'old-host.txt') $false
        Start-Sleep -Seconds 4;Assert (!$old.HasExited -and $old.Responding -and @($old.Modules|Where-Object ModuleName -eq 'coreclr.dll').Count -eq 1) "$version original starts with temporary runtime available"
        $oldMs=$clock.ElapsedMilliseconds;Close-TestApp $entry
        if(Test-Path -LiteralPath $frameworkLink){if(!(Get-Item -LiteralPath $frameworkLink).LinkType){throw 'Expected test framework junction'};[IO.Directory]::Delete($frameworkLink)}
        # Extract the actual helper embedded in this released old version. Do
        # not substitute the new helper when testing the update path.
        $recovery=Join-Path $app 'XiaomangUpdateRecovery.ps1'
        $updateRoot=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('XiaomangUpdates/runtime34-'+[guid]::NewGuid().ToString('N'));$stage=Join-Path $updateRoot 'attempt';New-Item -ItemType Directory -Force $stage | Out-Null
        $snapshot=Join-Path $updateRoot 'data-snapshot.json'
        @{Personal=$personal;Input=(Join-Path $case 'input');Media=(Join-Path $case 'media');Hotkeys=(Join-Path $personal 'HotkeysConfig.json');Backup='';Files=@()} | ConvertTo-Json -Depth 8 | Set-Content $snapshot -Encoding utf8
        & (Get-Process -Id $PID).Path -NoProfile -File (Join-Path $PSScriptRoot 'Export-UpdateHelper.ps1') -Assembly (Join-Path $app 'ShareX.dll') -ScriptPath (Join-Path $stage 'install.ps1') -RecoveryPath $recovery
        if($LASTEXITCODE){throw 'Old helper extraction failed'}
        @{App=$app;Stage=$stage;Payload=$Payload;RecoveryScript=$recovery;Snapshot=$snapshot;ProcessId=99999999;Started=0} | ConvertTo-Json | Set-Content (Join-Path $stage 'install.json') -Encoding utf8
        $saved=@{};foreach($name in @('DOTNET_ROOT','DOTNET_ROOT_X64','DOTNET_MULTILEVEL_LOOKUP','COREHOST_TRACE','COREHOST_TRACEFILE','DOTNET_DISABLE_GUI_ERRORS')){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
        try{
            $empty=Join-Path $Output 'empty-dotnet';New-Item -ItemType Directory -Force $empty|Out-Null
            $env:DOTNET_ROOT=$empty;$env:DOTNET_ROOT_X64=$empty;$env:DOTNET_MULTILEVEL_LOOKUP='0';$env:COREHOST_TRACE='1';$env:COREHOST_TRACEFILE=Join-Path $case 'upgraded-host.txt';$env:DOTNET_DISABLE_GUI_ERRORS='1'
            $clock.Restart();$helper=Start-Process powershell.exe -ArgumentList @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',('"'+(Join-Path $stage 'install.ps1')+'"'),'-Config',('"'+(Join-Path $stage 'install.json')+'"'),'-NoUI') -WindowStyle Hidden -PassThru
            if(!$helper.WaitForExit(180000)){throw 'Real old update helper timed out; preserve evidence'}
        }finally{foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}
        $result=Get-Content (Join-Path $stage 'result.txt') -Raw -Encoding utf8;Assert ($result -match '^更新成功') "$version actual embedded update helper succeeds"
        $receipt=Get-Content ($snapshot+'.result') -Raw|ConvertFrom-Json;Assert ($receipt.Success -and $receipt.Version -eq '3.4') "$version -> 3.4 new main confirms data loading"
        $upgradeMs=$clock.ElapsedMilliseconds;$new=Get-Process -Id $receipt.ProcessId;Assert $new.Responding 'Updated application responds'
        Assert ((Get-FileHash (Join-Path $app 'ShareX.dll')).Hash -eq (Get-FileHash (Join-Path $Payload 'ShareX.dll')).Hash) 'Actual installed main matches release payload'
        $modules=@($new.Modules | Where-Object {$_.ModuleName -in @('coreclr.dll','hostfxr.dll','hostpolicy.dll')} | ForEach-Object {$_.FileName})
        Assert ($modules.Count -eq 3 -and @($modules | Where-Object {![IO.Path]::GetFullPath($_).StartsWith($app+'\',[StringComparison]::OrdinalIgnoreCase)}).Count -eq 0) 'Actual updated process loads runtime only from its application directory'
        Close-TestApp $entry
        $config=Get-Content (Join-Path $personal 'ApplicationConfig.json') -Raw|ConvertFrom-Json
        Assert ($config.TodoSettings.Items[0].Text -eq $text -and $config.TodoSettings.FavoriteItems[0].Text -eq '合成收藏' -and $config.TodoSettings.WidgetWidth -eq 420 -and $config.DisableHotkeys) 'Todo, favorite, width and hotkey settings retained'
        Assert ((Get-FileHash (Join-Path $personal 'data/keep.txt')).Hash -eq $keepHash) 'Unrelated synthetic private file retained'
        foreach($file in @('result.txt','transaction.json')){if(Test-Path (Join-Path $stage $file)){Copy-Item (Join-Path $stage $file) $case}}
        Copy-Item ($snapshot+'.result') (Join-Path $case 'startup-receipt.json')
        $clock.Restart();$reopened=Start-Fixture $entry (Join-Path $case 'reopen-host.txt') $true;Start-Sleep -Seconds 3
        Assert (!$reopened.HasExited -and $reopened.Responding) 'Reopens with unavailable system .NET root'
        $restartMs=$clock.ElapsedMilliseconds;Close-TestApp $entry
        $rows.Add([pscustomobject]@{OldVersion=$version;NewVersion='3.4';OldObservationMs=$oldMs;UpgradeMs=$upgradeMs;ReopenObservationMs=$restartMs;LocalRuntimeModules=$modules;SystemDotnetRoots='empty directories';Scope='Real old app and old embedded installer; local payload replaces network download, existing user authorization reused locally; no clean physical-machine claim'})
        # Only remove this successful synthetic transaction after retaining its
        # receipt. No user records or real update transactions are removed.
        $resolved=[IO.Path]::GetFullPath($updateRoot);$allowed=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'XiaomangUpdates'
        if(!$resolved.StartsWith($allowed+'\',[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^runtime34-[a-f0-9]{32}$'){throw 'Test cleanup boundary mismatch'}
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    @{Passed=$true;Checks=$checks;Rows=$rows.ToArray()}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $Output 'results.json') -Encoding utf8
}finally{
    foreach($version in @('3.2','3.3')){Close-TestApp (Join-Path $Output ('from-'+$version+'/app/xiaomang.exe'))}
    Start-Process -FilePath $daily -ArgumentList '-silent' -WorkingDirectory (Split-Path $daily) -WindowStyle Hidden | Out-Null
}
