param(
    [string] $UnityEditor = 'C:\Program Files\Unity Editors\6000.3.17f1\Editor\Unity.exe',
    [ValidateSet('Editor', 'Mono', 'IL2CPP')]
    [string] $Backend = 'IL2CPP',
    [ValidateSet('Windows', 'Android')]
    [string] $Platform = 'Windows',
    [ValidateSet('x64', 'x86', 'armv7', 'arm64')]
    [string] $Architecture = 'x64',
    [ValidateSet('Debug', 'Release', 'Master')]
    [string] $CompilerConfiguration = 'Release',
    [switch] $SkipNativeBuild,
    [string] $NativeDirectory = ''
)

$ErrorActionPreference = 'Stop'
if (($Platform -eq 'Windows' -and $Architecture -notin @('x64', 'x86')) -or
    ($Platform -eq 'Android' -and $Architecture -notin @('armv7', 'arm64'))) {
    throw "Invalid target: $Platform $Architecture"
}
if ($Platform -eq 'Android' -and $Architecture -eq 'arm64' -and $Backend -eq 'Mono') {
    throw 'Unity supports Android Mono on ARMv7 only. Use IL2CPP for ARM64.'
}
if (-not (Test-Path -LiteralPath $UnityEditor)) { throw "Unity Editor not found: $UnityEditor" }
$testRoot = $PSScriptRoot
$repository = (Resolve-Path -LiteralPath (Join-Path $testRoot '../..')).Path
$packageRoot = Join-Path $repository 'Unity'
$package = Get-Content -LiteralPath (Join-Path $packageRoot 'package.json') -Raw | ConvertFrom-Json
$runtimeAssembly = Get-Content -LiteralPath (Join-Path $packageRoot 'Runtime/FFmpeg.AutoGen.asmdef') -Raw | ConvertFrom-Json
if (-not $NativeDirectory) { $NativeDirectory = Join-Path $testRoot 'Native' }
if (-not $SkipNativeBuild) { & (Join-Path $testRoot 'build-native.ps1') -UnityEditor $UnityEditor -Architecture $Architecture }
$project = Join-Path $testRoot "Projects/$Platform-$Architecture"
$configurationSuffix = if ($Backend -eq 'IL2CPP' -and $CompilerConfiguration -ne 'Release') { "-$CompilerConfiguration" } else { '' }
$results = Join-Path $testRoot "Results/$Platform-$Architecture/$Backend$configurationSuffix"
foreach ($folder in @('Assets/Editor', 'Packages', 'ProjectSettings')) {
    New-Item -ItemType Directory -Path (Join-Path $project $folder) -Force | Out-Null
}
New-Item -ItemType Directory -Path $results -Force | Out-Null
foreach ($reportName in @('editor.txt', 'build.txt', 'player.txt')) {
    $oldReport = Join-Path $results $reportName
    if (Test-Path -LiteralPath $oldReport) { Remove-Item -LiteralPath $oldReport }
}
$utf8 = New-Object System.Text.UTF8Encoding($false)
function Write-JsonFile([string] $Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 8), $utf8)
}
function Copy-SmokePlugins([string] $NativeTarget, [string] $AssetFolder, [string[]] $Files) {
    $destination = Join-Path $project "Assets/Plugins/$AssetFolder"
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($name in $Files) {
        $source = Join-Path $NativeDirectory "$NativeTarget/$name"
        if (-not (Test-Path -LiteralPath $source)) { throw "Missing native smoke stub: $source" }
        Copy-Item -LiteralPath $source -Destination (Join-Path $destination $name) -Force
    }
}
$dependencies = @{}
$dependencies[$package.name] = 'file:' + ($packageRoot -replace '\\', '/')
Write-JsonFile (Join-Path $project 'Packages/manifest.json') @{ dependencies = $dependencies }
Write-JsonFile (Join-Path $project 'Assets/FFmpeg.Smoke.asmdef') @{
    name = 'FFmpeg.Smoke'; references = @($runtimeAssembly.name); allowUnsafeCode = $true
}
Write-JsonFile (Join-Path $project 'Assets/Editor/FFmpeg.Smoke.Editor.asmdef') @{
    name = 'FFmpeg.Smoke.Editor'; references = @('FFmpeg.Smoke'); includePlatforms = @('Editor')
}
[IO.File]::WriteAllText((Join-Path $project 'ProjectSettings/ProjectVersion.txt'), "m_EditorVersion: 6000.3.17f1`n", $utf8)
Copy-Item -LiteralPath (Join-Path $testRoot 'SmokeChecks.cs') -Destination (Join-Path $project 'Assets/SmokeChecks.cs') -Force
Copy-Item -LiteralPath (Join-Path $testRoot 'SmokeBuild.cs') -Destination (Join-Path $project 'Assets/Editor/SmokeBuild.cs') -Force
Copy-SmokePlugins 'Windows-x64' 'Windows/x86_64' @('avutil-61.dll', 'swscale-10.dll')
if ($Platform -eq 'Windows' -and $Architecture -eq 'x86') {
    Copy-SmokePlugins 'Windows-x86' 'Windows/x86' @('avutil-61.dll', 'swscale-10.dll')
}
if ($Platform -eq 'Android') {
    $abi = if ($Architecture -eq 'armv7') { 'armeabi-v7a' } else { 'arm64-v8a' }
    Copy-SmokePlugins "Android-$Architecture" "Android/$abi" @('libavutil.so', 'libswscale.so')
}
$editorLog = Join-Path $results 'editor.log'
$buildTarget = if ($Platform -eq 'Android') { 'Android' } elseif ($Architecture -eq 'x86') { 'Win' } else { 'Win64' }
$editorArguments = @('-batchmode', '-nographics', '-quit', '-buildTarget', $buildTarget,
    '-projectPath', ('"' + $project + '"'), '-logFile', ('"' + $editorLog + '"'), '-executeMethod', 'SmokeBuild.Run',
    '-smokeBackend', $Backend, '-smokePlatform', $Platform, '-smokeArchitecture', $Architecture,
    '-smokeCompilerConfiguration', $CompilerConfiguration,
    '-smokeOutput', ('"' + $results + '"'))
