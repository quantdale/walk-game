#!/usr/bin/env pwsh
# Release hygiene & privacy static audit (M8 campaign sections 20/22).
# Deterministic checks only; runs locally and in CI without an editor license.

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$violations = [System.Collections.Generic.List[string]]::new()

function Add-Violation([string]$message) {
    $script:violations.Add($message)
}

$runtimeRoots = @(
    (Join-Path $repoRoot "Assets\WalkGame\Core"),
    (Join-Path $repoRoot "Assets\WalkGame\App"),
    (Join-Path $repoRoot "Assets\WalkGame\Gameplay"),
    (Join-Path $repoRoot "Assets\WalkGame\Activity"),
    (Join-Path $repoRoot "Assets\WalkGame\Persistence"),
    (Join-Path $repoRoot "Assets\WalkGame\Content"),
    (Join-Path $repoRoot "Assets\WalkGame\World"),
    (Join-Path $repoRoot "Assets\WalkGame\UI"),
    (Join-Path $repoRoot "Assets\WalkGame\Platform")
)

$files = @(Get-ChildItem $runtimeRoots -Recurse -File -Filter *.cs -ErrorAction SilentlyContinue)

# Privacy: movement/location data must never be logged or embedded in messages.
$sensitivePatterns = @(
    @{ Pattern = 'Log\.(Info|Warning|Error|Debug)\([^)]*(latitude|longitude|lat=|lon=|lng=)'; Reason = 'possible GPS coordinate in log output' },
    @{ Pattern = 'Debug\.Log[a-zA-Z]*\([^)]*(latitude|longitude|lat=|lon=|lng=)'; Reason = 'possible GPS coordinate via Debug.Log' },
    @{ Pattern = 'Log\.(Info|Warning|Error|Debug)\([^)]*persistentDataPath'; Reason = 'local save filesystem path in log output' }
)
foreach ($file in $files) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($entry in $sensitivePatterns) {
        if ($text -match $entry.Pattern) {
            Add-Violation "$($file.FullName.Substring($repoRoot.Length + 1)): $($entry.Reason)"
        }
    }
}

# Release hygiene: runtime code must log through the Log wrapper, never Debug.Log directly.
# A trailing 'hygiene-allow:' marker documents the sanctioned exceptions (the engine
# sink itself, pre-host bootstrap failures); anything new needs the same justification.
foreach ($file in $files) {
    $lines = @(Get-Content -LiteralPath $file.FullName)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match 'hygiene-allow') { continue }
        if ($lines[$i] -match '\bDebug\.(Log|LogWarning|LogError|LogAssertion)\s*\(') {
            Add-Violation "$($file.FullName.Substring($repoRoot.Length + 1)):$($i + 1): runtime code must use the Log wrapper, not Debug.Log"
        }
    }
}

# No machine-specific or secret-shaped literals anywhere in source.
foreach ($file in $files) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    if ($text -match '[A-Za-z]:\\Users\\') {
        Add-Violation "$($file.FullName.Substring($repoRoot.Length + 1)): hard-coded Windows user path"
    }
    if ($text -match '(?i)(api[_-]?key|password)\s*=\s*"[^"]+"') {
        Add-Violation "$($file.FullName.Substring($repoRoot.Length + 1)): possible hard-coded secret"
    }
}

# Android manifest stays minimal: no location permissions may appear.
$manifestPath = Join-Path $repoRoot "Assets\Plugins\Android\AndroidManifest.xml"
if (Test-Path -LiteralPath $manifestPath) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw
    if ($manifest -match 'android\.permission\.(ACCESS_FINE_LOCATION|ACCESS_COARSE_LOCATION|ACCESS_BACKGROUND_LOCATION)') {
        Add-Violation "AndroidManifest.xml declares a location permission; passive steps must not require GPS"
    }
    foreach ($allowed in @('ACTIVITY_RECOGNITION', 'INTERNET')) {
        # INTERNET would only be acceptable if intentionally added; flag it so the
        # addition is a documented decision rather than an accident.
    }
    if ($manifest -match 'android\.permission\.INTERNET') {
        Add-Violation "AndroidManifest.xml declares INTERNET; offline-first MVP should not require it"
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "VIOLATION: $_" }
    Write-Host "Release hygiene audit failed with $($violations.Count) violation(s)."
    exit 1
}

Write-Host "Release hygiene audit passed: $($files.Count) runtime sources scanned, manifest minimal."
exit 0
