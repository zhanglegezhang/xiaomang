[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Payload,
    [Parameter(Mandatory=$true)][string]$Archive,
    [Parameter(Mandatory=$true)][string]$SevenZipPath
)
$ErrorActionPreference='Stop'
$payloadPath=(Resolve-Path -LiteralPath $Payload).Path
if(Get-ChildItem -LiteralPath $payloadPath -Recurse -Force | Where-Object { $_.Name -eq 'PrivateCredentials' -or $_.Name -like 'baidu-translation.dpapi*' }){throw 'Private translation credentials must never be distributed.'}
$archivePath=[IO.Path]::GetFullPath($Archive)
$tool=(Resolve-Path -LiteralPath $SevenZipPath).Path
if(!(Test-Path -LiteralPath $payloadPath -PathType Container)){throw 'Payload must be a prepared release directory.'}
if([IO.Path]::GetExtension($archivePath) -ne '.zip'){throw 'Release archive must use ZIP format.'}
if(Test-Path -LiteralPath $archivePath){throw 'Refusing to overwrite an existing release archive.'}
if($archivePath.StartsWith($payloadPath.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Archive must be outside the payload.'}
if(!(Test-Path -LiteralPath ([IO.Path]::GetDirectoryName($archivePath)) -PathType Container)){throw 'Archive parent directory is missing.'}
if((Get-Item -LiteralPath $tool).VersionInfo.FileVersion -ne '26.03'){throw 'Use the approved 7-Zip version 26.03.'}
if(@(Get-ChildItem -LiteralPath $payloadPath -Recurse -Force -Attributes ReparsePoint).Count){throw 'Release payload must not contain links or junctions.'}
$temporary=$archivePath+'.partial-'+[Guid]::NewGuid().ToString('N')+'.zip'
$clock=[Diagnostics.Stopwatch]::StartNew()
Push-Location ([IO.Path]::GetDirectoryName($payloadPath))
try {
    # Keep ZIP compatible with Windows extraction and the application updater.
    & $tool a -tzip -mm=Deflate -mx=9 -mfb=258 -mpass=15 -mmt=off -mcu=on -scsUTF-8 -bd -bb0 -- $temporary ([IO.Path]::GetFileName($payloadPath))
    if($LASTEXITCODE -ne 0){throw "7-Zip compression failed ($LASTEXITCODE); partial archive retained: $temporary"}
    & $tool t -bd -bb0 -- $temporary
    if($LASTEXITCODE -ne 0){throw "7-Zip integrity test failed; partial archive retained: $temporary"}
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip=[IO.Compression.ZipFile]::OpenRead($temporary)
    try {
        $names=@{}
        foreach($entry in $zip.Entries){if($entry.Name){$names[$entry.FullName]=$true}}
        $files=@(Get-ChildItem -LiteralPath $payloadPath -Recurse -Force -File)
        if($names.Count -ne $files.Count){throw 'ZIP file count mismatch.'}
        foreach($file in $files){
            $relative=$file.FullName.Substring($payloadPath.Length).TrimStart('\').Replace('\','/')
            $expected=[IO.Path]::GetFileName($payloadPath)+'/'+$relative
            if(!$names.ContainsKey($expected)){throw "ZIP filename encoding mismatch: $expected"}
        }
    } finally { $zip.Dispose() }
    Move-Item -LiteralPath $temporary -Destination $archivePath -ErrorAction Stop
} finally { Pop-Location }
$clock.Stop()
[pscustomobject]@{Archive=$archivePath;Bytes=(Get-Item -LiteralPath $archivePath).Length;SHA256=(Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash;SevenZipVersion='26.03';Parameters='-tzip -mm=Deflate -mx=9 -mfb=258 -mpass=15 -mmt=off -mcu=on -scsUTF-8';ElapsedSeconds=$clock.Elapsed.TotalSeconds}
