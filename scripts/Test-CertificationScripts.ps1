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
Record "R7 compile script parses" (Test-ScriptParses 'scripts/verify-unity-compile.ps1')
Record "R7 iOS build script parses" (Test-ScriptParses 'scripts/build-ios-xcode.ps1')

$smokeSrc = Get-Content -LiteralPath (Join-Path $RepoRoot 'scripts/verify-android-smoke.ps1') -Raw
Record "R7 smoke uses finally block" ($smokeSrc -match 'finally\s*\{')
Record "R7 smoke uses idempotent uninstall" ($smokeSrc -match 'Uninstall-AndroidPackageIdempotent')
Record "R7 smoke records finalDisposition" ($smokeSrc -match 'finalDisposition')

$compileSrc = Get-Content -LiteralPath (Join-Path $RepoRoot 'scripts/verify-unity-compile.ps1') -Raw
Record "R7 compile uses fail-closed log check" ($compileSrc -match 'Test-UnityCompileLog')
Record "R7 compile binds sourceSha and dirty state" (($compileSrc -match 'sourceSha') -and ($compileSrc -match 'preDirty') -and ($compileSrc -match 'postDirty'))
Record "R7 compile checks pinned editor and reported version" (($compileSrc -match 'Get-UnityPinnedVersion') -and ($compileSrc -match 'editorReportedVersion'))
Record "R7 compile removes stale artifacts" (($compileSrc -match 'Remove-Item') -and ($compileSrc -match 'compile-run\.log'))
Record "R7 compile reports canonical/unexpected mutation" (($compileSrc -match 'Test-UnityMutationSet') -and ($compileSrc -match 'unexpectedMutationFiles'))

# --- M8.8 H5: Android API 36 source regression -----------------------------

$editorToolsSrc = Get-Content -LiteralPath (Join-Path $RepoRoot 'Assets/WalkGame/Editor/WalkGameEditorTools.cs') -Raw
$androidBuildSrc = Get-Content -LiteralPath (Join-Path $RepoRoot 'scripts/build-android-development.ps1') -Raw
Record "H5 editor build targets Android API 36" ($editorToolsSrc -match 'targetSdkVersion\s*=\s*AndroidSdkVersions\.AndroidApiLevel36')
Record "H5 editor build has no API 35 target fallback" (-not ($editorToolsSrc -match 'AndroidApiLevel35'))
Record "H5 build wrapper invokes deterministic editor method" ($androidBuildSrc -match 'BuildAndroidDevelopment')
Record "H5 Android wrapper verifies generated targetSdk" (($androidBuildSrc -match 'targetSdkVersion') -and ($androidBuildSrc -match 'aapt') -and ($androidBuildSrc -match 'targetSdk -lt 36'))
Record "H5 Android wrapper binds APK/toolchain evidence" (($androidBuildSrc -match 'apkSha256') -and ($androidBuildSrc -match 'buildToolsVersion') -and ($androidBuildSrc -match 'sourceSha'))

$iosEditorSrc = Get-Content -LiteralPath (Join-Path $RepoRoot 'Assets/WalkGame/Editor/WalkGameEditorTools.cs') -Raw
$iosBuildSrc = Get-Content -LiteralPath (Join-Path $RepoRoot 'scripts/build-ios-xcode.ps1') -Raw
$iosProviderSrc = Get-Content -LiteralPath (Join-Path $RepoRoot 'Assets/WalkGame/Platform/iOS/CSharp/IosCoreMotionProvider.cs') -Raw
Record "S25 iOS editor path has deterministic bundle identity" (($iosEditorSrc -match 'SetApplicationIdentifier\(BuildTargetGroup\.iOS') -and ($iosEditorSrc -match 'IosBundleIdentifier'))
Record "S25 iOS editor path invokes Xcode generation" ($iosEditorSrc -match 'BuildIosXcodeDevelopment')
Record "S25 iOS wrapper binds Unity/Xcode/SDK evidence" (($iosBuildSrc -match 'unityPinnedVersion') -and ($iosBuildSrc -match 'xcodeVersion') -and ($iosBuildSrc -match 'sdkVersion') -and ($iosBuildSrc -match 'sourceSha'))
Record "S25 iOS wrapper verifies motion privacy and CoreMotion" (($iosBuildSrc -match 'NSMotionUsageDescription') -and ($iosBuildSrc -match 'WalkGamePedometerBridge\.mm'))
Record "S26 iOS wrapper requires Xcode 26 and SDK 26" (($iosBuildSrc -match 'Xcode 26') -and ($iosBuildSrc -match 'iOS 26 SDK'))
Record "S25 iOS wrapper keeps signing explicit" (($iosBuildSrc -match 'CODE_SIGNING_ALLOWED') -and ($iosBuildSrc -match 'RequireSigned'))
Record "P2 iOS provider roots its AOT callback delegate" (($iosProviderSrc -match 'ManagedQueryResultCallback') -and ($iosProviderSrc -match 'WG_SetQueryResultCallback\(ManagedQueryResultCallback\)'))
Record "P2 iOS provider cancels pending work on shutdown" (($iosProviderSrc -match '_pendingRequestIds') -and ($iosProviderSrc -match 'TrySetCanceled'))
Record "P2 iOS provider exposes generation identity" ($iosProviderSrc -match 'ProviderGeneration')

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

