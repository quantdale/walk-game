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
        /// A passive movement delivery prepared by a provider (ADR 0009). Preparation
        /// stages the movement inside the provider without making it permanently
        /// unavailable: until the application resolves the delivery against a proven
        /// durability outcome, the provider can restore exactly this movement for
        /// retry. The opaque id makes repeated/stale resolutions safe no-ops.
        /// </summary>
        public sealed class PreparedActivityDelivery
        {
            public string deliveryId = Guid.NewGuid().ToString("N");
            public ActivitySnapshot snapshot;
        }

        /// <summary>
        /// The only surface through which game code observes real-world movement.
        /// Reward/restoration code must never call CMPedometer, SensorManager, HealthKit,
        /// Health Connect or location APIs directly (AGENT_EXECUTION_GUIDE 11).
        ///
        /// Delivery durability contract (ADR 0009): passive movement crosses this
        /// boundary as a prepared delivery, and the application MUST resolve every
        /// prepared delivery exactly once through
        /// <see cref="ResolvePreparedDelivery"/> after the enclosing profile commit
        /// outcome is known - true to acknowledge (drop staged state), false to reject
        /// (return staged movement to retryable pending state). Session completions
        /// follow the same pattern through <see cref="ResolveSessionCompletion"/>.
        /// Resolutions of unknown or stale deliveries are safe no-ops; they never
        /// duplicate credit or produce negative pending state.
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

            /// <summary>
            /// Prepares the next passive movement delivery without irreversibly
            /// consuming it. Returns null when nothing is available (no sensor,
            /// permission missing, session active, empty window, or a previous
            /// delivery still unresolved). The same movement stays recoverable via
            /// <see cref="ResolvePreparedDelivery"/> until it is durably acknowledged.
            /// </summary>
            Task<PreparedActivityDelivery> PreparePassiveDeliveryAsync(ActivityCursor cursor);

            /// <summary>
            /// Resolves a previously prepared passive delivery exactly once.
            /// durable=true acknowledges the delivery: the provider may irreversibly
            /// drop its staged pending state because the profile commit proved it
            /// consumed. durable=false rejects it: the provider must restore/re-expose
            /// the staged movement so a retry cannot lose base movement. Calls with an
            /// unknown, stale, or already-resolved delivery are ignored.
            /// </summary>
            void ResolvePreparedDelivery(PreparedActivityDelivery delivery, bool durable);

            /// <summary>
            /// Resolves a completed active-session result against the durability
            /// outcome of its profile commit. durable=true drops any provider-private
            /// session completion state; durable=false keeps the session's base
            /// movement recoverable (restored to the passive stream where the platform
            /// cannot replay the session result itself). Unknown session ids are
            /// ignored safely.
            /// </summary>
            void ResolveSessionCompletion(string sessionId, bool durable);

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
