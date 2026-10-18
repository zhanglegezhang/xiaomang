using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ShareX
{
    internal static class ProductUpdateInstaller
    {
        // The helper receives paths as JSON data, never interpolated PowerShell commands.
        internal static async Task Start(string stage, string payload, IProgress<string> progress = null)
        {
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                string preparation = Path.Combine(stage, "attempt-" + attempt + "-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(preparation);
                try { await StartAttempt(preparation, payload, progress); return; }
                catch (TimeoutException) when (attempt == 1)
                {
                    progress?.Report("准备较慢，正在自动重试；无需重新下载。");
                }
            }
        }

        private static async Task StartAttempt(string stage, string payload, IProgress<string> progress)
        {
            string app = Path.GetFullPath(AppContext.BaseDirectory);
            string executable = Path.Combine(app, "xiaomang.exe");
            if (!File.Exists(executable)) throw new IOException("当前不是标准使用包目录，不能自动更新。");
            string probe = Path.Combine(app, ".update-write-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, ""); File.Delete(probe);
            string config = Path.Combine(stage, "install.json"), script = Path.Combine(stage, "install.ps1");
            File.WriteAllText(config, JsonSerializer.Serialize(new { App = app, Stage = stage, Payload = payload,
                Snapshot = Path.Combine(Path.GetDirectoryName(stage), "data-snapshot.json"),
                ProcessId = Environment.ProcessId, Started = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks }));
            File.WriteAllText(script, Script, new UTF8Encoding(true));
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = stage };
            foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Config", config }) info.ArgumentList.Add(arg);
            using var helper = Process.Start(info) ?? throw new IOException("更新助手无法启动。");
            try { await WaitForPreparation(stage, helper, progress); }
            catch
            {
                File.WriteAllText(Path.Combine(stage, "cancel"), "cancel");
                // The caller is still alive: this helper cannot have started replacement.
                // Never start a second helper until the first is confirmed stopped.
                if (!helper.HasExited)
                {
                    var stop = Stopwatch.StartNew();
                    while (!helper.HasExited && stop.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(100);
                    if (!helper.HasExited) { helper.Kill(); await helper.WaitForExitAsync(); }
                }
                throw;
            }
        }

        internal static async Task WaitUntilReady(string stage, Process helper)
            => await WaitForPreparation(stage, helper, null);

        private static async Task WaitForPreparation(string stage, Process helper, IProgress<string> progress)
        {
            var elapsed = Stopwatch.StartNew();
            var idle = Stopwatch.StartNew();
            string previous = null;
            progress?.Report("正在启动更新助手并检查程序文件…");
            while (elapsed.Elapsed < TimeSpan.FromMinutes(10) && idle.Elapsed < TimeSpan.FromSeconds(60))
            {
                if (helper.HasExited) throw new IOException("更新助手准备失败，请查看更新目录中的 result.txt。");
                if (File.Exists(Path.Combine(stage, "ready"))) return;
                try
                {
                    string path = Path.Combine(stage, "progress.txt");
                    if (File.Exists(path))
                    {
                        string value = File.ReadAllText(path).Trim();
                        if (value.Length > 0 && value != previous)
                        {
                            previous = value; idle.Restart();
                            progress?.Report("正在检查程序文件：" + value);
                        }
                    }
                }
                catch (IOException) { /* The helper may be replacing the progress file. */ }
                await Task.Delay(200);
            }
            if (!helper.HasExited && File.Exists(Path.Combine(stage, "ready"))) return;
            File.WriteAllText(Path.Combine(stage, "cancel"), "cancel");
            throw new TimeoutException("更新助手长时间没有进展，原程序保留。");
        }

        internal const string Script = """
param([Parameter(Mandatory=$true)][string]$Config,[switch]$NoUI)
$ErrorActionPreference='Stop'
$c=Get-Content -LiteralPath $Config -Raw -Encoding UTF8|ConvertFrom-Json
$journal=[Collections.Generic.List[object]]::new()
$applied=[Collections.Generic.List[object]]::new()
$backup=Join-Path $c.Stage 'program-backup'
$result=Join-Path $c.Stage 'result.txt'
$entry=Join-Path $c.App 'xiaomang.exe'
$changed=$false
$new=$null
function CheckCancelled {
 if(Test-Path -LiteralPath (Join-Path $c.Stage 'cancel')){throw 'Update cancelled'}
}
function Hash([string]$path) {
 $stream=[IO.File]::OpenRead($path);$sha=[Security.Cryptography.SHA256]::Create()
 try{return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$sha.Dispose();$stream.Dispose()}
}
function Safe([string]$path) {
 for($p=[IO.Path]::GetFullPath($path);$p;$p=[IO.Path]::GetDirectoryName($p)) {
  if((Test-Path -LiteralPath $p) -and ((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Path contains a link'}
 }
}
try {
 Safe $c.App; Safe $c.Payload; Safe $c.Stage
 $appRoot=[IO.Path]::GetFullPath($c.App).TrimEnd('\')+'\'
 $payloadRoot=[IO.Path]::GetFullPath($c.Payload).TrimEnd('\')+'\'
 $items=@(Get-ChildItem -LiteralPath $c.Payload -Recurse -File)
 if(!$items.Count){throw 'Empty payload'}
 $completed=0
 foreach($file in $items) {
  CheckCancelled
  Safe $file.FullName
  $rel=$file.FullName.Substring($payloadRoot.Length)
  $target=[IO.Path]::GetFullPath((Join-Path $c.App $rel))
  if(!$target.StartsWith($appRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Outside application directory'}
  Safe $target
  $journal.Add([pscustomobject]@{Relative=$rel;Existed=(Test-Path -LiteralPath $target -PathType Leaf);Hash=(Hash $file.FullName)})
  $completed++
  if($completed -eq 1 -or $completed % 10 -eq 0 -or $completed -eq $items.Count){
   Set-Content -LiteralPath (Join-Path $c.Stage 'progress.txt') -Value "$completed / $($items.Count)" -Encoding UTF8
  }
 }
 $journal|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $c.Stage 'journal.json') -Encoding UTF8
 CheckCancelled
 Set-Content -LiteralPath (Join-Path $c.Stage 'ready') -Value 'ready'
 $old=Get-Process -Id $c.ProcessId -ErrorAction SilentlyContinue
 if($old -and $old.StartTime.ToUniversalTime().Ticks -eq $c.Started) {
  $waiting=[Diagnostics.Stopwatch]::StartNew()
  while(!$old.WaitForExit(100)) {
   CheckCancelled
   if($waiting.ElapsedMilliseconds -ge 90000){throw 'Application did not exit; no files replaced'}
  }
 }
 if(Test-Path -LiteralPath (Join-Path $c.Stage 'cancel')){throw 'Update cancelled'}
 foreach($running in Get-Process xiaomang -ErrorAction SilentlyContinue){if($running.Path -eq $entry){throw 'Application was reopened; no files replaced'}}
 if(!$c.Snapshot -or !(Test-Path -LiteralPath $c.Snapshot)){throw 'Missing data path manifest'}
 $snapshot=Get-Content -LiteralPath $c.Snapshot -Raw -Encoding UTF8|ConvertFrom-Json
 # Save the final on-disk state AFTER normal application shutdown, including last exit saves.
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 Add-Type -AssemblyName System.IO.Compression
 $finalBackup=Join-Path $c.Stage 'final-data-backup.zip'
 $dataFiles=[Collections.Generic.List[object]]::new()
 $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 $roots=@($snapshot.Personal,$snapshot.Input,$snapshot.Media)|Where-Object {$_}|Select-Object -Unique
 $zip=[IO.Compression.ZipFile]::Open($finalBackup,[IO.Compression.ZipArchiveMode]::Create)
 try {
  foreach($root in $roots){
   Safe $root
   if(!(Test-Path -LiteralPath $root)){continue}
   foreach($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force){
    Safe $file.FullName
    $rel=$file.FullName.Substring($root.TrimEnd('\').Length).TrimStart('\')
    if($rel -match '^Logs\\' -or $file.Name -eq '.personal-transfer.lock' -or !$seen.Add($file.FullName)){continue}
    $hash=Hash $file.FullName;$name=[string]$dataFiles.Count
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,$name,[IO.Compression.CompressionLevel]::Fastest)|Out-Null
    if((Hash $file.FullName) -ne $hash){throw 'Data changed while backing up'}
    $dataFiles.Add(@{Source=$file.FullName;Archive=$name;Hash=$hash})
   }
  }
  if($snapshot.Hotkeys -and (Test-Path -LiteralPath $snapshot.Hotkeys) -and $seen.Add($snapshot.Hotkeys)){
   Safe $snapshot.Hotkeys;$hash=Hash $snapshot.Hotkeys;$name=[string]$dataFiles.Count
   [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$snapshot.Hotkeys,$name,[IO.Compression.CompressionLevel]::Fastest)|Out-Null
   $dataFiles.Add(@{Source=$snapshot.Hotkeys;Archive=$name;Hash=$hash})
  }
 }finally{$zip.Dispose()}
 $zip=[IO.Compression.ZipFile]::OpenRead($finalBackup)
 try {foreach($file in $dataFiles){$stream=$zip.GetEntry($file.Archive).Open();$sha=[Security.Cryptography.SHA256]::Create();try{$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()};if($hash -ne $file.Hash){throw 'Final data backup verification failed'}}}finally{$zip.Dispose()}
 $snapshot.Files=$dataFiles.ToArray();$snapshot.Backup=$finalBackup
 $snapshot|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $c.Snapshot -Encoding UTF8
 $localManifest=Join-Path $c.Stage 'data-snapshot.json'
 if([IO.Path]::GetFullPath($localManifest) -ne [IO.Path]::GetFullPath($c.Snapshot)){Copy-Item -LiteralPath $c.Snapshot -Destination $localManifest}
 foreach($root in $roots){
  if(!(Test-Path -LiteralPath $root)){continue}
  foreach($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force){
   $rel=$file.FullName.Substring($root.TrimEnd('\').Length).TrimStart('\')
   if($rel -match '^Logs\\' -or $file.Name -eq '.personal-transfer.lock'){continue}
   if(!$seen.Contains($file.FullName)){throw 'New data appeared during backup; update cancelled'}
  }
 }
 foreach($file in $dataFiles){if((Hash $file.Source) -ne $file.Hash){throw 'Data changed after backup; update cancelled'}}
 New-Item -ItemType Directory -Path $backup -Force|Out-Null
 foreach($item in $journal) {
  $target=Join-Path $c.App $item.Relative; Safe $target
  if($item.Existed){$saved=Join-Path $backup $item.Relative;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved))|Out-Null;Copy-Item -LiteralPath $target -Destination $saved}
 }
 $changed=$true
 foreach($item in $journal) {
  $source=Join-Path $c.Payload $item.Relative; $target=Join-Path $c.App $item.Relative; Safe $target
  if((Hash $source) -ne $item.Hash){throw 'Payload changed'}
  [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))|Out-Null
  $temporary=$target+'.update-'+[Guid]::NewGuid().ToString('N')
  try {
   [IO.File]::Copy($source,$temporary)
   if($item.Existed){[IO.File]::Replace($temporary,$target,$temporary+'.old')}else{[IO.File]::Move($temporary,$target)}
   $applied.Add($item)
   $applied|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $c.Stage 'applied.json') -Encoding UTF8
  } finally {if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}; if(Test-Path -LiteralPath ($temporary+'.old')){Remove-Item -LiteralPath ($temporary+'.old')}}
  if((Hash $target) -ne $item.Hash){throw 'Installed file mismatch'}
 }
 if(!$c.Snapshot -or !(Test-Path -LiteralPath $c.Snapshot)){throw 'Missing verified data snapshot'}
 $snapshot=Get-Content -LiteralPath $c.Snapshot -Raw -Encoding UTF8|ConvertFrom-Json
 foreach($file in $snapshot.Files){if(!(Test-Path -LiteralPath $file.Source) -or (Hash $file.Source) -ne $file.Hash){throw 'Personal data changed during upgrade'}}
 $receipt=$c.Snapshot+'.result'
 if(Test-Path -LiteralPath $receipt){Remove-Item -LiteralPath $receipt}
 $env:XIAOMANG_UPGRADE_CHECK=$c.Snapshot
 try {$new=Start-Process -FilePath $entry -WorkingDirectory $c.App -PassThru}finally{Remove-Item Env:XIAOMANG_UPGRADE_CHECK -ErrorAction SilentlyContinue}
 $timer=[Diagnostics.Stopwatch]::StartNew()
 while(!(Test-Path -LiteralPath $receipt)){
  if($new.WaitForExit(200)){throw 'Updated application exited before loading data'}
  if($timer.Elapsed.TotalSeconds -gt 90){throw 'Updated application did not confirm data loading'}
 }
 $loaded=Get-Content -LiteralPath $receipt -Raw -Encoding UTF8|ConvertFrom-Json
 if(!$loaded.Success -or $loaded.ProcessId -ne $new.Id -or $loaded.Personal -ne $snapshot.Personal){throw ('Updated application failed data loading validation: '+$loaded.Error)}
 Set-Content -LiteralPath $result -Value '更新成功。个人数据备份和旧程序保留在此目录。' -Encoding UTF8
 if(!$NoUI){Add-Type -AssemblyName System.Windows.Forms;[Windows.Forms.MessageBox]::Show(('升级完成，原数据已加载。备份与日志：'+$c.Stage),'小蟒升级完成')|Out-Null}
} catch {
 $reason=$_.Exception.Message
 try {
  if($new -and !$new.HasExited){$new.Kill();$new.WaitForExit()}
  if($new -and $finalBackup -and (Test-Path -LiteralPath $finalBackup)){
   $zip=[IO.Compression.ZipFile]::OpenRead($finalBackup)
   try {foreach($file in $dataFiles){Safe $file.Source;[IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry($file.Archive),$file.Source,$true);if((Hash $file.Source) -ne $file.Hash){throw 'Data rollback verification failed'}}}finally{$zip.Dispose()}
  }
  if($changed){
   $restoreErrors=[Collections.Generic.List[string]]::new()
   for($i=$applied.Count-1;$i -ge 0;$i--){
    $item=$applied[$i]
    try {
    $target=Join-Path $c.App $item.Relative;Safe $target
    if($item.Existed){Copy-Item -LiteralPath (Join-Path $backup $item.Relative) -Destination $target -Force}
    elseif(Test-Path -LiteralPath $target -PathType Leaf){Remove-Item -LiteralPath $target}
    } catch {$restoreErrors.Add($_.Exception.Message)}
   }
   if($restoreErrors.Count){throw ($restoreErrors -join '; ')}
  }
  $stillRunning=@(Get-Process xiaomang -ErrorAction SilentlyContinue|Where-Object Path -eq $entry)
  if(!$stillRunning.Count){Start-Process -FilePath $entry -ArgumentList '-silent' -WorkingDirectory $c.App -WindowStyle Hidden}
  Set-Content -LiteralPath $result -Value ('更新未完成，已保留或恢复旧程序：'+$reason) -Encoding UTF8
 } catch {Set-Content -LiteralPath $result -Value ('恢复未完成，请保留 program-backup、final-data-backup.zip 和 data-snapshot.json：'+$_.Exception.Message+'；原错误：'+$reason) -Encoding UTF8}
 if(!$NoUI){
  Add-Type -AssemblyName System.Windows.Forms
  [Windows.Forms.MessageBox]::Show((Get-Content -LiteralPath $result -Raw -Encoding UTF8), '小蟒更新结果')|Out-Null
 }
}
""";
    }
}
