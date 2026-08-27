#!/usr/bin/env pwsh
# Android emulator/device smoke certification (M8.6 section 7 / spec C1-C5).
# Verifies the APK installs, launches into the Bootstrap scene, survives
# background/resume and force-stop/relaunch, and shows no fatal errors in logcat.
#
# IMPORTANT: this certifies ANDROID LIFECYCLE only - it cannot certify real step
# sensors. Runs on an emulator without a genuine TYPE_STEP_COUNTER are labeled
# "lifecycle-only" and MUST NOT satisfy physical movement requirements (P1-P10).
#
# Target selection is fail-closed (spec C1): every adb command is bound to one
# exact serial. If -DeviceSerial is supplied it must be the single authorized/
# online target; otherwise exactly one eligible target must be present.
# Artifacts (logcat, summary) are written to an ignored output folder and are
# preserved even when a check fails (spec C3).

param(
    [string]$ApkPath = "",
    [string]$PackageId = "com.quantdale.walkgame",
    [string]$AdbPath = "",
    [string]$DeviceSerial = "",
    [int]$LaunchTimeoutSeconds = 90,
    [switch]$KeepInstalled
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'cert-script-helpers.ps1')

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

# Invoke adb, automatically binding every command to the selected serial unless
# -NoSerial is given (used only for the initial `adb devices` enumeration).
function Invoke-Adb {
    param([string[]]$Arguments, [switch]$NoSerial)
    $full = @()
    if (-not $NoSerial -and $script:SelectedSerial) {
        $full += '-s'
        $full += $script:SelectedSerial
    }
    $full += $Arguments
    $output = & $script:adbExe @full 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "adb $($full -join ' ') failed (exit $LASTEXITCODE): $output"
    }
    return $output
}

$script:AdbPath = $AdbPath
$script:adbExe = Find-Adb
if (-not $script:adbExe) {
    Write-Error "adb not found. Install Android platform-tools or pass -AdbPath."
}

if (-not (Test-Path -LiteralPath $ApkPath)) {
    Write-Error "APK not found at '$ApkPath'. Build it first via scripts/build-android-development.ps1."
}

# Fail-closed target selection (spec C1).
$rawDevices = Invoke-Adb @("devices") -NoSerial | Select-Object -Skip 1 | Where-Object { $_.Trim().Length -gt 0 }
try {
    $script:SelectedSerial = Select-AndroidTarget -DeviceLines $rawDevices -PreferredSerial $DeviceSerial
}
catch {
    Write-Error "Android target selection failed: $($_.Exception.Message)"
}
Write-Host "Selected Android target serial: $($script:SelectedSerial)"

$meta = Get-AndroidDeviceMetadata -Serial $script:SelectedSerial -AdbExe $script:adbExe

