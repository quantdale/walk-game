#!/usr/bin/env pwsh
# Deterministic, engine-free regression tests for the M8.6 certification helpers
# (scripts/cert-script-helpers.ps1). These validate fail-closed evidence semantics
# WITHOUT invoking Unity, adb or a physical device.
#
#   ./scripts/Test-CertificationScripts.ps1
#
# Style mirrors scripts/Test-AgentGuards.ps1 (Record/PassCount/FailCount).

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'cert-script-helpers.ps1')

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

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("walk-game-cert-tests-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

function New-TempFile([string]$Name, [string]$Content) {
    $p = Join-Path $tmp $Name
    Set-Content -Path $p -Value $Content -NoNewline
    return $p
}

# --- Test-NUnitResultXml -------------------------------------------------

$passNunit3 = New-TempFile "pass3.xml" @'
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<test-run id="2" name="WalkGame" total="5" passed="5" failed="0" skipped="0" inconclusive="0">
  <test-case name="A" result="Passed" />
  <test-case name="B" result="Passed" />
</test-run>
'@

$failNunit3 = New-TempFile "fail3.xml" @'
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<test-run id="2" name="WalkGame" total="3" passed="2" failed="1">
  <test-case name="A" result="Passed" />
  <test-case name="B" result="Failed" />
</test-run>
'@

$passNunit2 = New-TempFile "pass2.xml" @'
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<test-results total="4" errors="0" failures="0">
  <test-case name="A" success="True" />
  <test-case name="B" success="True" />
</test-results>
'@

$failNunit2 = New-TempFile "fail2.xml" @'
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<test-results total="2" errors="0" failures="1">
  <test-case name="A" success="True" />
  <test-case name="B" success="False" />
</test-results>
'@

$noTests = New-TempFile "notests.xml" @'
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<test-run id="2" name="WalkGame" total="0" passed="0" failed="0" />
'@

$garbage = New-TempFile "garbage.xml" "this is not xml <<<"
$missingName = "does-not-exist.xml"

$summary = ''
Record "NUnit3-zero-failures passes" (Test-NUnitResultXml -Path $passNunit3 -Summary ([ref]$summary)) $summary
Record "NUnit3-with-failure fails" (-not (Test-NUnitResultXml -Path $failNunit3 -Summary ([ref]$summary))) $summary
Record "NUnit2-zero-failures passes" (Test-NUnitResultXml -Path $passNunit2 -Summary ([ref]$summary)) $summary
Record "NUnit2-with-failure fails" (-not (Test-NUnitResultXml -Path $failNunit2 -Summary ([ref]$summary))) $summary
Record "NUnit-no-tests fails" (-not (Test-NUnitResultXml -Path $noTests -Summary ([ref]$summary))) $summary
Record "NUnit-malformed fails" (-not (Test-NUnitResultXml -Path $garbage -Summary ([ref]$summary))) $summary
Record "NUnit-missing-file fails" (-not (Test-NUnitResultXml -Path $missingName -Summary ([ref]$summary))) $summary

# --- Select-AndroidTarget ------------------------------------------------

$oneDevice = @("List of devices attached", "R3CN12345	device")
$multiDevice = @("List of devices attached", "R3CN12345	device", "emulator-5554	device")
$offlineDevice = @("List of devices attached", "R3CN12345	offline", "emulator-5554	unauthorized")
$emptyDevices = @("List of devices attached")

$sel = $null
try { $sel = Select-AndroidTarget -DeviceLines $oneDevice } catch { }
Record "single device selected" ($sel -eq 'R3CN12345') "got '$sel'"

$threw = $false
try { Select-AndroidTarget -DeviceLines $multiDevice } catch { $threw = $true }
Record "multiple devices without serial throws" $threw

$sel2 = $null
try { $sel2 = Select-AndroidTarget -DeviceLines $multiDevice -PreferredSerial 'emulator-5554' } catch { }
Record "preferred serial selected among many" ($sel2 -eq 'emulator-5554') "got '$sel2'"

$threw2 = $false
try { Select-AndroidTarget -DeviceLines $multiDevice -PreferredSerial 'absent999' } catch { $threw2 = $true }
Record "absent preferred serial throws" $threw2

$threw3 = $false
try { Select-AndroidTarget -DeviceLines $offlineDevice } catch { $threw3 = $true }
Record "offline/unauthorized excluded (no eligible)" $threw3

$threw4 = $false
try { Select-AndroidTarget -DeviceLines $emptyDevices } catch { $threw4 = $true }
Record "empty device list throws" $threw4

# --- Get-FileSha256 ------------------------------------------------------

$fileA = New-TempFile "sha-a.bin" "walk-game-cert-sample-a"
$fileB = New-TempFile "sha-b.bin" "walk-game-cert-sample-b"
$hA1 = Get-FileSha256 -Path $fileA
$hA2 = Get-FileSha256 -Path $fileA
$hB = Get-FileSha256 -Path $fileB
Record "file hash deterministic" ($hA1 -eq $hA2) "$hA1 vs $hA2"
Record "different files differ" ($hA1 -ne $hB) "$hA1 vs $hB"
Record "hash is lowercase hex" ($hA1 -match '^[0-9a-f]{64}$') $hA1

# --- R4: Unity toolchain-identity preflight -------------------------------

$projVer = New-TempFile "ProjectVersion.txt" "m_EditorVersion: 6000.3.4f1`n"
$pin = Get-UnityPinnedVersion -ProjectSettingsPath $projVer
Record "R4 pinned version parsed" ($pin -eq '6000.3.4f1') "got '$pin'"

$badVer = New-TempFile "ProjectVersion-bad.txt" "m_EditorVersion: 6000.2.0f1`n"
$pinBad = Get-UnityPinnedVersion -ProjectSettingsPath $badVer
Record "R4 pinned version parses other pins" ($pinBad -eq '6000.2.0f1') "got '$pinBad'"

$editorOkDir = Join-Path $tmp 'Unity_6000.3.4f1'
New-Item -ItemType Directory -Force -Path $editorOkDir | Out-Null
$editorOk = Join-Path $editorOkDir 'Unity.exe'
New-Item -ItemType File -Force -Path $editorOk | Out-Null
$mOk = Test-UnityEditorMatchesPin -EditorPath $editorOk -PinnedVersion '6000.3.4f1'
Record "R4 matching editor path passes preflight" ([string]::IsNullOrEmpty($mOk)) "reason: '$mOk'"

$editorBadDir = Join-Path $tmp 'Unity_6000.2.0f1'
New-Item -ItemType Directory -Force -Path $editorBadDir | Out-Null
$editorBad = Join-Path $editorBadDir 'Unity.exe'
New-Item -ItemType File -Force -Path $editorBad | Out-Null
$mBad = Test-UnityEditorMatchesPin -EditorPath $editorBad -PinnedVersion '6000.3.4f1'
Record "R4 mismatched editor path fails preflight" (-not [string]::IsNullOrEmpty($mBad)) "reason: '$mBad'"

$mMissing = Test-UnityEditorMatchesPin -EditorPath (Join-Path $tmp 'nope.exe') -PinnedVersion '6000.3.4f1'
Record "R4 missing editor path fails preflight" (-not [string]::IsNullOrEmpty($mMissing)) "reason: '$mMissing'"

$mVer = Test-UnityEditorMatchesPin -EditorPath $editorOk -PinnedVersion '6000.3.4f1' -EditorVersion '6000.2.0f1'
Record "R4 reported-version mismatch fails preflight" (-not [string]::IsNullOrEmpty($mVer)) "reason: '$mVer'"

# --- R6: idempotent Android uninstall ---------------------------------------

$mockAbsent = {
    param([string[]]$a)
    if ($a[0] -eq 'uninstall') { throw 'adb: package not found' }
    return @()   # pm list packages -> nothing
}
$rAbsent = Uninstall-AndroidPackageIdempotent -PackageId 'com.quantdale.walkgame' -Adb $mockAbsent
Record "R6 absent package uninstall returns absent" ($rAbsent -eq 'absent') "got '$rAbsent'"

$mockPresent = {
    param([string[]]$a)
    if ($a[0] -eq 'uninstall') { throw 'adb: failure' }
    return @('package:com.quantdale.walkgame')
}
$threwR6 = $false
try { Uninstall-AndroidPackageIdempotent -PackageId 'com.quantdale.walkgame' -Adb $mockPresent } catch { $threwR6 = $true }
Record "R6 still-installed uninstall throws" $threwR6

$mockRemoved = {
    param([string[]]$a)
    if ($a[0] -eq 'uninstall') { return @('Success') }
    return @()
}
$rRemoved = Uninstall-AndroidPackageIdempotent -PackageId 'com.quantdale.walkgame' -Adb $mockRemoved
Record "R6 installed package uninstall returns removed" ($rRemoved -eq 'removed') "got '$rRemoved'"

# --- R7: smoke script structural fail-closed evidence discipline ------------

function Test-ScriptParses([string]$RelPath) {
    $full = Join-Path $RepoRoot $RelPath
    $errs = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($full, [ref]$null, [ref]$errs)
    return ($null -eq $errs -or $errs.Count -eq 0)
}

Record "R7 smoke script parses" (Test-ScriptParses 'scripts/verify-android-smoke.ps1')
Record "R7 editmode script parses" (Test-ScriptParses 'scripts/verify-unity-editmode.ps1')
Record "R7 playmode script parses" (Test-ScriptParses 'scripts/verify-unity-playmode.ps1')
Record "R7 helpers script parses" (Test-ScriptParses 'scripts/cert-script-helpers.ps1')

$smokeSrc = Get-Content -LiteralPath (Join-Path $RepoRoot 'scripts/verify-android-smoke.ps1') -Raw
Record "R7 smoke uses finally block" ($smokeSrc -match 'finally\s*\{')
Record "R7 smoke uses idempotent uninstall" ($smokeSrc -match 'Uninstall-AndroidPackageIdempotent')
Record "R7 smoke records finalDisposition" ($smokeSrc -match 'finalDisposition')

# --- R17.2.10: foreground/resumed activity evidence ------------------------

$mockFg = {
    param([string[]]$a)
    if ($a[2] -eq 'shell' -and $a[3] -eq 'am') { return @('state: resumed') }
    if ($a[2] -eq 'shell' -and $a[3] -eq 'dumpsys') { return @('    mCurrentFocus=com.quantdale.walkgame/com.unity3d.player.UnityPlayerActivity') }
    return @()
}
$fg = Get-AndroidForegroundActivity -Serial 'emulator-5554' -Adb $mockFg
Record "R17.2.10 foreground activity captured" ($fg.FocusedActivity -match 'com.quantdale.walkgame') "got '$($fg.FocusedActivity)'"
Record "R17.2.10 foreground state captured" ($fg.State -eq 'state: resumed') "got '$($fg.State)'"

$mockFgWrong = {
    param([string[]]$a)
    if ($a[2] -eq 'shell' -and $a[3] -eq 'am') { return @('state: resumed') }
    if ($a[2] -eq 'shell' -and $a[3] -eq 'dumpsys') { return @('    mCurrentFocus=com.other.app/SomeActivity') }
    return @()
}
$fgWrong = Get-AndroidForegroundActivity -Serial 'emulator-5554' -Adb $mockFgWrong
Record "R17.2.10 wrong-package activity distinguishable" ($fgWrong.FocusedActivity -match 'com.other.app') "got '$($fgWrong.FocusedActivity)'"

Write-Host ""
Write-Host ("Certification-script tests complete: {0} passed, {1} failed." -f $script:PassCount, $script:FailCount)
if ($script:FailCount -gt 0) { exit 1 }
exit 0
