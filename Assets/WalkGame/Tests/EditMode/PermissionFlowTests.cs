using System;
using System.Threading.Tasks;
using NUnit.Framework;
using WalkGame.Activity;
using WalkGame.Core;

namespace WalkGame.Tests
{
    /// <summary>
    /// Permission lifecycle regression coverage (campaign S4/S16): contextual requests,
    /// sticky denial as a normal state, retry after Settings-enabled access, and the
    /// no-stacked-prompts guarantee - all against injectable providers, no hardware.
    /// Each test builds its own coordinator and captures notification counts locally so
    /// results never depend on fixture execution order or scheduler timing.
    /// </summary>
    public sealed class PermissionFlowTests
    {
        private static readonly DateTime TestEpoch = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        private static DebugActivityProvider NewProvider()
        {
            return new DebugActivityProvider(new MutableClock(TestEpoch));
        }

        [Test]
        public void FreshInstall_AssumesNotDetermined()
        {
            var coordinator = new MotionPermissionCoordinator(NewProvider());
            Assert.AreEqual(ActivityPermissionState.NotDetermined, coordinator.CurrentState);
        }

        [Test]
        public void Refresh_MapsPlatformState_AndNotifiesOnlyOnChange()
        {
            var provider = NewProvider();
            int changes = 0;
            var coordinator = new MotionPermissionCoordinator(provider);
            coordinator.StateChanged += _ => changes++;

            provider.SetSimulatedPermission(ActivityPermissionState.Denied);

            var state = coordinator.RefreshAsync().GetAwaiter().GetResult();

            Assert.AreEqual(ActivityPermissionState.Denied, state);
            Assert.AreEqual(1, changes);

            // Same platform answer again must not spam change notifications.
            coordinator.RefreshAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1, changes);
        }

        [Test]
        public void Request_FromNotDetermined_Grants_AndFiresChange()
        {
            var provider = NewProvider();
            int changes = 0;
            var coordinator = new MotionPermissionCoordinator(provider);
            coordinator.StateChanged += _ => changes++;

            provider.SetSimulatedPermission(ActivityPermissionState.NotDetermined);

            var outcome = coordinator.RequestAsync().GetAwaiter().GetResult();

            Assert.AreEqual(MotionPermissionOutcome.Granted, outcome);
            Assert.AreEqual(ActivityPermissionState.Granted, coordinator.CurrentState);
            Assert.AreEqual(1, changes);
        }

        [Test]
        public void Request_UnansweredDialog_StaysUndetermined_ThenSettingsRetryGrants()
        {
            var provider = NewProvider();
            var coordinator = new MotionPermissionCoordinator(provider);
            provider.SetSimulatedPermission(ActivityPermissionState.NotDetermined);
            provider.SetAutoGrantOnRequest(false); // player never answers

            var first = coordinator.RequestAsync().GetAwaiter().GetResult();
            Assert.AreEqual(MotionPermissionOutcome.StillNotDetermined, first);
            Assert.AreEqual(ActivityPermissionState.NotDetermined, coordinator.CurrentState);

            // Later enable from OS settings + app resume is the sanctioned retry path.
            provider.SetSimulatedPermission(ActivityPermissionState.Granted);
            var refreshed = coordinator.RefreshAsync().GetAwaiter().GetResult();

            Assert.AreEqual(ActivityPermissionState.Granted, refreshed);
        }

        [Test]
        public void Denial_IsNormalState_SensorPathsFailClosed_GameplayContinues()
        {
            var provider = NewProvider();
            var coordinator = new MotionPermissionCoordinator(provider);
            provider.SetSimulatedPermission(ActivityPermissionState.Denied);
            provider.DebugAddSteps(5000);

            Assert.IsNull(provider.PreparePassiveDeliveryAsync(new ActivityCursor()).GetAwaiter().GetResult());
            Assert.AreEqual(SessionStartError.PermissionDenied,
                provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult());

            // The coordinator reports denial without throwing and without crediting.
            var outcome = coordinator.RequestAsync().GetAwaiter().GetResult();
            Assert.AreEqual(MotionPermissionOutcome.Denied, outcome);
            Assert.IsFalse(coordinator.RequestInFlight);
        }

