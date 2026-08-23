using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WalkGame.Activity;
using WalkGame.Core;
using WalkGame.Gameplay;
using WalkGame.Persistence;
using WalkGame.UI;
using WalkGame.World;

namespace WalkGame.App
{
    /// <summary>
    /// Builds the UI layer and wires it to domain services through UiContext (one-way
    /// dependency: App -> services + UI abstractions; UI never reaches back into App).
    /// Also owns debug action implementations, which are gated out of release builds.
    /// </summary>
    public sealed class UiComposer : MonoBehaviour
    {
        private HudController _hud;
        private ProjectPanelController _projects;
        private DebugMenuController _debugMenu;
        private ActivityTicker _ticker;
        private AppFlowController _flow;
        private ExpeditionController _expedition;
        private FeedbackController _feedback;
        private MotionPermissionCoordinator _motionPermissions;
        private bool _permissionRequestInFlight;
        private string _resumeProductionMessage = string.Empty;

        public void Compose(AppFlowController flow, ActivityTicker ticker)
        {
            _flow = flow;
            _ticker = ticker;

            var host = GameHost.Current;

            var feedbackGo = new GameObject("FeedbackController");
            feedbackGo.transform.SetParent(transform, false);
            _feedback = feedbackGo.AddComponent<FeedbackController>();
            _feedback.Bind(host.Profile);

            var expeditionGo = new GameObject("ExpeditionController");
            expeditionGo.transform.SetParent(transform, false);
            _expedition = expeditionGo.AddComponent<ExpeditionController>();
            if (host.ResumeProductionSummary != null && host.ResumeProductionSummary.TotalProduced > 0)
            {
                _resumeProductionMessage = $"While you were away, restored systems prepared {host.ResumeProductionSummary.TotalProduced:N0} materials to collect.";
            }

            var hudGo = new GameObject("Hud");
            hudGo.transform.SetParent(transform, false);
            _hud = hudGo.AddComponent<HudController>();

            _motionPermissions = new MotionPermissionCoordinator(host.Provider, host.Log);
            _motionPermissions.StateChanged += _ => RefreshAll();
            StartCoroutine(RefreshMotionPermissionRoutine());

            _hud.Bind(new UiContext
            {
                GetProfile = () => host.Profile,
                GetIsExplore = () => host.Modes.Current == GameMode.ExploreMode,
                ToggleExploreRequested = ToggleMode,
                GetCollectables = GetCollectables,
                CollectProducerRequested = CollectProducer,
                CollectAllRequested = CollectAll,
                GetProducerStatuses = GetProducerStatuses,
                GetNextGoal = GetNextGoal,
                GetBuilderSelection = _flow.GetBuilderSelection,
                BeginBuildingMoveRequested = _flow.BeginSelectedBuildingMove,
                RotateBuildingRequested = _flow.RotateSelectedBuilding,
                ConfirmBuildingMoveRequested = _flow.ConfirmBuildingMove,
                CancelBuildingMoveRequested = _flow.CancelBuildingMove,
                ResetBuildingPreviewRequested = _flow.ResetBuildingPreview,
                ExploreMoveInputChanged = _flow.SetExploreMoveInput,
                GetInteractionPrompt = _flow.GetInteractionPrompt,
                InteractRequested = _flow.Interact,
                StartWalkExpeditionRequested = () => StartExpedition(SessionType.Walk),
                StartRunExpeditionRequested = () => StartExpedition(SessionType.Run),
                FinishExpeditionRequested = FinishExpedition,
                IsExpeditionActive = () => _expedition.IsActive,
                GetExpeditionStatus = () => _expedition.StatusMessage,
                GetExpeditionProgress = GetExpeditionProgress,
                GetMotionPermission = () => _motionPermissions.CurrentState,
                EnableMotionAccessRequested = BeginMotionPermissionRequest,
                GetOnboardingMessage = GetOnboardingMessage,
                IsOnboardingVisible = IsOnboardingVisible,
                AdvanceOnboardingRequested = AdvanceOnboarding,
                DismissOnboardingRequested = DismissOnboarding,
                GetAudioSettings = () => _feedback.GetSettingsSummary(),
                ToggleSettingsRequested = () => _hud.ToggleSettings(),
                IsSettingsVisible = () => _hud.IsSettingsVisible,
                ToggleHapticsRequested = ToggleHaptics,
                AdjustMasterVolumeRequested = delta => AdjustAudioSetting(delta, AudioSetting.Master),
                AdjustMusicVolumeRequested = delta => AdjustAudioSetting(delta, AudioSetting.Music),
                AdjustEffectsVolumeRequested = delta => AdjustAudioSetting(delta, AudioSetting.Effects),
            });

            var projectsGo = new GameObject("ProjectPanel");
            projectsGo.transform.SetParent(transform, false);
            _projects = projectsGo.AddComponent<ProjectPanelController>();
            _projects.Bind(
                getProfile: () => host.Profile,
                getProjects: BuildProjectViews,
                evaluate: projectId => host.Restoration.Evaluate(projectId, out _, out _),
                tryComplete: TryCompleteProject,
                changed: RefreshAll);

            if (IsDebugAllowed(host))
            {
                host.Profile.settings.debugToolsEnabled = true;
                var debugGo = new GameObject("DebugMenu");
                debugGo.transform.SetParent(transform, false);
                _debugMenu = debugGo.AddComponent<DebugMenuController>();
                _debugMenu.Bind(
                    addSteps: steps =>
                    {
                        if (host.Provider is DebugActivityProvider debug)
                        {
                            debug.DebugAddSteps(steps);
                            _ticker.ProcessPassiveNow();
                        }
                    },
                    simulateWalk: () => _ticker.CompleteDebugSession(SessionType.Walk, 6500, 5000, 3600),
                    simulateRun: () => _ticker.CompleteDebugSession(SessionType.Run, 4800, 5000, 1500),
                    simulateVehicleSession: () =>
                    {
                        // Vehicle-like session must earn base steps but no bonus.
                        StartCoroutine(VehicleSessionRoutine());
                    },
                    grantVitality: amount =>
                    {
                        host.Ledger.Credit(new VitalityCredit { amount = amount, reasonCode = WellKnownIds.ReasonCodes.DebugGrant });
                        host.Persist();
                    },
                    advanceClockOneHour: () =>
                    {
                        if (host.Clock is OffsetClock offset)
                        {
                            offset.Advance(TimeSpan.FromHours(1));
                            host.Production.AccrueAll(host.Profile.worldState.currentRegionId);
                        }
                    },
                    resetRegion: ResetRegionDebug,
                    changed: RefreshAll);
            }

            host.Events.Subscribe<VitalityCredited>(_ => RefreshAll());
            host.Events.Subscribe<VitalitySpent>(_ => RefreshAll());
            host.Events.Subscribe<ProjectCompleted>(_ => RefreshAll());
            host.Events.Subscribe<BuildingRestored>(_ => RefreshAll());
            host.Events.Subscribe<RegionStageChanged>(_ => RefreshAll());
            host.Events.Subscribe<EnvironmentFlagChanged>(_ => RefreshAll());
            host.Events.Subscribe<LoreDiscovered>(_ => RefreshAll());
            host.Events.Subscribe<ProjectCompleted>(_ => _feedback.Play(FeedbackCue.Restoration));
            host.Events.Subscribe<VitalitySpent>(_ => _feedback.Play(FeedbackCue.Restoration));
            host.Events.Subscribe<RegionStageChanged>(_ => _feedback.Play(FeedbackCue.Milestone));
            host.Events.Subscribe<ActivityMilestoneReached>(_ => _feedback.Play(FeedbackCue.Milestone));
            host.Events.Subscribe<LoreDiscovered>(_ => _feedback.Play(FeedbackCue.Lore));
            host.Events.Subscribe<ModeChanged>(_ => _feedback.Play(FeedbackCue.ModeSwitch));
            _flow.PlacementFeedback += OnPlacementFeedback;

            if (_ticker != null)
            {
                _ticker.ActivityProcessed += RefreshAll;
            }

            _flow.PresentationChanged += RefreshAll;
            _expedition.Changed += RefreshAll;

            RefreshAll();
        }