$artifactDir = Join-Path $repoRoot ("Artifacts\android-smoke\" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
$logcatFile = Join-Path $artifactDir "logcat.txt"
$apkHash = Get-FileSha256 -Path $ApkPath
$sourceSha = (git -C $repoRoot rev-parse HEAD 2>$null)
if (-not $sourceSha) { $sourceSha = "unknown" }

$summary = [ordered]@{
    apk             = $ApkPath
    apkSha256       = $apkHash
    apkSizeBytes    = (Get-Item -LiteralPath $ApkPath).Length
    packageId       = $PackageId
    sourceSha       = $sourceSha
    deviceSerial    = $meta.Serial
    manufacturer    = $meta.Manufacturer
    model           = $meta.Model
    androidRelease  = $meta.AndroidRelease
    sdk             = $meta.Sdk
    abi             = $meta.Abi
    hasStepCounter  = $meta.HasStepCounter
    lifecycleOnly   = (-not $meta.HasStepCounter)
    finalDisposition = $null
    foregroundActivity = $null
    foregroundState = $null
    startedUtc      = (DateTimeOffset.UtcNow).ToString("o")
    steps           = @()
}
function Record([string]$message) {
    Write-Host "[smoke] $message"
    $summary.steps += $message
}
if ($summary.lifecycleOnly) {
    Record "emulator/no-step-counter target: lifecycle-only certification (cannot satisfy physical movement)"
}

try {
    # 1. Clean install so every run starts from a fresh profile. Pre-install
    # uninstall is idempotent: an absent package is a clean success, not a
    # failure (R6). A real removal/transport failure still throws.
    $preUninstall = Uninstall-AndroidPackageIdempotent -PackageId $PackageId -Adb { param([string[]]$a) Invoke-Adb @a }
    Record "pre-install uninstall=$preUninstall"
    Invoke-Adb @("install", "-r", $ApkPath) | Out-Null
    Record "installed=$PackageId"
    Invoke-Adb @("shell", "pm", "clear", $PackageId) | Out-Null
    Record "data cleared (fresh-profile start)"

    Invoke-Adb @("logcat", "-c")
    $launchActivity = "$PackageId/com.unity3d.player.UnityPlayerActivity"
    Invoke-Adb @("shell", "am", "start", "-n", $launchActivity) | Out-Null
    Record "launched $launchActivity"

    function Test-GameProcessAlive {
        $processId = (Invoke-Adb @("shell", "pidof", $PackageId) 2>$null) -join "`n"
        return -not [string]::IsNullOrWhiteSpace("$processId")
    }

    $deadline = (Get-Date).AddSeconds($LaunchTimeoutSeconds)
    do {
        Start-Sleep -Seconds 3
    } while ((Get-Date) -lt $deadline -and -not (Test-GameProcessAlive))

    if (-not (Test-GameProcessAlive)) {
        Invoke-Adb @("logcat", "-d") | Set-Content -Path $logcatFile
        Write-Error "Game process never came up within ${LaunchTimeoutSeconds}s. Logcat: $logcatFile"
    }
    Record "main scene process alive"

    # Strengthen launch evidence (R17.2.10): prove the expected package/activity
    # is actually foreground/resumed, not merely spawned.
    $fg = Get-AndroidForegroundActivity -Serial $script:SelectedSerial -Adb { param([string[]]$a) Invoke-Adb @a }
    $summary.foregroundActivity = $fg.FocusedActivity
    $summary.foregroundState = $fg.State
    if ($fg.FocusedActivity -notmatch [regex]::Escape($PackageId)) {
        Invoke-Adb @("logcat", "-d") | Set-Content -Path $logcatFile
        Write-Error "Launched process is alive but '$PackageId' is not the foreground activity ($($fg.FocusedActivity) / $($fg.State)). Logcat: $logcatFile"
    }
    Record "foreground activity=$($fg.FocusedActivity) state=$($fg.State)"

    Start-Sleep -Seconds 8   # let Bootstrap compose rig/UI before judging stability
    if (-not (Test-GameProcessAlive)) {
        Invoke-Adb @("logcat", "-d") | Set-Content -Path $logcatFile
        Write-Error "Game process died during startup composition. Logcat: $logcatFile"
    }
    Record "startup composition stable"

    # 2. Background -> resume.
    Invoke-Adb @("shell", "input", "keyevent", "KEYCODE_HOME")
    Start-Sleep -Seconds 4
    Invoke-Adb @("shell", "am", "start", "-n", $launchActivity) | Out-Null
    Start-Sleep -Seconds 5
    if (-not (Test-GameProcessAlive)) {
        Invoke-Adb @("logcat", "-d") | Set-Content -Path $logcatFile
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
            Invoke-Adb @("logcat", "-d") | Set-Content -Path $logcatFile
            Write-Error "Game process died on rotation. Logcat: $logcatFile"
        }
        Record "rotation survived"
    }
    catch {
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
        Invoke-Adb @("logcat", "-d") | Set-Content -Path $logcatFile
        Write-Error "Relaunch after force-stop failed. Logcat: $logcatFile"
    }
    Start-Sleep -Seconds 6
    Record "force-stop/relaunch survived"

    # 5. Fatal-error sweep over the full session logcat.
    Invoke-Adb @("logcat", "-d") | Set-Content -Path $logcatFile
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
    if (-not $KeepInstalled) {
        $postUninstall = Uninstall-AndroidPackageIdempotent -PackageId $PackageId -Adb { param([string[]]$a) Invoke-Adb @a }
        Record "uninstalled (artifacts preserved, uninstall=$postUninstall)"
        $summary.finalDisposition = 'uninstalled'
    }
    else {
        Record "kept installed per -KeepInstalled"
        $summary.finalDisposition = 'kept-installed'
    }

    Write-Host "Android smoke PASSED. Artifacts: $artifactDir"
    exit 0
}
catch {
    $summary.finalDisposition = 'failed'
    Write-Error "Android smoke FAILED: $($_.Exception.Message). Artifacts: $artifactDir"
    exit 1
}
finally {
    # R7: always persist the truthful final state, including the cleanup
    # disposition, even when a check failed. Do not write the summary before
    # the optional uninstall/keep decision is recorded.
    try { Invoke-Adb @("logcat", "-d") 2>$null | Set-Content -Path $logcatFile -ErrorAction SilentlyContinue } catch { }
    try { $summary | ConvertTo-Json -Depth 3 | Set-Content -Path (Join-Path $artifactDir "summary.json") -ErrorAction SilentlyContinue } catch { }
}
