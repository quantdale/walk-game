using System;
using System.Collections.Generic;

namespace WalkGame.Core
{
    /// <summary>Normalized activity data crossing the provider boundary. DATA_MODEL.md 9.</summary>
    public enum ActivitySourceType
    {
        Unknown = 0,
        PhoneSensor = 1,
        Wearable = 2,
        Imported = 3
    }

    public enum ActivityRecordingType
    {
        Unknown = 0,
        Passive = 1,
        Active = 2,
        Manual = 3
    }

    [Flags]
    public enum ActivitySuspicionFlag
    {
        None = 0,
        VehicleLikeSpeed = 1 << 0,
        NoStepEvidence = 1 << 1,
        TeleportJump = 1 << 2,
        MockLocation = 1 << 3,
        TimestampAnomaly = 1 << 4,
        ManualEntry = 1 << 5
    }

    public sealed class ActivityQuality
    {
        public bool hasStepEvidence;
        public bool hasDistanceEvidence;
        public bool hasCadenceEvidence;
        public bool hasLocationEvidence;
        /// <summary>0..1 confidence derived from evidence coherence; neutral start.</summary>
        public float accuracyScore = 0.5f;
        public HashSet<ActivitySuspicionFlag> suspiciousFlags = new HashSet<ActivitySuspicionFlag>();

        public bool HasFlag(ActivitySuspicionFlag flag)
        {
            return suspiciousFlags.Contains(flag);
        }
    }

    public sealed class ActivitySnapshot
    {
        public string providerId = string.Empty;
        public DateTime intervalStartUtc = DateTime.MinValue;
        public DateTime intervalEndUtc = DateTime.MinValue;
        public long stepCount;
        public double? estimatedDistanceMeters;
        public ActivitySourceType sourceType = ActivitySourceType.Unknown;
        public ActivityRecordingType recordingType = ActivityRecordingType.Unknown;
        public List<string> providerRecordIds = new List<string>();
        public ActivityQuality quality = new ActivityQuality();

        /// <summary>Deterministic dedup key for interval-based credit (provider + interval bounds).</summary>
        public string IntervalDedupKey()
        {
            return $"{providerId}:{intervalStartUtc:O}:{intervalEndUtc:O}";
        }
    }

    public sealed class ActiveSessionState
    {
        public string sessionId = Guid.NewGuid().ToString("D");
        public SessionType sessionType = SessionType.Walk;
        public DateTime startedAtUtc = DateTime.MinValue;
        public long? initialStepBaseline;
        public long accumulatedSteps;
        public double accumulatedDistanceMeters;
        public double movingSeconds;

        public bool HasBaseline => initialStepBaseline.HasValue;
    }

    public enum SessionType
    {
        Walk = 0,
        Run = 1
    }

    public sealed class ActivitySessionResult
    {
        public string sessionId = string.Empty;
        public SessionType type = SessionType.Walk;
        public DateTime startUtc = DateTime.MinValue;
        public DateTime endUtc = DateTime.MinValue;
        public long acceptedSteps;
        public double verifiedDistanceMeters;
        public double verifiedMovingSeconds;
        public float? cadenceConsistency;
        public float trustScore;
        public ActivityBonusBreakdown bonusBreakdown = new ActivityBonusBreakdown();
    }

    /// <summary>
    /// Explainable bonus result so balancing/debugging can show why a session paid what it did.
    /// ACTIVITY_REWARD_SYSTEM.md section 13.
    /// </summary>
    public sealed class ActivityBonusBreakdown
    {
        public long explorerBonus;
        public long enduranceBonus;
        public long rhythmBonus;
        public long tempoBonus;
        public long growthBonus;
        public long totalBonus;
        public bool capped;

        public long SumParts()
        {
            return explorerBonus + enduranceBonus + rhythmBonus + tempoBonus + growthBonus;
        }
    }

    /// <summary>
    /// Bounded dedup structure for credited activity keys (DATA_MODEL.md 8).
    /// Keeps the most recent entries; drops the oldest once capacity is exceeded.
    ///
    /// Persistence shape: <see cref="entries"/> is a plain public field so Newtonsoft
    /// round-trips it directly. The earlier property-based design was silently dropped
    /// by the serializer (collection getters are populated by reuse, setters skipped),
    /// which lost exactly-once state across every restart - campaign S8 regression.
    /// Call <see cref="Rebuild"/> after external assignment (load path) to restore the
    /// membership index.
    /// </summary>
    public sealed class CreditedActivityKeys : IPostCopyRepair
    {
        private const int DefaultCapacity = 512;

        private readonly int _capacity;
        private readonly HashSet<string> _set = new HashSet<string>();

        /// <summary>Oldest-to-newest ordered key log; the canonical serialized form.</summary>
        public List<string> entries = new List<string>();

        public CreditedActivityKeys() : this(DefaultCapacity)
        {
        }

        public CreditedActivityKeys(int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _capacity = capacity;
        }

        public int Count => _set.Count;

        /// <summary>Restores the membership index after <see cref="entries"/> was
        /// assigned externally (deserialization); applies the bounded-window policy.</summary>
        public void Rebuild()
        {
            _set.Clear();
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var key = entries[i];
                if (string.IsNullOrEmpty(key))
                {
                    entries.RemoveAt(i);
                }
            }

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                _set.Add(entries[i]);
            }

            while (entries.Count > _capacity)
            {
                var oldest = entries[0];
                entries.RemoveAt(0);
                _set.Remove(oldest);
            }
        }

        /// <summary>In-place state-copy hook (rollback/cloud merge); same contract as Rebuild.</summary>
        void IPostCopyRepair.Repair()
        {
            Rebuild();
        }

        public bool Contains(string key)
        {
            return key != null && _set.Contains(key);
        }

        /// <summary>Returns true when the key was new (not previously credited).</summary>
        public bool TryMarkCredited(string key)
        {
            if (key == null || !_set.Add(key))
            {
                return false;
            }

            entries.Add(key);
            while (entries.Count > _capacity)
            {
                var oldest = entries[0];
                entries.RemoveAt(0);
                _set.Remove(oldest);
            }

            return true;
        }
    }

    /// <summary>
    /// Provider-specific sync cursors wrapped so platform details do not leak into game logic.
    /// </summary>
    public sealed class ActivitySyncState
    {
        public string providerId = string.Empty;
        public DateTime? lastSuccessfulSyncUtc;
        public string providerCursor;

        // Android cumulative counter tracking (TYPE_STEP_COUNTER is steps-since-reboot).
        public double? androidLastRawStepCounter;
        public DateTime? androidLastCounterObservedUtc;

        public CreditedActivityKeys creditedIntervals = new CreditedActivityKeys();

        /// <summary>Durable identity of already-credited Expeditions (campaign S8): a
        /// re-delivered session result must never pay base steps or bonuses twice.</summary>
        public CreditedActivityKeys creditedSessionIds = new CreditedActivityKeys();

        public ActiveSessionState activeSession;
    }
}
