# Walk Game — Canonical Data Model

## 1. Purpose

This document defines the minimum persistent and runtime data structures required for the vertical slice. Names are conceptual; coding agents may adjust exact C# syntax but must preserve the ownership boundaries and invariants.

## 2. Player profile

```text
PlayerProfile
- schemaVersion: int
- profileId: string
- createdAtUtc: timestamp
- lastSavedAtUtc: timestamp
- lifetimeAcceptedSteps: long
- lifetimeVerifiedDistanceMeters: double
- vitalityBalance: long
- resources: Dictionary<ResourceId, long>
- worldState: WorldState
- activityState: ActivitySyncState
- achievementState: AchievementState
- settings: PlayerSettings
```

## 3. World state

```text
WorldState
- currentEra: int
- currentRegionId: string
- unlockedRegionIds: Set<string>
- regionStates: Dictionary<RegionId, RegionState>
```

## 4. Region state

```text
RegionState
- regionId: string
- restorationStage: int
- ecologyScore: int
- infrastructureScore: int
- communityScore: int
- knowledgeScore: int
- completedProjectIds: Set<string>
- buildingStates: Dictionary<BuildingInstanceId, BuildingState>
- discoveredLoreIds: Set<string>
- arrivedNpcIds: Set<string>
- producerStates: Dictionary<ProducerId, ProducerState>
- lastVisitedAtUtc: timestamp
```

Invariant: `regionId` must correspond to one immutable `RegionDefinition`.

## 5. Building state

```text
BuildingState
- instanceId: string
- definitionId: string
- lifecycleState: Ruin | Restoring | Restored
- upgradeTier: int
- placement: BuildingPlacement
- restorationCompletedAtUtc: timestamp?
```

## 6. Building placement

```text
BuildingPlacement
- localPositionX: float
- localPositionY: float
- localPositionZ: float
- rotationYDegrees: float
- placementVersion: int
```

For grid-based placement, optionally persist integer grid coordinates rather than raw floats:

```text
gridX
gridY
rotationQuarterTurns
```

Grid coordinates are preferable if the region supports them because they are deterministic and migration-friendly.

## 7. Producer state

```text
ProducerState
- producerId: string
- buildingInstanceId: string
- lastCheckpointUtc: timestamp
- storedOutput: long
```

Do not persist a continuously incrementing timer.

## 8. Activity sync state

```text
ActivitySyncState
- providerId: string
- lastSuccessfulSyncUtc: timestamp?
- providerCursor: string?
- androidLastRawStepCounter: double?
- androidLastCounterObservedUtc: timestamp?
- creditedIntervals: bounded dedup structure
- activeSession: ActiveSessionState?
```

The exact provider cursor is adapter-specific and should be wrapped so platform details do not leak into game logic.

## 9. Activity snapshot

Normalized provider result:

```text
ActivitySnapshot
- providerId: string
- intervalStartUtc: timestamp
- intervalEndUtc: timestamp
- stepCount: long
- estimatedDistanceMeters: double?
- sourceType: PhoneSensor | Wearable | Imported | Unknown
- recordingType: Passive | Active | Manual | Unknown
- providerRecordIds: List<string>
- quality: ActivityQuality
```

## 10. Activity quality

```text
ActivityQuality
- hasStepEvidence: bool
- hasDistanceEvidence: bool
- hasCadenceEvidence: bool
- hasLocationEvidence: bool
- accuracyScore: float 0..1
- suspiciousFlags: Set<ActivitySuspicionFlag>
```

## 11. Active session state

```text
ActiveSessionState
- sessionId: string
- sessionType: Walk | Run
- startedAtUtc: timestamp
- initialStepBaseline: long?
- accumulatedSteps: long
- accumulatedDistanceMeters: double
- movingSeconds: double
- samples: transient / bounded buffer
```

Do not persist raw GPS history unless the product explicitly decides it is required. For crash recovery, persist only minimum session checkpoints where possible.

## 12. Activity session result

```text
ActivitySessionResult
- sessionId: string
- type: Walk | Run
- startUtc: timestamp
- endUtc: timestamp
- acceptedSteps: long
- verifiedDistanceMeters: double
- verifiedMovingSeconds: double
- cadenceConsistency: float?
- trustScore: float
- bonusBreakdown: ActivityBonusBreakdown
```

## 13. Bonus breakdown

```text
ActivityBonusBreakdown
- explorerBonus: long
- enduranceBonus: long
- rhythmBonus: long
- tempoBonus: long
- growthBonus: long
- totalBonus: long
- capped: bool
```