# --- M8.8 H3: semantic compile log/evidence false-green guards -------------

$goodLog = New-TempFile "compile-good.log" @'
Unity Editor 6000.3.4f1 BatchMode starting
Importing project...
Compilation succeeded
WalkGame Validate OK
Exiting batchmode successfully
'@
$badCsLog = New-TempFile "compile-bad-cs.log" @'
Unity Editor 6000.3.4f1 BatchMode starting
error CS0246: the type or namespace name 'GraphicsSettings' could not be found
Exiting batchmode successfully
'@
$badCompilerLog = New-TempFile "compile-bad-compiler.log" @'
Unity Editor 6000.3.4f1
Compiler Error at Assets/WalkGame/Editor/WalkGameEditorTools.cs(143,13): error CS0103
'@
$emptyLog = New-TempFile "compile-empty.log" ""
$noMarkerLog = New-TempFile "compile-nomarker.log" "Unity Editor 6000.3.4f1 BatchMode starting"
$missingLogPath = Join-Path $tmp "does-not-exist-compile.log"

$s = ''
Record "compile good log passes" (Test-UnityCompileLog -Path $goodLog -Summary ([ref]$s)) $s
Record "compile bad CS log fails" (-not (Test-UnityCompileLog -Path $badCsLog -Summary ([ref]$s))) $s
Record "compile bad Compiler Error fails" (-not (Test-UnityCompileLog -Path $badCompilerLog -Summary ([ref]$s))) $s
Record "compile empty log fails" (-not (Test-UnityCompileLog -Path $emptyLog -Summary ([ref]$s))) $s
Record "compile missing log fails" (-not (Test-UnityCompileLog -Path $missingLogPath -Summary ([ref]$s))) $s
Record "compile missing completion fails" (-not (Test-UnityCompileLog -Path $noMarkerLog -Summary ([ref]$s))) $s

$fakeSha = "abc123def456abc123def456abc123def456abcd"
$nowIso = [DateTimeOffset]::UtcNow.ToString("o")
$laterIso = [DateTimeOffset]::UtcNow.AddSeconds(1).ToString("o")
$goodEvidence = [ordered]@{
    schemaVersion = 2
    sourceSha = $fakeSha
    preDirty = $true
    preStatus = " M source.cs"
    postDirty = $true
    postStatus = " M source.cs"
    mutatedFiles = @()
    canonicalMutationFiles = @()
    unexpectedMutationFiles = @()
    allowProjectMutation = $false
    editorPath = "C:\Unity\6000.3.4f1\Editor\Unity.exe"
    pinnedVersion = "6000.3.4f1"
    editorReportedVersion = "6000.3.4f1"
    startUtc = $nowIso
    endUtc = $laterIso
    exitCode = 0
    launchError = ""
    logPath = [IO.Path]::GetFullPath($goodLog)
    logFresh = $true
    compilerErrorCount = 0
    semanticComplete = $true
    semanticSummary = "compile log clean"
}
$evidenceGood = New-TempFile "compile-ev-good.json" ($goodEvidence | ConvertTo-Json -Depth 6)
$s2 = ''
Record "compile evidence good passes" (Test-UnityCompileEvidence -EvidencePath $evidenceGood -ExpectedSha $fakeSha -LogPath $goodLog -ExpectedPinnedVersion "6000.3.4f1" -Summary ([ref]$s2)) $s2

function Copy-Evidence([System.Collections.IDictionary]$Source) {
    $copy = [ordered]@{}
    foreach ($key in $Source.Keys) { $copy[$key] = $Source[$key] }
    return $copy
}

