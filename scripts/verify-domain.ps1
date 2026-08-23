#!/usr/bin/env pwsh
# Canonical domain verification gate (ADR 0001). Engine-free sources compiled
# outside Unity and executed under NUnit; exits non-zero on any failure so it
# is directly usable from CI and local pre-commit checks.
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

dotnet test (Join-Path $repoRoot "verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj") --logger "console;verbosity=normal"
exit $LASTEXITCODE