Write-Output "Compiling and running Unity Editor smoke; target=$Platform/$Architecture; backend=$Backend"
$editorProcess = Start-Process -FilePath $UnityEditor -ArgumentList $editorArguments -PassThru -WindowStyle Hidden
$editorProcess.WaitForExit()
if ($editorProcess.ExitCode -ne 0) {
    Get-Content -LiteralPath $editorLog -Tail 100
    throw "Unity Editor failed ($($editorProcess.ExitCode)); see $editorLog"
}
$editorReport = Join-Path $results 'editor.txt'
if (-not (Test-Path -LiteralPath $editorReport) -or -not (Get-Content -LiteralPath $editorReport -Raw).StartsWith('PASS:')) {
    throw "Unity did not pass the Editor smoke method; see $editorLog"
}
if ($Backend -eq 'Editor') { Get-Content -LiteralPath $editorReport; return }
$buildReport = Join-Path $results 'build.txt'
if (-not (Test-Path -LiteralPath $buildReport) -or -not (Get-Content -LiteralPath $buildReport -Raw).StartsWith('PASS:')) {
    throw "Unity did not pass the Player build; see $editorLog"
}
if ($Platform -eq 'Android') {
    Get-Content -LiteralPath $buildReport
    Write-Output "APK built: $(Join-Path $results 'Smoke.apk'). Device execution was not requested by this runner."
    return
}
$playerLog = Join-Path $results 'player.log'
$playerReport = Join-Path $results 'player.txt'
$playerArguments = @('-batchmode', '-nographics', '-logFile', ('"' + $playerLog + '"'), '-smokeReport', ('"' + $playerReport + '"'))
Write-Output "Running $Backend Windows $Architecture player"
$playerProcess = Start-Process -FilePath (Join-Path $results 'Smoke.exe') -ArgumentList $playerArguments -PassThru -WindowStyle Hidden
$playerProcess.WaitForExit()
if ($playerProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $playerReport)) {
    Get-Content -LiteralPath $playerLog -Tail 100
    throw "Unity $Backend player failed ($($playerProcess.ExitCode)); see $playerLog"
}
$report = Get-Content -LiteralPath $playerReport -Raw
if (-not $report.StartsWith('PASS:')) { throw $report }
Write-Output "$Platform/$Architecture/$Backend $report"
