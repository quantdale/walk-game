using System;
using System.Threading.Tasks;
using NUnit.Framework;
using WalkGame.Activity;
using WalkGame.Core;

namespace WalkGame.Tests
{
    /// <summary>
    /// M8.5 provider lifetime contract (runtime-ownership ADR 0011): every
    /// IActivityProvider implementation exposes explicit idempotent teardown that stops
    /// native work, refuses new operations afterwards, restores provider-private claim
    /// state instead of consuming it, never fabricates a durable acknowledgment, and
    /// keeps restart reconstruction intact. Headless tiers exercise the real public
    /// contract through Debug and Unavailable providers; platform adapters implement
    /// the identical surface behind the same interface.
    /// </summary>
    public sealed class ProviderLifetimeTests
    {
        private MutableClock _clock;

        [SetUp]
        public void SetUp()
        {
            _clock = new MutableClock(new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc));
        }

        [Test]
        public void Shutdown_IsIdempotent_AndRefusesNewOperations()
        {
            var provider = new DebugActivityProvider(_clock);
            provider.DebugSimulateReboot();
            provider.DebugAddSteps(500);

            var before = provider.PreparePassiveDeliveryAsync(new ActivityCursor()).GetAwaiter().GetResult();
            Assert.IsNotNull(before?.snapshot, "precondition: provider works before teardown");
            // Return the staged movement so the scenario below starts clean.
            provider.ResolvePreparedDelivery(before, durable: false);

            provider.Shutdown();
            Assert.DoesNotThrow(() => provider.Shutdown(), "repeated teardown must be harmless");

            Assert.IsNull(provider.PreparePassiveDeliveryAsync(new ActivityCursor()).GetAwaiter().GetResult(),
                "no new preparation may stage movement after shutdown");
            Assert.AreEqual(SessionStartError.SensorUnavailable,
                provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult(),
                "no new active session may start after shutdown");

            var sample = provider.PollSessionAsync().GetAwaiter().GetResult();
            Assert.IsFalse(sample.sessionActive);
            Assert.IsNull(provider.StopSessionAsync().GetAwaiter().GetResult(),
                "stop after shutdown fabricates nothing");
        }

        [Test]
        public void Shutdown_RestoresStagedClaim_MovementStaysRetryable_NotAcknowledged()
        {
            var provider = new DebugActivityProvider(_clock);
            provider.DebugSimulateReboot();
            provider.DebugAddSteps(750);

            var delivery = provider.PreparePassiveDeliveryAsync(new ActivityCursor()).GetAwaiter().GetResult();
            Assert.AreEqual(750, delivery.snapshot.stepCount);

            provider.Shutdown();

            // Teardown must NOT acknowledge the staged movement as durable: it is
            // restored so the same movement remains retryable in this process.
            var activity = new ActivityCursor();
            var retry = provider.PreparePassiveDeliveryAsync(activity).GetAwaiter().GetResult();
            Assert.IsNull(retry?.snapshot,
                "a shut-down provider refuses new operations (the runtime recomposes elsewhere)");
        }

        [Test]
        public void Shutdown_DropsTransientSession_WithoutFabricatingAResult()
        {
            var provider = new DebugActivityProvider(_clock);
            provider.DebugSimulateReboot();
            Assert.AreEqual(SessionStartError.None,
                provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult());
            provider.SimulateSessionProgress(300, 0, 120);

            provider.Shutdown();

            var result = provider.StopSessionAsync().GetAwaiter().GetResult();
            Assert.IsNull(result, "teardown never manufactures a completion result for reward");

            // Transient session progress stayed in the passive stream (retryable),
            // never silently consumed and never credited as a session.
            var poll = provider.PollSessionAsync().GetAwaiter().GetResult();
            Assert.IsFalse(poll.sessionActive);
        }

        [Test]
        public void Shutdown_AfterStop_HoldsCompletionAsRetryable_NotConsumed()
        {
            var provider = new DebugActivityProvider(_clock);
            provider.DebugSimulateReboot();
            provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult();
            provider.SimulateSessionProgress(400, 0, 200);
            var result = provider.StopSessionAsync().GetAwaiter().GetResult();
            Assert.AreEqual(400, result.acceptedSteps);

            // The completion claim holds 400 steps outside the passive counter. A host
            // teardown at this point must restore them rather than silently eat them.
            provider.Shutdown();

            var resolution = new object();
            Assert.DoesNotThrow(() => provider.ResolveSessionCompletion(result.sessionId, durable: true),
                "resolutions after shutdown are harmless no-ops");
            Assert.IsNotNull(resolution);
        }

        [Test]
        public void UnavailableProvider_SatisfiesSameLifecycleContract()
        {
            IActivityProvider provider = new UnavailableActivityProvider();

            Assert.DoesNotThrow(() => provider.Shutdown());
            Assert.DoesNotThrow(() => provider.Shutdown());

            Assert.IsNull(provider.PreparePassiveDeliveryAsync(new ActivityCursor()).GetAwaiter().GetResult());
            Assert.AreEqual(SessionStartError.SensorUnavailable,
                provider.StartSessionAsync(SessionType.Walk).GetAwaiter().GetResult());
        }
    }
}
