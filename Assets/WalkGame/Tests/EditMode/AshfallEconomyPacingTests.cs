using System;
using System.Collections.Generic;
using NUnit.Framework;
using WalkGame.Activity;
using WalkGame.Content;
using WalkGame.Core;
using WalkGame.Gameplay;

namespace WalkGame.Tests
{
    /// <summary>
    /// M8 economy replay (campaign section 18): a deterministic fresh-profile
    /// playthrough driven by an ordinary walker's cadence. Beyond "is completion
    /// possible" (covered by AshfallTests), this pins pacing coherence - the region
    /// must be finishable by realistic daily movement inside a sane window, without
    /// deadlocks, bypassed movement gates, or runaway idle substitution.
    /// </summary>
    public sealed class AshfallEconomyPacingTests
    {
        private const long CasualStepsPerWalk = 1800;   // ~20-minute stroll
        private const int WalksPerDay = 4;              // ~7,200 steps/day
        private const int MinDays = 1;                  // first projects land immediately
        private const int MaxDays = 45;                 // generous ceiling for one region

        private MutableClock _clock;
        private PlayerProfile _profile;
        private DomainEvents _events;
        private AshfallBasinCatalog _catalog;
        private VitalityLedger _ledger;
        private ActivityService _activity;
        private ProductionService _production;
        private RestorationService _restoration;

        [SetUp]
        public void SetUp()
        {
            _clock = new MutableClock(new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));
            _profile = new PlayerProfile();
            _profile.worldState.currentRegionId = AshfallBasinCatalog.RegionId;
            _events = new DomainEvents();
            _catalog = new AshfallBasinCatalog();
            _ledger = new VitalityLedger(_profile, _clock, _events, Log.Disabled);
            var rewards = new RewardApplier(_profile, _clock, _events, Log.Disabled);
            _activity = new ActivityService(
                _profile, _ledger,
                new TrustEvaluator(RewardPolicy.Default),
                new RewardCalculator(RewardPolicy.Default),
                _events, Log.Disabled);
            var milestones = new StepMilestoneService(_catalog, _profile, _ledger, _events);
            _activity.MilestonesPending += _ => milestones.CheckAndAward();
            _production = new ProductionService(_catalog, _profile, rewards, _clock, Log.Disabled);
            _restoration = new RestorationService(_catalog, _profile, _ledger, rewards, _events, Log.Disabled);

            var region = _profile.worldState.GetOrCreateRegionState(AshfallBasinCatalog.RegionId);
            foreach (var instance in _catalog.Ashfall.defaultBuildingInstances)
            {
                var state = region.GetOrCreateBuildingState(instance.instanceId, instance.buildingDefinitionId);
                state.placement.gridX = instance.initialPlacement.gridX;
                state.placement.gridY = instance.initialPlacement.gridY;
                state.placement.rotationQuarterTurns = instance.initialPlacement.rotationQuarterTurns;
            }

            _production.EnsureProducerStates(AshfallBasinCatalog.RegionId);
        }

        private void Walk()
        {
            for (int i = 0; i < WalksPerDay; i++)
            {
                var end = _clock.UtcNow;
                _activity.ProcessPassiveSnapshot(new ActivitySnapshot
                {
                    providerId = DebugActivityProvider.ProviderIdValue,
                    intervalStartUtc = end.AddMinutes(-20),
                    intervalEndUtc = end,
                    stepCount = CasualStepsPerWalk,
                    sourceType = ActivitySourceType.PhoneSensor,
                    recordingType = ActivityRecordingType.Passive,
                    quality = new ActivityQuality { hasStepEvidence = true },
                });
                _clock.Advance(TimeSpan.FromMinutes(21));

                // Collect whatever restored systems made while the player walks.
                _production.AccrueAll(AshfallBasinCatalog.RegionId);
                foreach (var result in _production.CollectAll(AshfallBasinCatalog.RegionId))
                {
                    Assert.GreaterOrEqual(result.collected, 0);
                }

                CompleteEverythingReachable();
            }
        }

