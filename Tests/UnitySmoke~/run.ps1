param(
    [string]$Compiler,
    [switch]$CompileOnly,
    [switch]$SkipMatrix,
    [switch]$WindowsX86,
    [string]$DotnetX86 = 'C:/Program Files (x86)/dotnet/dotnet.exe'
)

$ErrorActionPreference = 'Stop'
$hostProject = Join-Path $PSScriptRoot 'Host/UnitySmoke.csproj'
$config = Join-Path $PSScriptRoot 'NuGet.Config'
$hostDll = Join-Path $PSScriptRoot 'Host/bin/Release/net9.0/UnitySmoke.dll'
$runningOnWindows = $env:OS -eq 'Windows_NT'
$nativePlatform = if ($runningOnWindows) { 'Windows' }
    elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) { 'MacOS' }
    else { 'Linux' }

function Build-SmokeHost([string]$Platform) {
    & dotnet build $hostProject --no-restore --configuration Release --verbosity quiet "-p:UnityPlatform=$Platform"
    if ($LASTEXITCODE -ne 0) { throw "C# 9 / netstandard2.1 compilation failed for $Platform" }
}

& dotnet restore $hostProject --configfile $config --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Offline restore failed. Install a .NET 9 SDK and runtime before running this check.' }

if (-not $SkipMatrix) {
    foreach ($platform in @('Windows', 'WindowsX86', 'Linux', 'MacOS', 'Android', 'AndroidArmv7', 'IOS', 'EditorAndroid', 'EditorIOS')) {
        Build-SmokeHost $platform
        & dotnet $hostDll --metadata $platform
        if ($LASTEXITCODE -ne 0) { throw "Interop metadata validation failed for $platform" }
    }
}

if ($CompileOnly) {
    if ($SkipMatrix) {
        Build-SmokeHost $nativePlatform
        & dotnet $hostDll --metadata $nativePlatform
        if ($LASTEXITCODE -ne 0) { throw "Interop metadata validation failed for $nativePlatform" }
    }
    Write-Host 'Compile/metadata checks passed; native runtime checks skipped.'
    exit 0
}

if (-not $Compiler) {
    $candidate = Get-Command gcc -ErrorAction SilentlyContinue
    if ($candidate) { $Compiler = $candidate.Source }
    elseif ($runningOnWindows -and (Test-Path -LiteralPath 'C:/Strawberry/c/bin/gcc.exe')) {
        $Compiler = 'C:/Strawberry/c/bin/gcc.exe'
    }
    else {
        $candidate = Get-Command cc -ErrorAction SilentlyContinue
        if ($candidate) { $Compiler = $candidate.Source }
        else { throw 'A C compiler is required. Pass -Compiler <gcc-or-clang-path>, or use -CompileOnly.' }
    }
}

$nativeDirectory = Join-Path $PSScriptRoot 'native/bin'
New-Item -ItemType Directory -Path $nativeDirectory -Force | Out-Null
$source = Join-Path $PSScriptRoot 'native/smoke.c'
$abiSource = Join-Path $PSScriptRoot 'native/abi.c'
$headers = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../FFmpeg/include'))
$fileName = if ($runningOnWindows) { 'avutil-61.dll' }
    elseif ($nativePlatform -eq 'MacOS') { 'libffmpeg-unity-smoke.dylib' }
    else { 'libffmpeg-unity-smoke.so' }
$nativeLibrary = Join-Path $nativeDirectory $fileName
$compilerOptions = @('-O2', '-Wall', '-Wextra', '-Werror')
if ($nativePlatform -eq 'MacOS') { $compilerOptions += '-dynamiclib' }
else { $compilerOptions += '-shared' }
if (-not $runningOnWindows) { $compilerOptions += '-fPIC' }
& $Compiler @compilerOptions '-I' $headers $source $abiSource -o $nativeLibrary
if ($LASTEXITCODE -ne 0) { throw 'Native test library compilation failed.' }
if ($runningOnWindows) {
    Copy-Item -LiteralPath $nativeLibrary -Destination (Join-Path $nativeDirectory 'swscale-10.dll') -Force
}

Build-SmokeHost $nativePlatform
& dotnet $hostDll $nativeLibrary
if ($LASTEXITCODE -ne 0) { throw 'Native interop smoke test failed.' }

if ($WindowsX86) {
    if (-not $runningOnWindows) { throw '-WindowsX86 requires a Windows host.' }
    if (-not (Test-Path -LiteralPath $DotnetX86)) { throw 'Install the x86 .NET 9 runtime, or pass -DotnetX86 <path>.' }
    & (Join-Path $PSScriptRoot 'native/build-windows-x86.ps1')
    Build-SmokeHost 'WindowsX86'
    & $DotnetX86 $hostDll (Join-Path $nativeDirectory 'x86/avutil-61.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Windows x86 native interop smoke test failed.' }
}