        private void ToggleMode()
        {
            var host = GameHost.Current;
            if (host.Modes.Current == GameMode.BuilderMode)
            {
                _flow.EnterExplore();
            }
            else if (host.Modes.Current == GameMode.ExploreMode)
            {
                _flow.EnterBuilder();
            }
        }

        /// <summary>Debug-only vehicle Expedition through the async provider surface,
        /// observed from a coroutine so the main thread never blocks on .Result.</summary>
        private IEnumerator VehicleSessionRoutine()
        {
            var host = GameHost.Current;
            if (!(host.Provider is DebugActivityProvider debug))
            {
                yield break;
            }

            var startTask = debug.StartSessionAsync(SessionType.Walk);
            var startObservation = new TaskObservation<SessionStartError>();
            var startObserver = TaskObservation.Observe(startTask, startObservation);
            while (!startObserver.IsCompleted)
            {
                yield return null;
            }

            if (startObservation.IsFaulted || startObservation.IsCanceled ||
                startObservation.Value != SessionStartError.None)
            {
                yield break;
            }

            if (!host.Activity.BeginExpedition(SessionType.Walk, host.Clock.UtcNow))
            {
                yield break;
            }

            debug.SimulateVehicleDrive(minutes: 30);

            var stopTask = debug.StopSessionAsync();
            var stopObservation = new TaskObservation<ActivitySessionResult>();
            var stopObserver = TaskObservation.Observe(stopTask, stopObservation);
            while (!stopObserver.IsCompleted)
            {
                yield return null;
            }

            if (stopObservation.IsFaulted || stopObservation.IsCanceled)
            {
                host.Activity.AbandonExpedition();
                yield break;
            }

            var result = stopObservation.Value;
            if (result == null)
            {
                host.Activity.AbandonExpedition();
                yield break;
            }
            var trust = new TrustEvaluator(RewardPolicy.Default);
            result.trustScore = trust.EvaluateSession(new ActiveSessionState
            {
                accumulatedSteps = result.acceptedSteps,
                accumulatedDistanceMeters = result.verifiedDistanceMeters,
                movingSeconds = result.verifiedMovingSeconds,
            }, true, false, false);
            host.Activity.ProcessSessionResult(result, growthEligible: false);
            host.Persist();
            RefreshAll();
        }

