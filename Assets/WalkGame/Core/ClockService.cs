using System;

namespace WalkGame.Core
{
    /// <summary>
    /// Trusted application time abstraction.
    /// Gameplay code must never call DateTime.UtcNow directly; offline production,
    /// activity reconciliation and anomaly detection all depend on this seam.
    /// </summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    /// <summary>Production clock backed by the device UTC time.</summary>
    public sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new SystemClock();
        public DateTime UtcNow => DateTime.UtcNow;
    }

    /// <summary>
    /// Deterministic clock for tests and debug tools ("advance clock" debug action).
    /// Starts at a fixed epoch and moves only when explicitly advanced or set,
    /// which keeps simulated offline windows reproducible.
    /// </summary>
    public sealed class MutableClock : IClock
    {
        private DateTime _utcNow;

        public MutableClock(DateTime initialUtc)
        {
            _utcNow = DateTime.SpecifyKind(initialUtc, DateTimeKind.Utc);
        }

        public DateTime UtcNow => _utcNow;

        public void Set(DateTime utc)
        {
            _utcNow = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        }

        public void Advance(TimeSpan delta)
        {
            _utcNow += delta;
        }
    }
}
