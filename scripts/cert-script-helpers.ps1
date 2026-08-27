#!/usr/bin/env pwsh
# Shared, engine-free helpers for M8.6 certification scripts.
#
# These functions are intentionally dependency-free so they can be unit-tested
# without Unity, adb or a physical device (see scripts/Test-CertificationScripts.ps1).
# They implement the fail-closed evidence semantics required by the M8.6 spec
# (sections T1/T2 and C1/C2/C3/C4).

$ErrorActionPreference = 'Stop'

# Parse a Unity/NUnit test-result XML file and prove the run completed with zero
# failures. Returns $true only when:
#   * the file exists and is non-empty;
#   * it parses as XML with a recognised root (NUnit3 <test-run> or NUnit2 <test-results>);
#   * it contains at least one test;
#   * zero test cases failed or errored.
# $Summary (a [ref] string) receives a short human reason on failure.
function Test-NUnitResultXml {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [ref]$Summary
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        if ($Summary) { $Summary.Value = 'result file missing' }
        return $false
    }

    [xml]$doc = $null
    try {
        $doc = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop
    }
    catch {
        if ($Summary) { $Summary.Value = 'result file unreadable' }
        return $false
    }

    if ($null -eq $doc -or $null -eq $doc.DocumentElement) {
        if ($Summary) { $Summary.Value = 'result file empty/malformed' }
        return $false
    }

    $root = $doc.DocumentElement
    $total = 0
    $failed = 0

    if ($root.LocalName -eq 'test-run') {
        if (-not [int]::TryParse($root.GetAttribute('total'), [ref]$total)) { $total = 0 }
        if (-not [int]::TryParse($root.GetAttribute('failed'), [ref]$failed)) { $failed = 0 }
        $badNodes = $doc.SelectNodes("//test-case[@result='Failed' or @result='Error']")
        if ($badNodes) { $failed += $badNodes.Count }
    }
    elseif ($root.LocalName -eq 'test-results') {
        if (-not [int]::TryParse($root.GetAttribute('total'), [ref]$total)) { $total = 0 }
        $f = 0; $e = 0
        [int]::TryParse($root.GetAttribute('failures'), [ref]$f) | Out-Null
        [int]::TryParse($root.GetAttribute('errors'), [ref]$e) | Out-Null
        $failed = $f + $e
        $badNodes = $doc.SelectNodes("//test-case[@success='False']")
        if ($badNodes) { $failed += $badNodes.Count }
    }
    else {
        if ($Summary) { $Summary.Value = "unrecognised result format: $($root.LocalName)" }
        return $false
    }

    if ($total -le 0) {
        if ($Summary) { $Summary.Value = 'result contained no tests' }
        return $false
    }

    if ($failed -gt 0) {
        if ($Summary) { $Summary.Value = "$failed failure(s) in $total test(s)" }
        return $false
    }

    if ($Summary) { $Summary.Value = "$total test(s), 0 failures" }
    return $true
}

# Select exactly one authorized/online adb target from `adb devices` output lines.
# With -PreferredSerial, that serial MUST be present and authorized or the call
# throws. Without it, exactly one eligible target is required (fail-closed on
# ambiguity). Offline/unauthorized/unknown states are excluded.
function Select-AndroidTarget {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$DeviceLines,
        [string]$PreferredSerial = ''
    )

    $eligible = @()
    foreach ($line in $DeviceLines) {
        if ($line -match '^\s*(\S+)\s+device\b') {
            $eligible += $Matches[1]
        }
    }

    if ($PreferredSerial) {
        if ($eligible -notcontains $PreferredSerial) {
            $found = ($eligible -join ', ')
            throw "Preferred serial '$PreferredSerial' is not an authorized/online adb target. Eligible: $found"
        }
        return $PreferredSerial
    }

    if ($eligible.Count -eq 0) {
        throw 'No authorized/online adb target found. Connect exactly one device or pass -DeviceSerial.'
    }
    if ($eligible.Count -gt 1) {
        $found = ($eligible -join ', ')
        throw "Multiple eligible adb targets ($found); pass -DeviceSerial to select exactly one."
    }
    return $eligible[0]
}