        /// <summary>
        /// Accrual happens here so collect amounts are live at every HUD refresh; the
        /// checkpoint math is deterministic and capped, so extra accrual passes cannot
        /// over-produce (TECHNICAL_ARCHITECTURE 16).
        /// </summary>
        private IReadOnlyList<PendingCollect> GetCollectables()
        {
            var host = GameHost.Current;
            string regionId = host.Profile.worldState.currentRegionId;
            host.Production.AccrueAll(regionId);
            return host.Production.GetPendingCollectables(regionId);
        }

        private void CollectProducer(string producerId)
        {
            var host = GameHost.Current;
            var result = host.Production.Collect(host.Profile.worldState.currentRegionId, producerId);
            if (result.collected <= 0)
            {
                return;
            }

            host.Persist();
            _feedback.Play(FeedbackCue.Collection);
            _flow.Presenter?.Refresh();
            RefreshAll();
        }

        private void CollectAll()
        {
            var host = GameHost.Current;
            if (host == null)
            {
                return;
            }

            var results = host.Production.CollectAll(host.Profile.worldState.currentRegionId);
            if (results.Count == 0)
            {
                return;
            }

            host.Persist();
            _feedback.Play(FeedbackCue.Collection);
            _flow.Presenter?.Refresh();
            RefreshAll();
        }

        private IReadOnlyList<ProducerStatus> GetProducerStatuses()
        {
            var host = GameHost.Current;
            if (host == null)
            {
                return Array.Empty<ProducerStatus>();
            }

            host.Production.AccrueAll(host.Profile.worldState.currentRegionId);
            return host.Production.GetStatuses(host.Profile.worldState.currentRegionId);
        }

