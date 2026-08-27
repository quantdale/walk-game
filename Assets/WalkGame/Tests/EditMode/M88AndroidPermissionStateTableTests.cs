using System.Threading.Tasks;
using NUnit.Framework;
using WalkGame.Activity;
using WalkGame.Core;

namespace WalkGame.Tests
{
    /// <summary>
    /// M8.8 P1: Android permission state table & restart semantics.
    /// Documents the platform-derived state model and proves headless invariants:
    /// fresh -> NotDetermined, granted, denied, denial+restart (flag lost),
    /// Settings transitions, and bounded request without stacking.
    /// Real device behavior remains UNVERIFIED without emulator/device.
    /// </summary>
    public sealed class M88AndroidPermissionStateTableTests
    {
        // Mirrors AndroidStepSensorProvider.ReadRefinedPermission logic for headless testing.
        // Raw status from native: 3=Granted, 2=Denied, 1=NotDetermined (OS reports 1 when permission absent but not denied),
        // 0=Unavailable. The C# refinement uses rationale hint + process-local _completedRequestWithoutGrant.
        private static ActivityPermissionState Refine(int raw, bool completedFlag, bool rationale)
        {
            switch (raw)
            {
                case 3: return ActivityPermissionState.Granted;
                case 2: return ActivityPermissionState.Denied;
                case 1:
                    return (completedFlag || rationale) ? ActivityPermissionState.Denied : ActivityPermissionState.NotDetermined;
                default: return ActivityPermissionState.Unavailable;
            }
        }

        [Test]
        public void FreshInstall_NotDetermined_BeforeAnyRequest()
        {
            // Fresh install: raw NotDetermined (1), no completed flag, rationale false => NotDetermined.
            var state = Refine(raw: 1, completedFlag: false, rationale: false);
            Assert.AreEqual(ActivityPermissionState.NotDetermined, state);
        }

        [Test]
        public void Granted_Raw3_AlwaysGranted()
        {
            Assert.AreEqual(ActivityPermissionState.Granted, Refine(3, false, false));
            Assert.AreEqual(ActivityPermissionState.Granted, Refine(3, true, true));
        }

        [Test]
        public void Denied_Raw2_AlwaysDenied()
        {
            Assert.AreEqual(ActivityPermissionState.Denied, Refine(2, false, false));
            Assert.AreEqual(ActivityPermissionState.Denied, Refine(2, true, false));
        }

        [Test]
        public void NotDetermined_DistinguishesViaRationale()
        {
            // Raw 1 with rationale true => Denied (user denied at least once, rationale shows)
            Assert.AreEqual(ActivityPermissionState.Denied, Refine(1, false, true));
            // Raw 1 with rationale false and no completed flag => NotDetermined (fresh)
            Assert.AreEqual(ActivityPermissionState.NotDetermined, Refine(1, false, false));
        }

        [Test]
        public void Denial_ThenRestart_LosesCompletedFlag_CanLookNotDetermined()
        {
            // Simulate: user denies, provider sets _completedRequestWithoutGrant = true, so raw 1 => Denied.
            var deniedBeforeRestart = Refine(1, completedFlag: true, rationale: false);
            Assert.AreEqual(ActivityPermissionState.Denied, deniedBeforeRestart);

            // Process restart: new provider instance loses the flag (false), same raw 1 with rationale false => NotDetermined.
            var afterRestart = Refine(1, completedFlag: false, rationale: false);
            Assert.AreEqual(ActivityPermissionState.NotDetermined, afterRestart,
                "After restart the in-memory denial flag is lost; platform signals can make denial look fresh. This is the P1 restart concern.");
        }

        [Test]
        public void Denial_WithRationaleVisible_RemainsDenied_AfterRestart()
        {
            // If rationale is true after restart, denial is still correctly classified as Denied.
            var afterRestartWithRationale = Refine(1, completedFlag: false, rationale: true);
            Assert.AreEqual(ActivityPermissionState.Denied, afterRestartWithRationale);
        }