# Collect identity metadata for a selected adb serial: manufacturer, model,
# Android release/SDK, ABI and whether the genuine step-counter feature exists.
function Get-AndroidDeviceMetadata {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Serial,
        [Parameter(Mandatory = $true)]
        [string]$AdbExe
    )

    $getProp = {
        param([string]$prop)
        $v = (& $AdbExe -s $Serial shell getprop $prop 2>$null) -join "`n"
        return ($v -replace "`r?`n", ' ').Trim()
    }

    $features = (& $AdbExe -s $Serial shell pm list features 2>$null) -join "`n"
    $hasStep = $false
    if ($features -match 'android\.hardware\.sensor\.stepcounter') { $hasStep = $true }

    return [PSCustomObject]@{
        Serial         = $Serial
        Manufacturer   = (& $getProp 'ro.product.manufacturer')
        Model          = (& $getProp 'ro.product.model')
        AndroidRelease = (& $getProp 'ro.build.version.release')
        Sdk            = (& $getProp 'ro.build.version.sdk')
        Abi            = (& $getProp 'ro.product.cpu.abi')
        HasStepCounter = $hasStep
    }
}

# Idempotent Android package uninstall used by the smoke/lifecycle certifier (R6).
# `adb uninstall` returns non-zero when the package is absent, which must NOT be
# treated as a certification failure. This helper treats an absent package as a
# clean success and only rethrows when the package is still present after the
# failed uninstall (a real removal failure) or when adb itself cannot be invoked.
#
# $Adb is a scriptblock taking `string[]` arguments that invokes adb and THROWS
# on a genuine transport/command failure, mirroring Invoke-Adb in the smoke
# script. Returns 'removed' (was installed) or 'absent' (nothing to remove).
function Uninstall-AndroidPackageIdempotent {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackageId,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Adb
    )

    try {
        & $Adb @('uninstall', $PackageId) 2>$null | Out-Null
        return 'removed'
    }
    catch {
        # Distinguish "not installed" from a real removal/transport failure.
        $pkgLines = & $Adb @('shell', 'pm', 'list', 'packages', $PackageId) 2>$null
        $stillInstalled = ($pkgLines | Where-Object { $_ -match "package:$PackageId`$" }).Count -gt 0
        if (-not $stillInstalled) {
            return 'absent'
        }
        throw "adb uninstall failed for $PackageId while the package is still present: $($_.Exception.Message)"
    }
}

# R4: fail-closed Unity toolchain-identity preflight.
# Reads the version pinned in ProjectSettings/ProjectVersion.txt (the repository
# pin) and proves the configured UNITY_EDITOR_PATH exists and resolves to a path
# containing exactly that version token. This is the cheapest fail-closed check
# available without launching the editor; callers needing stronger proof can pass
# an editor-reported version into -EditorVersion.
function Get-UnityPinnedVersion {
    [CmdletBinding()]
    param([string]$ProjectSettingsPath)

    if (-not $ProjectSettingsPath) {
        $repo = Split-Path -Parent $PSScriptRoot
        $ProjectSettingsPath = Join-Path $repo "ProjectSettings/ProjectVersion.txt"
    }
    if (-not (Test-Path -LiteralPath $ProjectSettingsPath)) {
        throw "ProjectVersion.txt not found at '$ProjectSettingsPath'"
    }
    $txt = Get-Content -LiteralPath $ProjectSettingsPath -Raw
    if ($txt -match 'm_EditorVersion:\s*([0-9]+\.[0-9]+\.[0-9]+[a-z0-9]*)') {
        return $Matches[1]
    }
    throw "Could not parse m_EditorVersion from '$ProjectSettingsPath'"
}

# Returns '' when the editor path is present and matches the pinned version, or a
# non-empty reason string describing the mismatch/failure.
function Test-UnityEditorMatchesPin {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$EditorPath,
        [string]$PinnedVersion,
        [string]$EditorVersion = ''
    )
    if (-not $PinnedVersion) { $PinnedVersion = Get-UnityPinnedVersion }
    if (-not (Test-Path -LiteralPath $EditorPath)) {
        return "editor executable not found at '$EditorPath'"
    }
    if ($EditorVersion -and $EditorVersion -ne $PinnedVersion) {
        return "editor reports version '$EditorVersion' but repository pins '$PinnedVersion'"
    }
    $resolved = (Resolve-Path -LiteralPath $EditorPath -ErrorAction SilentlyContinue).Path
    if (-not $resolved) { $resolved = $EditorPath }
    if ($resolved -notmatch [regex]::Escape($PinnedVersion)) {
        return "editor path '$resolved' does not contain pinned version '$PinnedVersion'"
    }
    return ''
}

