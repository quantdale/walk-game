#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Deterministic verification of the quantdale/walk-game agent guards
    (identity, single-writer lock, lost-update protection). Runs each scenario
    against BOTH implementations: PowerShell (.ps1) and POSIX sh (via bash).

.SAFETY
    Every fixture remote is a local filesystem path under a temporary directory.
    The suite never contacts github.com; scenario S12 asserts structurally that
    no fixture remote points outside the sandbox.

.EXAMPLE
    ./scripts/Test-AgentGuards.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# Egress kill-switch: restrict git transports to local files for this process so
# any accidental contact with github.com fails loudly instead of silently
# touching the real remotes (scenario S12 asserts this stays in force).
$env:GIT_ALLOW_PROTOCOL = 'file'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ExpectedSlug = 'quantdale/walk-game'
$SiblingSlug = 'quantdale/simple-walk-game'
$ZeroOid = ('0' * 40)

$script:PassCount = 0
$script:FailCount = 0

function Record([string]$Name, [bool]$Ok, [string]$Detail = '') {
    if ($Ok) {
        $script:PassCount++
        Write-Host ("PASS  {0}" -f $Name) -ForegroundColor Green
    }
    else {
        $script:FailCount++
        Write-Host ("FAIL  {0} {1}" -f $Name, $Detail) -ForegroundColor Red
    }
}

$Tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("walk-game-guard-tests-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $Tmp | Out-Null

$PwshExe = (Get-Process -Id $PID).Path
if (-not $PwshExe) { $PwshExe = 'pwsh' }
$BashExe = (Get-Command bash -ErrorAction SilentlyContinue).Source

$FingerprintFiles = @(
    'Assets/WalkGame/App/GameHost.cs',
    'verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj',
    'docs/IMPLEMENTATION_STATUS.md',
    'docs/MASTER_PLAN.md',
    'AGENTS.md'
)

function New-Fixture {
    param(
        [string]$Name,
        [string]$OriginUrl = 'https://github.com/quantdale/walk-game.git',
        [string]$IdentitySlug = $ExpectedSlug,
        [bool]$CopyShScripts = $false
    )
    $dir = Join-Path $Tmp $Name
    New-Item -ItemType Directory -Path $dir | Out-Null
    git init -q -b main $dir
    foreach ($f in ($FingerprintFiles + 'ProjectSettings/ProjectVersion.txt')) {
        $p = Join-Path $dir $f
        New-Item -ItemType Directory -Path (Split-Path $p -Parent) -Force | Out-Null
        Set-Content -Path $p -Value 'fixture fingerprint' -Encoding utf8NoBOM
    }
    Set-Content -Path (Join-Path $dir 'ProjectSettings/ProjectVersion.txt') `
        -Value 'm_EditorVersion: 6000.3.4f1' -Encoding utf8NoBOM
    $idJson = "{`n  `"schemaVersion`": 1,`n  `"repository`": `"$IdentitySlug`",`n  `"project`": `"Walk Game`"`n}"
    Set-Content -Path (Join-Path $dir '.repo-identity.json') -Value $idJson -Encoding utf8NoBOM
    if ($CopyShScripts) {
        New-Item -ItemType Directory -Path (Join-Path $dir 'scripts') -Force | Out-Null
        foreach ($s in @('assert-repo-identity.sh', 'writer-lock.sh', 'check-remote-advance.sh')) {
            Copy-Item (Join-Path $RepoRoot "scripts/$s") (Join-Path $dir "scripts/$s")
        }
        Copy-Item (Join-Path $RepoRoot '.githooks') (Join-Path $dir '.githooks') -Recurse
    }
    git -C $dir remote add origin $OriginUrl
    git -C $dir -c user.email=guard@fixture.local -c user.name=guard commit -q --allow-empty -m 'fixture'
    return $dir
}

# Runs scripts/Assert-RepoIdentity.ps1 or .sh with cwd inside $Dir. Returns exit code.
# $ScriptDir locates the sh script when cwd is a subdirectory of the fixture.
function Run-Guard {
    param([string]$Impl, [string]$Dir, [string[]]$GuardArgs = @(), [string]$ScriptDir = $null)
    Push-Location $Dir
    try {
        if ($Impl -eq 'ps1') {
            & $PwshExe -NoProfile -File "$RepoRoot/scripts/Assert-RepoIdentity.ps1" @GuardArgs *> $null
        }
        else {
            $shDir = if ($ScriptDir) { $ScriptDir } else { $Dir }
            & $BashExe (Join-Path $shDir 'scripts/assert-repo-identity.sh') @GuardArgs *> $null
        }
        return $LASTEXITCODE
    }
    finally { Pop-Location }
}

# Runs WriterLock acquire/release with an explicit session id. Returns exit code.
function Run-Lock {
    param([string]$Impl, [string]$Dir, [string]$Session, [switch]$Force, [switch]$Release)
    Push-Location $Dir
    try {
        $old = $env:WRITER_LOCK_SESSION
        $env:WRITER_LOCK_SESSION = $Session
        try {
            if ($Impl -eq 'ps1') {
                $lockArgs = @('release')
                if (-not $Release) { $lockArgs = @('acquire') }
                if ($Force) { $lockArgs += '-Force' }
                & $PwshExe -NoProfile -File "$RepoRoot/scripts/WriterLock.ps1" @lockArgs *> $null
            }
            else {
                $lockArgs = @('release')
                if (-not $Release) { $lockArgs = @('acquire') }
                if ($Force) { $lockArgs += '--force' }
                & $BashExe './scripts/writer-lock.sh' @lockArgs *> $null
            }
            return $LASTEXITCODE
        }
        finally {
            if ($null -ne $old) { $env:WRITER_LOCK_SESSION = $old }
            else { Remove-Item Env:\WRITER_LOCK_SESSION -ErrorAction SilentlyContinue }
        }
    }
    finally { Pop-Location }
}

# Runs Check-RemoteAdvance (.ps1/.sh) with cwd inside $Dir. Returns exit code.
function Run-RaceCheck {
    param([string]$Impl, [string]$Dir)
    Push-Location $Dir
    try {
        if ($Impl -eq 'ps1') {
            & $PwshExe -NoProfile -File "$RepoRoot/scripts/Check-RemoteAdvance.ps1" *> $null
        }
        else {
            # $Dir may be a plain clone without copied scripts; use the canonical script.
            & $BashExe "$RepoRoot/scripts/check-remote-advance.sh" *> $null
        }
        return $LASTEXITCODE
    }
    finally { Pop-Location }
}

$HaveBash = [bool]$BashExe
if (-not $HaveBash) {
    Write-Host 'NOTE: bash not found on PATH; POSIX sh matrices will be skipped.' -ForegroundColor Yellow
}

# --- S1-S7: identity guard ----------------------------------------------------
foreach ($impl in @('ps1', 'sh')) {
    if ($impl -eq 'sh' -and -not $HaveBash) { continue }
    $copySh = ($impl -eq 'sh')
    $tag = "[$impl]"

    $fx = New-Fixture "s1-$impl" -CopyShScripts:$copySh
    Record "$tag S1 walk-game identity passes" ((Run-Guard $impl $fx) -eq 0)

    $fx = New-Fixture "s2-$impl" -IdentitySlug $SiblingSlug -CopyShScripts:$copySh
    Record "$tag S2 simple-walk-game identity fails" ((Run-Guard $impl $fx) -ne 0)

    $fx = New-Fixture "s3-$impl" -OriginUrl "https://github.com/$SiblingSlug.git" -CopyShScripts:$copySh
    Record "$tag S3 wrong origin fails" ((Run-Guard $impl $fx) -ne 0)

    $fx = New-Fixture "s4-$impl" -CopyShScripts:$copySh
    $ciArgs = if ($impl -eq 'ps1') { , @('-CiMode') } else { , @('--ci-mode') }
    $prevGh = $env:GITHUB_REPOSITORY
    try {
        $env:GITHUB_REPOSITORY = $SiblingSlug
        $badCode = Run-Guard $impl $fx $ciArgs
        $env:GITHUB_REPOSITORY = $ExpectedSlug
        $goodCode = Run-Guard $impl $fx $ciArgs
    }
    finally {
        if ($null -ne $prevGh) { $env:GITHUB_REPOSITORY = $prevGh }
        else { Remove-Item Env:\GITHUB_REPOSITORY -ErrorAction SilentlyContinue }
    }
    Record "$tag S4 wrong GITHUB_REPOSITORY fails" ($badCode -eq 1) "exit=$badCode"
    Record "$tag S4b correct GITHUB_REPOSITORY passes" ($goodCode -eq 0) "exit=$goodCode"

    $fx = New-Fixture "s5-$impl" -CopyShScripts:$copySh
    $nested = Join-Path $fx 'Assets/WalkGame/App'
    New-Item -ItemType Directory -Path $nested -Force | Out-Null
    Record "$tag S5 nested invocation finds root" ((Run-Guard $impl $nested -ScriptDir $fx) -eq 0)

    $fx = New-Fixture "s6-$impl" -OriginUrl 'https://github.com/quantdale/walk-game' -CopyShScripts:$copySh
    Record "$tag S6 HTTPS remote without .git suffix passes" ((Run-Guard $impl $fx) -eq 0)

    $fx = New-Fixture "s7-$impl" -OriginUrl 'git@github.com:quantdale/walk-game.git' -CopyShScripts:$copySh
    Record "$tag S7 SSH remote passes" ((Run-Guard $impl $fx) -eq 0)
}

# --- S8-S10: single-writer lock -------------------------------------------------
foreach ($impl in @('ps1', 'sh')) {
    if ($impl -eq 'sh' -and -not $HaveBash) { continue }
    $tag = "[$impl]"
    $fx = New-Fixture "lock-$impl" -CopyShScripts:($impl -eq 'sh')

    Record "$tag S8a first lock acquires" ((Run-Lock $impl $fx 'sess-A') -eq 0)
    Record "$tag S8b concurrent acquisition refused" ((Run-Lock $impl $fx 'sess-B') -ne 0)
    Record "$tag S9a holder releases cleanly" ((Run-Lock $impl $fx 'sess-A' -Release) -eq 0)
    Record "$tag S9b released lock permits another session" ((Run-Lock $impl $fx 'sess-B') -eq 0)

    $lockFile = Join-Path $fx '.git/walk-game-writer.lock/lock.json'
    $raw = Get-Content $lockFile -Raw
    $staleEpoch = ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) - (72 * 3600)
    Set-Content -Path $lockFile -Value ($raw -replace '"acquiredEpoch": "\d+"', ('"acquiredEpoch": "' + $staleEpoch + '"')) -Encoding utf8NoBOM

    $stealCode = Run-Lock $impl $fx 'sess-C'
    $forceCode = Run-Lock $impl $fx 'sess-C' -Force
    $forcedJson = Get-Content $lockFile -Raw
    Record "$tag S10 stale lock refused without explicit force" ($stealCode -ne 0) "exit=$stealCode"
    Record "$tag S10b force recovery records override provenance" (($forceCode -eq 0) -and $forcedJson.Contains('forcedOverride')) "exit=$forceCode"
}

