#!/usr/bin/env pwsh
# Dedicated semantic Unity import/compile verifier (M8.8 H3).
# This is deliberately separate from static, EditMode, PlayMode and build gates:
# a zero process exit is accepted only when a fresh pinned-editor log proves
# semantic completion and the machine-readable evidence binds the run to source.
#
#   $env:UNITY_EDITOR_PATH = "C:\Program Files\Unity\Hub\Editor\6000.3.4f1\Editor\Unity.exe"
#   ./scripts/verify-unity-compile.ps1
#
# TestResults/compile-run.log and compile-evidence.json are ignored artifacts.
# They are preserved on every failure for diagnosis and are never source proof
# unless all fail-closed checks below pass.
[CmdletBinding()]
param(
    [switch]$AllowProjectMutation
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'cert-script-helpers.ps1')

function Get-GitStatusSnapshot {
    $lines = @(& git -C $repoRoot status --porcelain=v1 --untracked-files=all 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git status failed with exit $LASTEXITCODE."
    }

    return @($lines | ForEach-Object {
        $line = ([string]$_).TrimEnd()
        if (-not [string]::IsNullOrWhiteSpace($line)) { $line }
    })
}

function Join-StatusLines([string[]]$Lines) {
    return ($Lines -join [Environment]::NewLine)
}

function Get-NewStatusLines([string[]]$Before, [string[]]$After) {
    $known = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($line in $Before) { [void]$known.Add($line) }
    return @($After | Where-Object { -not $known.Contains($_) })
}

if (-not $env:UNITY_EDITOR_PATH) {
    Write-Error "UNITY_EDITOR_PATH is not set. Point it at Unity 6000.3.4f1."
    exit 1
}

