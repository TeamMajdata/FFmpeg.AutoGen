param([string]$VcVars32)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'This helper requires Windows and MSVC x86 build tools.' }
if (-not $VcVars32) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio Installer vswhere.exe was not found. Pass -VcVars32 <path>.' }
    $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($LASTEXITCODE -ne 0 -or -not $installation) { throw 'MSVC x86 build tools were not found.' }
    $VcVars32 = Join-Path $installation 'VC/Auxiliary/Build/vcvars32.bat'
}
if (-not (Test-Path -LiteralPath $VcVars32)) { throw "vcvars32.bat not found: $VcVars32" }

$outputDirectory = Join-Path $PSScriptRoot 'bin/x86'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$source = Join-Path $PSScriptRoot 'smoke.c'
$abiSource = Join-Path $PSScriptRoot 'abi.c'
$headers = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../FFmpeg/include'))
$dll = Join-Path $outputDirectory 'avutil-61.dll'
$objectDirectory = $outputDirectory + '/'
$importLibrary = Join-Path $outputDirectory 'avutil-61.lib'
$commandFile = Join-Path $outputDirectory 'build.cmd'
foreach ($path in @($VcVars32, $source, $abiSource, $headers, $dll, $objectDirectory, $importLibrary, $commandFile)) {
    if ($path -match '[%"\r\n]') { throw 'Build paths cannot contain percent signs, quotes, or line breaks.' }
}
@"
@echo off
call "$VcVars32" >nul
if errorlevel 1 exit /b 1
cl /nologo /LD /O2 /W4 /WX /utf-8 /std:c11 /external:I"$headers" /external:W0 /Fo"$objectDirectory" /Fe"$dll" "$source" "$abiSource" /link /IMPLIB:"$importLibrary"
exit /b %errorlevel%
"@ | Set-Content -LiteralPath $commandFile -Encoding ascii
& cmd.exe /d /c $commandFile
if ($LASTEXITCODE -ne 0) { throw 'MSVC x86 native stub compilation failed.' }
Copy-Item -LiteralPath $dll -Destination (Join-Path $outputDirectory 'swscale-10.dll') -Force
Write-Host "Built Windows x86 native stubs in $outputDirectory"
