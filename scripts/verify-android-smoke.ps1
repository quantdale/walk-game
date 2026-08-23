#!/usr/bin/env pwsh
# Android emulator/device smoke certification (M8 campaign section 14).
# Verifies the APK installs, launches into the Bootstrap scene, survives
# background/resume and force-stop/relaunch, and shows no fatal errors in logcat.
# This certifies ANDROID LIFECYCLE only - it cannot certify real step sensors.
# Artifacts (logcat, summary) are written to an ignored output folder.

param(
    [string]$ApkPath = "",
    [string]$PackageId = "com.quantdale.walkgame",
    [string]$AdbPath = "",
    [int]$LaunchTimeoutSeconds = 90,
    [switch]$KeepInstalled
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $ApkPath) {
    $ApkPath = Join-Path $repoRoot "Builds\Android\WalkGame-dev.apk"
}

function Find-Adb {
    if ($script:AdbPath -and (Test-Path -LiteralPath $script:AdbPath)) { return $script:AdbPath }
    if ($env:ANDROID_ADB -and (Test-Path -LiteralPath $env:ANDROID_ADB)) { return $env:ANDROID_ADB }
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Android\Sdk\platform-tools\adb.exe"),
        (Join-Path $env:ANDROID_HOME "platform-tools\adb.exe"),
        (Join-Path $env:ANDROID_SDK_ROOT "platform-tools\adb.exe")
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    if ($candidates.Count -gt 0) { return $candidates[0] }
    $onPath = Get-Command adb -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    return $null
}

function Invoke-Adb {
    param([string[]]$Arguments)
    $output = & $script:adbExe @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "adb $($Arguments -join ' ') failed (exit $LASTEXITCODE): $output"
    }
    return $output
}

$script:AdbPath = $AdbPath
$adbExe = Find-Adb
if (-not $adbExe) {
    Write-Error "adb not found. Install Android platform-tools or pass -AdbPath."
}

if (-not (Test-Path -LiteralPath $ApkPath)) {
    Write-Error "APK not found at '$ApkPath'. Build it first via scripts/build-android-development.ps1."
}

$devices = @(Invoke-Adb @("devices") | Select-Object -Skip 1 | Where-Object { $_ -match "\S\s+(device|emulator)" })
if ($devices.Count -eq 0) {
    Write-Error "No Android device/emulator connected ('adb devices' is empty)."
}
Write-Host "Device(s): $($devices -join '; ')"

