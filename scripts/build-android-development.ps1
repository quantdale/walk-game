#!/usr/bin/env pwsh
# Reproducible Android development build and target-SDK evidence gate.
# Requires UNITY_EDITOR_PATH to point at Unity 6000.3.4f1 with Android Build
# Support, an Android SDK containing platform android-36 and build-tools/aapt.
# The APK is accepted only when its generated manifest reports targetSdkVersion
# >= 36; source text alone is not release evidence.
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resultsDir = Join-Path $repoRoot "TestResults"
$logFile = Join-Path $resultsDir "android-development-build.log"
$evidenceFile = Join-Path $resultsDir "android-development-evidence.json"
$apkPath = Join-Path $repoRoot "Builds\Android\WalkGame-dev.apk"

function Get-AndroidSdkRoot {
    $candidates = @()
    if ($env:ANDROID_SDK_ROOT) { $candidates += $env:ANDROID_SDK_ROOT }
    if ($env:ANDROID_HOME) { $candidates += $env:ANDROID_HOME }

    if ($env:UNITY_EDITOR_PATH) {
        $editorDir = Split-Path -Parent $env:UNITY_EDITOR_PATH
        $candidates += (Join-Path $editorDir "Data/PlaybackEngines/AndroidPlayer/SDK")
        $candidates += (Join-Path $editorDir "../PlaybackEngines/AndroidPlayer/SDK")
    }

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath (Join-Path $candidate "platforms/android-36"))) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    return $null
}

function Get-AndroidBuildTools([string]$SdkRoot) {
    $root = Join-Path $SdkRoot "build-tools"
    if (-not (Test-Path -LiteralPath $root)) { return $null }
    $dirs = @(Get-ChildItem -LiteralPath $root -Directory | Sort-Object Name -Descending)
    foreach ($dir in $dirs) {
        foreach ($name in @("aapt.exe", "aapt")) {
            $candidate = Join-Path $dir.FullName $name
            if (Test-Path -LiteralPath $candidate) { return $candidate }
        }
    }
    return $null
}

if (-not $env:UNITY_EDITOR_PATH) {
    Write-Error "UNITY_EDITOR_PATH is not set. Point it at Unity 6000.3.4f1."
    exit 1
}

$unityPin = "6000.3.4f1"
$projectVersion = Get-Content -LiteralPath (Join-Path $repoRoot "ProjectSettings/ProjectVersion.txt") -Raw
if ($projectVersion -notmatch "m_EditorVersion:\s*$unityPin") {
    Write-Error "ProjectVersion.txt does not pin Unity $unityPin."
    exit 1
}
$editorResolved = (Resolve-Path -LiteralPath $env:UNITY_EDITOR_PATH -ErrorAction SilentlyContinue).Path
if (-not $editorResolved -or $editorResolved -notmatch [regex]::Escape($unityPin)) {
    Write-Error "UNITY_EDITOR_PATH does not resolve to the pinned Unity $unityPin editor."
    exit 1
}

New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
foreach ($artifact in @($logFile, $evidenceFile, $apkPath)) {
    if (Test-Path -LiteralPath $artifact) {
        Remove-Item -LiteralPath $artifact -Recurse -Force
    }
}

$sourceSha = (& git -C $repoRoot rev-parse HEAD 2>&1).Trim()
$preStatus = (& git -C $repoRoot status --porcelain=v1 --untracked-files=all 2>&1) -join [Environment]::NewLine
$sdkRoot = Get-AndroidSdkRoot
if (-not $sdkRoot) {
    Write-Error "Android SDK platform android-36 is unavailable. Install it through the pinned Unity/SDK toolchain."
    exit 1
}
$aaptPath = Get-AndroidBuildTools $sdkRoot
if (-not $aaptPath) {
    Write-Error "Android build-tools with aapt are unavailable under '$sdkRoot'."
    exit 1
}
$buildToolsVersion = Split-Path -Leaf (Split-Path -Parent $aaptPath)
$platformPath = Join-Path $sdkRoot "platforms/android-36"

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
    Get-Content $logFile -Tail 120
}
if ($unityExit -ne 0 -or -not (Test-Path -LiteralPath $apkPath)) {
    Write-Error "Android development build failed (Unity exit $unityExit). See $logFile"
    exit 1
}

$badging = @(& $aaptPath dump badging $apkPath 2>&1)
$aaptExit = $LASTEXITCODE
$targetMatch = [regex]::Match(($badging -join [Environment]::NewLine), "targetSdkVersion:'(\d+)'")
if ($aaptExit -ne 0 -or -not $targetMatch.Success) {
    Write-Error "Could not read generated APK targetSdkVersion with '$aaptPath'. See $logFile"
    exit 1
}
$targetSdk = [int]$targetMatch.Groups[1].Value
if ($targetSdk -lt 36) {
    Write-Error "Generated APK targetSdkVersion=$targetSdk is below required API 36; refusing release evidence."
    exit 1
}

$apkHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $apkPath).Hash.ToLowerInvariant()
$evidence = [ordered]@{
    schemaVersion = 1
    sourceSha = $sourceSha
    preDirty = -not [string]::IsNullOrWhiteSpace($preStatus)
    preStatus = $preStatus
    unityEditorPath = $editorResolved
    unityVersion = $unityPin
    sdkRoot = $sdkRoot
    sdkPlatform = $platformPath
    buildToolsVersion = $buildToolsVersion
    targetSdk = $targetSdk
    minSdk = 26
    scriptingBackend = "IL2CPP"
    architecture = "ARM64"
    apkPath = [IO.Path]::GetFullPath($apkPath)
    apkSha256 = $apkHash
    apkSizeBytes = (Get-Item -LiteralPath $apkPath).Length
    unityExitCode = $unityExit
    aaptExitCode = $aaptExit
    logPath = [IO.Path]::GetFullPath($logFile)
    recordedUtc = [DateTimeOffset]::UtcNow.ToString("o")
}
$evidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $evidenceFile -Encoding UTF8
Write-Host "Android development APK and API 36 evidence ready: $apkPath"
Write-Host "Evidence: $evidenceFile"
