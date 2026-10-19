param([string]$Marker,[switch]$NoUI,[switch]$Library,[int]$WaitForProcessId=0,[long]$WaitForStarted=0)
$ErrorActionPreference='Stop'
$script:XMRecoveryRoot=[IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
function Get-XMHash([string]$Path) {
 $stream=[IO.File]::OpenRead($Path);$sha=[Security.Cryptography.SHA256]::Create()
 try {[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$sha.Dispose();$stream.Dispose()}
}
function Sync-XMFile([string]$Path) {
 $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::Read)
 try {$stream.Flush($true)}finally{$stream.Dispose()}
}
function Assert-XMPath([string]$Path) {
 if(!$Path -or ![IO.Path]::IsPathRooted($Path)){throw 'Recovery path must be absolute'}
 for($p=[IO.Path]::GetFullPath($Path);$p;$p=[IO.Path]::GetDirectoryName($p)) {
  if((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Recovery path contains a link'}
 }
}
function Test-XMInside([string]$Path,[string]$Root) {
 $full=[IO.Path]::GetFullPath($Path);$base=[IO.Path]::GetFullPath($Root).TrimEnd('\')
 $full.Equals($base,[StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)
}
function Write-XMJson([string]$Path,$Value) {
 Assert-XMPath $Path
 $temp=$Path+'.write-'+[Guid]::NewGuid().ToString('N')
 try {
  $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($Value|ConvertTo-Json -Depth 12 -Compress))
  $stream=[IO.File]::Open($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
  try {$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
  if([IO.File]::Exists($Path)){[IO.File]::Replace($temp,$Path,[NullString]::Value)}else{[IO.File]::Move($temp,$Path)}
 }finally{if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}
}
function Read-XMJson([string]$Path,[long]$Max=8388608) {
 Assert-XMPath $Path
 if((Get-Item -LiteralPath $Path).Length -gt $Max){throw 'Recovery metadata is too large'}
 Get-Content -LiteralPath $Path -Raw -Encoding UTF8|ConvertFrom-Json
}
function Test-XMProcess([int]$Id,[long]$Started) {
 $process=Get-Process -Id $Id -ErrorAction SilentlyContinue
 $process -and $Started -gt 0 -and $process.StartTime.ToUniversalTime().Ticks -eq $Started
}
function Get-XMTarget([string]$App,[string]$Relative) {
 if(!$Relative -or $Relative.Length -gt 2000 -or [IO.Path]::IsPathRooted($Relative)){throw 'Invalid recovery relative path'}
 foreach($part in $Relative.Replace('/','\').Split('\')) {
  if(!$part -or $part -in @('.','..') -or $part.EndsWith('.') -or $part.EndsWith(' ') -or $part.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0){throw 'Invalid recovery path segment'}
 }
 if($Relative -in @('.xiaomang-update.json','XiaomangUpdateRecovery.ps1','恢复小蟒更新.cmd')){throw 'Update payload includes recovery control files'}
 $target=[IO.Path]::GetFullPath((Join-Path $App $Relative))
 if(!(Test-XMInside $target $App) -or $target.Equals($App,[StringComparison]::OrdinalIgnoreCase)){throw 'Recovery target outside application'}
 Assert-XMPath $target
 $target
}
function Read-XMTransaction([string]$MarkerPath) {
 $pointer=Read-XMJson $MarkerPath 16384
 $app=[IO.Path]::GetFullPath($pointer.App).TrimEnd('\')
 if($pointer.Version -ne 1 -or $pointer.Id -notmatch '^[a-f0-9]{32}$' -or $app -ne $script:XMRecoveryRoot -or
   [IO.Path]::GetFullPath($MarkerPath) -ne (Join-Path $app '.xiaomang-update.json')){throw 'Recovery pointer does not belong to this application'}
 if($pointer.ScriptHash -ne (Get-XMHash (Join-Path $app 'XiaomangUpdateRecovery.ps1'))){throw 'Recovery script hash does not match transaction'}
 $updates=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'XiaomangUpdates'
 if(!(Test-XMInside $pointer.Transaction $updates) -or [IO.Path]::GetFileName($pointer.Transaction) -ne 'transaction.json'){throw 'Unexpected recovery transaction location'}
 $state=Read-XMJson $pointer.Transaction
 if($state.Version -ne 1 -or $state.Id -ne $pointer.Id -or $state.App -ne $app -or $state.Phase -notin @('prepared','committing','validating','rollingBack','rolledBack','complete')){throw 'Unknown recovery transaction state'}
 $entries=@($state.Entries)
 if(!$entries.Count -or $entries.Count -gt 10000 -or ($state.StartedCount -isnot [int] -and $state.StartedCount -isnot [long]) -or $state.StartedCount -lt 0 -or $state.StartedCount -gt $entries.Count){throw 'Invalid recovery operation count'}
 $stage=[IO.Path]::GetDirectoryName($pointer.Transaction)
 if($state.Snapshot -ne (Join-Path ([IO.Path]::GetDirectoryName($stage)) 'data-snapshot.json')){throw 'Unexpected recovery data manifest'}
 $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 foreach($item in $entries) {
  $target=Get-XMTarget $app $item.Relative
  if(!$seen.Add($target) -or $item.Existed -isnot [bool] -or $item.Hash -notmatch '^[A-Fa-f0-9]{64}$' -or ($item.Existed -and $item.OriginalHash -notmatch '^[A-Fa-f0-9]{64}$')){throw 'Invalid recovery operation metadata'}
 }
 [pscustomobject]@{Pointer=$pointer;State=$state;Stage=$stage;StatePath=$pointer.Transaction;Marker=$MarkerPath}
}
function Clear-XMMarker($Context) {
 $current=Read-XMJson $Context.Marker 16384
 if($current.Id -ne $Context.State.Id -or $current.Transaction -ne $Context.StatePath){throw 'Recovery pointer changed'}
 [IO.File]::Delete($Context.Marker)
}
function Assert-XMDataUnchanged($Context) {
 $snapshot=Read-XMJson $Context.State.Snapshot 134217728
 if($snapshot.Backup -ne (Join-Path $Context.Stage 'final-data-backup.zip')){throw 'Unexpected personal backup location'}
 $roots=@($snapshot.Personal,$snapshot.Input,$snapshot.Media)|Where-Object {$_}|Select-Object -Unique
 foreach($root in $roots){Assert-XMPath $root}
 $files=@($snapshot.Files);if($files.Count -gt 100000){throw 'Personal backup has too many entries'}
 $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 $archives=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 Add-Type -AssemblyName System.IO.Compression
 $zip=[IO.Compression.ZipFile]::OpenRead($snapshot.Backup)
 try {
  if($zip.Entries.Count -ne $files.Count){throw 'Personal backup entries do not match manifest'}
  foreach($file in $files) {
   Assert-XMPath $file.Source
   $allowed=$file.Source -eq $snapshot.Hotkeys
   foreach($root in $roots){if(Test-XMInside $file.Source $root){$allowed=$true}}
   if(!$allowed -or !$seen.Add($file.Source) -or $file.Archive -notmatch '^\d+$' -or !$archives.Add([string]$file.Archive) -or $file.Hash -notmatch '^[A-Fa-f0-9]{64}$'){throw 'Invalid personal backup metadata'}
   $entry=$zip.GetEntry([string]$file.Archive);if(!$entry){throw 'Missing personal backup entry'}
   $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
   try {$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}
   if($hash -ne $file.Hash){throw 'Personal backup verification failed'}
   if(![IO.File]::Exists($file.Source) -or (Get-XMHash $file.Source) -ne $file.Hash){throw 'Personal data changed; automatic recovery stopped to preserve it'}
  }
 }finally{$zip.Dispose()}
 # Inspect directories before descending, never recurse through a link.
 $visited=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 $count=0
 foreach($root in $roots) {
  if(!(Test-Path -LiteralPath $root)){continue}
  $pending=[Collections.Generic.Stack[string]]::new();$pending.Push($root)
  while($pending.Count) {
   $directory=$pending.Pop();Assert-XMPath $directory
   if(!$visited.Add([IO.Path]::GetFullPath($directory))){continue}
   if($visited.Count -gt 200000){throw 'Personal directory scan exceeds recovery limit'}
   foreach($file in Get-ChildItem -LiteralPath $directory -Force) {
    $rel=$file.FullName.Substring($root.TrimEnd('\').Length).TrimStart('\')
    if($rel -match '^Logs(\\|$)' -or $file.Name -eq '.personal-transfer.lock'){continue}
    Assert-XMPath $file.FullName
    if($file.PSIsContainer){$pending.Push($file.FullName);continue}
    if(++$count -gt 100000){throw 'Personal file scan exceeds recovery limit'}
    if(!$seen.Contains($file.FullName)){throw 'Additional personal data found; automatic recovery stopped to preserve it'}
   }
  }
 }
}
function Initialize-XMTransaction($Config,$Entries) {
 $app=[IO.Path]::GetFullPath($Config.App).TrimEnd('\')
 if($app -ne $script:XMRecoveryRoot){throw 'Recovery library does not belong to application'}
 $marker=Join-Path $app '.xiaomang-update.json'
 if(Test-Path -LiteralPath $marker){throw 'A previous update requires recovery first'}
 $id=[Guid]::NewGuid().ToString('N');$statePath=Join-Path $Config.Stage 'transaction.json'
 $me=Get-Process -Id $PID
 $state=[pscustomobject]@{Version=1;Id=$id;App=$app;Snapshot=$Config.Snapshot;Phase='prepared';StartedCount=0;HelperId=$PID;HelperStarted=$me.StartTime.ToUniversalTime().Ticks;Entries=@($Entries)}
 Write-XMJson $statePath $state
 Write-XMJson $marker ([pscustomobject]@{Version=1;Id=$id;App=$app;Transaction=$statePath;ScriptHash=(Get-XMHash (Join-Path $app 'XiaomangUpdateRecovery.ps1'))})
 $context=Read-XMTransaction $marker
 Assert-XMDataUnchanged $context
 $context
}
function Set-XMPhase($Context,[string]$Phase,[int]$StartedCount) {
 $Context.State.Phase=$Phase;$Context.State.StartedCount=$StartedCount
 Write-XMJson $Context.StatePath $Context.State
}
function Invoke-XMRecovery([string]$MarkerPath,[switch]$NoRestart,[switch]$FromInstaller) {
 $context=Read-XMTransaction $MarkerPath;$state=$context.State
 if(!$FromInstaller -and (Test-XMProcess $state.HelperId $state.HelperStarted)){throw 'The update helper is still running; wait for it to finish'}
 $entry=Join-Path $state.App 'xiaomang.exe'
 $running=@(Get-Process xiaomang -ErrorAction SilentlyContinue|Where-Object Path -eq $entry)
 if($running.Count){throw 'Close the application normally before recovery'}
 if($state.Phase -eq 'complete'){
  Clear-XMMarker $context
  if(!$NoRestart){Start-Process -FilePath $entry -ArgumentList '-silent' -WorkingDirectory $state.App -WindowStyle Hidden}
  return 'complete'
 }
 # Validate ALL originals, targets, and data before changing even the first file.
 $entries=@($state.Entries);$index=0
 foreach($item in $entries) {
  $target=Get-XMTarget $state.App $item.Relative
  if(Test-Path -LiteralPath $target -PathType Container){throw 'A program file was replaced by a directory'}
  if($item.Existed) {
   $backup=Get-XMTarget (Join-Path $context.Stage 'program-backup') $item.Relative
   if(![IO.File]::Exists($backup) -or (Get-XMHash $backup) -ne $item.OriginalHash){throw 'Original program backup is missing or damaged'}
  }
  if([IO.File]::Exists($target)) {
   $hash=Get-XMHash $target
   if($hash -ne $item.Hash -and (!$item.Existed -or $hash -ne $item.OriginalHash)){throw 'Program file has additional changes; automatic recovery stopped'}
   if($index -ge $state.StartedCount -and (!$item.Existed -or $hash -ne $item.OriginalHash)){throw 'Program state does not match recovery progress'}
  }elseif($item.Existed -and $index -ge $state.StartedCount){throw 'An unmodified program file is missing'}
  $index++
 }
 Assert-XMDataUnchanged $context
 if($state.Phase -ne 'rolledBack') {
  Set-XMPhase $context 'rollingBack' $state.StartedCount
  for($index=$state.StartedCount-1;$index -ge 0;$index--) {
   $item=$entries[$index];$target=Get-XMTarget $state.App $item.Relative
   if($item.Existed) {
    if([IO.File]::Exists($target) -and (Get-XMHash $target) -eq $item.OriginalHash){continue}
    $backup=Get-XMTarget (Join-Path $context.Stage 'program-backup') $item.Relative
    $temp=$target+'.recovery-'+$state.Id
    Assert-XMPath $temp
    try {
     [IO.File]::Copy($backup,$temp,$true)
     if((Get-XMHash $temp) -ne $item.OriginalHash){throw 'Recovery copy verification failed'}
     Sync-XMFile $temp
     if([IO.File]::Exists($target)){[IO.File]::Replace($temp,$target,[NullString]::Value)}else{[IO.File]::Move($temp,$target)}
    }finally{if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}
    if((Get-XMHash $target) -ne $item.OriginalHash){throw 'Program rollback verification failed'}
   }elseif([IO.File]::Exists($target)){[IO.File]::Delete($target)}
  }
  Set-XMPhase $context 'rolledBack' $state.StartedCount
 }
 Clear-XMMarker $context
 if(!$NoRestart){Start-Process -FilePath $entry -ArgumentList '-silent' -WorkingDirectory $state.App -WindowStyle Hidden}
 'rolledBack'
}
if(!$Library) {
 try {
  if(!$Marker){$Marker=Join-Path $script:XMRecoveryRoot '.xiaomang-update.json'}
  if($WaitForProcessId -gt 0 -and (Test-XMProcess $WaitForProcessId $WaitForStarted)) {
   $waiting=Get-Process -Id $WaitForProcessId
   if(!$waiting.WaitForExit(30000)){throw 'The application did not exit for recovery'}
  }
  if(!(Test-Path -LiteralPath $Marker)){throw 'No pending update recovery was found'}
  $result=Invoke-XMRecovery $Marker
  $message=if($result -eq 'complete'){'更新已完成，已清理遗留状态；小蟒已重新启动。'}else{'已校验并恢复原程序，个人数据保持不变；小蟒已重新启动。'}
  if(!$NoUI){Add-Type -AssemblyName System.Windows.Forms;[Windows.Forms.MessageBox]::Show($message,'小蟒更新恢复')|Out-Null}
 }catch {
  $message='自动恢复未执行或未完成。请保留更新目录及备份，不要重新覆盖文件。原因：'+$_.Exception.Message
  if($Marker){$message+="`n状态文件："+$Marker}
  if(!$NoUI){Add-Type -AssemblyName System.Windows.Forms;[Windows.Forms.MessageBox]::Show($message,'小蟒更新恢复')|Out-Null}
  Write-Error $message
  exit 1
 }
}
