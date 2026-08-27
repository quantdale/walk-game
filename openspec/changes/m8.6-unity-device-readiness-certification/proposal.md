# M8.6 Proposal — Unity First-Import & Device Readiness Certification

**Status:** COMPLETE — in-repo certification-harness lanes executed; EDITOR/DEVICE/iOS lanes UNVERIFIED by environment blocker
**Depends on:** completed M8.5 Runtime Ownership & Rollback Fidelity / ADR 0011  
**Target:** M8 — Device Ready

## Problem

The repository has reached diminishing returns from additional speculative headless hardening. Its engine-free correctness gates are strong, but the current vertical slice is still missing the evidence that matters most before closed playtest:

- a clean licensed Unity import and semantic compile of every assembly;
- passing EditMode and PlayMode certification on the current tree;
- a release-shaped Android IL2CPP + ARM64 development build;
- deterministic install/launch/background/resume/force-stop evidence;
- real `TYPE_STEP_COUNTER` and `ACTIVITY_RECOGNITION` lifecycle proof on physical Android hardware;
- exactly-once movement probes across screen-off, background, process death and reboot;
- physical touch/safe-area/Builder/Explore/UI/save-recovery validation;
- measured frame time, GC, memory, battery and thermal baselines;
- iOS compile/device evidence when a macOS/Xcode/signing environment is genuinely available.

The certification harness itself also has a small number of fail-open/ambiguity issues that could overstate or muddy evidence. These must be repaired as part of certification rather than treated as unrelated cleanup.

## Proposed change

Execute one coherent **M8.6 Unity First-Import & Device Readiness Certification** campaign with five ordered lanes:

1. **Unity compile/import gate:** establish a real pinned-editor compile; fix compile/import errors, especially editor-only/platform-only code invisible to the standalone test harness.
2. **Editor runtime certification:** run and harden EditMode/PlayMode gates; reproduce Bootstrap, save, activity, restoration, placement, Builder/Explore and recovery flows in Unity.
3. **Android build/lifecycle gate:** install the required Build Support legitimately, build IL2CPP/ARM64, make smoke target selection deterministic, and capture lifecycle artifacts.
4. **Physical Android movement/performance gate:** execute the repository's device checklist on genuine step-counter hardware, including exactly-once and battery/thermal/performance evidence.
5. **iOS readiness/certification lane:** run only where macOS/Xcode/signing exist. Otherwise perform only deterministic readiness checks and record the external blocker honestly.

The executor may fix defects discovered by these gates. Any fix to correctness-critical domain/activity/persistence behavior must add or extend the lowest viable deterministic regression test; Unity-only fixes must add EditMode/PlayMode coverage where feasible; native fixes must add platform-neutral tests where behavior can be extracted without faking OS semantics.

## Goals

- Convert the largest current evidence tier from UNVERIFIED to proven where the environment permits.
- Make all certification scripts fail closed and bind evidence to an exact editor/build/device identity.
- Obtain one reproducible Android build and runtime artifact set.
- Certify exactly-once real movement on at least one genuine step-counter device when available.
- Establish honest device-performance baselines before optimization.
- Produce a clear M8 exit matrix: PASS, FAIL, or UNVERIFIED with specific prerequisite/blocker for every gate.

## Non-goals

This campaign does **not** add:

- Region 2 or new region content;
- HealthKit / Health Connect;
- new GPS/location scope beyond existing optional Expedition behavior;
- cloud save, accounts, social or multiplayer;
- broad art overhaul;
- Addressables migration;
- economy/reward rebalance without a defect;
- speculative LOD/vegetation/shader optimization before measurements identify a real bottleneck;
- bypasses for Unity licensing, OS permissions, signing, UAC/elevation or hardware prerequisites.

## Success criteria

The campaign succeeds when every locally executable lane is either:

- backed by fresh reproducible PASS evidence, or
- blocked by a precisely documented external prerequisite after all legitimate in-repo work is exhausted.

At minimum, with a licensed editor but no physical device, success requires:

- clean Unity compile/import;
- EditMode result XML with green results;
- PlayMode result XML with green results;
- all prior standalone/static/privacy gates green;
- certification scripts hardened against false-positive/ambiguous evidence;
- Android build attempted if Build Support is present, with exact result captured.

With Android Build Support + genuine step-counter hardware, success additionally requires the mandatory Android device checklist subset and exactly-once probes defined in `specs/device-readiness/spec.md`.

## 12-hour execution budget

The executor is authorized to work through the full prioritized queue for **up to 12 hours**. It should keep going after individual fixes/tests succeed, moving to the next highest-value certification lane. It must not pad the session with unrelated changes merely to consume time; completing all available gates earlier is acceptable and preferable to fabricated work.
