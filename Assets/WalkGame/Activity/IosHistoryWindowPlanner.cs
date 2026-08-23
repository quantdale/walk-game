using System;

namespace WalkGame.Activity
{
    /// <summary>
    /// Engine-free planner for CMPedometer historical reconciliation windows
    /// (MOBILE_ACTIVITY_INTEGRATION 3, ACTIVITY_REWARD_SYSTEM 17). Decides whether a
    /// query is worth issuing and which interval it must cover so the credited
    /// windows stay contiguous: never overlapping the already-credited past, never
    /// reaching beyond Core Motion's seven-day availability. Unit-tested in the
    /// standalone harness; the Unity provider only executes the planned query.
    /// </summary>
    public sealed class IosHistoryWindowPlanner
    {
        /// <summary>Core Motion exposes at most seven days of pedometer history.</summary>
        public static readonly TimeSpan HistoryWindow = TimeSpan.FromDays(7);

        /// <summary>Suppresses hot-loop queries that would re-read an empty sliver.</summary>
        public const double MinimumQuerySeconds = 60;

        /// <summary>Default lookback for a first successful sync with no stored cursor.</summary>
        public static readonly TimeSpan FirstSyncLookback = TimeSpan.FromHours(24);

        public bool TryPlan(DateTime? lastSuccessfulSyncUtc, DateTime nowUtc, out DateTime since, out DateTime until)
        {
            until = nowUtc;
            DateTime earliestAvailable = nowUtc - HistoryWindow;

            since = lastSuccessfulSyncUtc ?? nowUtc - FirstSyncLookback;

            // Never ask for data the system cannot have; the lost pre-window gap is a
            // documented product limitation until an optional HealthKit provider exists.
            if (since < earliestAvailable)
            {
                since = earliestAvailable;
            }

            if (since >= until || (until - since).TotalSeconds < MinimumQuerySeconds)
            {
                return false;
            }

            return true;
        }
    }
}
