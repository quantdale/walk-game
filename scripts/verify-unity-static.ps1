#!/usr/bin/env pwsh
# Static Unity-project validation. This intentionally does not import Unity or claim
# compile/test evidence; it catches deterministic checkout regressions before a licensed
# editor is available.
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$errors = [System.Collections.Generic.List[string]]::new()

function Add-ValidationError([string]$message) {
    $script:errors.Add($message)
}

$projectVersionPath = Join-Path $repoRoot "ProjectSettings/ProjectVersion.txt"
if (-not (Test-Path -LiteralPath $projectVersionPath)) {
    Add-ValidationError "Missing ProjectSettings/ProjectVersion.txt."
} elseif (-not (Get-Content -LiteralPath $projectVersionPath -Raw).Contains("m_EditorVersion: 6000.3.4f1")) {
    Add-ValidationError "ProjectVersion.txt does not pin Unity 6000.3.4f1."
}

$assetsRoot = Join-Path $repoRoot "Assets"
$assetFiles = @(Get-ChildItem -LiteralPath $assetsRoot -Recurse -File -ErrorAction Stop)
$missingMeta = @($assetFiles | Where-Object { $_.Extension -ne ".meta" -and -not (Test-Path -LiteralPath "$($_.FullName).meta") })
foreach ($file in $missingMeta) {
    Add-ValidationError "Missing asset meta: $($file.FullName.Substring($repoRoot.Length + 1))"
}

$guidOwners = @{}
$metaFiles = @($assetFiles | Where-Object { $_.Extension -eq ".meta" })
$sourceFiles = @($assetFiles | Where-Object { $_.Extension -ne ".meta" })
foreach ($meta in $metaFiles) {
    $match = [regex]::Match((Get-Content -LiteralPath $meta.FullName -Raw), '(?m)^guid: ([0-9a-f]{32})\s*$')
    if (-not $match.Success) {
        Add-ValidationError "Meta file has no 32-character GUID: $($meta.FullName.Substring($repoRoot.Length + 1))"
        continue
    }

    $guid = $match.Groups[1].Value
    if ($guidOwners.ContainsKey($guid)) {
        Add-ValidationError "Duplicate asset GUID $guid in '$($guidOwners[$guid])' and '$($meta.FullName.Substring($repoRoot.Length + 1))'."
    } else {
        $guidOwners[$guid] = $meta.FullName.Substring($repoRoot.Length + 1)
    }
}

$manifestPath = Join-Path $repoRoot "Packages/manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath)) {
    Add-ValidationError "Missing Packages/manifest.json."
} else {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw
    foreach ($package in @("com.unity.inputsystem", "com.unity.render-pipelines.universal", "com.unity.test-framework")) {
        if (-not $manifest.Contains('"' + $package + '"')) {
            Add-ValidationError "Packages/manifest.json does not include $package."
        }
    }
}

$androidManifestPath = Join-Path $repoRoot "Assets/Plugins/Android/AndroidManifest.xml"
if (-not (Test-Path -LiteralPath $androidManifestPath)) {
    Add-ValidationError "Missing Android manifest additions."
} else {
    $androidManifest = Get-Content -LiteralPath $androidManifestPath -Raw
    if (-not $androidManifest.Contains("android.permission.ACTIVITY_RECOGNITION")) {
        Add-ValidationError "Android manifest does not declare ACTIVITY_RECOGNITION."
    }
    if (-not $androidManifest.Contains("android.hardware.sensor.stepcounter") -or
        -not $androidManifest.Contains('android:required="false"')) {
        Add-ValidationError "Android step-counter feature must remain optional."
    }
    if ($androidManifest -match "android\.permission\.(ACCESS_FINE_LOCATION|ACCESS_COARSE_LOCATION|ACCESS_BACKGROUND_LOCATION)") {
        Add-ValidationError "Android manifest introduces a mandatory location permission."
    }
}

$buildSettingsPath = Join-Path $repoRoot "ProjectSettings/EditorBuildSettings.asset"
if (-not (Test-Path -LiteralPath $buildSettingsPath)) {
    Add-ValidationError "Missing ProjectSettings/EditorBuildSettings.asset."
} else {
    $buildLines = @(Get-Content -LiteralPath $buildSettingsPath)
    $bootstrapEnabled = $false
    for ($index = 0; $index -lt $buildLines.Count; $index++) {
        if ($buildLines[$index].Trim() -ne "path: Assets/WalkGame/Core/Bootstrap.unity") {
            continue
        }

        for ($lookback = [Math]::Max(0, $index - 3); $lookback -lt $index; $lookback++) {
            if ($buildLines[$lookback].Trim() -match "enabled:\s+1$") {
                $bootstrapEnabled = $true
                break
            }
        }
    }

    if (-not $bootstrapEnabled) {
        Add-ValidationError "Bootstrap.unity is not enabled in EditorBuildSettings.asset."
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Host "ERROR: $_" }
    Write-Host "Unity static validation failed with $($errors.Count) error(s)."
    exit 1
}

Write-Host "Unity static validation passed: $($sourceFiles.Count) asset files, $($metaFiles.Count) meta files, Unity 6000.3.4f1 pin, package/manifest invariants, and enabled Bootstrap scene."
exit 0