        [Test]
        public void Request_WhileInFlight_DoesNotStackPrompts()
        {
            var inner = NewProvider();
            inner.SetSimulatedPermission(ActivityPermissionState.NotDetermined);

            // Plain completion source: the continuation must run inline on SetResult
            // so the coordinator settles deterministically before assertions.
            var gate = new TaskCompletionSource<ActivityPermissionState>();
            var provider = new SingleRequestProvider(inner, gate.Task);
            var coordinator = new MotionPermissionCoordinator(provider);

            var first = coordinator.RequestAsync();
            Assert.IsTrue(coordinator.RequestInFlight, "request must be observable as in flight");

            var second = coordinator.RequestAsync();
            Assert.AreEqual(MotionPermissionOutcome.StillNotDetermined, second.GetAwaiter().GetResult(),
                "overlapping request must be a no-op while one is in flight");

            gate.SetResult(ActivityPermissionState.Granted);
            Assert.AreEqual(MotionPermissionOutcome.Granted, first.GetAwaiter().GetResult());
            Assert.AreEqual(ActivityPermissionState.Granted, coordinator.CurrentState);
            Assert.IsFalse(coordinator.RequestInFlight);
        }

        [Test]
        public void FaultingProvider_DoesNotBreakGameplay()
        {
            var coordinator = new MotionPermissionCoordinator(new ThrowingProvider());

            var outcome = coordinator.RequestAsync().GetAwaiter().GetResult();

            Assert.AreEqual(MotionPermissionOutcome.StillNotDetermined, outcome);
            Assert.IsFalse(coordinator.RequestInFlight);
        }

        [Test]
        public void UnavailableProvider_FailsClosed_WithoutMovementCredit()
        {
            var provider = new UnavailableActivityProvider();

            Assert.AreEqual(ActivityPermissionState.Unavailable,
                provider.GetCapabilityAsync().GetAwaiter().GetResult().motionPermission);
            Assert.AreEqual(ActivityPermissionState.Unavailable,
                provider.RequestMotionPermissionAsync().GetAwaiter().GetResult());
            Assert.IsNull(provider.PreparePassiveDeliveryAsync(new ActivityCursor()).GetAwaiter().GetResult());
            Assert.AreEqual(SessionStartError.SensorUnavailable,
                provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult());
        }

        /// <summary>Wraps the debug provider but gates the request round-trip so tests
        /// control exactly when permission resolution arrives.</summary>
        private sealed class SingleRequestProvider : IActivityProvider
        {
            private readonly DebugActivityProvider _inner;
            private readonly Task<ActivityPermissionState> _request;

            public SingleRequestProvider(DebugActivityProvider inner, Task<ActivityPermissionState> request)
            {
                _inner = inner;
                _request = request;
            }

            public string ProviderId => "test.gated";

            public Task<ActivityCapability> GetCapabilityAsync() => _inner.GetCapabilityAsync();

            public Task<ActivityPermissionState> RequestMotionPermissionAsync() => _request;

            public Task<PreparedActivityDelivery> PreparePassiveDeliveryAsync(ActivityCursor cursor) =>
                _inner.PreparePassiveDeliveryAsync(cursor);

            public void ResolvePreparedDelivery(PreparedActivityDelivery delivery, bool durable) =>
                _inner.ResolvePreparedDelivery(delivery, durable);

            public void ResolveSessionCompletion(string sessionId, bool durable) =>
                _inner.ResolveSessionCompletion(sessionId, durable);

            public Task<SessionStartError> StartSessionAsync(SessionType sessionType) =>
                _inner.StartSessionAsync(sessionType);

            public Task<ActiveSessionSample> PollSessionAsync() => _inner.PollSessionAsync();

            public Task<ActivitySessionResult> StopSessionAsync() => _inner.StopSessionAsync();
        }

        private sealed class ThrowingProvider : IActivityProvider
        {
            public string ProviderId => "test.throwing";
            public Task<ActivityCapability> GetCapabilityAsync() => throw new InvalidOperationException("boom");
            public Task<ActivityPermissionState> RequestMotionPermissionAsync() => throw new InvalidOperationException("boom");
            public Task<PreparedActivityDelivery> PreparePassiveDeliveryAsync(ActivityCursor cursor) => throw new InvalidOperationException("boom");
            public void ResolvePreparedDelivery(PreparedActivityDelivery delivery, bool durable) { }
            public void ResolveSessionCompletion(string sessionId, bool durable) { }
            public Task<SessionStartError> StartSessionAsync(SessionType sessionType) => throw new InvalidOperationException("boom");
            public Task<ActiveSessionSample> PollSessionAsync() => throw new InvalidOperationException("boom");
            public Task<ActivitySessionResult> StopSessionAsync() => throw new InvalidOperationException("boom");
        }
    }
}