# Strengthen launch evidence (R17.2.10): process-alive is not gameplay-ready.
# Captures the activity/process state so the certifier can prove the expected
# package/activity is actually foreground/resumed, not merely spawned.
# $Adb is a scriptblock taking string[] args (mirrors Invoke-Adb in the smoke
# script) so it can be mocked engine-free.
function Get-AndroidForegroundActivity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Serial,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Adb
    )
    $state = (& $Adb @('-s', $Serial, 'shell', 'am', 'get-state') 2>$null) -join "`n"
    $focus = (& $Adb @('-s', $Serial, 'shell', 'dumpsys', 'window') 2>$null) -join "`n"
    $m = [regex]::Match($focus, 'mCurrentFocus=([^\s}]+)')
    $focused = if ($m.Success) { $m.Groups[1].Value } else { '' }
    return [PSCustomObject]@{
        State           = ($state -replace "`r?`n", ' ').Trim()
        FocusedActivity = $focused
    }
}

# Compute SHA-256 of an APK (or any file) and return the lowercase hex digest.
function Get-FileSha256 {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash
    return $hash.ToLowerInvariant()
}

# Semantic compile/import log validation (M8.8 H3). A Unity process exit code is
# not sufficient: a stale/partial log or a compiler error must never become a
# green certification result.
function Test-UnityCompileLog {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [ref]$Summary
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        if ($Summary) { $Summary.Value = 'compile log missing' }
        return $false
    }

    try { $content = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop }
    catch {
        if ($Summary) { $Summary.Value = 'compile log unreadable' }
        return $false
    }

    if ([string]::IsNullOrWhiteSpace($content)) {
        if ($Summary) { $Summary.Value = 'compile log empty' }
        return $false
    }

    $errorPatterns = @(
        'error CS\d+',
        '(?i)\bcompiler error\b',
        '(?i)\bfailed to compile\b',
        '(?i)\bcompilation failed\b',
        '(?i)\bscript compilation failed\b',
        '(?i)\bunhandled exception\b',
        '(?i)\bimport(?:ing)? .* failed\b'
    )
    foreach ($pattern in $errorPatterns) {
        if ($content -match $pattern) {
            if ($Summary) { $Summary.Value = "compiler/import error detected ($pattern)" }
            return $false
        }
    }

    # ValidateContent is the semantic completion method used by the wrapper.
    # Keep fixture compatibility with the explicit completion markers used by
    # Unity logs, while requiring a marker that proves the requested operation
    # completed rather than merely that Unity launched.
    $completionPatterns = @(
        '(?i)\[Validate\].*Ashfall Basin OK',
        '(?i)WalkGame.*Validate.*OK',
        '(?i)Compilation succeeded',
        '(?i)Exiting batchmode.*success',
        '(?i)import.*complete'
    )
    foreach ($pattern in $completionPatterns) {
        if ($content -match $pattern) {
            if ($Summary) { $Summary.Value = 'compile log clean with completion marker' }
            return $true
        }
    }

    if ($Summary) { $Summary.Value = 'compile log missing semantic completion marker' }
    return $false
}

