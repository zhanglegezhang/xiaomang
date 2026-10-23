param([Parameter(Mandatory=$true)][string]$Assembly,[Parameter(Mandatory=$true)][string]$Archive,[Parameter(Mandatory=$true)][string]$Payload,[Parameter(Mandatory=$true)][string]$Output,[string]$ReleaseJson)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $Output){throw 'Use a fresh extraction output'}
# Run in a separate PowerShell process for each released assembly. Loading an
# assembly here keeps its DLL locked until this process exits.
$loaded=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($Assembly))
$updater=$loaded.GetType('ShareX.ProductUpdater',$true)
$flags=[Reflection.BindingFlags]'Public,NonPublic,Static'
$parsed=$null
if($ReleaseJson){
 $parsed=$updater.GetMethod('ParseRelease',$flags).Invoke($null,@([IO.File]::ReadAllText($ReleaseJson)))
 if($parsed.Version.ToString() -ne '3.4' -or $parsed.Hash -ne (Get-FileHash $Archive).Hash.ToLowerInvariant()){throw 'Old updater rejected the new release metadata'}
}
$current=$Output+'-current'
$result=$updater.GetMethod('Extract',$flags).Invoke($null,@($Archive,$Output,[string](Join-Path $current 'personal'),$current,[Threading.CancellationToken]::None))
if($result -ne $Output){throw 'Old updater extraction returned an unexpected path'}
$files=@(Get-ChildItem -LiteralPath $Output -Recurse -File)
if($files.Count -ne @(Get-ChildItem -LiteralPath $Payload -Recurse -File).Count){throw 'Old updater extraction file count mismatch'}
foreach($file in $files){
 $relative=[IO.Path]::GetRelativePath($Output,$file.FullName)
 if((Get-FileHash $file.FullName).Hash -ne (Get-FileHash (Join-Path $Payload $relative)).Hash){throw ('Old updater extraction differs: '+$relative)}
}
@{Assembly=$Assembly;Files=$files.Count;AllHashesMatch=$true;ReleaseAccepted=($null -ne $parsed)}|ConvertTo-Json|Tee-Object -FilePath ($Output+'-result.json')