        private string GetExpeditionProgress()
        {
            if (_expedition == null || !_expedition.IsActive)
            {
                return _expedition != null && !string.IsNullOrEmpty(_expedition.LastRewardMessage)
                    ? _expedition.LastRewardMessage + " · optional bonuses are bounded."
                    : "Optional · base steps count; performance bonuses are bounded.";
            }

            var sample = _expedition.LatestSample;
            double minutes = Math.Max(0d, sample.movingSeconds) / 60d;
            return $"{sample.accumulatedSteps:N0} steps · {sample.accumulatedDistanceMeters / 1000d:0.0} km · {minutes:0} min moving";
        }

        private string GetNextGoal()
        {
            var host = GameHost.Current;
            if (host == null)
            {
                return string.Empty;
            }

            if (host.LastSaveResult == SaveLoadResult.RecoveredFromBackup)
            {
                return "Recovered your last good save. Your progress is safe; keep playing normally.";
            }

            if (host.LastSaveResult == SaveLoadResult.Failed || host.LastSaveResult == SaveLoadResult.IncompatibleSchema)
            {
                return "Save recovery needs attention. Your current session is still playable; do not uninstall while diagnostics are reviewed.";
            }

            if (!string.IsNullOrEmpty(_resumeProductionMessage))
            {
                var resumeMessage = _resumeProductionMessage;
                _resumeProductionMessage = string.Empty;
                return resumeMessage;
            }

            var statuses = host.Restoration.GetStatuses();
            foreach (var status in statuses)
            {
                if (status.project == null || status.failure == RestorationFailure.AlreadyCompleted)
                {
                    continue;
                }

                if (status.failure == RestorationFailure.None)
                {
                    return $"Next goal: restore {FriendlyProjectTitle(status.project.titleKey)} — the basin changes immediately.";
                }

                if (status.failure == RestorationFailure.InsufficientVitality)
                {
                    return $"Next goal: walk for {status.project.vitalityCost:N0} Vitality to restore {FriendlyProjectTitle(status.project.titleKey)}.";
                }
            }

            return "Next goal: collect restored-system output, then inspect the next locked project.";
        }

        private bool IsOnboardingVisible()
        {
            return GameHost.Current != null && !GameHost.Current.Profile.settings.onboardingCompleted;
        }

        private string GetOnboardingMessage()
        {
            var host = GameHost.Current;
            if (host == null)
            {
                return string.Empty;
            }

            var profile = host.Profile;
            var region = profile.worldState.GetOrCreateRegionState(profile.worldState.currentRegionId);
            int step = profile.settings.onboardingStep;
            if (step < 2 && profile.lifetimeAcceptedSteps > 0) step = profile.settings.onboardingStep = 2;
            if (step < 3 && region.completedProjectIds.Count > 0) step = profile.settings.onboardingStep = 3;
            if (step < 4 && HasRestoredProducer(region)) step = profile.settings.onboardingStep = 4;
            if (step < 5 && HasMovedBuilding(region)) step = profile.settings.onboardingStep = 5;
            if (step < 6 && host.Modes.Current == GameMode.ExploreMode) step = profile.settings.onboardingStep = 6;

            switch (step)
            {
                case 0: return "Ashfall Basin is silent and broken. Your movement can bring it back to life.";
                case 1: return "Walk in the real world to earn Vitality. You can build and explore even before motion access is ready.";
                case 2: return "Vitality is the basin's spark. Open a project on the left and restore the first useful system.";
                case 3: return "Restored buildings keep working while you are away. Collect their output when you return.";
                case 4: return "This settlement is yours. Select a restored building, move it to a clear cell, and confirm the placement.";
                case 5: return "Enter Explore to walk through the same arrangement you built and find the basin's records.";
                default: return "The loop is yours now: move, restore, arrange, collect, and explore. Keep the next landmark in sight.";
            }
        }

        private void AdvanceOnboarding()
        {
            var host = GameHost.Current;
            if (host == null)
            {
                return;
            }

            host.Profile.settings.onboardingStep++;
            if (host.Profile.settings.onboardingStep >= 6)
            {
                host.Profile.settings.onboardingCompleted = true;
            }

            host.Persist();
            RefreshAll();
        }

        private void DismissOnboarding()
        {
            var host = GameHost.Current;
            if (host == null) return;
            host.Profile.settings.onboardingCompleted = true;
            host.Persist();
            RefreshAll();
        }

