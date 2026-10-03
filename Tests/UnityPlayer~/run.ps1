param(
    [string] $UnityEditor = 'C:\Program Files\Unity Editors\6000.3.17f1\Editor\Unity.exe',
    [ValidateSet('Editor', 'Mono', 'IL2CPP')]
    [string] $Backend = 'IL2CPP',
    [string] $NativeDirectory = ''
)

$ErrorActionPreference = 'Stop'
$testRoot = $PSScriptRoot
$repository = (Resolve-Path -LiteralPath (Join-Path $testRoot '../..')).Path
$packageRoot = Join-Path $repository 'Unity'
$package = Get-Content -LiteralPath (Join-Path $packageRoot 'package.json') -Raw | ConvertFrom-Json
$runtimeAssembly = Get-Content -LiteralPath (Join-Path $packageRoot 'Runtime/FFmpeg.AutoGen.asmdef') -Raw | ConvertFrom-Json
if (-not $NativeDirectory) { $NativeDirectory = Join-Path $testRoot '../UnitySmoke~/native/bin' }
foreach ($name in @('avutil-61.dll', 'swscale-10.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $NativeDirectory $name))) {
        throw "Missing $name. Run Tests/UnitySmoke~/run.ps1 first, or pass -NativeDirectory with the compiled smoke stubs."
    }
}
if (-not (Test-Path -LiteralPath $UnityEditor)) { throw "Unity Editor not found: $UnityEditor" }

$project = Join-Path $testRoot 'Project'
$results = Join-Path $testRoot "Results/$Backend"
foreach ($folder in @('Assets/Editor', 'Assets/Plugins/x86_64', 'Packages', 'ProjectSettings')) {
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
foreach ($name in @('avutil-61.dll', 'swscale-10.dll')) {
    Copy-Item -LiteralPath (Join-Path $NativeDirectory $name) -Destination (Join-Path $project "Assets/Plugins/x86_64/$name") -Force
}

$editorLog = Join-Path $results 'editor.log'
$editorArguments = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $project + '"'),
    '-logFile', ('"' + $editorLog + '"'), '-executeMethod', 'SmokeBuild.Run',
    '-smokeBackend', $Backend, '-smokeOutput', ('"' + $results + '"'))
Write-Output "Compiling and running Unity Editor smoke; backend=$Backend"
$editorProcess = Start-Process -FilePath $UnityEditor -ArgumentList $editorArguments -PassThru -WindowStyle Hidden
$editorProcess.WaitForExit()
if ($editorProcess.ExitCode -ne 0) {
    Get-Content -LiteralPath $editorLog -Tail 100
    throw "Unity Editor failed ($($editorProcess.ExitCode)); see $editorLog"
}
if (-not (Test-Path -LiteralPath (Join-Path $results 'editor.txt'))) {
    throw "Unity did not execute the smoke method; see $editorLog"
}
if ($Backend -eq 'Editor') { Get-Content -LiteralPath (Join-Path $results 'editor.txt'); return }

$playerLog = Join-Path $results 'player.log'
$playerReport = Join-Path $results 'player.txt'
$playerArguments = @('-batchmode', '-nographics', '-logFile', ('"' + $playerLog + '"'), '-smokeReport', ('"' + $playerReport + '"'))
Write-Output "Running $Backend Windows x64 player"
$playerProcess = Start-Process -FilePath (Join-Path $results 'Smoke.exe') -ArgumentList $playerArguments -PassThru -WindowStyle Hidden
$playerProcess.WaitForExit()
if ($playerProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $playerReport)) {
    Get-Content -LiteralPath $playerLog -Tail 100
    throw "Unity $Backend player failed ($($playerProcess.ExitCode)); see $playerLog"
}
$report = Get-Content -LiteralPath $playerReport -Raw
if (-not $report.StartsWith('PASS:')) { throw $report }
Write-Output "$Backend $report"
