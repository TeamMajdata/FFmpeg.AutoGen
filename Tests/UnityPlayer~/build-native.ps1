param(
    [string] $UnityEditor = 'C:\Program Files\Unity Editors\6000.3.17f1\Editor\Unity.exe',
    [ValidateSet('x64', 'x86', 'armv7', 'arm64')]
    [string] $Architecture = 'x64'
)

$ErrorActionPreference = 'Stop'
$nativeSource = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../UnitySmoke~/native/smoke.c')).Path
$abiSource = Join-Path $PSScriptRoot '../UnitySmoke~/native/abi.c'
$ffmpegInclude = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../FFmpeg/include')).Path
$nativeRoot = Join-Path $PSScriptRoot 'Native'
$unityData = Join-Path (Split-Path -Parent $UnityEditor) 'Data'

function Build-WindowsStub([string] $Cpu) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $visualStudio) { throw 'MSVC x86/x64 build tools are required for the native smoke stub.' }
    $toolset = Get-ChildItem -LiteralPath (Join-Path $visualStudio 'VC/Tools/MSVC') -Directory |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    $kitRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
    $kit = Get-ChildItem -LiteralPath (Join-Path $kitRoot 'Lib') -Directory |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    $compilerDirectory = Join-Path $toolset.FullName "bin/Hostx64/$Cpu"
    $destination = Join-Path $nativeRoot "Windows-$Cpu"
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $library = Join-Path $destination 'avutil-61.dll'
    $arguments = @('/nologo', '/LD', '/MT', '/Gd', '/O2', '/W4', '/WX', '/utf-8',
        ('/I' + (Join-Path $toolset.FullName 'include')),
        ('/I' + (Join-Path $kitRoot "Include/$($kit.Name)/ucrt")),
        ('/I' + (Join-Path $kitRoot "Include/$($kit.Name)/shared")),
        ('/I' + (Join-Path $kitRoot "Include/$($kit.Name)/um")),
        ('/external:I' + $ffmpegInclude), '/external:W0', ('/Fo' + $destination + '\'), $nativeSource, $abiSource, '/link',
        ('/LIBPATH:' + (Join-Path $toolset.FullName "lib/$Cpu")),
        ('/LIBPATH:' + (Join-Path $kitRoot "Lib/$($kit.Name)/ucrt/$Cpu")),
        ('/LIBPATH:' + (Join-Path $kitRoot "Lib/$($kit.Name)/um/$Cpu")),
        ('/OUT:' + $library), ('/IMPLIB:' + (Join-Path $destination 'smoke.lib')))
    $savedPath = $env:PATH
    try {
        $env:PATH = $compilerDirectory + ';' + $savedPath
        & (Join-Path $compilerDirectory 'cl.exe') @arguments
        if ($LASTEXITCODE -ne 0) { throw "Native Windows $Cpu smoke compilation failed." }
    }
    finally { $env:PATH = $savedPath }
    Copy-Item -LiteralPath $library -Destination (Join-Path $destination 'swscale-10.dll') -Force
}

Build-WindowsStub 'x64'
if ($Architecture -eq 'x86') { Build-WindowsStub 'x86' }
if ($Architecture -in @('armv7', 'arm64')) {
    $destination = Join-Path $nativeRoot "Android-$Architecture"
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $ndk = Join-Path $unityData 'PlaybackEngines/AndroidPlayer/NDK'
    $clang = Join-Path $ndk 'toolchains/llvm/prebuilt/windows-x86_64/bin/clang.exe'
    $triple = if ($Architecture -eq 'armv7') { 'armv7a-linux-androideabi23' } else { 'aarch64-linux-android23' }
    & $clang "--target=$triple" -shared -fPIC -O2 -Wall -Wextra -Werror -isystem $ffmpegInclude $nativeSource $abiSource -o (Join-Path $destination 'libavutil.so') '-Wl,-soname,libavutil.so'
    if ($LASTEXITCODE -ne 0) { throw "Native Android $Architecture smoke compilation failed." }
    Copy-Item -LiteralPath (Join-Path $destination 'libavutil.so') -Destination (Join-Path $destination 'libswscale.so') -Force
}
