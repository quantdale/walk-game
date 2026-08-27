#!/usr/bin/env pwsh
# Unity batch-mode verification (campaign S3). Requires the pinned Unity 6000.3.4f1
# installation; the editor path is read from the UNITY_EDITOR_PATH environment
# variable so no machine-specific path is committed.
#
#   $env:UNITY_EDITOR_PATH = "C:\Program Files\Unity\Hub\Editor\6000.3.4f1\Editor\Unity.exe"
#   ./scripts/verify-unity-editmode.ps1
#
# Runs the EditMode test assembly and writes machine-readable results to
# TestResults/editmode-results.xml plus the full editor log beside it.
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'cert-script-helpers.ps1')

if (-not $env:UNITY_EDITOR_PATH) {
    Write-Error "UNITY_EDITOR_PATH is not set. Point it at the Unity 6000.3.4f1 editor executable."
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
$logFile = Join-Path $resultsDir "editmode-run.log"
$resultsFile = Join-Path $resultsDir "editmode-results.xml"
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
    '-testPlatform', 'EditMode',
    '-testResults', $resultsFile,
    '-logFile', $logFile)
$unityProcess = Start-Process -FilePath $env:UNITY_EDITOR_PATH -ArgumentList $unityArguments -WindowStyle Hidden -Wait -PassThru
$unityExit = $unityProcess.ExitCode

if (Test-Path $logFile) {
    Get-Content $logFile -Tail 40
}
if ($unityExit -ne 0) {
    Write-Error "Unity EditMode run FAILED (exit $unityExit). See $logFile"
    exit $unityExit
}
if (-not (Test-Path -LiteralPath $resultsFile)) {
    Write-Error "Unity EditMode exited 0 but produced no result file: $resultsFile"
    exit 1
}
$resultSummary = ''
if (-not (Test-NUnitResultXml -Path $resultsFile -Summary ([ref]$resultSummary))) {
    Write-Error "Unity EditMode result invalid or has failures ($resultSummary): $resultsFile"
    exit 1
}
Write-Host "Unity EditMode PASSED ($resultSummary). Results: $resultsFile Log: $logFile"
exit 0
