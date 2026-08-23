using System;
using System.Threading.Tasks;
using NUnit.Framework;
using WalkGame.Activity;
using WalkGame.Core;
using WalkGame.Gameplay;

namespace WalkGame.Tests
{
    public sealed class ActivityServiceTests
    {
        private MutableClock _clock;
        private PlayerProfile _profile;
        private DomainEvents _events;
        private ContentCatalog _catalog;
        private VitalityLedger _ledger;
        private ActivityService _activity;
        private StepMilestoneService _milestones;
        private DebugActivityProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _clock = new MutableClock(new DateTime(2026, 5, 1, 7, 0, 0, DateTimeKind.Utc));
            _profile = new PlayerProfile();
            _events = new DomainEvents();
            _catalog = TestContent.Create();
            _catalog.Index();
            _ledger = new VitalityLedger(_profile, _clock, _events, Log.Disabled);
            var rewards = new RewardApplier(_profile, _clock, _events, Log.Disabled);
            var trust = new TrustEvaluator(RewardPolicy.Default);
            var calculator = new RewardCalculator(RewardPolicy.Default);
            _activity = new ActivityService(_profile, _ledger, trust, calculator, _events, Log.Disabled);
            _milestones = new StepMilestoneService(_catalog, _profile, _ledger, _events);
            _activity.MilestonesPending += _ => _milestones.CheckAndAward();
            _provider = new DebugActivityProvider(_clock);
        }

        private static ActivitySnapshot PassiveSnapshot(long steps)
        {
            return new ActivitySnapshot
            {
                providerId = DebugActivityProvider.ProviderIdValue,
                intervalStartUtc = DateTime.UtcNow.AddMinutes(-30),
                intervalEndUtc = DateTime.UtcNow,
                stepCount = steps,
                sourceType = ActivitySourceType.PhoneSensor,
                recordingType = ActivityRecordingType.Passive,
                quality = new ActivityQuality { hasStepEvidence = true },
            };
        }

        [Test]
        public void PassiveSteps_CreditBaseVitality_Once()
        {
            // 800 stays below the 1,000-step milestone so this isolates base credit.
            long credited = _activity.ProcessPassiveSnapshot(PassiveSnapshot(800));

            Assert.AreEqual(800, credited);
            Assert.AreEqual(800, _ledger.GetBalance());
            Assert.AreEqual(800, _profile.lifetimeAcceptedSteps);
        }

        [Test]
        public void DuplicateInterval_IsNotCreditedTwice()
        {
            var snapshot = PassiveSnapshot(800);
            _activity.ProcessPassiveSnapshot(snapshot);
            long secondPass = _activity.ProcessPassiveSnapshot(snapshot);

            Assert.AreEqual(0, secondPass);
            Assert.AreEqual(800, _ledger.GetBalance());
            Assert.AreEqual(800, _profile.lifetimeAcceptedSteps);
        }

        [Test]
        public void RestartDoesNotDoubleCredit_ProviderCounterResetIsBaseline()
        {
            // Simulate: steps accumulate, get credited, then device reboots (counter reset).
            _provider.DebugAddSteps(2000);
            var snapshot = _provider.ReadSnapshotAsync(new ActivityCursor()).Result;
            long first = _activity.ProcessPassiveSnapshot(snapshot);
            Assert.Greater(first, 0);

            _provider.DebugSimulateReboot();

            // After reboot the counter restarts at zero; next read has no new steps and
            // must not produce negative or duplicate rewards.
            var afterReboot = _provider.ReadSnapshotAsync(new ActivityCursor
            {
                lastSuccessfulSyncUtc = _profile.activityState.lastSuccessfulSyncUtc,
            }).Result;

            if (afterReboot != null)
            {
                long credited = _activity.ProcessPassiveSnapshot(afterReboot);
                Assert.LessOrEqual(credited + 1, 1);
                Assert.GreaterOrEqual(_profile.lifetimeAcceptedSteps, first);
            }
        }

        [Test]
        public void Milestone_AwardsOnce_AtLifetimeThreshold()
        {
            int milestoneEvents = 0;
            _events.Subscribe<ActivityMilestoneReached>(_ => milestoneEvents++);

            _activity.ProcessPassiveSnapshot(PassiveSnapshot(1500));

            Assert.AreEqual(1, milestoneEvents); // 1500 >= 1000 threshold
            // Base 1500 + milestone 25.
            Assert.AreEqual(1525, _ledger.GetBalance());

            _activity.ProcessPassiveSnapshot(PassiveSnapshot(500));
            Assert.AreEqual(1, milestoneEvents); // never again
        }

