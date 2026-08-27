#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Lost-update protection for quantdale/walk-game (PowerShell twin of
    scripts/check-remote-advance.sh).

.DESCRIPTION
    Fetches the target branch from origin and proves the remote has NOT gained
    commits unreachable from local HEAD since session start. On divergence it
    STOPS with exit 1 — never force-pushes, resets over, discards, or blindly
    overwrites competing work. Never mutates the remote.

.EXAMPLE
    ./scripts/Check-RemoteAdvance.ps1            # current branch vs origin
#>
[CmdletBinding()]
param(
    [string]$Branch,
    [string]$ExpectedSlug = 'quantdale/walk-game'
)

$ErrorActionPreference = 'Continue'

function FailEnv([string]$Message) { Write-Host "REMOTE-ADVANCE GUARD: $Message" -ForegroundColor Red; exit 2 }

$git = Get-Command git -ErrorAction SilentlyContinue
if (-not $git) { FailEnv 'git not found on PATH' }
$root = & git rev-parse --show-toplevel 2>$null
if ($LASTEXITCODE -ne 0 -or -not $root) { FailEnv 'not inside a git work tree' }
$root = (Resolve-Path $root).Path
Set-Location $root

if (-not $Branch) {
    $Branch = & git rev-parse --abbrev-ref HEAD 2>$null
}
if (-not $Branch -or $Branch -eq 'HEAD') { FailEnv 'detached HEAD; pass an explicit -Branch' }
$local = & git rev-parse -q --verify HEAD
if (-not $local) { FailEnv 'no local commit' }

Write-Host "fetching origin/$Branch for race check..."
# M8.7 H5: an absent new branch must be positively distinguished from a
# transport/auth failure. Query the exact ref first; only a confirmed absence
# means "new branch", while a failed query fails closed.
$remoteUrl = (& git remote get-url --push origin) 2>$null
if ($LASTEXITCODE -ne 0 -or -not $remoteUrl) { FailEnv "could not determine origin push transport" }
$remoteUrl = "$remoteUrl".Trim()
if (-not $remoteUrl) { FailEnv 'origin has no usable push transport' }

$probe = (& git ls-remote --heads $remoteUrl "refs/heads/$Branch") 2>$null
if ($LASTEXITCODE -ne 0) { FailEnv "could not query origin for '$Branch' (transport/auth)" }

if (-not $probe) {
    Write-Host "remote-advance OK: origin/$Branch does not exist yet (new branch)"
    exit 0
}

& git fetch --quiet $remoteUrl $Branch
if ($LASTEXITCODE -ne 0) { FailEnv "could not fetch '$Branch' from origin" }

$remote = (& git rev-parse -q --verify 'FETCH_HEAD') 2>$null
if (-not $remote) {
    # ls-remote proved the ref exists, but the fetch produced nothing: treat as
    # an environment failure rather than a missing branch.
    FailEnv "origin/$Branch ref reported present but fetch yielded no commit"
}

& git merge-base --is-ancestor $remote $local 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "remote-advance OK: origin/$Branch ($remote) is contained in local HEAD ($local)"
    exit 0
}

Write-Host "REMOTE-ADVANCE GUARD: origin/$Branch moved during this session with commits not in your HEAD. STOP automatic integration." -ForegroundColor Red
Write-Host ""
Write-Host "Unexpected remote-only commits:"
& git log --oneline --decorate "$local..$remote"
Write-Host ""
Write-Host "Do NOT force-push, reset --hard over, discard, or blindly overwrite this work."
Write-Host "Inspect and reconcile deliberately (merge/rebase after reading the other session's intent),"
Write-Host "then rerun all impacted verification before integrating."
exit 1
