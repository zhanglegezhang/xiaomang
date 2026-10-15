param([string]$Toolchain = "$PSScriptRoot\..\..\..\..\native-toolchain\llvm-mingw-20261006-ucrt-x86_64")
$ErrorActionPreference = 'Stop'
foreach ($architecture in @('x86_64', 'i686')) {
    $directory = Join-Path $PSScriptRoot "bin\$architecture"
    New-Item -ItemType Directory -Force $directory | Out-Null
    $compiler = Join-Path $Toolchain "bin\$architecture-w64-mingw32-clang++.exe"
    & $compiler -std=c++17 -O2 -Wall -Wextra -shared -static '-Wl,--kill-at' "$PSScriptRoot\VoiceInput.cpp" "$PSScriptRoot\Exports.def" -o "$directory\XiaomangVoiceInput.dll" -lole32 -loleaut32 -luuid -ladvapi32 -luser32
    if ($LASTEXITCODE) { throw "Native DLL build failed: $architecture" }
    & $compiler -O2 -static -municode "$PSScriptRoot\Register.cpp" -o "$directory\RegisterVoiceInput.exe" -lole32
    if ($LASTEXITCODE) { throw "Native registration helper build failed: $architecture" }
}
Push-Location $PSScriptRoot
try {
    & "$Toolchain\bin\llvm-rc.exe" /fo bin/setup.res setup.rc
    if ($LASTEXITCODE) { throw 'Setup resource compilation failed' }
    & "$Toolchain\bin\x86_64-w64-mingw32-clang++.exe" -std=c++17 -O2 -Wall -Wextra -static -municode Setup.cpp bin/setup.res -o bin/VoiceInputSetup.exe -lole32 -lshell32 -luuid -ladvapi32
    if ($LASTEXITCODE) { throw 'Setup compilation failed' }
} finally { Pop-Location }
New-Item -ItemType Directory -Force "$PSScriptRoot\bin\Licenses" | Out-Null
Copy-Item -Path "$PSScriptRoot\Licenses\*.txt" -Destination "$PSScriptRoot\bin\Licenses"
