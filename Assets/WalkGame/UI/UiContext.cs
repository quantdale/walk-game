using System;
using System.Collections.Generic;
using UnityEngine;
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
        public Action CollectAllRequested;
        public Func<IReadOnlyList<WalkGame.Gameplay.ProducerStatus>> GetProducerStatuses;
        public Func<string> GetNextGoal;

        public Func<BuilderSelectionView> GetBuilderSelection;
        public Action BeginBuildingMoveRequested;
        public Action RotateBuildingRequested;
        public Action ConfirmBuildingMoveRequested;
        public Action CancelBuildingMoveRequested;
        public Action ResetBuildingPreviewRequested;

        public Action<Vector2> ExploreMoveInputChanged;
        public Func<string> GetInteractionPrompt;
        public Action InteractRequested;

        public Action StartWalkExpeditionRequested;
        public Action StartRunExpeditionRequested;
        public Action FinishExpeditionRequested;
        public Func<bool> IsExpeditionActive;
        public Func<string> GetExpeditionStatus;
        public Func<string> GetExpeditionProgress;

        public Func<string> GetOnboardingMessage;
        public Func<bool> IsOnboardingVisible;
        public Action AdvanceOnboardingRequested;
        public Action DismissOnboardingRequested;

        public Func<string> GetAudioSettings;
        public Action ToggleSettingsRequested;
        public Func<bool> IsSettingsVisible;
        public Action ToggleHapticsRequested;
        public Action<float> AdjustMasterVolumeRequested;
        public Action<float> AdjustMusicVolumeRequested;
        public Action<float> AdjustEffectsVolumeRequested;

        /// <summary>Current motion-access state; drives the contextual HUD banner.</summary>
        public Func<ActivityPermissionState> GetMotionPermission;

        /// <summary>Invoked by the banner after explicit user intent (MOBILE_ACTIVITY_INTEGRATION 15).</summary>
        public Action EnableMotionAccessRequested;

        public bool DebugToolsVisible;
    }

    public sealed class BuilderSelectionView
    {
        public bool hasSelection;
        public string title = string.Empty;
        public string status = string.Empty;
        public string placement = string.Empty;
        public bool canMove;
        public bool isMoving;
        public bool previewValid;
        public string previewStatus = string.Empty;
    }

    public enum FeedbackCue
    {
        Button = 0,
        Restoration = 1,
        Collection = 2,
        Milestone = 3,
        PlacementConfirm = 4,
        PlacementInvalid = 5,
        ModeSwitch = 6,
        ExpeditionStart = 7,
        ExpeditionFinish = 8,
        Lore = 9,
    }
}
