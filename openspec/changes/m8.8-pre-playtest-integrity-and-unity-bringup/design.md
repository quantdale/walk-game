# M8.8 Design — Pre-Playtest Integrity & Unity Bring-Up Closure

Status: COMPLETE
Planned-From: main@cf260d04fefbb2d5e7da265de5ae03a9aa768a0a

## 1. Design principles

### 1.1 Semantic evidence outranks textual confidence

A Unity project is not compile-certified because asmdefs, metas and package manifests look structurally correct. Static validation remains useful, but semantic compilation under the pinned editor is its own evidence tier.

The campaign must preserve the distinction:

STATIC -> EDITOR-COMPILE -> EDITMODE -> PLAYMODE -> BUILD -> DEVICE.

A lower tier never implies a higher tier.

### 1.2 A success return requires its postcondition

Persistence compatibility APIs must not return success on partial or unchanged state. For migration:

`TryMigrateToCurrent(profile) == true`

means:

`profile != null && profile.schemaVersion == SaveSchemaVersions.Current`.

There is no "best effort" success state.

### 1.3 Migration is a finite state machine

Every supported migration step must:
1. match one exact source version;
2. deterministically transform only that version;
3. increment to one exact next version;
4. be testable independently;
5. fail if no step exists.

The migration loop must verify forward progress. It must never spin, break-and-succeed, skip unknown versions, or downgrade newer data.

Because schema v1 is currently the initial real schema, an explicit schema below v1 should fail closed unless the executor introduces and documents a genuine migration from that schema.

### 1.4 Clean-checkout state is part of build provenance

Certification must identify not just the Git SHA but the project state Unity actually compiled/built.

If first import/setup generates stable canonical settings required for normal operation, prefer editor-generated tracked state once a genuine Unity editor can materialize it.

If generation is intentionally retained:
- the generator must be deterministic and idempotent;
- the evidence harness must record the pre/post source status;
- unexpected project mutation must fail certification;
- generated artifact identity must be bound to the build evidence.

Never hand-author opaque Unity serialized project assets just to make a checkbox green.

### 1.5 Reproduce platform behavior before changing native contracts

Android permission behavior and iOS callback/lifetime behavior depend on platform runtime semantics. Source review identifies risk, not proof of device behavior.

For each platform finding:
- define a state table/invariant first;
- reproduce on the strongest available real tier;
- add an engine-testable seam for deterministic regression when feasible;
- change native/provider code only when the reproduced contract or official SDK semantics justify it.

### 1.6 Provider generation ownership is explicit

After `IActivityProvider.Shutdown()`:
- new operations are refused;
- native monitoring/live session work is stopped;
- no late completion may mutate canonical profile state;
- no prepared movement claim may be acknowledged as durable merely because teardown occurred;
- old-generation callbacks may be discarded/rejected safely but cannot attach to a new generation accidentally.

This extends the existing ADR 0011 intent; M8.8 must not invent a parallel reward path.

### 1.7 Canonical numeric mutation must not wrap

Currency/resources/region scores are canonical state. Integer overflow must not silently:
- wrap positive progress negative;
- clamp wrapped values to zero;
- create a huge positive value from negative underflow.

Use checked failure or explicit saturation according to the domain contract, with boundary tests. Do not silently change normal pacing.

## 2. Unity semantic compile architecture

Add one dedicated script, recommended name:

`scripts/verify-unity-compile.ps1`

It should reuse shared certification helpers and:
- resolve repository root;
- prove `UNITY_EDITOR_PATH` exists and matches exact pin `6000.3.4f1`;
- record source SHA and dirty/clean state before launch;
- delete or uniquely timestamp prior compile evidence so stale artifacts cannot satisfy the run;
- launch Unity batch mode against the project without running test suites;
- force a real import/script compilation cycle;
- write a full editor log to ignored evidence output;
- detect Unity process failure and compiler/import errors;
- verify the current run actually reached a completed import/compile state;
- record machine-readable evidence: source SHA, dirty state, editor path/version, start/end UTC, exit code, log path, compiler-error count, post-run dirty state;
- fail when unexpected tracked/untracked canonical project mutation appears, unless the run is explicitly the controlled materialization task;
- preserve artifacts on failure.

The executor may choose another exact implementation if it proves the same semantics. Do not infer compile PASS from `verify-unity-static.ps1`.

## 3. Editor source fix

`WalkGameEditorTools.cs` should use the actual Unity namespaces rather than local aliases that obscure API ownership.

Expected minimal correction:
- import `UnityEngine.Rendering` for `GraphicsSettings`;
- import `UnityEditor.Build` for `IPostprocessBuildWithReport`.

Then run a full Editor-assembly sweep under the real compiler because the first error may mask additional errors.

Do not stop at making a text scanner happy.

## 4. Save migration contract

Recommended shape:

