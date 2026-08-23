using System;
using NUnit.Framework;
using WalkGame.Activity;

namespace WalkGame.Tests
{
    /// <summary>
    /// Campaign S6/S7/S16 coverage for the engine-free Core Motion reconciliation
    /// window planner: seven-day availability bound, first-sync lookback, hot-loop
    /// suppression and contiguity (no overlap with already-credited history).
    /// </summary>
    public sealed class IosHistoryPlanningTests
    {
        private static readonly DateTime Now = new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);
        private IosHistoryWindowPlanner _planner;

        [SetUp]
        public void SetUp()
        {
            _planner = new IosHistoryWindowPlanner();
        }

        [Test]
        public void FreshInstall_NoCursor_PlansDefault24hLookback()
        {
            bool planned = _planner.TryPlan(null, Now, out var since, out var until);

            Assert.IsTrue(planned);
            Assert.AreEqual(Now.AddHours(-24), since);
            Assert.AreEqual(Now, until);
        }

        [Test]
        public void CursorOlderThanSevenDays_IsClampedToAvailabilityWindow()
        {
            var staleCursor = Now.AddDays(-30);

            bool planned = _planner.TryPlan(staleCursor, Now, out var since, out var until);

            Assert.IsTrue(planned);
            Assert.AreEqual(Now.AddDays(-7), since, "never query beyond CMPedometer availability");
            Assert.AreEqual(Now, until);
        }

        [Test]
        public void RecentCursor_InsideSliver_IsSuppressed()
        {
            var recentCursor = Now.AddSeconds(-30);

            Assert.IsFalse(_planner.TryPlan(recentCursor, Now, out _, out _),
                "sub-minute windows would hot-loop the sensor for empty slivers");
        }

        [Test]
        public void NormalCursor_IsHonoredExactly_KeepingWindowsContiguous()
        {
            var cursor = Now.AddHours(-2);

            bool planned = _planner.TryPlan(cursor, Now, out var since, out var until);

            Assert.IsTrue(planned);
            Assert.AreEqual(cursor, since, "windows start exactly where credit stopped");
            Assert.AreEqual(Now, until);
        }

        [Test]
        public void CursorInFuture_ClampsToEmptyAndSuppresses()
        {
            var futureCursor = Now.AddMinutes(5);

            Assert.IsFalse(_planner.TryPlan(futureCursor, Now, out _, out _),
                "backward-clock anomalies must not produce inverted query windows");
        }

        [Test]
        public void ExactlySixtySecondWindow_IsPlanned()
        {
            var cursor = Now.AddSeconds(-60);

            Assert.IsTrue(_planner.TryPlan(cursor, Now, out _, out _));
        }
    }
}
