#!/usr/bin/env pwsh
# Unity batch-mode verification (campaign S3). Requires a local Unity 6000.3.x
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

if (-not $env:UNITY_EDITOR_PATH) {
    Write-Error "UNITY_EDITOR_PATH is not set. Point it at the Unity 6000.3.x editor executable."
}

$resultsDir = Join-Path $repoRoot "TestResults"
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
$logFile = Join-Path $resultsDir "editmode-run.log"
$resultsFile = Join-Path $resultsDir "editmode-results.xml"

& $env:UNITY_EDITOR_PATH `
    -batchmode `
    -nographics `
    -projectPath $repoRoot `
    -runTests `
    -testPlatform EditMode `
    -testResults $resultsFile `
    -logFile $logFile
$unityExit = $LASTEXITCODE

Get-Content $logFile -Tail 40
if ($unityExit -ne 0) {
    Write-Host "Unity EditMode run FAILED (exit $unityExit). See $logFile"
} else {
    Write-Host "Unity EditMode run passed. Results: $resultsFile"
}
exit $unityExit