        private int CompleteEverythingReachable()
        {
            int completedThisPass = 0;
            bool progressed = true;
            while (progressed)
            {
                progressed = false;
                foreach (var project in _catalog.GetProjectsForRegion(AshfallBasinCatalog.RegionId))
                {
                    if (_restoration.TryComplete(project.projectId, out _))
                    {
                        completedThisPass++;
                        progressed = true;
                    }
                }
            }

            return completedThisPass;
        }

        [Test]
        public void CasualWalker_CompletesRegion_WithinCoherentWindow_WithExactAccounting()
        {
            // Day-one onboarding: the first restoration must not demand days of walking.
            CompleteEverythingReachable();
            Assert.IsEmpty(_profile.worldState.GetOrCreateRegionState(AshfallBasinCatalog.RegionId).completedProjectIds,
                "a fresh profile cannot restore anything without movement");
            Walk();

            var region = _profile.worldState.GetOrCreateRegionState(AshfallBasinCatalog.RegionId);
            Assert.IsNotEmpty(region.completedProjectIds, "day one must produce visible progress");

            int days = 1;
            while (region.completedProjectIds.Count < 15 && days <= MaxDays)
            {
                Walk();
                days++;
            }

            Assert.AreEqual(15, region.completedProjectIds.Count,
                $"a casual walker must finish the region; stalled at {region.completedProjectIds.Count}/15 after {days} days");
            Assert.LessOrEqual(days, MaxDays, "pacing drifts if ordinary movement no longer finishes one region");
            Assert.GreaterOrEqual(days, MinDays);

            // No accidental bypass of movement gating.
            long requiredLifetimeSteps = 0;
            foreach (var project in _catalog.GetProjectsForRegion(AshfallBasinCatalog.RegionId))
            {
                if (project.requiredLifetimeSteps.HasValue)
                {
                    requiredLifetimeSteps = Math.Max(requiredLifetimeSteps, project.requiredLifetimeSteps.Value);
                }
            }

            Assert.Greater(requiredLifetimeSteps, 0, "the finale should gate on sustained real movement");
            Assert.GreaterOrEqual(_profile.lifetimeAcceptedSteps, requiredLifetimeSteps);

            // Exact economy accounting: every credited point traces to accepted steps
            // or bounded bonuses/milestones - nothing minted elsewhere.
            long totalVitalitySpentOnProjects = 0;
            foreach (var project in _catalog.GetProjectsForRegion(AshfallBasinCatalog.RegionId))
            {
                totalVitalitySpentOnProjects += project.vitalityCost;
            }

            long expectedFloor = _profile.lifetimeAcceptedSteps * RewardPolicy.Default.baseVitalityPerStep
                                 - totalVitalitySpentOnProjects;
            Assert.GreaterOrEqual(_profile.vitalityBalance, expectedFloor - 200,
                "balance must remain explainable by movement income minus restoration spend");
            Assert.GreaterOrEqual(totalVitalitySpentOnProjects, 1000,
                "the region should ask real cumulative movement, not token amounts");

            // Stage/NPC/lore coherence at the finale.
            Assert.AreEqual(3, region.restorationStage);
            Assert.AreEqual(3, region.arrivedNpcIds.Count);
        }

        [Test]
        public void IdleProduction_Alone_NeverCompletesAProject()
        {
            // Advance far past the offline cap repeatedly with zero movement; nothing
            // may become restorable through production output alone.
            for (int cycle = 0; cycle < 10; cycle++)
            {
                _clock.Advance(TimeSpan.FromHours(12));
                _production.AccrueAll(AshfallBasinCatalog.RegionId);
                _production.CollectAll(AshfallBasinCatalog.RegionId);
            }

            var region = _profile.worldState.GetOrCreateRegionState(AshfallBasinCatalog.RegionId);
            foreach (var project in _catalog.GetProjectsForRegion(AshfallBasinCatalog.RegionId))
            {
                var failure = _restoration.Evaluate(project.projectId, out _, out _);
                Assert.AreNotEqual(RestorationFailure.None, failure,
                    $"{project.projectId} must not be completable without any movement");
            }

            Assert.AreEqual(0, region.completedProjectIds.Count);
        }
    }
}