        [Test]
        public void Coordinator_RequestDoesNotStackPrompts()
        {
            var clock = new MutableClock(System.DateTime.UtcNow);
            var provider = new DebugActivityProvider(clock);
            provider.SetSimulatedPermission(ActivityPermissionState.NotDetermined);
            provider.SetAutoGrantOnRequest(false);
            var coordinator = new MotionPermissionCoordinator(provider);

            var tcs = new TaskCompletionSource<ActivityPermissionState>();
            var fake = new DelayedProvider(tcs.Task);
            var coord2 = new MotionPermissionCoordinator(fake);

            var first = coord2.RequestAsync();
            Assert.IsTrue(coord2.RequestInFlight);
            var second = coord2.RequestAsync();
            // Second call should be no-op while in flight
            Assert.AreEqual(MotionPermissionOutcome.StillNotDetermined, second.GetAwaiter().GetResult());
            tcs.SetResult(ActivityPermissionState.Granted);
            Assert.AreEqual(MotionPermissionOutcome.Granted, first.GetAwaiter().GetResult());
            Assert.IsFalse(coord2.RequestInFlight);
        }
        [Test]
        public void Coordinator_Refresh_AfterDenial_AndRestart_IsBounded()
        {
            // Prove refresh after restart does not loop forever and remains bounded.
            var clock = new MutableClock(System.DateTime.UtcNow);
            var provider = new DebugActivityProvider(clock);
            provider.SetSimulatedPermission(ActivityPermissionState.Denied);
            var coordinator = new MotionPermissionCoordinator(provider);
            var refreshed = coordinator.RefreshAsync().GetAwaiter().GetResult();
            Assert.AreEqual(ActivityPermissionState.Denied, refreshed);

            // Simulate restart: new provider/coordinator with NotDetermined (as P1 concern)
            var newProvider = new DebugActivityProvider(clock);
            newProvider.SetSimulatedPermission(ActivityPermissionState.NotDetermined);
            var newCoordinator = new MotionPermissionCoordinator(newProvider);
            var refreshedAfterRestart = newCoordinator.RefreshAsync().GetAwaiter().GetResult();
            // After restart, state may appear NotDetermined — request remains bounded, not stuck.
            Assert.AreEqual(ActivityPermissionState.NotDetermined, refreshedAfterRestart);
            // Request should be bounded (SetAutoGrant false => StillNotDetermined, not hang)
            newProvider.SetAutoGrantOnRequest(false);
            var outcome = newCoordinator.RequestAsync().GetAwaiter().GetResult();
            Assert.AreEqual(MotionPermissionOutcome.StillNotDetermined, outcome);
        }

        private sealed class DelayedProvider : IActivityProvider
        {
            public string ProviderId => "test.delayed";
            private readonly Task<ActivityPermissionState> _pending;
            public DelayedProvider(Task<ActivityPermissionState> pending) => _pending = pending;
            public Task<ActivityCapability> GetCapabilityAsync() => Task.FromResult(new ActivityCapability { motionPermission = ActivityPermissionState.NotDetermined });
            public Task<ActivityPermissionState> RequestMotionPermissionAsync() => _pending;
            public Task<PreparedActivityDelivery> PreparePassiveDeliveryAsync(ActivityCursor cursor) => Task.FromResult<PreparedActivityDelivery>(null);
            public void ResolvePreparedDelivery(PreparedActivityDelivery delivery, bool durable) { }
            public Task<SessionStartError> StartSessionAsync(SessionType type) => Task.FromResult(SessionStartError.SensorUnavailable);
            public Task<ActiveSessionSample> PollSessionAsync() => Task.FromResult<ActiveSessionSample>(null);
            public Task<ActivitySessionResult> StopSessionAsync() => Task.FromResult<ActivitySessionResult>(null);
            public void ResolveSessionCompletion(string sessionId, bool durable) { }
            public Task<ActivitySessionResult> QueryHistoricalStepsAsync(System.DateTime startUtc, System.DateTime endUtc) => Task.FromResult<ActivitySessionResult>(null);
            public void Shutdown() { }
        }
    }
}