$badShaEvidence = Copy-Evidence $goodEvidence
$badShaEvidence.sourceSha = "differentSha123"
$evidenceBadSha = New-TempFile "compile-ev-badsha.json" ($badShaEvidence | ConvertTo-Json -Depth 6)
Record "compile evidence bad SHA fails" (-not (Test-UnityCompileEvidence -EvidencePath $evidenceBadSha -ExpectedSha $fakeSha -LogPath $goodLog -ExpectedPinnedVersion "6000.3.4f1" -Summary ([ref]$s2))) $s2

$staleEvidence = Copy-Evidence $goodEvidence
$staleEvidence.startUtc = "2020-01-01T00:00:00Z"
$staleEvidence.endUtc = "2020-01-01T00:01:00Z"
$evidenceStale = New-TempFile "compile-ev-stale.json" ($staleEvidence | ConvertTo-Json -Depth 6)
Record "compile evidence stale fails" (-not (Test-UnityCompileEvidence -EvidencePath $evidenceStale -ExpectedSha $fakeSha -LogPath $goodLog -ExpectedPinnedVersion "6000.3.4f1" -Summary ([ref]$s2))) $s2

$launchFailureEvidence = Copy-Evidence $goodEvidence
$launchFailureEvidence.exitCode = 127
$launchFailureEvidence.launchError = "simulated editor launch failure"
$evidenceLaunchFailure = New-TempFile "compile-ev-launch-failure.json" ($launchFailureEvidence | ConvertTo-Json -Depth 6)
Record "compile evidence launch failure fails" (-not (Test-UnityCompileEvidence -EvidencePath $evidenceLaunchFailure -ExpectedSha $fakeSha -LogPath $goodLog -ExpectedPinnedVersion "6000.3.4f1" -Summary ([ref]$s2))) $s2

$compilerFailureEvidence = Copy-Evidence $goodEvidence
$compilerFailureEvidence.compilerErrorCount = 2
$evidenceCompilerFailure = New-TempFile "compile-ev-compiler-failure.json" ($compilerFailureEvidence | ConvertTo-Json -Depth 6)
Record "compile evidence non-zero compiler count fails" (-not (Test-UnityCompileEvidence -EvidencePath $evidenceCompilerFailure -ExpectedSha $fakeSha -LogPath $goodLog -ExpectedPinnedVersion "6000.3.4f1" -Summary ([ref]$s2))) $s2

$missingCompletionEvidence = Copy-Evidence $goodEvidence
$missingCompletionEvidence.semanticComplete = $false
$evidenceMissingCompletion = New-TempFile "compile-ev-missing-completion.json" ($missingCompletionEvidence | ConvertTo-Json -Depth 6)
Record "compile evidence missing completion fails" (-not (Test-UnityCompileEvidence -EvidencePath $evidenceMissingCompletion -ExpectedSha $fakeSha -LogPath $goodLog -ExpectedPinnedVersion "6000.3.4f1" -Summary ([ref]$s2))) $s2

$mutationEvidence = Copy-Evidence $goodEvidence
$mutationEvidence.mutatedFiles = @("?? Assets/UnexpectedGenerated.asset")
$mutationEvidence.unexpectedMutationFiles = @("?? Assets/UnexpectedGenerated.asset")
$mutationEvidence.allowProjectMutation = $true
$evidenceMutation = New-TempFile "compile-ev-mutation.json" ($mutationEvidence | ConvertTo-Json -Depth 6)
Record "compile evidence unexpected mutation fails even when allowed" (-not (Test-UnityCompileEvidence -EvidencePath $evidenceMutation -ExpectedSha $fakeSha -LogPath $goodLog -ExpectedPinnedVersion "6000.3.4f1" -AllowMutation -Summary ([ref]$s2))) $s2

$canonicalMutation = Test-UnityMutationSet -MutatedFiles @("?? Packages/packages-lock.json") -AllowCanonical
Record "canonical Unity mutation can be classified" (($canonicalMutation.Unexpected.Count -eq 0) -and ($canonicalMutation.Canonical.Count -eq 1))
$unexpectedMutation = Test-UnityMutationSet -MutatedFiles @("?? Packages/packages-lock.json", "?? Assets/UnexpectedGenerated.asset") -AllowCanonical
Record "unexpected Unity mutation is classified" ($unexpectedMutation.Unexpected.Count -eq 1)

Write-Host ""
Write-Host ("Certification-script tests complete: {0} passed, {1} failed." -f $script:PassCount, $script:FailCount)
if ($script:FailCount -gt 0) { exit 1 }
exit 0