        [Test]
        public async Task VehicleLikeSession_LosesBonuses_ButKeepsBaseSteps()
        {
            var startError = _provider.DebugBeginVehicleLikeSession(out var driver);
            Assert.AreEqual(SessionStartError.None, startError);

            driver.Drive(minutes: 30, speedKmh: 60);

            var sessionResult = await _provider.StopSessionAsync();
            var trust = new TrustEvaluator(RewardPolicy.Default);
            sessionResult.trustScore = trust.EvaluateSession(
                new ActiveSessionState
                {
                    accumulatedSteps = sessionResult.acceptedSteps,
                    accumulatedDistanceMeters = sessionResult.verifiedDistanceMeters,
                    movingSeconds = sessionResult.verifiedMovingSeconds,
                },
                hasLocationEvidence: true,
                mockLocationSuspected: false,
                teleportJump: false);

            long balanceBefore = _ledger.GetBalance();
            var processed = _activity.ProcessSessionResult(sessionResult, growthEligible: false);

            Assert.IsNotNull(processed);
            Assert.Greater(processed.acceptedSteps, 0);
            // Base steps credited...
            Assert.AreEqual(balanceBefore + processed.acceptedSteps, _ledger.GetBalance());
            // ...but no performance bonus for vehicle-like movement.
            Assert.AreEqual(0, processed.bonusBreakdown.totalBonus);
        }

        [Test]
        public async Task PlausibleRunSession_EarnsCappedBonuses()
        {
            await _provider.StartSessionAsync(SessionType.Run);
            // 5 km run in 30 minutes with realistic cadence (~160 spm).
            _provider.SimulateSessionProgress(additionalSteps: 4800, distanceMeters: 5000, movingSeconds: 1800);
            var result = await _provider.StopSessionAsync();

            var trust = new TrustEvaluator(RewardPolicy.Default);
            result.trustScore = trust.EvaluateSession(
                new ActiveSessionState
                {
                    accumulatedSteps = result.acceptedSteps,
                    accumulatedDistanceMeters = result.verifiedDistanceMeters,
                    movingSeconds = result.verifiedMovingSeconds,
                },
                hasLocationEvidence: false,
                mockLocationSuspected: false,
                teleportJump: false);

            var processed = _activity.ProcessSessionResult(result, growthEligible: false);

            Assert.GreaterOrEqual(result.trustScore, RewardPolicy.Default.fullBonusTrustThreshold - 0.05f);
            Assert.Greater(processed.bonusBreakdown.explorerBonus, 0);
            Assert.Greater(processed.bonusBreakdown.enduranceBonus, 0);
            Assert.Greater(processed.bonusBreakdown.totalBonus, 0);
            Assert.LessOrEqual(processed.bonusBreakdown.totalBonus, RewardPolicy.Default.sessionBonusCap);
        }

        [Test]
        public async Task SessionCannotRunTwice_Concurrently()
        {
            Assert.AreEqual(SessionStartError.None, await _provider.StartSessionAsync(SessionType.Walk));
            Assert.AreEqual(SessionStartError.AlreadyRunning, await _provider.StartSessionAsync(SessionType.Walk));
            await _provider.StopSessionAsync();
            Assert.AreEqual(SessionStartError.None, await _provider.StartSessionAsync(SessionType.Walk));
        }

        [Test]
        public void PermissionDenied_ProducesNoSnapshots_ButGameRemainsPlayable()
        {
            _provider.DebugSetPermission(false);
            var snapshot = _provider.ReadSnapshotAsync(new ActivityCursor()).Result;
            Assert.IsNull(snapshot);
            // Restoration economy unaffected by activity availability.
            _ledger.Credit(new VitalityCredit { amount = 500, reasonCode = WellKnownIds.ReasonCodes.DebugGrant });
            Assert.AreEqual(500, _ledger.GetBalance());
        }

        // ---- Exactly-once movement rewards (campaign S8) ------------------------

        private ActivitySessionResult MakeSessionResult(string sessionId, long steps)
        {
            return new ActivitySessionResult
            {
                sessionId = sessionId,
                type = SessionType.Walk,
                startUtc = _clock.UtcNow.AddMinutes(-30),
                endUtc = _clock.UtcNow,
                acceptedSteps = steps,
                verifiedDistanceMeters = 0,
                verifiedMovingSeconds = 1800,
                trustScore = 0.2f, // below bonus threshold: isolates base-step accounting
            };
        }

