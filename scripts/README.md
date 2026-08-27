# Verification scripts

| Script | Needs | Purpose |
| --- | --- | --- |
| `verify-domain.ps1` | .NET SDK 8+ | Canonical gate (ADR 0001): compiles the engine-free domain sources outside Unity and runs the NUnit suite. Non-zero exit on failure. |
| `verify-domain.cmd` | Windows + pwsh | Thin wrapper around `verify-domain.ps1`. |
| `verify-unity-static.ps1` | PowerShell 7+ | Non-Unity static gate for the pinned editor version, asset metas/GUIDs, packages, Bootstrap build scene, and Android permission invariants. It does not claim Unity compile evidence. |
| `verify-unity-editmode.ps1` | Unity 6000.3.x editor, `UNITY_EDITOR_PATH` env var | Batch-mode EditMode test run; writes `TestResults/editmode-results.xml` + full log. Never commit machine paths; set the variable per machine. |
| `verify-unity-playmode.ps1` | Licensed Unity 6000.3.4f1 editor, `UNITY_EDITOR_PATH` env var | Batch-mode Bootstrap/runtime certification; writes `TestResults/playmode-results.xml` + full log. |
| `verify-unity-compile.ps1` | Licensed Unity 6000.3.4f1 editor, `UNITY_EDITOR_PATH` env var | Dedicated fail-closed semantic import/compile gate. It requires a fresh pinned-editor log with a completion marker, zero compiler/import errors, source/dirty-state binding, and no unexpected project mutation; writes ignored evidence under `TestResults/`. |
| `setup-unity-project.ps1` | Unity 6000.3.4f1 editor, `UNITY_EDITOR_PATH` env var | Idempotent batch-mode URP/Input/product/content setup. |
| `build-android-development.ps1` | Unity 6000.3.4f1 + Android Build Support + Android SDK platform 36, `UNITY_EDITOR_PATH` env var | Builds `Builds/Android/WalkGame-dev.apk` with IL2CPP + ARM64, minSdk 26, and target API 36; verifies the generated APK manifest with `aapt` and records source/toolchain/APK evidence. |
| `verify-android-smoke.ps1` | adb + device/emulator + built APK | Installs the APK, clears data, launches, exercises background/resume, rotation attempt, force-stop/relaunch; fails on fatal logcat conditions. Writes ignored artifacts under `Artifacts/android-smoke/`. Certifies Android lifecycle only - never real step sensors. |
| `build-ios-xcode.ps1` | macOS + licensed Unity 6000.3.4f1 + Xcode 26/iOS 26 SDK | Generates the deterministic iOS Xcode project, verifies bundle/CoreMotion privacy configuration, builds unsigned by default (or explicitly signs/installs), and records separate Unity/Xcode logs plus source/tool/output evidence. |
| `verify-release-hygiene.ps1` | PowerShell 7+ | Static privacy/release audit: no GPS/save-path logging, no direct Debug.Log in runtime code (explicit `hygiene-allow` exceptions only), no hard-coded machine paths/secrets, minimal Android manifest. Runs in CI. |
| `Assert-RepoIdentity.ps1` / `assert-repo-identity.sh` | git; PowerShell 7+ / sh | Fail-closed repository identity guard (`quantdale/walk-game`, NOT the sibling repo): identity file, normalized HTTPS/SSH origin, fingerprints, `GITHUB_REPOSITORY` under CI. Runs in hooks and CI (ADR 0008). |
| `WriterLock.ps1` / `writer-lock.sh` | git | Single-writer lease per worktree under untracked `.git/`; second writers refused; stale recovery requires explicit `--force`. |
| `Check-RemoteAdvance.ps1` / `check-remote-advance.sh` | git + network | Lost-update guard: fetches the target branch and refuses integration when origin contains commits unreachable from HEAD. |
| `setup-hooks.ps1` / `setup-hooks.sh` | git | One-time activation of tracked `.githooks/` via `git config core.hooksPath .githooks`. |
| `Test-AgentGuards.ps1` | PowerShell 7+ (+ bash for the sh matrix) | Deterministic verification of all guards across twelve scenarios on both implementations, entirely against local fixture repos (`GIT_ALLOW_PROTOCOL=file`). Runs in CI. |

## Local gate order before pushing

```bash
sh scripts/assert-repo-identity.sh                                            # or scripts/Assert-RepoIdentity.ps1
dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj   # or scripts/verify-domain.ps1
./scripts/verify-unity-static.ps1
./scripts/verify-release-hygiene.ps1
pwsh scripts/Test-AgentGuards.ps1                                             # when touching agent infrastructure
pwsh scripts/Test-CertificationScripts.ps1                                    # script/evidence false-green fixtures
git diff --check
# with an installed editor:
#   set UNITY_EDITOR_PATH=...Unity.exe
#   ./scripts/setup-unity-project.ps1
#   ./scripts/verify-unity-compile.ps1
#   ./scripts/verify-unity-editmode.ps1
#   ./scripts/verify-unity-playmode.ps1
#   ./scripts/build-android-development.ps1
#   ./scripts/build-ios-xcode.ps1                                             # macOS only
# with a connected emulator/device:
#   ./scripts/verify-android-smoke.ps1
```

CI (`.github/workflows/domain-tests.yml`) runs the domain suite on every push/PR to
`main`. Unity compile/EditMode/PlayMode, Android generated-manifest/device, and iOS
Xcode/device evidence require their named external prerequisites. Until those lanes run,
record them as `UNVERIFIED` in `docs/IMPLEMENTATION_STATUS.md`; source/static passes must
not be promoted to editor/build/device claims.
