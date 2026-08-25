#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fail-closed repository identity guard for quantdale/walk-game.

.DESCRIPTION
    Validates, in order: git work tree root, .repo-identity.json contents,
    normalized origin remote (HTTPS/SSH), repository-specific fingerprints,
    and GITHUB_REPOSITORY when running in CI. Any mismatch terminates with a
    non-zero exit before the caller performs further work. This project is NOT
    quantdale/simple-walk-game; see AGENTS.md REPOSITORY IDENTITY.

.EXAMPLE
    ./scripts/Assert-RepoIdentity.ps1

.EXAMPLE
    ./scripts/Assert-RepoIdentity.ps1 -CiMode   # require + enforce GITHUB_REPOSITORY
#>
[CmdletBinding()]
param(
    [switch]$CiMode,
    [string]$ExpectedSlug = 'quantdale/walk-game'
)

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Host "REPO IDENTITY GUARD: $Message" -ForegroundColor Red
    exit 1
}

$git = Get-Command git -ErrorAction SilentlyContinue
if (-not $git) { Fail 'git not found on PATH' }

$root = & git rev-parse --show-toplevel 2>$null
if ($LASTEXITCODE -ne 0 -or -not $root) { Fail 'not inside a git work tree' }
$root = (Resolve-Path $root).Path
Set-Location $root

$idPath = Join-Path $root '.repo-identity.json'
if (-not (Test-Path $idPath)) { Fail '.repo-identity.json missing' }
try {
    $identity = Get-Content $idPath -Raw | ConvertFrom-Json
}
catch {
    Fail "identity file unreadable: $_"
}
if ([int]$identity.schemaVersion -ne 1) { Fail "unsupported identity schemaVersion '$($identity.schemaVersion)'" }
if ($identity.repository -cne $ExpectedSlug) {
    Fail "identity file repository '$($identity.repository)' != '$ExpectedSlug' (this project is NOT quantdale/simple-walk-game)"
}
if ($identity.project -cne 'Walk Game') { Fail "identity project '$($identity.project)' != 'Walk Game'" }

$url = & git remote get-url origin 2>$null
if ($LASTEXITCODE -ne 0 -or -not $url) { $url = & git config --get remote.origin.url 2>$null }
if ($LASTEXITCODE -ne 0 -or -not $url) { Fail 'origin remote is not configured' }
$url = "$url".Trim()

$slug = switch -Regex ($url) {
    '^git@github\.com:(?<s>.+/.+)$' { $Matches.s; break }
    '^ssh://git@github\.com/(?<s>.+)$' { $Matches.s; break }
    '^https://github\.com/(?<s>.+)$' { $Matches.s; break }
    '^http://github\.com/(?<s>.+)$' { $Matches.s; break }
    default { Fail "origin '$url' is not a recognized github.com remote for $ExpectedSlug" }
}
$slug = $slug -replace '\.git$', ''
if ($slug -cne $ExpectedSlug) { Fail "origin '$url' resolves to '$slug', expected '$ExpectedSlug'" }

$fingerprints = @(
    'Assets/WalkGame/App/GameHost.cs',
    'verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj',
    'docs/IMPLEMENTATION_STATUS.md',
    'docs/MASTER_PLAN.md',
    'AGENTS.md',
    'ProjectSettings/ProjectVersion.txt'
)
foreach ($f in $fingerprints) {
    if (-not (Test-Path (Join-Path $root $f))) { Fail "repository fingerprint missing: $f" }
}
$pin = Get-Content (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Raw
if ($pin -notmatch 'm_EditorVersion:\s*6000\.3') { Fail 'editor pin fingerprint mismatch in ProjectSettings/ProjectVersion.txt' }

$ghRepo = $env:GITHUB_REPOSITORY
if ($CiMode -and -not $ghRepo) { Fail '-CiMode: GITHUB_REPOSITORY is not set' }
if ($ghRepo -and $ghRepo -cne $ExpectedSlug) { Fail "GITHUB_REPOSITORY '$ghRepo' != '$ExpectedSlug'" }

Write-Host "repo identity OK: $ExpectedSlug (Walk Game)"
exit 0
