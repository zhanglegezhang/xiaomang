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
        internal static async Task Start(string stage, string payload)
        {
            string app = Path.GetFullPath(AppContext.BaseDirectory);
            string executable = Path.Combine(app, "xiaomang.exe");
            if (!File.Exists(executable)) throw new IOException("当前不是标准使用包目录，不能自动更新。");
            string probe = Path.Combine(app, ".update-write-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, ""); File.Delete(probe);
            string config = Path.Combine(stage, "install.json"), script = Path.Combine(stage, "install.ps1");
            File.WriteAllText(config, JsonSerializer.Serialize(new { App = app, Stage = stage, Payload = payload, ProcessId = Environment.ProcessId, Started = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks }));
            File.WriteAllText(script, Script, new UTF8Encoding(true));
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = stage };
            foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Config", config }) info.ArgumentList.Add(arg);
            using var helper = Process.Start(info) ?? throw new IOException("更新助手无法启动。");
            for (int i = 0; i < 50; i++)
            {
                if (File.Exists(Path.Combine(stage, "ready"))) return;
                if (helper.HasExited) throw new IOException("更新助手准备失败，请查看更新目录中的 result.txt。");
                await Task.Delay(100);
            }
            File.WriteAllText(Path.Combine(stage, "cancel"), "cancel");
            throw new IOException("更新助手准备超时，原程序保留。");
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
 foreach($file in $items) {
  Safe $file.FullName
  $rel=$file.FullName.Substring($payloadRoot.Length)
  $target=[IO.Path]::GetFullPath((Join-Path $c.App $rel))
  if(!$target.StartsWith($appRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Outside application directory'}
  Safe $target
  $journal.Add([pscustomobject]@{Relative=$rel;Existed=(Test-Path -LiteralPath $target -PathType Leaf);Hash=(Hash $file.FullName)})
 }
 $journal|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $c.Stage 'journal.json') -Encoding UTF8
 Set-Content -LiteralPath (Join-Path $c.Stage 'ready') -Value 'ready'
 $old=Get-Process -Id $c.ProcessId -ErrorAction SilentlyContinue
 if($old -and $old.StartTime.ToUniversalTime().Ticks -eq $c.Started) {
  if(!$old.WaitForExit(90000)){throw 'Application did not exit; no files replaced'}
 }
 if(Test-Path -LiteralPath (Join-Path $c.Stage 'cancel')){throw 'Update cancelled'}
 foreach($running in Get-Process xiaomang -ErrorAction SilentlyContinue){if($running.Path -eq $entry){throw 'Application was reopened; no files replaced'}}
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
 $new=Start-Process -FilePath $entry -ArgumentList '-silent' -WorkingDirectory $c.App -WindowStyle Hidden -PassThru
 if($new.WaitForExit(10000)){throw 'Updated application exited during startup'}
 Set-Content -LiteralPath $result -Value '更新成功。个人数据备份和旧程序保留在此目录。' -Encoding UTF8
} catch {
 $reason=$_.Exception.Message
 try {
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
 } catch {Set-Content -LiteralPath $result -Value ('恢复未完成，请保留 program-backup 和 personal-backup.zip：'+$_.Exception.Message+'；原错误：'+$reason) -Encoding UTF8}
 if(!$NoUI){
  Add-Type -AssemblyName System.Windows.Forms
  [Windows.Forms.MessageBox]::Show((Get-Content -LiteralPath $result -Raw -Encoding UTF8), '小蟒更新结果')|Out-Null
 }
}
""";
    }
}
