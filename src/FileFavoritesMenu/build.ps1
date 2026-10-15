param(
 [string]$Output=(Join-Path $PSScriptRoot 'bundle'),
 [Parameter(Mandatory=$true)][string]$Toolchain,
 [Parameter(Mandatory=$true)][string]$WindowsSdkBin
)
$ErrorActionPreference='Stop'
$workspace=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$compiler=Join-Path $Toolchain 'bin/x86_64-w64-mingw32-clang++.exe'
$pack=Join-Path $WindowsSdkBin 'makeappx.exe'
$pri=Join-Path $WindowsSdkBin 'makepri.exe'
$Output=[IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path $Output -Force|Out-Null
& $compiler -std=c++17 -O2 -Werror -shared -static-libgcc -static-libstdc++ -o (Join-Path $Output 'XiaomangFileFavoritesMenu.dll') (Join-Path $PSScriptRoot 'source/Menu.cpp') (Join-Path $PSScriptRoot 'source/Menu.def') -lole32 -luuid
if($LASTEXITCODE -ne 0){throw 'Native menu compilation failed'}
$package=Join-Path $Output 'identity-footprint'
New-Item -ItemType Directory -Path $package -Force|Out-Null
New-Item -ItemType Directory -Path (Join-Path $Output 'Assets') -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Destination $package
Add-Type -AssemblyName System.Drawing
$image=[Drawing.Image]::FromFile((Join-Path $workspace 'resources/Branding/xiaomang-robot-logo.png'))
function Save-Icon([string]$Name,[int]$Size) {
 $bitmap=[Drawing.Bitmap]::new($Size,$Size)
 $graphics=[Drawing.Graphics]::FromImage($bitmap)
 try {
  $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $graphics.DrawImage($image,0,0,$Size,$Size)
  $bitmap.Save((Join-Path $Output ('Assets/'+$Name)),[Drawing.Imaging.ImageFormat]::Png)
 } finally {$graphics.Dispose();$bitmap.Dispose()}
}
try {
 Save-Icon 'Logo.png' 150
 foreach($scale in @(100,200,400)) {
  Save-Icon "Square44x44Logo.scale-$scale.png" (44*$scale/100)
  Save-Icon "Square150x150Logo.scale-$scale.png" (150*$scale/100)
 }
 foreach($size in @(16,24,32,48,64,96,256)) {
  Save-Icon "Square44x44Logo.targetsize-$size.png" $size
  Save-Icon "Square44x44Logo.targetsize-${size}_altform-unplated.png" $size
 }
} finally {$image.Dispose()}
# Sparse-package resources are resolved from ExternalLocation, not WindowsApps.
# Build the index against the exact relative paths used in the deployed program.
$external=Join-Path $Output 'external-resources'
New-Item -ItemType Directory -Path (Join-Path $external 'ThirdParty/FileFavoritesMenu/Assets') -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Destination $external
Get-ChildItem -LiteralPath (Join-Path $Output 'Assets') -Filter '*.png' -File | Copy-Item -Destination (Join-Path $external 'ThirdParty/FileFavoritesMenu/Assets')
$config=Join-Path $Output 'priconfig.xml'
& $pri createconfig /cf $config /dq zh-CN /pv 10.0.0 /o
if($LASTEXITCODE -ne 0){throw 'Icon resource configuration failed'}
# Keep all scale variants in one PRI; there are no separately installed resource packages.
$configXml=[xml](Get-Content -LiteralPath $config -Raw)
$configXml.resources.RemoveChild($configXml.resources.packaging)|Out-Null
$configXml.Save($config)
& $pri new /pr $external /cf $config /mn (Join-Path $external 'AppxManifest.xml') /of (Join-Path $Output 'resources.pri') /o
if($LASTEXITCODE -ne 0){throw 'Icon resource index creation failed'}
# The identity package only holds footprint files. Content ships at ExternalLocation.
& $pack pack /o /d $package /nv /p (Join-Path $Output 'Xiaomang.FileFavoritesMenu.msix')
if($LASTEXITCODE -ne 0){throw 'Sparse package creation failed'}