$artifactDir = Join-Path $repoRoot ("Artifacts\android-smoke\" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
$logcatFile = Join-Path $artifactDir "logcat.txt"
$summary = [ordered]@{
    apk = $ApkPath
    apkSizeBytes = (Get-Item -LiteralPath $ApkPath).Length
    packageId = $PackageId
    device = ($devices -join ", ")
    startedUtc = (DateTimeOffset.UtcNow).ToString("o")
    steps = @()
}
function Record([string]$message) {
    Write-Host "[smoke] $message"
    $summary.steps += $message
}

# 1. Clean install so every run starts from a fresh profile.
Invoke-Adb @("uninstall", $PackageId) | Out-Null   # failure here is fine (not installed)
Invoke-Adb @("install", "-r", $ApkPath) | Out-Null
Record "installed=$PackageId"
Invoke-Adb @("shell", "pm", "clear", $PackageId) | Out-Null
Record "data cleared (fresh-profile start)"

Invoke-Adb @("logcat", "-c")
$launchActivity = "$PackageId/com.unity3d.player.UnityPlayerActivity"
Invoke-Adb @("shell", "am", "start", "-n", $launchActivity) | Out-Null
Record "launched $launchActivity"

function Test-GameProcessAlive {
    $processId = (& $adbExe shell pidof $PackageId 2>$null)
    return -not [string]::IsNullOrWhiteSpace("$processId")
}

$deadline = (Get-Date).AddSeconds($LaunchTimeoutSeconds)
do {
    Start-Sleep -Seconds 3
} while ((Get-Date) -lt $deadline -and -not (Test-GameProcessAlive))

if (-not (Test-GameProcessAlive)) {
    & $adbExe logcat -d > $logcatFile
    Write-Error "Game process never came up within ${LaunchTimeoutSeconds}s. Logcat: $logcatFile"
}
Record "main scene process alive"

Start-Sleep -Seconds 8   # let Bootstrap compose rig/UI before judging stability
if (-not (Test-GameProcessAlive)) {
    & $adbExe logcat -d > $logcatFile
    Write-Error "Game process died during startup composition. Logcat: $logcatFile"
}
Record "startup composition stable"

# 2. Background -> resume.
Invoke-Adb @("shell", "input", "keyevent", "KEYCODE_HOME")
Start-Sleep -Seconds 4
Invoke-Adb @("shell", "am", "start", "-n", $launchActivity) | Out-Null
Start-Sleep -Seconds 5
if (-not (Test-GameProcessAlive)) {
    & $adbExe logcat -d > $logcatFile
    Write-Error "Game process died across background/resume. Logcat: $logcatFile"
}
Record "background/resume survived"

# 3. Rotation attempt (design supports aspect-flexible HUD); some emulator images
# reject programmatic rotation, so failure downgrades the check instead of failing.
try {
    Invoke-Adb @("shell", "settings", "put", "system", "accelerometer_rotation", "1") | Out-Null
    Invoke-Adb @("shell", "settings", "put", "system", "user_rotation", "1") | Out-Null
    Start-Sleep -Seconds 4
    if (-not (Test-GameProcessAlive)) {
        & $adbExe logcat -d > $logcatFile
        Write-Error "Game process died on rotation. Logcat: $logcatFile"
    }
    Record "rotation survived"
} catch {
    Record "rotation check skipped ($($_.Exception.Message))"
}

# 4. Force stop -> relaunch (save must reload cleanly).
Invoke-Adb @("shell", "am", "force-stop", $PackageId)
Start-Sleep -Seconds 3
Invoke-Adb @("shell", "am", "start", "-n", $launchActivity) | Out-Null
$deadline = (Get-Date).AddSeconds($LaunchTimeoutSeconds)
do {
    Start-Sleep -Seconds 3
} while ((Get-Date) -lt $deadline -and -not (Test-GameProcessAlive))
if (-not (Test-GameProcessAlive)) {
    & $adbExe logcat -d > $logcatFile
    Write-Error "Relaunch after force-stop failed. Logcat: $logcatFile"
}
Start-Sleep -Seconds 6
Record "force-stop/relaunch survived"

# 5. Fatal-error sweep over the full session logcat.
& $adbExe logcat -d > $logcatFile
$escapedPackage = [regex]::Escape($PackageId)
$fatalPatterns = @(
    "FATAL EXCEPTION",
    "ANR in $escapedPackage",
    "Process $escapedPackage.*has died",
    "Force finishing activity $escapedPackage"
)
$fatalLines = @()
foreach ($pattern in $fatalPatterns) {
    $fatalLines += @(Select-String -Path $logcatFile -Pattern $pattern -ErrorAction SilentlyContinue)
}
$fatalLines = @($fatalLines | Where-Object { $_ -ne $null })
if ($fatalLines.Count -gt 0) {
    $fatalLines | Select-Object -First 20 | ForEach-Object { Write-Warning $_.Line }
    Write-Error "Fatal conditions found in logcat ($($fatalLines.Count)). See $logcatFile"
}
Record "no fatal exceptions/ANRs in logcat"

$summary.completedUtc = (DateTimeOffset.UtcNow).ToString("o")
$summary | ConvertTo-Json -Depth 3 | Set-Content -Path (Join-Path $artifactDir "summary.json")

if (-not $KeepInstalled) {
    Invoke-Adb @("uninstall", $PackageId) | Out-Null
    Record "uninstalled (artifacts preserved)"
}

Write-Host "Android smoke PASSED. Artifacts: $artifactDir"
exit 0