try {
    $unityPin = Get-UnityPinnedVersion
    $unityMismatch = Test-UnityEditorMatchesPin -EditorPath $env:UNITY_EDITOR_PATH -PinnedVersion $unityPin
    if ($unityMismatch) {
        Write-Error "Unity toolchain identity check FAILED: $unityMismatch"
        exit 1
    }

    $sourceSha = (& git -C $repoRoot rev-parse HEAD 2>&1).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceSha)) {
        throw "Could not determine source SHA via git rev-parse HEAD."
    }

    $preStatusLines = @(Get-GitStatusSnapshot)
    $preDirty = $preStatusLines.Count -gt 0

    $resultsDir = Join-Path $repoRoot "TestResults"
    New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
    $logFile = [IO.Path]::GetFullPath((Join-Path $resultsDir "compile-run.log"))
    $evidenceFile = [IO.Path]::GetFullPath((Join-Path $resultsDir "compile-evidence.json"))

    # Evidence from an earlier run is never reused. Removal is checked, not best effort.
    foreach ($artifact in @($logFile, $evidenceFile)) {
        if (Test-Path -LiteralPath $artifact) {
            Remove-Item -LiteralPath $artifact -Force
        }
        if (Test-Path -LiteralPath $artifact) {
            throw "Could not remove stale semantic-compile artifact '$artifact'."
        }
    }

    # A placeholder makes launch failure diagnosable, but semantic completion still
    # requires Unity to overwrite it with a fresh log.
    New-Item -ItemType File -Path $logFile -Force | Out-Null
    $startUtc = [DateTimeOffset]::UtcNow
    $startIso = $startUtc.ToString("o")
    Write-Host "Unity semantic compile: sourceSha=$sourceSha preDirty=$preDirty editor=$env:UNITY_EDITOR_PATH pin=$unityPin start=$startIso"

    $unityArguments = @(
        '-batchmode',
        '-nographics',
        '-quit',
        '-projectPath', $repoRoot,
        '-logFile', $logFile,
        '-executeMethod', 'WalkGame.EditorTools.WalkGameEditorTools.ValidateContent'
    )

    $unityExit = 127
    $launchError = ''
    try {
        $unityProcess = Start-Process -FilePath $env:UNITY_EDITOR_PATH -ArgumentList $unityArguments -WindowStyle Hidden -Wait -PassThru
        $unityExit = $unityProcess.ExitCode
    }
    catch {
        $launchError = $_.Exception.Message
        Write-Host "Failed to launch Unity editor at '$env:UNITY_EDITOR_PATH': $launchError" -ForegroundColor Red
    }

    $endUtc = [DateTimeOffset]::UtcNow
    $endIso = $endUtc.ToString("o")
    $logContent = ''
    if (Test-Path -LiteralPath $logFile) {
        $logContent = Get-Content -LiteralPath $logFile -Raw -ErrorAction SilentlyContinue
        Write-Host "--- Unity log tail (last 100 lines) ---"
        Get-Content -LiteralPath $logFile -Tail 100 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }
        Write-Host "--- end log tail ---"
    }
    else {
        Write-Host "WARNING: Unity log not produced at $logFile"
    }

    $logFresh = $false
    if (Test-Path -LiteralPath $logFile) {
        $logFresh = (Get-Item -LiteralPath $logFile).LastWriteTimeUtc -ge $startUtc.UtcDateTime.AddSeconds(-2)
    }

    $compilerErrorCount = 0
    if (-not [string]::IsNullOrWhiteSpace($logContent)) {
        $compilerErrorCount = ([regex]::Matches(
            $logContent,
            'error CS\d+|(?i)\bcompiler error\b|(?i)\bfailed to compile\b|(?i)\bcompilation failed\b|(?i)\bunhandled exception\b',
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)).Count
    }

    $logSummary = ''
    $semanticComplete = Test-UnityCompileLog -Path $logFile -Summary ([ref]$logSummary)
    $editorReportedVersion = ''
    if (-not [string]::IsNullOrWhiteSpace($logContent)) {
        $versionMatch = [regex]::Match(
            $logContent,
            '(?im)(?:initialize engine version|unity editor version)\s*[:=]?\s*(\d+\.\d+\.\d+[a-z0-9]*)')
        if ($versionMatch.Success) {
            $editorReportedVersion = $versionMatch.Groups[1].Value
        }
    }

    $postStatusLines = @(Get-GitStatusSnapshot)
    $postDirty = $postStatusLines.Count -gt 0
    $mutatedFiles = @(Get-NewStatusLines -Before $preStatusLines -After $postStatusLines)
    $mutation = Test-UnityMutationSet -MutatedFiles $mutatedFiles -AllowCanonical:$AllowProjectMutation
    $canonicalMutationFiles = @($mutation.Canonical)
    $unexpectedMutationFiles = @($mutation.Unexpected)

    $evidence = [ordered]@{
        schemaVersion              = 2
        sourceSha                  = $sourceSha
        preDirty                   = $preDirty
        preStatus                  = Join-StatusLines $preStatusLines
        postDirty                  = $postDirty
        postStatus                 = Join-StatusLines $postStatusLines
        mutatedFiles               = $mutatedFiles
        canonicalMutationFiles     = $canonicalMutationFiles
        unexpectedMutationFiles    = $unexpectedMutationFiles
        allowProjectMutation       = [bool]$AllowProjectMutation
        editorPath                 = [IO.Path]::GetFullPath($env:UNITY_EDITOR_PATH)
        pinnedVersion              = $unityPin
        editorReportedVersion      = $editorReportedVersion
        startUtc                   = $startIso
        endUtc                     = $endIso
        exitCode                   = $unityExit
        launchError                = $launchError
        logPath                    = $logFile
        logFresh                   = $logFresh
        compilerErrorCount         = $compilerErrorCount
        semanticComplete           = $semanticComplete
        semanticSummary            = $logSummary
        evidencePath               = $evidenceFile
    }

    try {
        $evidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $evidenceFile -Encoding UTF8
    }
    catch {
        throw "Could not write semantic compile evidence '$evidenceFile': $($_.Exception.Message)"
    }
    Write-Host "Evidence written: $evidenceFile"

    # --- Fail-closed validation ---

    if ($unityExit -ne 0) {
        Write-Error "Unity semantic compile FAILED: Unity exit $unityExit. See $logFile and $evidenceFile"
        exit 1
    }
    if (-not $logFresh) {
        Write-Error "Unity semantic compile FAILED: log was not freshly written during this run. See $logFile and $evidenceFile"
        exit 1
    }
    if (-not $semanticComplete) {
        Write-Error "Unity semantic compile FAILED: log validation failed ($logSummary). See $logFile and $evidenceFile"
        exit 1
    }
    if ($compilerErrorCount -ne 0) {
        Write-Error "Unity semantic compile FAILED: compilerErrorCount $compilerErrorCount non-zero. See $logFile and $evidenceFile"
        exit 1
    }
    if ($editorReportedVersion -ne $unityPin) {
        Write-Error "Unity semantic compile FAILED: log reported editor '$editorReportedVersion', expected '$unityPin'. See $logFile and $evidenceFile"
        exit 1
    }

    $evSummary = ''
    $evidenceOk = Test-UnityCompileEvidence -EvidencePath $evidenceFile -ExpectedSha $sourceSha -LogPath $logFile -ExpectedPinnedVersion $unityPin -AllowMutation:$AllowProjectMutation -Summary ([ref]$evSummary)
    if (-not $evidenceOk) {
        Write-Error "Unity semantic compile FAILED: evidence invalid ($evSummary). See $logFile and $evidenceFile"
        exit 1
    }

    if ($unexpectedMutationFiles.Count -gt 0) {
        Write-Error ("Unity semantic compile FAILED: unexpected project mutation detected:" +
            [Environment]::NewLine + ($unexpectedMutationFiles -join [Environment]::NewLine) +
            [Environment]::NewLine + "Pre status was:" + [Environment]::NewLine +
            (Join-StatusLines $preStatusLines) +
            [Environment]::NewLine +
            "Canonical first-import materialization may be accepted only with -AllowProjectMutation and only for the documented canonical paths.")
        exit 1
    }

    Write-Host "Unity semantic compile PASSED (sourceSha=$sourceSha editor=$editorReportedVersion log=$logSummary evidence=$evSummary). Log: $logFile Evidence: $evidenceFile"
    exit 0
}
catch {
    Write-Error "Unity semantic compile failed closed: $($_.Exception.Message)"
    exit 1
}
