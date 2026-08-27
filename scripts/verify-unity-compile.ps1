#!/usr/bin/env pwsh
# Dedicated semantic Unity import/compile verifier (M8.8 H3).
# Fail-closed gate that proves the Editor assembly compiles under the pinned
# Unity 6000.3.4f1 without conflating with EditMode/PlayMode test execution.
#
#   $env:UNITY_EDITOR_PATH = "C:\Program Files\Unity\Hub\Editor\6000.3.4f1\Editor\Unity.exe"
#   ./scripts/verify-unity-compile.ps1
#
# Produces fresh evidence in TestResults/compile-run.log and
# TestResults/compile-evidence.json bound to current source SHA/dirty state.
# Preserves full log on failure and rejects stale/missing/compiler-error evidence.
[CmdletBinding()]
param(
    [switch]$AllowProjectMutation
)
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'cert-script-helpers.ps1')

if (-not $env:UNITY_EDITOR_PATH) {
    Write-Error "UNITY_EDITOR_PATH is not set. Point it at Unity 6000.3.4f1."
    exit 1
}

# R4: pinned toolchain identity preflight
$unityPin = Get-UnityPinnedVersion
$unityMismatch = Test-UnityEditorMatchesPin -EditorPath $env:UNITY_EDITOR_PATH -PinnedVersion $unityPin
if ($unityMismatch) {
    Write-Error "Unity toolchain identity check FAILED: $unityMismatch"
    exit 1
}

# Source binding
$sourceSha = $null
try { $sourceSha = (& git -C $repoRoot rev-parse HEAD 2>$null).Trim() } catch { }
if (-not $sourceSha) {
    Write-Error "Could not determine source SHA via git rev-parse HEAD."
    exit 1
}
$preStatus = $null
try { $preStatus = (& git -C $repoRoot status --porcelain --untracked-files=normal 2>$null) -join "`n" } catch { $preStatus = "" }
$preDirty = -not [string]::IsNullOrWhiteSpace($preStatus)
$preStatusLines = @($preStatus -split "`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

$resultsDir = Join-Path $repoRoot "TestResults"
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
$logFile = Join-Path $resultsDir "compile-run.log"
$evidenceFile = Join-Path $resultsDir "compile-evidence.json"
foreach ($artifact in @($logFile, $evidenceFile)) {
    if (Test-Path -LiteralPath $artifact) {
        Remove-Item -LiteralPath $artifact -Force
    }
}
# Ensure log file exists placeholder so timestamp is deterministic
New-Item -ItemType File -Path $logFile -Force | Out-Null

$startUtc = [DateTimeOffset]::UtcNow
$startIso = $startUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
Write-Host "Unity semantic compile: sourceSha=$sourceSha preDirty=$preDirty editor=$env:UNITY_EDITOR_PATH pin=$unityPin start=$startIso"

$unityArguments = @(
    '-batchmode',
    '-nographics',
    '-quit',
    '-projectPath', $repoRoot,
    '-logFile', $logFile,
    '-executeMethod', 'WalkGame.EditorTools.WalkGameEditorTools.ValidateContent'
)

$unityProcess = $null
$unityExit = -1
try {
    $unityProcess = Start-Process -FilePath $env:UNITY_EDITOR_PATH -ArgumentList $unityArguments -WindowStyle Hidden -Wait -PassThru
    $unityExit = $unityProcess.ExitCode
} catch {
    Write-Error "Failed to launch Unity editor at '$env:UNITY_EDITOR_PATH': $_"
    $unityExit = 127
}

$endUtc = [DateTimeOffset]::UtcNow
$endIso = $endUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")

if (Test-Path $logFile) {
    Write-Host "--- Unity log tail (last 80 lines) ---"
    Get-Content $logFile -Tail 80 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }
    Write-Host "--- end log tail ---"
} else {
    Write-Host "WARNING: Unity log not produced at $logFile"
}

