# Walk Game OpenSpec

This directory is the canonical specification surface for implementation campaigns planned after the M8.4 hardening cycle.

## Current completed change

- **M8.8 — Pre-Playtest Integrity & Unity Bring-Up Closure**
- Canonical package: `openspec/changes/m8.8-pre-playtest-integrity-and-unity-bringup/`
- Reconciled from: `main@15947a222b9812cb641066f40cb8e48a276207c7`
- Implementation publication: `main@0190c8ab59331f72e4b2ffa1636139ece6b4ab13`
- Executor adapter: `.agent/EXECUTION_PROMPT.md`
- Autonomous work budget: up to 12 hours.

M8.8 is complete at the locally executable implementation tier. M9 Closed Playtest
Readiness is not a claim of production certification: Unity/device/Apple/performance
evidence remains explicitly UNVERIFIED as recorded in the implementation status.

## Change layout

Each active change lives under `openspec/changes/<change-id>/` and SHOULD contain:

- `audit.md` — repository evidence and findings that justified the campaign.
- `proposal.md` — problem, goals, non-goals, scope, impact, and success criteria.
- `design.md` — architectural invariants and implementation constraints.
- `tasks.md` — ordered executable checklist with validation and completion gates.
- `specs/**/spec.md` — normative requirements and acceptance scenarios.

`.agent/EXECUTION_PROMPT.md` remains the compatibility entry point for `/goal continue` and other executor workflows. For an ACTIVE OpenSpec campaign it points to the canonical change package instead of duplicating every requirement.

## Status discipline

Use one of these states consistently:

- `PROPOSED` — planned but not selected for execution.
- `ACTIVE` — the one repository campaign an executor should pick up.
- `COMPLETE` — implementation and required available gates finished; blocked external gates are explicitly recorded as UNVERIFIED.
- `SUPERSEDED` — replaced by a newer change with a linked reason.

Only one implementation campaign should be ACTIVE unless the repository instructions explicitly authorize parallel independent work.

## Evidence discipline

Do not mark a task complete because an earlier report said it passed. The executing session must produce fresh evidence for every locally available gate. Unity editor, Android build/device, iOS/Xcode, signing, UAC/elevation, and physical-performance tiers remain honestly `UNVERIFIED` when the environment cannot run them.

Do not convert unavailable hardware validation into a code-only claim. Do not bypass repository identity or writer-lease rules.

Semantic Unity compilation is a distinct evidence tier from `verify-unity-static.ps1`. A structurally valid tree does not prove Editor/runtime assemblies compile.

## Change authority

For an ACTIVE change, resolve conflicts in this order:

1. `AGENTS.md` and `.agent/PLANNER_HANDOFF.md`.
2. The active OpenSpec `specs/**/spec.md` requirements.
3. The active change `design.md` and `tasks.md`.
4. Repository architecture/data/privacy/testing documentation and applicable ADRs.
5. `.agent/EXECUTION_PROMPT.md` as the executor adapter.

If `origin/main` advanced after the change's `Planned-From` SHA, reconcile the intervening commits first and preserve any equivalent landed fix. Never blindly re-implement stale planner observations.
