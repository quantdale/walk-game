#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Repository-local single-writer lease for quantdale/walk-game (PowerShell twin of
    scripts/writer-lock.sh; identical semantics).

.DESCRIPTION
    One mutable agent session per worktree. The lease lives under .git/ (never
    tracked) and captures repo slug, session id, hostname, PID, branch, start SHA,
    and acquisition time. A second acquire fails immediately (exit 2); stale or
    conflicting locks are NEVER stolen silently — clearing them requires explicit
    -Force from the operator, which is recorded inside the new lease.

.EXAMPLE
    ./scripts/WriterLock.ps1 acquire
    ./scripts/WriterLock.ps1 status
    $env:WRITER_LOCK_SESSION='sess-demo'; ./scripts/WriterLock.ps1 release
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('acquire', 'release', 'status')]
    [string]$Command,

    [switch]$Force,

    [string]$ExpectedSlug = 'quantdale/walk-game'
)

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Host "WRITER LOCK: $Message" -ForegroundColor Red
    exit 1
}

$git = Get-Command git -ErrorAction SilentlyContinue
if (-not $git) { Fail 'git not found on PATH' }
$root = (& git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $root) { Fail 'not inside a git work tree' }
$root = (Resolve-Path $root).Path

$lockDir = Join-Path $root '.git\walk-game-writer.lock'
$lockFile = Join-Path $lockDir 'lock.json'

function Read-Lock {
    if (-not (Test-Path $lockFile)) { return $null }
    try { Get-Content $lockFile -Raw | ConvertFrom-Json } catch { $null }
}

function Show-Lock($lock) {
    Write-Host "  holder session : $($lock.sessionId)"
    Write-Host "  repository     : $($lock.repository)"
    Write-Host "  hostname       : $($lock.hostname)"
    Write-Host "  pid            : $($lock.pid)"
    Write-Host "  branch         : $($lock.branch)"
    Write-Host "  start SHA      : $($lock.startSha)"
    Write-Host "  acquired (utc) : $($lock.acquiredUtc)"
}

if ($Command -eq 'status') {
    $lock = Read-Lock
    if ($null -eq $lock) {
        Write-Host 'no active writer lock'
        exit 0
    }
    Write-Host 'active writer lock:'
    Show-Lock $lock
    if ($lock.acquiredEpoch) {
        $ageH = [int](([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) - ([long]$lock.acquiredEpoch)) / 3600
        $maxH = 24; if ($env:WRITER_LOCK_MAX_AGE_HOURS) { $maxH = [int]$env:WRITER_LOCK_MAX_AGE_HOURS }
        Write-Host "  lock age       : ${ageH}h (advisory stale threshold: ${maxH}h)"
        if ($ageH -ge $maxH) { Write-Host '  assessment     : STALE (recovery still requires explicit -Force)' }
    }
    exit 0
}

$session = $env:WRITER_LOCK_SESSION
if (-not $session) {
    $session = 'sess-{0}-{1}-{2}' -f (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ'), $PID, (Get-Random)
}

$branch = & git rev-parse --abbrev-ref HEAD 2>$null
$startSha = & git rev-parse HEAD 2>$null
if ($LASTEXITCODE -ne 0 -or -not $startSha) { Fail 'repository has no commits yet' }
$nowIso = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$nowEpoch = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$hostName = [System.Net.Dns]::GetHostName()

function New-Lock([string]$ExtraJson) {
    $json = @"
{
  "schemaVersion": 1,
  "repository": "$ExpectedSlug",
  "sessionId": "$session",
  "hostname": "$hostName",
  "pid": "$PID",
  "branch": "$branch",
  "startSha": "$startSha",
  "acquiredUtc": "$nowIso",
  "acquiredEpoch": "$nowEpoch"$ExtraJson
}
"@
    Set-Content -Path $lockFile -Value $json -Encoding utf8NoBOM
    Write-Host "writer lock acquired: session=$session branch=$branch startSha=$startSha"
    exit 0
}

switch ($Command) {
    'acquire' {
        try {
            New-Item -ItemType Directory -Path $lockDir -ErrorAction Stop | Out-Null
            New-Lock ''
        }
        catch [System.IO.IOException] {
            # directory already exists: fall through to conflict handling
        }

        $existing = Read-Lock
        if ($null -eq $existing) {
            Fail 'lock directory exists but unreadable/corrupt; recover manually with -Force after inspection'
        }

        if ($existing.repository -cne $ExpectedSlug) {
            Write-Host "WRITER LOCK: existing lock belongs to a different repository identity ('$($existing.repository)'); refusing."
            Show-Lock $existing
            exit 2
        }

        if ($existing.sessionId -ceq $session) {
            Write-Host "writer lock already held by this session ($session)"
            exit 0
        }

        Write-Host 'WRITER LOCK: another writer holds this worktree; acquire refused.'
        Show-Lock $existing
        if ($existing.acquiredEpoch) {
            $ageH = [int](([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) - ([long]$existing.acquiredEpoch)) / 3600
            $maxH = 24; if ($env:WRITER_LOCK_MAX_AGE_HOURS) { $maxH = [int]$env:WRITER_LOCK_MAX_AGE_HOURS }
            Write-Host "  lock age       : ${ageH}h (advisory stale threshold: ${maxH}h)"
            if ($ageH -ge $maxH) { Write-Host '  assessment     : STALE (recovery still requires explicit -Force)' }
        }
        if ($existing.hostname -cne $hostName) {
            Write-Host "  assessment     : held by another host '$($existing.hostname)' (PID liveness unknowable here)"
        }
        elseif ($existing.pid) {
            $alive = Get-Process -Id ([int]$existing.pid) -ErrorAction SilentlyContinue
            if (-not $alive) { Write-Host "  assessment     : holder PID $($existing.pid) is not alive on this host (STALE candidate)" }
        }
        if ($Force) {
            $prev = "session=$($existing.sessionId) host=$($existing.hostname) pid=$($existing.pid) sha=$($existing.startSha) acquired=$($existing.acquiredUtc)"
            Remove-Item -LiteralPath $lockDir -Recurse -Force
            New-Item -ItemType Directory -Path $lockDir -ErrorAction Stop | Out-Null
            New-Lock ",
  `"forcedOverride`": `"true`",
  `"previousOwner`": `"$prev`""
        }
        Write-Host 'If this lock is genuinely abandoned, recover deliberately:'
        Write-Host '  1. inspect: ./scripts/WriterLock.ps1 status'
        Write-Host '  2. confirm no other live session, then: ./scripts/WriterLock.ps1 acquire -Force'
        exit 2
    }

    'release' {
        $existing = Read-Lock
        if ($null -eq $existing) {
            Write-Host 'no writer lock present; nothing to release'
            exit 0
        }
        if ($existing.sessionId -cne $session -and -not $Force) {
            Write-Host "WRITER LOCK: lock belongs to session '$($existing.sessionId)', not '$session'; refusing (use -Force only as explicit operator recovery)."
            exit 2
        }
        Remove-Item -LiteralPath $lockDir -Recurse -Force
        Write-Host "writer lock released: session=$session"
        exit 0
    }
}
