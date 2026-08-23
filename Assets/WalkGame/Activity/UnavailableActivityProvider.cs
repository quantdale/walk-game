using System.Threading.Tasks;
using WalkGame.Core;

namespace WalkGame.Activity
{
    /// <summary>
    /// Honest fail-closed provider for a platform packaging/bridge failure. It keeps
    /// the rest of the game usable without silently converting an Android/iOS runtime
    /// failure into debug movement credit.
    /// </summary>
    public sealed class UnavailableActivityProvider : IActivityProvider
    {
        public const string ProviderIdValue = "activity.unavailable";

        public string ProviderId => ProviderIdValue;

        public Task<ActivityCapability> GetCapabilityAsync()
        {
            return Task.FromResult(new ActivityCapability
            {
                motionPermission = ActivityPermissionState.Unavailable,
                locationPermission = ActivityPermissionState.Unavailable,
            });
        }

        public Task<ActivityPermissionState> RequestMotionPermissionAsync()
        {
            return Task.FromResult(ActivityPermissionState.Unavailable);
        }

        public Task<ActivitySnapshot> ReadSnapshotAsync(ActivityCursor cursor)
        {
            return Task.FromResult<ActivitySnapshot>(null);
        }

        public Task<SessionStartError> StartSessionAsync(SessionType sessionType)
        {
            return Task.FromResult(SessionStartError.SensorUnavailable);
        }

        public Task<ActiveSessionSample> PollSessionAsync()
        {
            return Task.FromResult(new ActiveSessionSample { sessionActive = false });
        }

        public Task<ActivitySessionResult> StopSessionAsync()
        {
            return Task.FromResult<ActivitySessionResult>(null);
        }
    }
}
