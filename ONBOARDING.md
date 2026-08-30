# Fresh-machine onboarding

This is the canonical bootstrap entry point for a new workstation or a fresh coding-agent environment. Complete this document before implementation work. The objective is a reproducible machine that can build, test, inspect, and operate this repository without rediscovering tooling mid-campaign.

## 1. Preflight rule

1. Clone the repository and enter its root.
2. Confirm the intended repository/branch and fetch current `origin/main`.
3. Read the repository control-plane documents before changing code: `AGENTS.md`, `README.md`, `docs/MASTER_PLAN.md`, `docs/ROADMAP.md`, `docs/AGENT_EXECUTION_GUIDE.md`, `.agent/`.
4. Install/verify the machine prerequisites below.
5. Enable the committed agent integrations and repository-local skills.
6. Restore dependencies from lockfiles/pins; do not casually upgrade them during bootstrap.
7. Run the baseline validation commands.
8. Only then begin a development campaign. If a prerequisite cannot be satisfied, record it as an environment blocker rather than weakening a gate.

Credentials, API keys, signing material, account logins, licensed assets, and other secrets are machine/user responsibilities. Never commit them.

## 2. Supported host and prerequisites

**Primary host:** Unity 6.3 LTS project. Cross-platform domain tests can run without Unity; full editor/mobile work requires the exact Unity editor and platform modules.

**Required machine tools**
- Git
- Unity 6000.3.4f1 (exact version from `ProjectSettings/ProjectVersion.txt`)
- Unity Hub or equivalent editor installation
- dotnet SDK for the standalone domain verification harness
- PowerShell for setup/validation scripts

**Task-dependent / optional tools**
- Unity Android Build Support + SDK/NDK/JDK for Android
- macOS + Xcode + Unity iOS Build Support for iOS
- physical Android/iOS devices for native activity/provider validation


## 3. Agent setup

- Load repository instructions before acting. Prefer committed repository state over chat history.
- Repository-local skills: `goal`.
- Discover and use committed agent adapter/config directories in-place; do not duplicate them globally unless the harness cannot load repository-local configuration.
- Relevant committed agent surfaces: `.agent/`, `.agents/`, `.claude/`, `.githooks/`, `.kimi-code/`, `.opencode/`, `.repo-identity.json`.
- MCP policy: No root `.mcp.json` is committed. Prefer Unity batchmode/editor tooling and the repository's verification harness; do not fake editor/device proof with a generic MCP.
- Keep diagnostic/documentation MCPs narrow. An MCP does not grant architecture, publishing, production, or gate-bypass authority.
- Authenticate GitHub and coding-agent CLIs separately on the machine. Never store tokens in tracked files.

## 4. Bootstrap

```powershell
dotnet test verification\WalkGame.Domain.Tests\WalkGame.Domain.Tests.csproj
.\scripts\assert-repo-identity.ps1
# After installing Unity 6000.3.4f1, open/import once and run:
.\scripts\setup-unity-project.ps1
```

Unity's exact editor version is not negotiable during bootstrap. Device and native pedometer gates remain unverified until run on appropriate hardware.


## 5. Editor/LSP baseline

Use Unity-aware Roslyn/C# tooling generated from the exact editor project. Keep the engine-free domain sources compatible with the standalone verification harness.

The editor is optional; reliable language diagnostics are not.

## 6. Baseline verification

```powershell
dotnet test verification\WalkGame.Domain.Tests\WalkGame.Domain.Tests.csproj
# Then, on a Unity-capable machine, run the repository's documented editor/batchmode validation and open Assets/WalkGame/Core/Bootstrap.unity for Play verification.
```

A fresh machine is **development-ready** when all applicable non-external gates pass. Hardware/device/signing/account gates may remain explicitly blocked when repository state already classifies them that way.

## 7. Fresh-agent instruction

> Read `ONBOARDING.md` first. Set up every applicable prerequisite, repository-local skill, MCP/plugin, dependency, browser/device/runtime tool, and validation gate described there. Then read the repository's durable agent state and only start implementation after preflight is green or a genuine environment blocker is recorded. Do not replace pinned tooling, skip gates, or invent work to compensate for a missing machine capability.
