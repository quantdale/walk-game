using System;
using System.Collections.Generic;
using WalkGame.Core;
using WalkGame.Activity;
using WalkGame.Gameplay;

namespace WalkGame.UI
{
    /// <summary>
    /// Dependency surface the App layer hands to UI controllers. UI never references
    /// the composition root directly, keeping the dependency direction one-way
    /// (UI -> abstractions only).
    /// </summary>
    public sealed class UiContext
    {
        public Func<PlayerProfile> GetProfile;
        public Func<bool> GetIsExplore;
        public Action ToggleExploreRequested;
        public Action OpenDebugMenu;
        public Func<IReadOnlyList<PendingCollect>> GetCollectables;
        public Action<string> CollectProducerRequested;

        /// <summary>Current motion-access state; drives the contextual HUD banner.</summary>
        public Func<ActivityPermissionState> GetMotionPermission;

        /// <summary>Invoked by the banner after explicit user intent (MOBILE_ACTIVITY_INTEGRATION 15).</summary>
        public Action EnableMotionAccessRequested;

        public bool DebugToolsVisible;
    }
}