# Compute compiler error count from log (defense in depth)
$compilerErrorCount = 0
$postStatus = ""
$postDirty = $false
$mutatedFiles = @()
if (Test-Path $logFile) {
    $logContent = Get-Content -LiteralPath $logFile -Raw -ErrorAction SilentlyContinue
    if ($logContent) {
        $matches = [regex]::Matches($logContent, 'error CS\d+', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        $compilerErrorCount = $matches.Count
        # Also count generic compiler error markers if no CS count but still error-like
        if ($compilerErrorCount -eq 0 -and $logContent -match 'Compiler Error') { $compilerErrorCount = 1 }
        if ($compilerErrorCount -eq 0 -and $logContent -match 'Failed to compile') { $compilerErrorCount = 1 }
    }
    # Stale check: log must have been written during this run
    $logWrite = (Get-Item -LiteralPath $logFile).LastWriteTimeUtc
    if ($logWrite -lt $startUtc.UtcDateTime.AddSeconds(-5)) {
        Write-Host "WARNING: log LastWriteTime $logWrite is before start $startUtc (stale evidence?)"
    }
}

try { $postStatus = (& git -C $repoRoot status --porcelain --untracked-files=normal 2>$null) -join "`n" } catch { $postStatus = "" }
$postDirty = -not [string]::IsNullOrWhiteSpace($postStatus)
$postStatusLines = @($postStatus -split "`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
# Compute delta for mutation reporting
$preSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$preStatusLines)
$mutatedFiles = @($postStatusLines | Where-Object { -not $preSet.Contains($_) })

$evidence = [ordered]@{
    schemaVersion      = 1
    sourceSha          = $sourceSha
    preDirty           = $preDirty
    preStatus          = $preStatus
    postDirty          = $postDirty
    postStatus         = $postStatus
    mutatedFiles       = $mutatedFiles
    editorPath         = $env:UNITY_EDITOR_PATH
    pinnedVersion      = $unityPin
    startUtc           = $startIso
    endUtc             = $endIso
    exitCode           = $unityExit
    logPath            = $logFile
    compilerErrorCount = $compilerErrorCount
    evidencePath       = $evidenceFile
}
$evidenceJson = $evidence | ConvertTo-Json -Depth 4
Set-Content -LiteralPath $evidenceFile -Value $evidenceJson -Encoding UTF8
Write-Host "Evidence written: $evidenceFile"

# --- Fail-closed validation ---

if ($unityExit -ne 0) {
    Write-Error "Unity semantic compile FAILED: Unity exit $unityExit. See $logFile and $evidenceFile"
    exit $unityExit
}

if (-not (Test-Path -LiteralPath $logFile)) {
    Write-Error "Unity semantic compile FAILED: log missing $logFile"
    exit 1
}

$logSummary = ''
if (-not (Test-UnityCompileLog -Path $logFile -Summary ([ref]$logSummary))) {
    Write-Error "Unity semantic compile FAILED: log validation failed ($logSummary). See $logFile"
    exit 1
}

if ($compilerErrorCount -ne 0) {
    Write-Error "Unity semantic compile FAILED: compilerErrorCount $compilerErrorCount non-zero. See $logFile"
    exit 1
}

$evSummary = ''
if (-not (Test-UnityCompileEvidence -EvidencePath $evidenceFile -ExpectedSha $sourceSha -LogPath $logFile -Summary ([ref]$evSummary))) {
    Write-Error "Unity semantic compile FAILED: evidence invalid ($evSummary). See $evidenceFile"
    exit 1
}

if ($mutatedFiles.Count -gt 0 -and -not $AllowProjectMutation) {
    Write-Error "Unity semantic compile FAILED: unexpected project mutation detected:`n$($mutatedFiles -join "`n")`nPre status was:`n$preStatus`nIf this is intentional canonical materialization, rerun with -AllowProjectMutation and commit the stable diff."
    exit 1
}

Write-Host "Unity semantic compile PASSED (sourceSha=$sourceSha logClean=$logSummary evidence=$evSummary). Log: $logFile Evidence: $evidenceFile"
exit 0
