#!/usr/bin/env pwsh
# Idempotent Unity project bring-up. Requires UNITY_EDITOR_PATH to point at the
# pinned Unity 6000.3.4f1 editor executable.
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $env:UNITY_EDITOR_PATH) {
    Write-Error "UNITY_EDITOR_PATH is not set. Point it at Unity 6000.3.4f1."
}

$resultsDir = Join-Path $repoRoot "TestResults"
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
$logFile = Join-Path $resultsDir "setup-unity-project.log"
if (Test-Path -LiteralPath $logFile) {
    Remove-Item -LiteralPath $logFile -Force
}
New-Item -ItemType File -Path $logFile -Force | Out-Null

$unityArguments = @(
    '-batchmode',
    '-nographics',
    '-quit',
    '-projectPath', $repoRoot,
    '-executeMethod', 'WalkGame.EditorTools.WalkGameEditorTools.ApplyProjectSetup',
    '-logFile', $logFile)
$unityProcess = Start-Process -FilePath $env:UNITY_EDITOR_PATH -ArgumentList $unityArguments -WindowStyle Hidden -Wait -PassThru
$unityExit = $unityProcess.ExitCode

if (Test-Path $logFile) {
    Get-Content $logFile -Tail 80
}

if ($unityExit -ne 0) {
    Write-Error "Unity project setup failed (exit $unityExit). See $logFile"
}

Write-Host "Unity project setup passed. Log: $logFile"
