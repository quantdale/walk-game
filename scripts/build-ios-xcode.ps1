#!/usr/bin/env pwsh
# Reproducible iOS Xcode-generation/build certification wrapper (M8.8 S25/S26).
# Run this on macOS with the pinned Unity editor and Xcode 26+/iOS 26+ SDK:
#
#   $env:UNITY_EDITOR_PATH = "/Applications/Unity/Hub/Editor/6000.3.4f1/Unity.app/Contents/MacOS/Unity"
#   ./scripts/build-ios-xcode.ps1
#
# Signing credentials are supplied at invocation time and are never persisted in
# the repository. Without -Sign this performs an unsigned Xcode build; that is
# useful for build-path validation but is not App Store/device certification.
[CmdletBinding()]
param(
    [switch]$Sign,
    [switch]$RequireSigned,
    [string]$SigningTeamId = "",
    [string]$DeviceSerial = "",
    [switch]$AllowCanonicalProjectMutation
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'cert-script-helpers.ps1')

function Get-GitStatusSnapshot {
    $lines = @(& git -C $repoRoot status --porcelain=v1 --untracked-files=all 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "git status failed with exit $LASTEXITCODE." }
    return @($lines | ForEach-Object {
        $line = ([string]$_).TrimEnd()
        if (-not [string]::IsNullOrWhiteSpace($line)) { $line }
    })
}

function Get-NewStatusLines([string[]]$Before, [string[]]$After) {
    $known = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($line in $Before) { [void]$known.Add($line) }
    return @($After | Where-Object { -not $known.Contains($_) })
}

function Join-StatusLines([string[]]$Lines) {
    return ($Lines -join [Environment]::NewLine)
}

function Get-CommandPath([string]$Name) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if (-not $command) { return $null }
    return $command.Source
}

if (-not [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::OSX)) {
    Write-Error "iOS Xcode certification requires macOS; this wrapper refuses to run on the current host."
    exit 1
}
if (-not $env:UNITY_EDITOR_PATH) {
    Write-Error "UNITY_EDITOR_PATH is not set. Point it at Unity 6000.3.4f1 on macOS."
    exit 1
}
if ($RequireSigned -and -not $Sign) {
    Write-Error "-RequireSigned requires -Sign; signing remains an explicit external operation."
    exit 1
}
if ($Sign -and [string]::IsNullOrWhiteSpace($SigningTeamId)) {
    Write-Error "-Sign requires -SigningTeamId. Do not commit provisioning credentials."
    exit 1
}

$unityPath = $env:UNITY_EDITOR_PATH
$unityPin = Get-UnityPinnedVersion
$unityMismatch = Test-UnityEditorMatchesPin -EditorPath $unityPath -PinnedVersion $unityPin
if ($unityMismatch) {
    Write-Error "Unity toolchain identity check FAILED: $unityMismatch"
    exit 1
}

$xcodebuild = Get-CommandPath "xcodebuild"
$xcrun = Get-CommandPath "xcrun"
if (-not $xcodebuild -or -not $xcrun) {
    Write-Error "Xcode command-line tools are missing (xcodebuild/xcrun)."
    exit 1
}

$preStatusLines = @(Get-GitStatusSnapshot)
$preDirty = $preStatusLines.Count -gt 0
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$artifactDir = Join-Path $repoRoot ("Artifacts/ios-build/" + $timestamp)
$unityLogFile = Join-Path $artifactDir "unity-xcode-generation.log"
$xcodeLogFile = Join-Path $artifactDir "xcodebuild.log"
$evidenceFile = Join-Path $artifactDir "ios-build-evidence.json"
$outputPath = [IO.Path]::GetFullPath((Join-Path $repoRoot "Builds/iOS/WalkGame-Xcode"))
$derivedDataPath = [IO.Path]::GetFullPath((Join-Path $artifactDir "DerivedData"))
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

$unityExit = 127
$xcodeExit = 127
$installExit = $null
$launchError = ""
$xcodeVersion = ""
$sdkVersion = ""
$bundleIdentifier = "com.quantdale.walkgame"
$motionUsageDescription = $false
$coreMotionBridgePresent = Test-Path -LiteralPath (Join-Path $repoRoot "Assets/Plugins/iOS/WalkGamePedometerBridge.mm")
$xcodeProjectPath = ""
$appPath = ""
$xcodeProjectSha256 = ""
$bundleIdentifierVerified = $false
$signResult = if ($Sign) { "requested" } else { "not-requested" }
$installResult = if ($DeviceSerial) { "requested" } else { "not-requested" }
$postStatusLines = @()
$mutatedFiles = @()
$mutation = [pscustomobject]@{ Canonical = @(); Unexpected = @() }
$completed = $false