        private void StartExpedition(SessionType type)
        {
            _feedback.Play(FeedbackCue.ExpeditionStart);
            _expedition.StartExpedition(type);
        }

        private void FinishExpedition()
        {
            _feedback.Play(FeedbackCue.ExpeditionFinish);
            _expedition.FinishExpedition();
        }

        private void OnPlacementFeedback(PlacementFailure failure)
        {
            _feedback.Play(failure == PlacementFailure.None
                ? FeedbackCue.PlacementConfirm
                : FeedbackCue.PlacementInvalid);
        }

        private void ToggleHaptics()
        {
            _feedback.ToggleHaptics();
            GameHost.Current?.Persist();
            RefreshAll();
        }

        private enum AudioSetting
        {
            Master,
            Music,
            Effects,
        }

        private void AdjustAudioSetting(float delta, AudioSetting setting)
        {
            switch (setting)
            {
                case AudioSetting.Master: _feedback.AdjustMaster(delta); break;
                case AudioSetting.Music: _feedback.AdjustMusic(delta); break;
                case AudioSetting.Effects: _feedback.AdjustEffects(delta); break;
            }

            GameHost.Current?.Persist();
            RefreshAll();
        }

        private static bool HasRestoredProducer(RegionState region)
        {
            foreach (var producer in region.producerStates.Values)
            {
                if (producer != null && region.buildingStates.TryGetValue(producer.buildingInstanceId, out var building) && building.IsRestored)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasMovedBuilding(RegionState region)
        {
            foreach (var building in region.buildingStates.Values)
            {
                if (building != null && building.placement.placementVersion > 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Runs the contextual permission round-trip from the HUD button without ever
        /// blocking the main thread; overlapping taps are ignored while in flight.
        /// </summary>
        private void BeginMotionPermissionRequest()
        {
            if (_permissionRequestInFlight || _motionPermissions == null)
            {
                return;
            }

            _permissionRequestInFlight = true;
            StartCoroutine(MotionPermissionRoutine());
        }

        private IEnumerator RefreshMotionPermissionRoutine()
        {
            var request = _motionPermissions.RefreshAsync();
            var observation = new TaskObservation<ActivityPermissionState>();
            var observer = TaskObservation.Observe(request, observation);
            while (!observer.IsCompleted)
            {
                yield return null;
            }

            if (observation.IsFaulted || observation.IsCanceled)
            {
                GameHost.Current?.Log.Warning("Motion permission refresh faulted; state left unchanged.");
            }
        }

        private IEnumerator MotionPermissionRoutine()
        {
            var request = _motionPermissions.RequestAsync();
            var observation = new TaskObservation<MotionPermissionOutcome>();
            var observer = TaskObservation.Observe(request, observation);
            while (!observer.IsCompleted)
            {
                yield return null;
            }

            _permissionRequestInFlight = false;
            if (observation.IsFaulted || observation.IsCanceled)
            {
                GameHost.Current?.Log.Warning("Motion permission request faulted; state left unchanged.");
            }
            else
            {
                GameHost.Current?.Log.Info($"Motion permission outcome: {observation.Value}.");
            }

            RefreshAll();
        }

        private IReadOnlyList<RestorationProjectView> BuildProjectViews()
        {
            var host = GameHost.Current;
            var views = new List<RestorationProjectView>();
            var region = host.Profile.worldState.GetOrCreateRegionState(host.Profile.worldState.currentRegionId);
            foreach (var project in host.Catalog.GetProjectsForRegion(host.Profile.worldState.currentRegionId))
            {
                string resourceCost = string.Empty;
                foreach (var cost in project.resourceCosts)
                {
                    resourceCost += $"{HumanizeId(cost.Key)} {cost.Value} ";
                }

                string prerequisites = string.Empty;
                foreach (var prerequisite in project.prerequisiteProjectIds)
                {
                    foreach (var candidate in host.Catalog.GetProjectsForRegion(project.regionId))
                    {
                        if (candidate.projectId == prerequisite)
                        {
                            prerequisites += FriendlyProjectTitle(candidate.titleKey) + ", ";
                            break;
                        }
                    }
                }

                bool affordable = host.Profile.vitalityBalance >= project.vitalityCost;
                foreach (var cost in project.resourceCosts)
                {
                    host.Profile.resources.TryGetValue(cost.Key, out var owned);
                    affordable &= owned >= cost.Value;
                }

                views.Add(new RestorationProjectView
                {
                    ProjectId = project.projectId,
                    Title = FriendlyProjectTitle(project.titleKey),
                    Category = project.category.ToString(),
                    Description = FriendlyProjectDescription(project.descriptionKey),
                    VitalityCost = project.vitalityCost,
                    ResourceCost = resourceCost.TrimEnd(),
                    Prerequisites = prerequisites.TrimEnd(' ', ','),
                    RewardSummary = BuildRewardSummary(project),
                    Completed = region.completedProjectIds.Contains(project.projectId),
                    Affordable = affordable,
                });
            }

            return views;
        }

        private static string BuildRewardSummary(RestorationProjectDefinition project)
        {
            var rewards = new List<string>();
            foreach (var reward in project.rewardActions)
            {
                if (reward == null) continue;
                switch (reward.kind)
                {
                    case RewardActionKind.SetBuildingRestored: rewards.Add("restore " + HumanizeId(reward.targetId)); break;
                    case RewardActionKind.SetEnvironmentFlag: rewards.Add("change the environment"); break;
                    case RewardActionKind.AddRegionScore: rewards.Add("+" + reward.amount + " " + reward.targetId); break;
                    case RewardActionKind.GrantResource: rewards.Add("grant " + HumanizeId(reward.secondaryId)); break;
                    case RewardActionKind.UnlockNpc: rewards.Add("bring a new NPC"); break;
                    case RewardActionKind.DiscoverLore: rewards.Add("reveal a story record"); break;
                    default: break;
                }
            }

            return rewards.Count == 0 ? "advance the basin" : string.Join(", ", rewards);
        }

        private static string FriendlyProjectTitle(string value)
        {
            return FriendlyKey(value, "title");
        }

        private static string FriendlyProjectDescription(string value)
        {
            return FriendlyKey(value, "desc");
        }

        private static string FriendlyKey(string value, string suffix)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string trimmed = value;
            if (trimmed.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - suffix.Length - 1);
            }
            int lastDot = trimmed.LastIndexOf('.');
            string text = lastDot >= 0 ? trimmed.Substring(lastDot + 1) : trimmed;
            return HumanizeId(text);
        }

        private static string HumanizeId(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            int lastDot = value.LastIndexOf('.');
            string text = lastDot >= 0 ? value.Substring(lastDot + 1) : value;
            text = text.Replace('_', ' ').Replace('-', ' ');
            return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        private bool TryCompleteProject(string projectId)
        {
            var host = GameHost.Current;
            bool completed = host.Restoration.TryComplete(projectId, out _);
            if (completed)
            {
                host.Persist();
                _flow.Presenter?.Refresh();
            }

            return completed;
        }

        private static bool IsDebugAllowed(GameHost host)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }

        private void ResetRegionDebug()
        {
            var host = GameHost.Current;
            var region = host.Profile.worldState.GetOrCreateRegionState(host.Profile.worldState.currentRegionId);
            region.completedProjectIds.Clear();
            region.unlockedProjectIds.Clear();
            region.arrivedNpcIds.Clear();
            region.discoveredLoreIds.Clear();
            region.environmentFlags.Clear();
            region.restorationStage = 0;
            region.ecologyScore = region.infrastructureScore = region.communityScore = region.knowledgeScore = 0;
            foreach (var instance in host.Catalog.Ashfall.defaultBuildingInstances)
            {
                var state = region.GetOrCreateBuildingState(instance.instanceId, instance.buildingDefinitionId);
                state.lifecycleState = BuildingLifecycleState.Ruin;
                state.upgradeTier = 0;
                state.placement.gridX = instance.initialPlacement.gridX;
                state.placement.gridY = instance.initialPlacement.gridY;
                state.placement.rotationQuarterTurns = instance.initialPlacement.rotationQuarterTurns;
                state.placement.placementVersion++;
            }

            _flow.Presenter?.Refresh();
            RefreshAll();
        }

        private void RefreshAll()
        {
            _hud?.Refresh();
            _projects?.Refresh();
        }
    }
}