This structure makes balancing/debugging explainable.

## 14. Vitality ledger

Even for local MVP, maintain an append-style recent transaction log or bounded audit list.

```text
VitalityTransaction
- transactionId: string
- timestampUtc: timestamp
- type: Credit | Spend
- amount: long
- reasonCode: string
- relatedEntityId: string?
- resultingBalance: long
```

Reason examples:
- `activity.steps`
- `activity.explorer_bonus`
- `milestone.lifetime_steps`
- `project.restore`
- `project.landmark`

Keep at least the most recent N transactions locally for debugging. If the backend later becomes authoritative, the transaction model can migrate to a server ledger.

## 15. Restoration project definition

Immutable content:

```text
RestorationProjectDefinition
- projectId: string
- regionId: string
- category: Micro | Building | Ecosystem | Landmark | Era
- vitalityCost: long
- resourceCosts: Dictionary<ResourceId, long>
- prerequisiteProjectIds: List<string>
- requiredLifetimeSteps: long?
- requiredRegionStage: int?
- rewardActions: List<RewardActionDefinition>
- visualStageId: string?
- titleKey: string
- descriptionKey: string
```

## 16. Reward actions

Use data-driven actions rather than giant switch statements.

Examples:

```text
UnlockBuilding(instanceId)
SetBuildingRestored(instanceId)
UnlockProject(projectId)
AddRegionScore(scoreType, amount)
UnlockNpc(npcId)
SetEnvironmentFlag(flagId)
UnlockRegion(regionId)
GrantResource(resourceId, amount)
```

## 17. Building definition

```text
BuildingDefinition
- definitionId: string
- displayNameKey: string
- footprint: FootprintDefinition
- movableAfterRestore: bool
- maxUpgradeTier: int
- visualPrefabReference
- upgradeDefinitions[]
- producerDefinitionId: string?
```

## 18. Region definition

```text
RegionDefinition
- regionId: string
- displayNameKey: string
- sceneReference
- defaultBuildingInstances[]
- projectDefinitions[]
- stageThresholds[]
- allowedPlacementAreaReference
- exploreSpawnId
- visualProfiles[]
```

## 19. Default building instance

```text
DefaultBuildingInstanceDefinition
- instanceId: string
- buildingDefinitionId: string
- initialPlacement
- startsRestored: bool
- fixedPlacement: bool
```

`instanceId` must be stable forever once shipped.

## 20. Save invariants

Validation on load must reject or repair impossible state:

- Vitality balance cannot be negative.
- Resource counts cannot be negative unless explicitly designed.
- Building definition IDs must resolve.
- Building instance IDs must belong to their region.
- Upgrade tier must not exceed definition max.
- Placement must be finite numbers.
- Completed projects must resolve or be preserved through migration mapping.
- Timestamps far in the future should be flagged for reconciliation.

## 21. Migration strategy

Every persisted model change that breaks compatibility must increment `schemaVersion`.

Example:

```text
v1 → v2: add communityScore = 0
v2 → v3: convert building rotation degrees to quarterTurns
v3 → v4: rename region ID through explicit map
```

Never rely on default deserializer behavior for destructive schema changes.

## 22. Content versioning

Save schema and content version are different concerns.

Add later if needed:

```text
contentRevision
```

This helps migrate project IDs/building definitions when content patches change authored data.

## 23. Serialization recommendations

For prototype:
- Human-readable JSON is acceptable and easy to debug.
- Wrap serialization behind `ISaveSerializer`.

For production:
- Compression/encryption/MessagePack may be considered for size/performance/tamper resistance.
- Do not mistake client-side encryption for anti-cheat authority.

## 24. Time zones

Store canonical timestamps in UTC.

For daily presentation and streak-like systems:
- Store the time-zone offset or local date at the time of event if needed.
- Avoid recalculating historical days solely from current timezone.

## 25. Data minimization

Game save should not contain:
- Raw continuous GPS route history by default.
- HealthKit/Health Connect samples copied wholesale.
- Unnecessary personal health metrics.

Persist only derived gameplay values and provider cursors needed to avoid duplicate credit.

## 26. Test fixtures

Create deterministic test profiles:

- Fresh profile.
- 10k-step profile.
- Region half-restored.
- Fully restored region.
- Building moved from default.
- Android counter reset.
- iOS seven-day reconciliation.
- Corrupted save recovery.
- Future timestamp anomaly.

These fixtures should be committed once code exists and reused in EditMode/PlayMode tests.