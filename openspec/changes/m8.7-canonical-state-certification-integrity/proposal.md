# M8.7 Proposal — Canonical State & Certification Integrity Closure

Status: ACTIVE
Depends on: M8.5 completed; M8.6 locally executed but not yet authoritatively integrated
Target: M8 — Device Ready

## Problem

Walk Game has strong engine-free coverage, but two integrity boundaries are still weaker than the product requires.

First, schema-compatible JSON is not automatically a valid canonical object graph. Several explicit-null or identity-inconsistent values can survive SaveValidator and later crash boot or rollback. That violates the project's fail-closed persistence objective.

Second, the repository's first-push race guard cannot distinguish "the remote branch does not exist yet" from "the remote cannot be inspected." The prior M8.6 executor was therefore unable to deliver a valid commit without manual remote-branch creation.

At the same time, M8.6's locally implemented certification-harness changes are stranded outside authoritative main and the real Unity/device tiers remain unverified.

## Proposed change

Execute one M8.7 integrity-closure campaign with six ordered lanes:

1. Reconcile and preserve the prior M8.6 executor work, including d0c8687 when it is genuinely available.
2. Reproduce and close parseable-save structural corruption defects across the full serializer-visible canonical graph.
3. Make ProfileStateCopier and PersistenceCoordinator recovery robust against repaired durable state without hiding unrecoverable corruption.
4. Fix first-push remote-ref semantics while preserving strict lost-update protection.
5. Complete/carry forward all locally executable M8.6 certification-integrity requirements and rerun the full headless/static suite.
6. If genuine prerequisites exist, continue into Unity/editor/build/device certification; otherwise record exact blockers and close honestly.

## Goals

- A syntactically valid, current-schema save must either normalize to a structurally valid canonical graph or fail closed before gameplay composition.
- The current region must always resolve to a non-null RegionState whose identity is coherent with the map key.
- Null/malformed collection elements that would crash rollback must be deterministically handled.
- A failed save must not turn into an unhandled rollback exception because durable state contained a repairable structural defect.
- The structural repair contract must be regression-locked across every persisted model family.
- A brand-new agent branch must be pushable normally when remote absence is positively proven.
- Network/auth uncertainty must still block pushes.
- Existing remote advancement must still block non-fast-forward automation.
- Prior M8.6 harness fixes must be preserved and verified rather than assumed.
- No evidence tier may be upgraded by inference.

## Non-goals

- Region 2 or new world content.
- Economy rebalance.
- New sensor sources, Health Connect or HealthKit.
- Cloud saves/accounts/social/multiplayer.
- Analytics/backend rollout.
- Broad UI/art redesign.
- Relaxing repository identity, writer-lock, lost-update or no-force-push policy.
- Inventing a Unity/device PASS without real prerequisites.
- Destructive repair that discards an entire player profile merely because one optional history element is invalid.

## Expected implementation surface

Likely:
- Assets/WalkGame/Core/PlayerProfile.cs and RegionState.cs for defensive map helpers if needed.
- Assets/WalkGame/Persistence/SaveValidator.cs.
- Assets/WalkGame/Persistence/ProfileStateCopier.cs.
- Persistence and save tests.
- .githooks/pre-push.
- scripts/Check-RemoteAdvance.ps1 and scripts/check-remote-advance.sh.
- scripts/Test-AgentGuards.ps1.
- M8.6 certification helpers/scripts only where equivalent fixes are absent after reconciliation.
- docs/IMPLEMENTATION_STATUS.md, DATA_MODEL.md, TESTING_AND_PERFORMANCE.md, AGENT_EXECUTION_GUIDE.md and ADR 0007 if the persistence contract materially changes.

The executor may touch adjacent files only when root-cause evidence requires it.

## Success criteria

M8.7 is complete when:
- every H1–H5 finding from audit.md has a documented disposition;
- focused regressions reproduce the pre-fix structural failures and pass after the fix;
- a full current-schema structural corruption matrix is covered;
- PersistenceCoordinator rollback remains deterministic under repairable malformed durable state;
- new-branch, existing-branch, remote-advance and transport-failure guard scenarios are all proven;
- all available baseline gates pass fresh from final source;
- M8.6 equivalent fixes are carried forward and their locally executable script tests pass;
- external editor/device tiers are either freshly executed with artifacts or explicitly UNVERIFIED with exact blockers;
- docs/OpenSpec describe only evidence actually produced;
- the implementation branch is committed and pushed without force.

## Next milestone decision

Recommend M9 Closed Playtest Readiness only if M8.7 closes all discovered Critical/High state-integrity defects and no real certification result exposes a release blocker. Otherwise the next campaign must target the measured blocker, not content breadth.
