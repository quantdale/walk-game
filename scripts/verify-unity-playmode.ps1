#!/usr/bin/env pwsh
# Unity batch-mode PlayMode certification. Requires a licensed Unity 6000.3.4f1
# editor in UNITY_EDITOR_PATH.
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'cert-script-helpers.ps1')

if (-not $env:UNITY_EDITOR_PATH) {
    Write-Error "UNITY_EDITOR_PATH is not set. Point it at Unity 6000.3.4f1."
}
# R4: fail-closed toolchain identity preflight before any editor launch.
$unityPin = Get-UnityPinnedVersion
$unityMismatch = Test-UnityEditorMatchesPin -EditorPath $env:UNITY_EDITOR_PATH -PinnedVersion $unityPin
if ($unityMismatch) {
    Write-Error "Unity toolchain identity check FAILED: $unityMismatch"
    exit 1
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
    Write-Error "Unity PlayMode run FAILED (exit $unityExit). See $logFile"
    exit $unityExit
}
if (-not (Test-Path -LiteralPath $resultsFile)) {
    Write-Error "Unity PlayMode returned success without producing $resultsFile"
    exit 1
}
$resultSummary = ''
if (-not (Test-NUnitResultXml -Path $resultsFile -Summary ([ref]$resultSummary))) {
    Write-Error "Unity PlayMode result invalid or has failures ($resultSummary): $resultsFile"
    exit 1
}
Write-Host "Unity PlayMode PASSED ($resultSummary). Results: $resultsFile Log: $logFile"
exit 0
