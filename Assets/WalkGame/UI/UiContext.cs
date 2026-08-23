using System;
using System.Collections.Generic;
using WalkGame.Core;
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
        public bool DebugToolsVisible;
    }
}