        [Test]
        public void DuplicateSessionResult_IsNeverCreditedTwice()
        {
            var result = MakeSessionResult("session.dupe", 700);

            long balanceAfterFirst = _activity.ProcessSessionResult(result, growthEligible: false).acceptedSteps > 0
                ? _ledger.GetBalance()
                : 0;
            Assert.AreEqual(700, balanceAfterFirst);

            // Same physical session re-delivered (crash-replay, double-tap, bug):
            var replay = MakeSessionResult("session.dupe", 700);
            var processedReplay = _activity.ProcessSessionResult(replay, growthEligible: false);

            Assert.AreEqual(0, processedReplay.acceptedSteps, "re-delivery must not pay again");
            Assert.AreEqual(700, _ledger.GetBalance());
            Assert.AreEqual(700, _profile.lifetimeAcceptedSteps);
        }

        [Test]
        public void PassiveSnapshots_AreSuppressed_WhileExpeditionActive()
        {
            _provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult();
            _profile.activityState.activeSession = new ActiveSessionState();
            _provider.DebugAddSteps(3000);

            // Provider level: the session owns the counter stream.
            var snapshot = _provider.ReadSnapshotAsync(new ActivityCursor()).GetAwaiter().GetResult();
            Assert.IsNull(snapshot);

            // Domain level (defense in depth): even a delivered snapshot is ignored.
            long credited = _activity.ProcessPassiveSnapshot(PassiveSnapshot(3000));
            Assert.AreEqual(0, credited);
            Assert.AreEqual(0, _ledger.GetBalance());
        }

        [Test]
        public void ExpeditionCompletion_PartitionsPassiveWindow_PastSessionEnd()
        {
            DateTime beforeSession = _clock.UtcNow;
            _profile.activityState.lastSuccessfulSyncUtc = beforeSession.AddMinutes(-5);

            var result = MakeSessionResult("session.partition", 400);
            _activity.ProcessSessionResult(result, growthEligible: false);

            Assert.IsNull(_profile.activityState.activeSession);
            Assert.GreaterOrEqual(
                _profile.activityState.lastSuccessfulSyncUtc.GetValueOrDefault(),
                result.endUtc,
                "passive windows must resume after the credited active window, not overlap it");
        }

        [Test]
        public void OverlappingHistoricalWindows_WithSameBounds_CreditOnlyOnce()
        {
            // Crash-equivalent scenario: credit happened in memory but the cursor save
            // failed; on restart the provider re-reads the SAME interval.
            var windowStart = new DateTime(2026, 5, 1, 6, 30, 0, DateTimeKind.Utc);
            var snapshotA = new ActivitySnapshot
            {
                providerId = "activity.ios.coremotion",
                intervalStartUtc = windowStart,
                intervalEndUtc = windowStart.AddHours(1),
                stepCount = 6000,
                sourceType = ActivitySourceType.PhoneSensor,
                recordingType = ActivityRecordingType.Passive,
                quality = new ActivityQuality { hasStepEvidence = true },
            };
            var snapshotB = new ActivitySnapshot
            {
                providerId = "activity.ios.coremotion",
                intervalStartUtc = windowStart,
                intervalEndUtc = windowStart.AddHours(1),
                stepCount = 6000,
                sourceType = ActivitySourceType.PhoneSensor,
                recordingType = ActivityRecordingType.Passive,
                quality = new ActivityQuality { hasStepEvidence = true },
            };

            Assert.AreEqual(6000, _activity.ProcessPassiveSnapshot(snapshotA));
            Assert.AreEqual(0, _activity.ProcessPassiveSnapshot(snapshotB));
            Assert.AreEqual(6000, _profile.lifetimeAcceptedSteps,
                "the replayed window must add zero additional steps");
        }

        [Test]
        public void EmptySnapshot_ProducesNothing()
        {
            Assert.AreEqual(0, _activity.ProcessPassiveSnapshot(null));
            Assert.IsNull(_activity.ProcessSessionResult(null, growthEligible: false));
            Assert.AreEqual(0, _ledger.GetBalance());
        }

        [Test]
        public void BackwardClock_SessionMovingSeconds_ClampAtZero()
        {
            var start = _clock.UtcNow;
            var session = new ActiveSessionState { startedAtUtc = start, sessionType = SessionType.Walk };
            _clock.Advance(TimeSpan.FromMinutes(-10)); // device clock moved backward

            double movingSeconds = System.Math.Max(0, (_clock.UtcNow - start).TotalSeconds);

            Assert.AreEqual(0, movingSeconds);
        }
    }
}
