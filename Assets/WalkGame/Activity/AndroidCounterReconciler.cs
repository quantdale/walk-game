using System;

namespace WalkGame.Activity
{
    public enum CounterFoldOutcome
    {
        /// <summary>Non-finite or negative payload ignored; state untouched.</summary>
        InvalidSample = 0,
        /// <summary>First valid observation: baseline set, nothing credited.</summary>
        BaselineEstablished = 1,
        /// <summary>Monotonic increase folded into the pending delta.</summary>
        DeltaCredited = 2,
        /// <summary>Decrease vs persisted/last raw: reboot or provider reset; new
        /// baseline taken, no negative credit ever.</summary>
        Rebaselined = 3,
        /// <summary>Increase too large to be a plausible single reconciliation window;
        /// treated as corrupt telemetry: re-baselined without crediting.</summary>
        AnomalyRebaselined = 4
    }

    /// <summary>
    /// Engine-free state machine for TYPE_STEP_COUNTER reconciliation
    /// (ACTIVITY_REWARD_SYSTEM 16, MOBILE_ACTIVITY_INTEGRATION 5). The native side
    /// reports an absolute steps-since-reboot value that begins life as "unknown";
    /// this class owns every transition from unknown to credited deltas and fails
    /// closed on anything implausible. Unit-tested in the standalone harness; the
    /// Unity provider is a thin shell over it.
    /// </summary>
    public sealed class AndroidCounterReconciler
    {
        /// <summary>
        /// Upper bound for one reconciliation window (~a month of walking). The app
        /// reconciles on foreground cadence, so legitimate multi-day catch-ups must
        /// still credit; anything beyond this is sensor corruption, not steps.
        /// </summary>
        public const long DefaultMaxPlausibleDelta = 1_000_000;

        private readonly long _maxPlausibleDelta;
        private double? _lastRaw;

        public AndroidCounterReconciler(long maxPlausibleDelta = DefaultMaxPlausibleDelta)
        {
            if (maxPlausibleDelta < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPlausibleDelta));
            }

            _maxPlausibleDelta = maxPlausibleDelta;
        }

        /// <summary>Last accepted raw value; null until the first valid sample.</summary>
        public double? LastRawCounter => _lastRaw;

        /// <summary>Steps folded in since the last drain.</summary>
        public long PendingDelta { get; private set; }

        public bool HasBaseline => _lastRaw.HasValue;

        /// <summary>
        /// Seeds the baseline from the persisted profile cursor so a process restart
        /// on the same boot resumes where the last save left off instead of dropping
        /// uncredited steps. A persisted value HIGHER than the next live sample means
        /// the device rebooted meanwhile; the normal decrease path handles that.
        /// Non-finite persisted junk is discarded.
        /// </summary>
        public void SeedFromPersistedCounter(double? persistedRaw)
        {
            if (!persistedRaw.HasValue)
            {
                return;
            }

            double value = persistedRaw.Value;
            if (IsUsable(value))
            {
                _lastRaw = value;
            }
        }

        /// <summary>Folds one absolute raw reading; never produces negative credit.</summary>
        public CounterFoldOutcome Fold(double raw)
        {
            if (!IsUsable(raw))
            {
                return CounterFoldOutcome.InvalidSample;
            }

            if (!_lastRaw.HasValue)
            {
                _lastRaw = raw;
                return CounterFoldOutcome.BaselineEstablished;
            }

            if (raw < _lastRaw.Value)
            {
                _lastRaw = raw;
                return CounterFoldOutcome.Rebaselined;
            }

            double delta = raw - _lastRaw.Value;
            if (delta > _maxPlausibleDelta)
            {
                _lastRaw = raw;
                return CounterFoldOutcome.AnomalyRebaselined;
            }

            if (delta == 0)
            {
                return CounterFoldOutcome.DeltaCredited; // no-op fold, explicit for clarity
            }

            PendingDelta += (long)delta;
            _lastRaw = raw;
            return CounterFoldOutcome.DeltaCredited;
        }

        /// <summary>Takes everything accumulated so far; called only when a snapshot is built.</summary>
        public long DrainPending()
        {
            long pending = PendingDelta;
            PendingDelta = 0;
            return pending;
        }

        private static bool IsUsable(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
        }
    }
}
