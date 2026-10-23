[CmdletBinding()]
param([switch]$PrepareOnly, [uri]$Proxy)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$version = 'v3.4-20261023'
$expected = '10C2F752AA53E9A6EAB959E06B2EF319EA08D341F87332F179B20284BCF26944'
$url = "https://github.com/zhanglegezhang/xiaomang/releases/download/$version/xiaomang3.4_2026-10-23_usage.zip"
$base = Join-Path $env:LOCALAPPDATA 'XiaomangSourceLauncher'
$destination = Join-Path $base $version
$receipt = Join-Path $destination '.verified-download'
$lock = $null
$temporary = $null
try {
    New-Item -ItemType Directory -Force -Path $base | Out-Null
    $lock = [IO.File]::Open((Join-Path $base 'download.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    if (!(Test-Path -LiteralPath $receipt)) {
        if (Test-Path -LiteralPath $destination) { throw 'An incomplete installation exists. Preserve its data and rename it before retrying.' }
        $temporary = Join-Path $base ('download-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $temporary | Out-Null
        $archive = Join-Path $temporary 'usage.zip'
        Write-Host 'Downloading the matching Xiaomang release (first run only)...'
        $request = @{ UseBasicParsing = $true; Uri = $url; OutFile = $archive; TimeoutSec = 180 }
        if ($Proxy) { $request.Proxy = $Proxy }
        Invoke-WebRequest @request
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'SHA256 verification failed. Nothing will be executed.' }
        $unpacked = Join-Path $temporary 'program'
        Expand-Archive -LiteralPath $archive -DestinationPath $unpacked
        $entries = @(Get-ChildItem -LiteralPath $unpacked -Filter xiaomang.exe -File -Recurse)
        if ($entries.Count -ne 1) { throw 'Expected exactly one Xiaomang startup executable.' }
        Set-Content -LiteralPath (Join-Path $unpacked '.verified-download') -Value $expected -Encoding ASCII
        Move-Item -LiteralPath $unpacked -Destination $destination
    }
    if ((Get-Content -LiteralPath $receipt -Raw).Trim() -ne $expected) { throw 'The installed release receipt does not match this source version.' }
    $entries = @(Get-ChildItem -LiteralPath $destination -Filter xiaomang.exe -File -Recurse)
    if ($entries.Count -ne 1) { throw 'The startup executable is missing or ambiguous.' }
    Write-Host ('Ready: ' + $entries[0].FullName)
    if (!$PrepareOnly) { Start-Process -FilePath $entries[0].FullName -WorkingDirectory $entries[0].DirectoryName }
} finally {
    if ($temporary -and (Test-Path -LiteralPath $temporary)) {
        $resolved = [IO.Path]::GetFullPath($temporary)
        if ($resolved.StartsWith([IO.Path]::GetFullPath($base) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
    if ($lock) { $lock.Dispose() }
}

