param([Parameter(Mandatory=$true)][string]$Published,[Parameter(Mandatory=$true)][string]$BadRelease,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
$gate=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../scripts/Test-SelfContainedRuntime.ps1'))
$checks=0
function Assert($ok,$message){if(!$ok){throw $message};$script:checks++;Write-Output "PASS $message"}
$bad=$false;try{& $gate -Directory $BadRelease | Out-Null}catch{$bad=$_.Exception.Message -match 'system .NET'}
Assert $bad 'Released 3.3 configuration is rejected, despite bundled runtime DLLs'
& $gate -Directory $Published -Entry 'ShareX.exe' | Out-Null
Assert $? 'Fresh self-contained publish accepted'
$fixture=Join-Path $Output 'gate-negative';New-Item -ItemType Directory -Path $fixture -Force | Out-Null
foreach($name in @('ShareX.exe','ShareX.dll','ShareX.deps.json','ShareX.runtimeconfig.json','hostfxr.dll','hostpolicy.dll','coreclr.dll','System.Private.CoreLib.dll','System.Windows.Forms.dll','PresentationFramework.dll')){Copy-Item -LiteralPath (Join-Path $Published $name) -Destination $fixture}
Move-Item -LiteralPath (Join-Path $fixture 'coreclr.dll') -Destination (Join-Path $fixture 'coreclr.saved')
$rejected=$false;try{& $gate -Directory $fixture -Entry ShareX.exe | Out-Null}catch{$rejected=$_.Exception.Message -match 'coreclr.dll'}
Assert $rejected 'Missing runtime core rejected'
Move-Item -LiteralPath (Join-Path $fixture 'coreclr.saved') -Destination (Join-Path $fixture 'coreclr.dll')
$original=[IO.File]::ReadAllText((Join-Path $fixture 'ShareX.runtimeconfig.json'))
$json=$original | ConvertFrom-Json;$json.runtimeOptions.includedFrameworks[1].version='9.0.20'
$json | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $fixture 'ShareX.runtimeconfig.json') -Encoding utf8
$rejected=$false;try{& $gate -Directory $fixture -Entry ShareX.exe | Out-Null}catch{$rejected=$_.Exception.Message -match 'framework'}
Assert $rejected 'Mixed framework versions rejected'
[IO.File]::WriteAllText((Join-Path $fixture 'ShareX.runtimeconfig.json'),$original)
$stream=[IO.File]::OpenWrite((Join-Path $fixture 'ShareX.exe'));try{$stream.Position=0x3C;$offsetBytes=New-Object byte[] 4;$read=[IO.File]::ReadAllBytes((Join-Path $Published 'ShareX.exe'));$offset=[BitConverter]::ToInt32($read,0x3C);$stream.Position=$offset+4;$stream.WriteByte(0x4C);$stream.WriteByte(0x01)}finally{$stream.Dispose()}
$rejected=$false;try{& $gate -Directory $fixture -Entry ShareX.exe | Out-Null}catch{$rejected=$_.Exception.Message -match 'x64'}
Assert $rejected 'Wrong architecture entry rejected'
@{Passed=$true;Checks=$checks}|ConvertTo-Json|Set-Content (Join-Path $Output 'gate-results.json') -Encoding utf8
