#!/usr/bin/env pwsh
# Reproducible Android development build. Requires UNITY_EDITOR_PATH to point at
# the pinned Unity 6000.3.4f1 editor and Android Build Support to be installed.
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $env:UNITY_EDITOR_PATH) {
    Write-Error "UNITY_EDITOR_PATH is not set. Point it at Unity 6000.3.4f1."
}

$resultsDir = Join-Path $repoRoot "TestResults"
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
$logFile = Join-Path $resultsDir "android-development-build.log"
$apkPath = Join-Path $repoRoot "Builds\Android\WalkGame-dev.apk"
if (Test-Path -LiteralPath $logFile) {
    Remove-Item -LiteralPath $logFile -Force
}
New-Item -ItemType File -Path $logFile -Force | Out-Null
if (Test-Path -LiteralPath $apkPath) {
    Remove-Item -LiteralPath $apkPath -Force
}

$unityArguments = @(
    '-batchmode',
    '-nographics',
    '-quit',
    '-projectPath', $repoRoot,
    '-executeMethod', 'WalkGame.EditorTools.WalkGameEditorTools.BuildAndroidDevelopment',
    '-logFile', $logFile)
$unityProcess = Start-Process -FilePath $env:UNITY_EDITOR_PATH -ArgumentList $unityArguments -WindowStyle Hidden -Wait -PassThru
$unityExit = $unityProcess.ExitCode

if (Test-Path $logFile) {
    Get-Content $logFile -Tail 100
}

if ($unityExit -ne 0 -or -not (Test-Path $apkPath)) {
    Write-Error "Android development build failed (Unity exit $unityExit). See $logFile"
}

Write-Host "Android development APK ready: $apkPath"
