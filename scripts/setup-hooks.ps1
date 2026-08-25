#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Activates the tracked repository hooks (.githooks) for this clone/worktree by
    setting core.hooksPath. Local-only config; run once per clone.
#>
[CmdletBinding()]
param(
    [string]$ExpectedSlug = 'quantdale/walk-game'
)

$ErrorActionPreference = 'Stop'

& git rev-parse --show-toplevel 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'setup-hooks: not inside a git work tree' -ForegroundColor Red; exit 1 }

& git config core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) { Write-Host 'setup-hooks: failed to set core.hooksPath' -ForegroundColor Red; exit 1 }

Write-Host "hooks activated: core.hooksPath=.githooks (identity + pre-push race guards for $ExpectedSlug)"
exit 0
