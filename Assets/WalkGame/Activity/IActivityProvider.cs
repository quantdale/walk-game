using System;
using System.Threading.Tasks;
using WalkGame.Core;

namespace WalkGame.Activity
{
    public enum ActivityPermissionState
    {
        Unavailable = 0,
        NotDetermined = 1,
        Denied = 2,
        Granted = 3
    }

    /// <summary>What the underlying platform can currently provide. MOBILE_ACTIVITY_INTEGRATION 16.</summary>
    public sealed class ActivityCapability
    {
        public bool supportsPassiveSteps;
        public bool supportsHistoricalQuery;
        public bool supportsActiveSession;
        public bool supportsDistance;
        public bool supportsCadence;
        public bool supportsLocationSession;
        public ActivityPermissionState motionPermission = ActivityPermissionState.Unavailable;
        public ActivityPermissionState locationPermission = ActivityPermissionState.Unavailable;
    }

    /// <summary>
    /// Provider cursor wrapper; platform-specific payload stays opaque to game logic.
    /// </summary>
    public sealed class ActivityCursor
    {
        public DateTime? lastSuccessfulSyncUtc;
        public string providerCursor;
    }

    public enum SessionStartError
    {
        None = 0,
        SensorUnavailable,
        PermissionDenied,
        AlreadyRunning,
        ProviderError
    }

        /// <summary>
        /// The only surface through which game code observes real-world movement.
        /// Reward/restoration code must never call CMPedometer, SensorManager, HealthKit,
        /// Health Connect or location APIs directly (AGENT_EXECUTION_GUIDE 11).
        /// </summary>
        public interface IActivityProvider
        {
            string ProviderId { get; }
            Task<ActivityCapability> GetCapabilityAsync();

            /// <summary>
            /// Contextual motion-permission request (MOBILE_ACTIVITY_INTEGRATION 15).
            /// Must only trigger the OS prompt when the platform reports NotDetermined;
            /// repeated calls act as a retry path and never throw. Returns the observed
            /// post-request state; implementations may return the unchanged current
            /// state when the dialog is still open or the platform cannot answer yet.
            /// </summary>
            Task<ActivityPermissionState> RequestMotionPermissionAsync();

            Task<ActivitySnapshot> ReadSnapshotAsync(ActivityCursor cursor);
            Task<SessionStartError> StartSessionAsync(SessionType sessionType);
            Task<ActiveSessionSample> PollSessionAsync();
            Task<ActivitySessionResult> StopSessionAsync();
        }

    /// <summary>Transient live reading for an in-progress Expedition.</summary>
    public sealed class ActiveSessionSample
    {
        public bool sessionActive;
        public long accumulatedSteps;
        public double accumulatedDistanceMeters;
        public double movingSeconds;
        public double? currentCadenceStepsPerMinute;
        public double? currentSpeedMetersPerSecond;
    }
}
