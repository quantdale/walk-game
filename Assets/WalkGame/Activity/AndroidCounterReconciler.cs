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
        private long _claimedDelta;
        private string _openClaimId;

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

        /// <summary>True while a prepared delivery holds claimed steps (ADR 0009).</summary>
        public bool HasOpenClaim => _claimedDelta > 0;

        /// <summary>
        /// Identity of the single open claim, or null. Resolution MUST supply this
        /// token (M8.5 runtime-ownership I4): stale, repeated, unknown, or null ids are
        /// harmless no-ops and can never mutate a newer claim.
        /// </summary>
        public string OpenClaimId => _openClaimId;

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

        /// <summary>
        /// Prepares a passive delivery (ADR 0009): moves all currently-pending steps
        /// into the open claim so exactly one delivery can hold them. Returns 0 when a
        /// claim is already open - overlapping reads can never prepare two claims over
        /// one pending window. The steps stay recoverable until the claim is resolved
        /// BY IDENTITY via <see cref="AcknowledgeClaim"/>/<see cref="RestoreClaim"/>.
        /// </summary>
        public long ClaimPending()
        {
            if (_claimedDelta > 0)
            {
                return 0;
            }

            long claimed = PendingDelta;
            PendingDelta = 0;
            if (claimed <= 0)
            {
                return 0; // nothing to hold: no claim opens, identity stays null
            }

            _claimedDelta = claimed;
            _openClaimId = Guid.NewGuid().ToString("N");
            return claimed;
        }

        /// <summary>Drops the open claim after a proven durable commit. Resolves only the
        /// NAMED current claim: an unknown/stale/repeated id is a no-op returning false.</summary>
        public bool AcknowledgeClaim(string claimId)
        {
            if (!MatchesOpenClaim(claimId))
            {
                return false;
            }

            _claimedDelta = 0;
            _openClaimId = null;
            return true;
        }

        /// <summary>Returns the NAMED open claim to the pending stream after a rejected/
        /// rolled-back delivery, making the same movement retryable exactly once in
        /// this process. Unknown/stale/repeated ids are no-ops returning false.</summary>
        public bool RestoreClaim(string claimId)
        {
            if (!MatchesOpenClaim(claimId))
            {
                return false;
            }

            if (_claimedDelta > 0)
            {
                PendingDelta += _claimedDelta;
                _claimedDelta = 0;
            }

            _openClaimId = null;
            return true;
        }

        private bool MatchesOpenClaim(string claimId)
        {
            return claimId != null &&
                   _openClaimId != null &&
                   string.Equals(_openClaimId, claimId, StringComparison.Ordinal);
        }

        /// <summary>Returns previously-undrained steps to the passive stream (campaign S8:
        /// while an Expedition runs it owns the counter deltas that had accumulated in
        /// the passive pipeline; the pre-session residue is restored on completion).</summary>
        public void RestorePending(long steps)
        {
            if (steps > 0)
            {
                PendingDelta += steps;
            }
        }

        private static bool IsUsable(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
        }
    }
}