- define `SaveSchemaVersions.MinimumSupported` or equivalent explicit policy;
- reject `profile == null`;
- reject `schemaVersion > Current`;
- reject `schemaVersion < MinimumSupported`;
- while version < Current:
  - dispatch one exact known migration;
  - capture before version;
  - run step;
  - require version == before + 1, otherwise fail;
- after loop require version == Current;
- only then return true.

For current v1:
- v1 -> success unchanged;
- explicit v0 / negative -> fail unless a real migration is deliberately authored;
- v2+ -> forward-schema refusal.

Do not coerce unsupported schema numbers to v1. That would reinterpret unknown data without evidence.

## 5. First-import / URP state

The early ADR 0003 bootstrap strategy remains historical context, but a device-ready build should be reproducible.

Controlled materialization lane, only with genuine Unity:
1. start from a clean implementation worktree at known SHA;
2. run the fixed semantic import/project setup;
3. capture the exact diff;
4. classify every generated file:
   - canonical project state to track;
   - cache/generated output that must remain ignored;
   - machine-specific state that must not be committed;
5. rerun setup and prove idempotence;
6. run semantic compile again from the materialized clean state;
7. commit only stable canonical state.

If no licensed editor exists, do not create `Assets/Settings/*.asset` manually. Leave this tier UNVERIFIED with the precise blocker.

## 6. Android permission state model

Model the observable states separately from local request history.

At minimum test:
- fresh install / no decision;
- grant;
- denial;
- denial then app process restart;
- "do not ask again"/platform equivalent where applicable;
- Settings grant after denial;
- Settings revoke after grant;
- sensor unavailable;
- API < 29 behavior if still supported by minSdk/runtime code.

The coordinator must not stack prompts. A request must have a bounded completion time and must not spend a long timeout merely because a prior durable denial was misclassified as fresh.

Prefer platform-authoritative signals. If some states are inherently ambiguous, expose that ambiguity honestly and design UI/request cadence safely rather than claiming certainty.

## 7. iOS callback/lifetime model

Audit these ownership objects:
- native process-global `CMPedometer`;
- native historical query handlers;
- native callback pointer;
- managed delegate lifetime under IL2CPP;
- managed static pending-query map;
- provider instance generation/shutdown;
- active session accumulators.

Required invariant:
a callback carries enough identity to prove whether a live owner still exists. A provider shutdown or GameHost recomposition cannot let an old query mutate/resolve a new provider generation incorrectly.

If managed callback delegate retention requires a strong static field under IL2CPP, make it explicit and test/source-document it. If native reads/writes need serialization/atomicity, use the narrowest correct mechanism supported by the platform.

Do not make unverified threading claims from Windows-only source inspection.

## 8. Ledger/resource numeric policy

### Vitality spend audit identity
A spend requires a non-empty reason code just like a credit. Existing valid callers should remain unchanged.

### Resource/score overflow
Choose and document one behavior:
- reject/throw before mutation for impossible authored reward data; or
- saturate to explicit domain maxima.

For a single-player local game, saturating canonical resource/score values may be more recovery-friendly, but the executor must preserve normal small-value behavior and add boundary tests.

Never allow unchecked wraparound.

## 9. Verification topology

### Locally available / no Unity license
- repository identity;
- domain tests;
- focused migration/ledger/reward tests;
- Unity static structure;
- release hygiene;
- agent guards;
- certification script fixture tests;
- compile-wrapper parse/fixture semantics;
- git diff check.

### Licensed Unity
- semantic import/compile;
- setup/materialization/idempotence;
- EditMode;
- PlayMode.

### Android build/device
- IL2CPP ARM64 build provenance;
- lifecycle smoke;
- permission state matrix;
- physical step counter exactly-once;
- touch/UX;
- performance.

### iOS
- Xcode generation;
- plist/postprocessor;
- build/sign/install;
- permission/history/live-session/lifecycle;
- AOT callback ownership.

## 10. Evidence artifact requirements

Every editor/build/device PASS should identify:
- source SHA;
- dirty/clean status;
- tool/editor version;
- target/device identity when relevant;
- start/end time;
- exact script/command;
- result summary;
- artifact/log location;
- output artifact hash for builds.

A historical log or a log from another SHA cannot satisfy a current gate.

## 11. Documentation rule

Update:
- `docs/IMPLEMENTATION_STATUS.md`;
- `docs/TESTING_AND_PERFORMANCE.md`;
- `scripts/README.md`;
- ADR 0003 if the first-import project-state decision changes;
- architecture/data docs only when behavior changes;
- this OpenSpec and execution prompt.

Do not rewrite unrelated product design documents.

## 12. Completion architecture

M8.8 closes implementation-level pre-playtest integrity. It does not need to fabricate unavailable physical evidence.

At completion:
- confirmed source defects are fixed;
- locally executable regressions are green;
- external tiers carry exact blockers if unavailable;
- if editor/device prerequisites were available, their fresh results control the next-campaign recommendation.

M9 is the default next step only after no Critical/High pre-playtest defect remains.
