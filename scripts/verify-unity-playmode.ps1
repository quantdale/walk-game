#!/usr/bin/env pwsh
# Unity batch-mode PlayMode certification. Requires a licensed Unity 6000.3.4f1
# editor in UNITY_EDITOR_PATH.
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $env:UNITY_EDITOR_PATH) {
    Write-Error "UNITY_EDITOR_PATH is not set. Point it at Unity 6000.3.4f1."
}

$resultsDir = Join-Path $repoRoot "TestResults"
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
$logFile = Join-Path $resultsDir "playmode-run.log"
$resultsFile = Join-Path $resultsDir "playmode-results.xml"
foreach ($artifact in @($logFile, $resultsFile)) {
    if (Test-Path -LiteralPath $artifact) {
        Remove-Item -LiteralPath $artifact -Force
    }
}
New-Item -ItemType File -Path $logFile -Force | Out-Null
$unityArguments = @(
    '-batchmode',
    '-nographics',
    '-projectPath', $repoRoot,
    '-runTests',
    '-testPlatform', 'PlayMode',
    '-testResults', $resultsFile,
    '-logFile', $logFile)

$unityProcess = Start-Process -FilePath $env:UNITY_EDITOR_PATH -ArgumentList $unityArguments -WindowStyle Hidden -Wait -PassThru
$unityExit = $unityProcess.ExitCode

if (Test-Path $logFile) {
    Get-Content $logFile -Tail 60
}

if ($unityExit -ne 0) {
    Write-Host "Unity PlayMode run FAILED (exit $unityExit). See $logFile"
} elseif (-not (Test-Path $resultsFile)) {
    Write-Error "Unity PlayMode returned success without producing $resultsFile"
} else {
    Write-Host "Unity PlayMode run passed. Results: $resultsFile"
}

exit $unityExit