# --- S11-S12: lost-update race detection against LOCAL bare origins -------------
if (-not $HaveBash) {
    Write-Host 'SKIP  S11/S12 race + sandbox checks (bash unavailable)' -ForegroundColor Yellow
}
else {
    $originPath = Join-Path $Tmp 'origin-bare.git'
    git init -q --bare $originPath
    # Pin the bare repo's HEAD to main so clones start on main (default would
    # otherwise fall back to the local init.defaultBranch, e.g. master).
    git -C $originPath symbolic-ref HEAD refs/heads/main
    $seed = Join-Path $Tmp 'seed'
    New-Item -ItemType Directory -Path $seed | Out-Null
    git init -q -b main $seed
    Set-Content -Path (Join-Path $seed 'a.txt') -Value 'seed' -Encoding utf8NoBOM
    git -C $seed add a.txt
    git -C $seed -c user.email=guard@fixture.local -c user.name=guard commit -q -m seed
    git -C $seed push -q $originPath main

    $work = Join-Path $Tmp 'work'
    git clone -q $originPath $work
    git -C $work checkout -q -B main
    Set-Content -Path (Join-Path $work 'b.txt') -Value 'local work' -Encoding utf8NoBOM
    git -C $work add b.txt
    git -C $work -c user.email=guard@fixture.local -c user.name=guard commit -q -m 'local session work'
    $localOid = (git -C $work rev-parse HEAD).Trim()

    Record '[ps1] S11 positive control: contained remote passes' ((Run-RaceCheck 'ps1' $work) -eq 0)
    Record '[sh]  S11 positive control: contained remote passes' ((Run-RaceCheck 'sh' $work) -eq 0)

    # simulate the competing session landing on origin first
    $other = Join-Path $Tmp 'other'
    git clone -q $originPath $other
    git -C $other checkout -q -B main
    Set-Content -Path (Join-Path $other 'c.txt') -Value 'competing session work' -Encoding utf8NoBOM
    git -C $other add c.txt
    git -C $other -c user.email=guard@fixture.local -c user.name=guard commit -q -m 'competing session landed first'
    git -C $other push -q origin main

    Record '[ps1] S11b unexpected advancement detected' ((Run-RaceCheck 'ps1' $work) -eq 1)
    Record '[sh]  S11b unexpected advancement detected' ((Run-RaceCheck 'sh' $work) -eq 1)

    # exercise the real pre-push hook with synthetic stdin (must refuse both cases)
    git -C $work fetch -q origin
    $remoteOid = (git -C $work rev-parse origin/main).Trim()
    Push-Location $work
    try {
        ("refs/heads/main {0} refs/heads/main {1}" -f $localOid, $remoteOid) |
            & $BashExe "$RepoRoot/.githooks/pre-push" *> $null
        $hookBlocked = ($LASTEXITCODE -eq 1)

        ("refs/heads/main {0} refs/heads/main {1}" -f $ZeroOid, $remoteOid) |
            & $BashExe "$RepoRoot/.githooks/pre-push" *> $null
        $deleteBlocked = ($LASTEXITCODE -eq 1)
    }
    finally { Pop-Location }
    Record '[hook] S11c pre-push refuses force-shaped push' $hookBlocked
    Record '[hook] S11d pre-push refuses remote deletion' $deleteBlocked

    # S12: structural proof that nothing left the sandbox. GIT_ALLOW_PROTOCOL=file
    # (set at suite start) makes any non-file transport fail loudly, so a passing
    # run already proves no real remote was contacted; additionally assert that
    # every configured remote is either inside the sandbox or one of the exact
    # inert identity strings used as configuration data only.
    $inertUrls = @(
        'https://github.com/quantdale/walk-game.git',
        'https://github.com/quantdale/walk-game',
        'git@github.com:quantdale/walk-game.git',
        "https://github.com/$SiblingSlug.git"
    )
    $remoteUrls = @(Get-ChildItem -Path $Tmp -Recurse -Force -Directory -Filter '.git' | ForEach-Object {
            $repo = Split-Path $_.FullName -Parent
            git -C $repo config --get remote.origin.url 2>$null
        } | Where-Object { $_ })
    $outside = @($remoteUrls | Where-Object {
            -not ($_.StartsWith($Tmp) -or ($inertUrls -ccontains $_))
        })
    Record 'S12 egress restricted to file transport' ($env:GIT_ALLOW_PROTOCOL -eq 'file')
    Record 'S12b every fixture remote stayed in the sandbox (or inert identity string)' (($remoteUrls.Count -gt 0) -and ($outside.Count -eq 0)) ("outside=" + ($outside -join ','))
}

Write-Host ''
Write-Host ("Guard suites complete: {0} passed, {1} failed." -f $script:PassCount, $script:FailCount)

try { Remove-Item -LiteralPath $Tmp -Recurse -Force -ErrorAction SilentlyContinue } catch {}

if ($script:FailCount -gt 0) { exit 1 }
exit 0