# Validate machine-readable semantic compile evidence. The wrapper writes this
# record only after its fresh run; callers still validate every binding before
# accepting it. This helper is intentionally engine-free for false-green tests.
function Test-UnityCompileEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$EvidencePath,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedSha,
        [Parameter(Mandatory = $true)]
        [string]$LogPath,
        [string]$ExpectedPinnedVersion = '',
        [switch]$AllowMutation,
        [ref]$Summary
    )

    if (-not (Test-Path -LiteralPath $EvidencePath)) {
        if ($Summary) { $Summary.Value = 'compile evidence missing' }
        return $false
    }

    try {
        $evidence = Get-Content -LiteralPath $EvidencePath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        if ($Summary) { $Summary.Value = 'compile evidence unreadable/malformed' }
        return $false
    }

    foreach ($field in @('sourceSha', 'preDirty', 'postDirty', 'startUtc', 'endUtc', 'editorPath', 'pinnedVersion', 'exitCode', 'logPath', 'logFresh', 'compilerErrorCount', 'semanticComplete')) {
        if ($null -eq $evidence.$field -or [string]::IsNullOrWhiteSpace([string]$evidence.$field)) {
            if ($Summary) { $Summary.Value = "evidence missing '$field'" }
            return $false
        }
    }

    if ($evidence.sourceSha -ne $ExpectedSha) {
        if ($Summary) { $Summary.Value = "evidence sourceSha mismatch (expected $ExpectedSha, got $($evidence.sourceSha))" }
        return $false
    }
    if ($ExpectedPinnedVersion -and $evidence.pinnedVersion -ne $ExpectedPinnedVersion) {
        if ($Summary) { $Summary.Value = "evidence pinned version mismatch (expected $ExpectedPinnedVersion, got $($evidence.pinnedVersion))" }
        return $false
    }
    if (-not (Test-Path -LiteralPath $LogPath)) {
        if ($Summary) { $Summary.Value = 'referenced compile log missing' }
        return $false
    }
    $expectedLog = [IO.Path]::GetFullPath($LogPath)
    $recordedLog = [IO.Path]::GetFullPath([string]$evidence.logPath)
    if ($recordedLog -ne $expectedLog) {
        if ($Summary) { $Summary.Value = "evidence logPath mismatch (expected $expectedLog, got $recordedLog)" }
        return $false
    }

    try {
        $start = [DateTimeOffset]::Parse([string]$evidence.startUtc)
        $end = [DateTimeOffset]::Parse([string]$evidence.endUtc)
        if ($end -lt $start) {
            if ($Summary) { $Summary.Value = 'evidence end before start' }
            return $false
        }
        if (([DateTimeOffset]::UtcNow - $end).TotalHours -gt 24) {
            if ($Summary) { $Summary.Value = 'evidence stale (>24h)' }
            return $false
        }
    }
    catch {
        if ($Summary) { $Summary.Value = 'evidence timestamp parse failed' }
        return $false
    }

    if ([int]$evidence.exitCode -ne 0) {
        if ($Summary) { $Summary.Value = "Unity exit $($evidence.exitCode)" }
        return $false
    }
    if ([int]$evidence.compilerErrorCount -ne 0) {
        if ($Summary) { $Summary.Value = "compilerErrorCount $($evidence.compilerErrorCount) non-zero" }
        return $false
    }
    if (-not [bool]$evidence.logFresh) {
        if ($Summary) { $Summary.Value = 'compile log is not fresh for this evidence record' }
        return $false
    }
    if ($null -eq $evidence.semanticComplete -or -not [bool]$evidence.semanticComplete) {
        if ($Summary) { $Summary.Value = 'evidence does not prove semantic completion' }
        return $false
    }

    $mutations = @($evidence.mutatedFiles)
    $unexpectedMutations = @($evidence.unexpectedMutationFiles)
    if ($unexpectedMutations.Count -gt 0) {
        if ($Summary) { $Summary.Value = "unexpected mutation recorded ($($unexpectedMutations.Count) file(s))" }
        return $false
    }
    if ($mutations.Count -gt 0 -and -not $AllowMutation) {
        if ($Summary) { $Summary.Value = "unexpected mutation recorded ($($mutations.Count) file(s))" }
        return $false
    }

    $logSummary = ''
    if (-not (Test-UnityCompileLog -Path $LogPath -Summary ([ref]$logSummary))) {
        if ($Summary) { $Summary.Value = "log check failed: $logSummary" }
        return $false
    }
    if ($Summary) { $Summary.Value = 'compile evidence valid' }
    return $true
}

# Only Unity-generated canonical project material may be explicitly accepted as
# a mutation during first import/setup. Runtime/source edits and unexplained
# files remain unexpected even when -AllowMutation is supplied.
function Get-UnityCanonicalMutationPaths {
    return @(
        'Packages/packages-lock.json',
        'ProjectSettings/ProjectSettings.asset',
        'Assets/Settings/URP-HighFidelity.asset',
        'Assets/Settings/URP-HighFidelity.asset.meta'
    )
}

function Test-UnityMutationSet {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$MutatedFiles,
        [switch]$AllowCanonical
    )

    $canonical = @(Get-UnityCanonicalMutationPaths)
    $unexpected = @()
    $acceptedCanonical = @()
    foreach ($statusLine in $MutatedFiles) {
        $line = ([string]$statusLine).TrimEnd()
        $path = if ($line.Length -gt 3) { $line.Substring(3).Trim() } else { $line.Trim() }
        $path = $path -replace '\\', '/'
        $isCanonical = $canonical -contains $path
        if ($isCanonical -and $AllowCanonical) {
            $acceptedCanonical += $statusLine
        }
        else {
            $unexpected += $statusLine
        }
    }
    return [PSCustomObject]@{
        All = @($MutatedFiles)
        Unexpected = $unexpected
        Canonical = $acceptedCanonical
    }
}
