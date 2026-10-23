[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Directory, [string]$RuntimeVersion='9.0.21', [string]$Entry='xiaomang.exe')
$ErrorActionPreference='Stop'
$runtimeRoot=[IO.Path]::GetFullPath($Directory)
$config=Get-Content -LiteralPath (Join-Path $runtimeRoot 'ShareX.runtimeconfig.json') -Raw | ConvertFrom-Json
$options=$config.runtimeOptions
if($options.framework -or @($options.frameworks).Where({$null -ne $_}).Count -gt 0){throw 'Release requires a system .NET runtime. Publish with --self-contained true; do not edit runtimeconfig by hand.'}
$frameworks=@($options.includedFrameworks)
foreach($name in @('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App')){
    $selected=@($frameworks | Where-Object {$_.name -eq $name})
    if($selected.Count -ne 1 -or $selected[0].version -ne $RuntimeVersion){throw "Missing or unexpected bundled framework: $name ($RuntimeVersion required)"}
}
foreach($name in @($Entry,'ShareX.dll','ShareX.deps.json','hostfxr.dll','hostpolicy.dll','coreclr.dll','System.Private.CoreLib.dll','System.Windows.Forms.dll','PresentationFramework.dll')){
    if(!(Test-Path -LiteralPath (Join-Path $runtimeRoot $name) -PathType Leaf)){throw "Missing self-contained runtime file: $name"}
}
foreach($name in @($Entry,'hostfxr.dll','hostpolicy.dll','coreclr.dll')){
    $file=Join-Path $runtimeRoot $name
    $reader=[IO.BinaryReader]::new([IO.File]::OpenRead($file))
    try{
        if($reader.ReadUInt16() -ne 0x5A4D){throw "Invalid PE image: $name"}
        $reader.BaseStream.Position=0x3C;$offset=$reader.ReadInt32();$reader.BaseStream.Position=$offset
        if($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664){throw "Release entry/runtime must be Windows x64: $name"}
    }finally{$reader.Dispose()}
}
foreach($name in @('hostfxr.dll','hostpolicy.dll')){
    $version=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $runtimeRoot $name)).ProductVersion
    if($version -notmatch ('^'+[regex]::Escape($RuntimeVersion)+'(?:\s|$)')){throw "Bundled runtime version differs: $name = $version"}
}
$deps=Get-Content -LiteralPath (Join-Path $runtimeRoot 'ShareX.deps.json') -Raw | ConvertFrom-Json
if($deps.runtimeTarget.name -notmatch '/win-x64$'){throw 'deps runtime target is not Windows x64'}
[pscustomobject]@{SelfContained=$true;RuntimeVersion=$RuntimeVersion;Architecture='win-x64';Directory=$runtimeRoot}