try {
    $xcodeVersionLines = @(& $xcodebuild -version 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "xcodebuild -version failed: $($xcodeVersionLines -join ' ')" }
    $xcodeVersion = ($xcodeVersionLines -join ' ').Trim()
    $xcodeMatch = [regex]::Match($xcodeVersion, '(?i)\bXcode\s+(\d+)(?:\.(\d+))?')
    if (-not $xcodeMatch.Success -or [int]$xcodeMatch.Groups[1].Value -lt 26) {
        throw "Xcode 26 or later is required for current App Store submission readiness; reported '$xcodeVersion'."
    }

    $sdkOutput = @(& $xcrun --sdk iphoneos --show-sdk-version 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Could not determine the iPhoneOS SDK version: $($sdkOutput -join ' ')" }
    $sdkVersion = ($sdkOutput -join '').Trim()
    $sdkMatch = [regex]::Match($sdkVersion, '^(\d+)(?:\.(\d+))?')
    if (-not $sdkMatch.Success -or [int]$sdkMatch.Groups[1].Value -lt 26) {
        throw "iOS 26 SDK or later is required for current App Store submission readiness; reported '$sdkVersion'."
    }

    if (Test-Path -LiteralPath $outputPath) {
        Remove-Item -LiteralPath $outputPath -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputPath) | Out-Null

    $unityArguments = @(
        '-batchmode',
        '-nographics',
        '-quit',
        '-projectPath', $repoRoot,
        '-executeMethod', 'WalkGame.EditorTools.WalkGameEditorTools.BuildIosXcodeDevelopment',
        '-logFile', $unityLogFile
    )
    try {
        $unityProcess = Start-Process -FilePath $unityPath -ArgumentList $unityArguments -Wait -PassThru
        $unityExit = $unityProcess.ExitCode
    }
    catch {
        $launchError = $_.Exception.Message
        throw "Unity iOS Xcode generation launch failed: $launchError"
    }
    if ($unityExit -ne 0) { throw "Unity iOS Xcode generation failed with exit $unityExit." }
    if (-not (Test-Path -LiteralPath $outputPath -PathType Container)) {
        throw "Unity exited successfully but did not create Xcode output '$outputPath'."
    }

    $xcodeProject = Get-ChildItem -LiteralPath $outputPath -Filter "*.xcodeproj" -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $xcodeProject) { throw "No .xcodeproj was generated under '$outputPath'." }
    $xcodeProjectPath = $xcodeProject.FullName
    $pbxproj = Join-Path $xcodeProjectPath "project.pbxproj"
    if (-not (Test-Path -LiteralPath $pbxproj)) { throw "Generated Xcode project.pbxproj is missing." }
    $xcodeProjectSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $pbxproj).Hash.ToLowerInvariant()
    $projectText = Get-Content -LiteralPath $pbxproj -Raw
    $bundleIdentifierVerified = $projectText -match 'PRODUCT_BUNDLE_IDENTIFIER\s*=\s*com\.quantdale\.walkgame\s*;'
    if (-not $bundleIdentifierVerified) {
        throw "Generated Xcode project does not bind PRODUCT_BUNDLE_IDENTIFIER to com.quantdale.walkgame."
    }

    $xcodeArgs = @(
        '-project', $xcodeProjectPath,
        '-scheme', 'Unity-iPhone',
        '-configuration', 'Debug',
        '-sdk', 'iphoneos',
        '-derivedDataPath', $derivedDataPath,
        'CODE_SIGNING_ALLOWED=' + $(if ($Sign) { 'YES' } else { 'NO' })
    )
    if ($Sign) {
        $xcodeArgs += 'DEVELOPMENT_TEAM=' + $SigningTeamId
        $xcodeArgs += 'CODE_SIGN_STYLE=Automatic'
    }

    $xcodeBuildLines = @(& $xcodebuild @xcodeArgs build 2>&1)
    $xcodeExit = $LASTEXITCODE
    $xcodeBuildLines | Set-Content -LiteralPath $xcodeLogFile -Encoding UTF8
    if ($xcodeExit -ne 0) { throw "xcodebuild failed with exit $xcodeExit. See $xcodeLogFile." }

    $app = Get-ChildItem -LiteralPath $derivedDataPath -Recurse -Directory -Filter "*.app" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($app) { $appPath = $app.FullName }
    if (-not $appPath) { throw "xcodebuild succeeded but no .app output was found." }

    $plistFiles = @(Get-ChildItem -LiteralPath $outputPath -Recurse -File -Filter "Info.plist" -ErrorAction SilentlyContinue)
    foreach ($plist in $plistFiles) {
        $plistText = Get-Content -LiteralPath $plist.FullName -Raw -ErrorAction SilentlyContinue
        if ($plistText -match "NSMotionUsageDescription") {
            $motionUsageDescription = $true
            if ($plistText -match "com\.quantdale\.walkgame") { $bundleIdentifier = "com.quantdale.walkgame" }
            break
        }
    }
    if (-not $motionUsageDescription) {
        throw "Generated Xcode output has no NSMotionUsageDescription in Info.plist. See $unityLogFile."
    }
    if (-not $coreMotionBridgePresent) {
        throw "Tracked CoreMotion bridge source is missing."
    }

    if ($Sign) { $signResult = "built-signed-requested" }
    if ($DeviceSerial) {
        if (-not $Sign) { throw "Device install requires -Sign so the app is installable." }
        $installLines = @(& $xcrun devicectl device install app --device $DeviceSerial $appPath 2>&1)
        $installExit = $LASTEXITCODE
        $installLines | Add-Content -LiteralPath $xcodeLogFile -Encoding UTF8
        if ($installExit -ne 0) { throw "device install failed with exit $installExit. See $xcodeLogFile." }
        $installResult = "installed:$DeviceSerial"
    }
    elseif ($Sign) {
        $installResult = "not-requested"
    }

    $completed = $true
}
catch {
    Write-Error $_.Exception.Message
}
finally {
    $postStatusLines = @(Get-GitStatusSnapshot)
    $mutatedFiles = @(Get-NewStatusLines -Before $preStatusLines -After $postStatusLines)
    $mutation = Test-UnityMutationSet -MutatedFiles $mutatedFiles -AllowCanonical:$AllowCanonicalProjectMutation
    $evidence = [ordered]@{
        schemaVersion = 1
        sourceSha = (& git -C $repoRoot rev-parse HEAD).Trim()
        preDirty = $preDirty
        preStatus = Join-StatusLines $preStatusLines
        postDirty = ($postStatusLines.Count -gt 0)
        postStatus = Join-StatusLines $postStatusLines
        mutatedFiles = $mutatedFiles
        canonicalMutationFiles = @($mutation.Canonical)
        unexpectedMutationFiles = @($mutation.Unexpected)
        allowCanonicalProjectMutation = [bool]$AllowCanonicalProjectMutation
        unityPath = $unityPath
        unityPinnedVersion = $unityPin
        xcodeVersion = $xcodeVersion
        sdkVersion = $sdkVersion
        unityExitCode = $unityExit
        xcodeExitCode = $xcodeExit
        installExitCode = $installExit
        launchError = $launchError
        outputPath = $outputPath
        xcodeProjectPath = $xcodeProjectPath
        xcodeProjectSha256 = $xcodeProjectSha256
        appPath = $appPath
        bundleIdentifier = $bundleIdentifier
        bundleIdentifierVerified = $bundleIdentifierVerified
        coreMotionBridgePresent = $coreMotionBridgePresent
        nsMotionUsageDescription = $motionUsageDescription
        signResult = $signResult
        installResult = $installResult
        completed = $completed
        unityLogPath = $unityLogFile
        xcodeLogPath = $xcodeLogFile
        recordedUtc = [DateTimeOffset]::UtcNow.ToString("o")
    }
    $evidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $evidenceFile -Encoding UTF8
    Write-Host "iOS evidence: $evidenceFile"
}

if (-not $completed -or $mutation.Unexpected.Count -gt 0) {
    if ($mutation.Unexpected.Count -gt 0) {
        Write-Error "iOS certification failed closed: unexpected project mutation(s): $($mutation.Unexpected -join ', ')"
    }
    exit 1
}

Write-Host "iOS Xcode build certification completed: Xcode=$xcodeVersion SDK=$sdkVersion bundle=$bundleIdentifier app=$appPath"
exit 0
