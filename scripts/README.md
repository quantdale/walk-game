# Verification scripts

| Script | Needs | Purpose |
| --- | --- | --- |
| `verify-domain.ps1` | .NET SDK 8+ | Canonical gate (ADR 0001): compiles the engine-free domain sources outside Unity and runs the NUnit suite. Non-zero exit on failure. |
| `verify-domain.cmd` | Windows + pwsh | Thin wrapper around `verify-domain.ps1`. |
| `verify-unity-static.ps1` | PowerShell 7+ | Non-Unity static gate for the pinned editor version, asset metas/GUIDs, packages, Bootstrap build scene, and Android permission invariants. It does not claim Unity compile evidence. |
| `verify-unity-editmode.ps1` | Unity 6000.3.x editor, `UNITY_EDITOR_PATH` env var | Batch-mode EditMode test run; writes `TestResults/editmode-results.xml` + full log. Never commit machine paths; set the variable per machine. |
| `verify-unity-playmode.ps1` | Licensed Unity 6000.3.4f1 editor, `UNITY_EDITOR_PATH` env var | Batch-mode Bootstrap/runtime certification; writes `TestResults/playmode-results.xml` + full log. |
| `setup-unity-project.ps1` | Unity 6000.3.4f1 editor, `UNITY_EDITOR_PATH` env var | Idempotent batch-mode URP/Input/product/content setup. |
| `build-android-development.ps1` | Unity 6000.3.4f1 + Android Build Support, `UNITY_EDITOR_PATH` env var | Builds `Builds/Android/WalkGame-dev.apk` with the committed Bootstrap scene and Android development settings. |

## Local gate order before pushing

```bash
dotnet test verification/WalkGame.Domain.Tests/WalkGame.Domain.Tests.csproj   # or scripts/verify-domain.ps1
# with an installed editor:
#   set UNITY_EDITOR_PATH=...Unity.exe
#   ./scripts/setup-unity-project.ps1
#   ./scripts/verify-unity-editmode.ps1
#   ./scripts/verify-unity-playmode.ps1
#   ./scripts/build-android-development.ps1
```

CI (`.github/workflows/domain-tests.yml`) runs the domain suite on every push/PR to
`main`. Unity compile/EditMode/PlayMode evidence currently requires a local licensed
editor; until CI licensing exists, record editor runs manually in
`docs/IMPLEMENTATION_STATUS.md` with their exact outcome.
