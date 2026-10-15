param([string]$Toolchain = "$PSScriptRoot\..\..\..\..\native-toolchain\llvm-mingw-20261006-ucrt-x86_64")
$ErrorActionPreference = 'Stop'
foreach ($architecture in @('x86_64', 'i686')) {
    $directory = Join-Path $PSScriptRoot "bin\$architecture"
    New-Item -ItemType Directory -Force $directory | Out-Null
    $compiler = Join-Path $Toolchain "bin\$architecture-w64-mingw32-clang++.exe"
    & $compiler -O2 -Wall -Wextra -static -municode "$PSScriptRoot\Register.cpp" -o "$directory\RegisterInputMethod.exe" -lole32
    if ($LASTEXITCODE) { throw "Registration helper build failed: $architecture" }
}
Push-Location $PSScriptRoot
try {
    & "$Toolchain\bin\llvm-rc.exe" /fo bin/setup.res setup.rc
    if ($LASTEXITCODE) { throw 'Setup resource build failed' }
    & "$Toolchain\bin\x86_64-w64-mingw32-clang++.exe" -std=c++17 -O2 -Wall -Wextra -static -municode Setup.cpp bin/setup.res -o bin/InputMethodSetup.exe -lole32 -lshell32 -luuid -ladvapi32
    if ($LASTEXITCODE) { throw 'Setup build failed' }
} finally { Pop-Location }
