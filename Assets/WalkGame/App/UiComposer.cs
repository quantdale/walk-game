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
        private string _saveHealthMessage = string.Empty;

        public void Compose(AppFlowController flow, ActivityTicker ticker)
        {
            _flow = flow;
            _ticker = ticker;

            var host = GameHost.Current;

            // uGUI is inert without an EventSystem; programmatic composition must
            // supply one before any button exists.
            UiRuntime.EnsureEventSystem(transform);

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
                        host.CommitChanges();
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

            host.Events.Subscribe<VitalityCredited>(OnVitalityCreditedRefresh);
            host.Events.Subscribe<VitalitySpent>(OnVitalitySpentRefresh);
            host.Events.Subscribe<ProjectCompleted>(OnProjectCompleted);
            host.Events.Subscribe<BuildingRestored>(OnBuildingRestored);
            host.Events.Subscribe<RegionStageChanged>(OnRegionStageChanged);
            host.Events.Subscribe<EnvironmentFlagChanged>(OnEnvironmentFlagChanged);
            host.Events.Subscribe<LoreDiscovered>(OnLoreDiscovered);
            host.Events.Subscribe<ProjectCompleted>(OnProjectCompletedRestorationCue);
            host.Events.Subscribe<VitalitySpent>(OnVitalitySpentRestorationCue);
            host.Events.Subscribe<RegionStageChanged>(OnRegionStageMilestoneCue);
            host.Events.Subscribe<ActivityMilestoneReached>(OnActivityMilestoneCue);
            host.Events.Subscribe<LoreDiscovered>(OnLoreDiscoveredCue);
            host.Events.Subscribe<ModeChanged>(OnModeChangedCue);
            host.PersistenceReverted += OnPersistenceReverted;
            host.DurableCommitResolved += OnDurableCommitResolved;
            _flow.PlacementFeedback += OnPlacementFeedback;

            if (_ticker != null)
            {
                _ticker.ActivityProcessed += RefreshAll;
            }

            _flow.PresentationChanged += RefreshAll;
            _expedition.Changed += RefreshAll;

            RefreshAll();
        }

        // Named handlers so OnDestroy can detach every subscription; the composition
        // root outlives this component across scene reloads, and a stale handler would
        // otherwise fire into destroyed UI (M8 lifecycle audit).

        private void OnVitalityCreditedRefresh(VitalityCredited _) => RefreshAll();
        private void OnVitalitySpentRefresh(VitalitySpent _) => RefreshAll();
        private void OnProjectCompleted(ProjectCompleted _) => RefreshAll();
        private void OnBuildingRestored(BuildingRestored _) => RefreshAll();
        private void OnRegionStageChanged(RegionStageChanged _) => RefreshAll();
        private void OnEnvironmentFlagChanged(EnvironmentFlagChanged _) => RefreshAll();
        private void OnLoreDiscovered(LoreDiscovered _) => RefreshAll();

        // Durable-success celebration cues are queued, not played immediately: the
        // events fire inside the mutation, before its CommitChanges resolves. The
        // commit outcome then flushes or drops them so a reverted action never
        // celebrates (M8.2 feedback-truthfulness repair).
        private void OnProjectCompletedRestorationCue(ProjectCompleted _) => _feedback.QueueDurable(FeedbackCue.Restoration);
        private void OnVitalitySpentRestorationCue(VitalitySpent _) => _feedback.QueueDurable(FeedbackCue.Restoration);
        private void OnRegionStageMilestoneCue(RegionStageChanged _) => _feedback.QueueDurable(FeedbackCue.Milestone);
        private void OnActivityMilestoneCue(ActivityMilestoneReached _) => _feedback.QueueDurable(FeedbackCue.Milestone);
        private void OnLoreDiscoveredCue(LoreDiscovered _) => _feedback.QueueDurable(FeedbackCue.Lore);
        private void OnModeChangedCue(ModeChanged _) => _feedback.Play(FeedbackCue.ModeSwitch);

        private void OnDurableCommitResolved(bool committed)
        {
            if (committed)
            {
                _feedback.FlushQueuedDurable();
            }
            else
            {
                _feedback.DropQueuedDurable();
            }
        }

        private void OnDestroy()
        {
            var host = GameHost.Current;
            if (host?.Events != null)
            {
                host.Events.Unsubscribe<VitalityCredited>(OnVitalityCreditedRefresh);
                host.Events.Unsubscribe<VitalitySpent>(OnVitalitySpentRefresh);
                host.Events.Unsubscribe<ProjectCompleted>(OnProjectCompleted);
                host.Events.Unsubscribe<BuildingRestored>(OnBuildingRestored);
                host.Events.Unsubscribe<RegionStageChanged>(OnRegionStageChanged);
                host.Events.Unsubscribe<EnvironmentFlagChanged>(OnEnvironmentFlagChanged);
                host.Events.Unsubscribe<LoreDiscovered>(OnLoreDiscovered);
                host.Events.Unsubscribe<ProjectCompleted>(OnProjectCompletedRestorationCue);
                host.Events.Unsubscribe<VitalitySpent>(OnVitalitySpentRestorationCue);
                host.Events.Unsubscribe<RegionStageChanged>(OnRegionStageMilestoneCue);
                host.Events.Unsubscribe<ActivityMilestoneReached>(OnActivityMilestoneCue);
                host.Events.Unsubscribe<LoreDiscovered>(OnLoreDiscoveredCue);
                host.Events.Unsubscribe<ModeChanged>(OnModeChangedCue);
            }

            if (host != null)
            {
                host.PersistenceReverted -= OnPersistenceReverted;
                host.DurableCommitResolved -= OnDurableCommitResolved;
            }

            if (_flow != null)
            {
                _flow.PlacementFeedback -= OnPlacementFeedback;
                _flow.PresentationChanged -= RefreshAll;
            }

            if (_ticker != null)
            {
                _ticker.ActivityProcessed -= RefreshAll;
            }

            if (_expedition != null)
            {
                _expedition.Changed -= RefreshAll;
            }
        }

        private void ToggleMode()
        {
            var host = GameHost.Current;
            if (host == null || host.Modes == null)
            {
                return;
            }

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
            bool durable = host.CommitChanges();
            debug.ResolveSessionCompletion(result.sessionId, durable);
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
            if (host?.Profile == null)
            {
                // Blocked/fatal persistence health composes no collectable surface.
                return Array.Empty<PendingCollect>();
            }

            string regionId = host.Profile.worldState.currentRegionId;
            host.Production.AccrueAll(regionId);
            return host.Production.GetPendingCollectables(regionId);
        }

        private void CollectProducer(string producerId)
        {
            var host = GameHost.Current;
            if (host?.Profile == null)
            {
                return;
            }

            var result = host.Production.Collect(host.Profile.worldState.currentRegionId, producerId);
            if (result.collected <= 0)
            {
                return;
            }

            if (!host.CommitChanges())
            {
                // The collection was reverted with the failed write; no reward cue.
                RefreshAll();
                return;
            }

            _feedback.Play(FeedbackCue.Collection);
            _flow.Presenter?.Refresh();
            RefreshAll();
        }

        private void CollectAll()
        {
            var host = GameHost.Current;
            if (host == null || host.Profile == null)
            {
                return;
            }

            var results = host.Production.CollectAll(host.Profile.worldState.currentRegionId);
            if (results.Count == 0)
            {
                return;
            }

            if (!host.CommitChanges())
            {
                // The collection was reverted with the failed write; no reward cue.
                RefreshAll();
                return;
            }

            _feedback.Play(FeedbackCue.Collection);
            _flow.Presenter?.Refresh();
            RefreshAll();
        }

        private IReadOnlyList<ProducerStatus> GetProducerStatuses()
        {
            var host = GameHost.Current;
            if (host == null || host.Profile == null)
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

        private void OnPersistenceReverted()
        {
            // The failed mutation was rolled back to disk truth; say so plainly instead
            // of letting celebration copy stand for an action that was never saved.
            _saveHealthMessage = "That change could not be saved, so it was undone. " +
                                 "Your world matches its last good save; try again in a moment.";
            RefreshAll();
        }

        private string GetNextGoal()
        {
            var host = GameHost.Current;
            if (host == null || host.Profile == null)
            {
                return "Save recovery needs attention. Your progress was not erased.";
            }

            if (!host.PersistenceBlocked && !string.IsNullOrEmpty(_saveHealthMessage))
            {
                var saveMessage = _saveHealthMessage;
                _saveHealthMessage = string.Empty;
                return saveMessage;
            }

            if (host.Health == PersistenceHealth.Recovered)
            {
                return "We restored your world from its last good save after a file problem. Keep playing normally.";
            }

            if (!string.IsNullOrEmpty(_resumeProductionMessage))
            {
                var resumeMessage = _resumeProductionMessage;
                _resumeProductionMessage = string.Empty;
                return resumeMessage;
            }

            var statuses = host.Restoration.GetStatuses();

            // Region-finale communication (M8 section 19): once every project is done,
            // say so plainly instead of pointing at locked work that does not exist.
            // Post-region content is intentionally out of scope - communicate that
            // cleanly rather than leaving a dead end.
            var allProjects = host.Catalog.GetProjectsForRegion(host.Profile.worldState.currentRegionId);
            if (allProjects.Count > 0)
            {
                bool allComplete = true;
                foreach (var project in allProjects)
                {
                    if (!host.Profile.worldState.GetOrCreateRegionState(host.Profile.worldState.currentRegionId)
                        .completedProjectIds.Contains(project.projectId))
                    {
                        allComplete = false;
                        break;
                    }
                }

                if (allComplete)
                {
                    return "Ashfall Basin is fully restored and the transit gate stands open. New regions will arrive in a future expedition - keep walking to stay strong.";
                }
            }

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
            var host = GameHost.Current;
            return host != null && host.Profile != null &&
                   !host.Profile.settings.onboardingCompleted;
        }

        private string GetOnboardingMessage()
        {
            var host = GameHost.Current;
            if (host == null || host.Profile == null)
            {
                return string.Empty;
            }

            // Pure derivation: this getter runs on every HUD refresh, so it must not
            // write canonical state as a side effect. Persisted advancement happens in
            // AdvanceOnboarding/DismissOnboarding through CommitChanges (M8.2).
            int step = DeriveOnboardingStep(host);

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

        /// <summary>
        /// Onboarding progress derived from actual world facts; the persisted
        /// settings.onboardingStep only ever advances durably through the explicit
        /// advance/dismiss paths, never from a presentation read.
        /// </summary>
        private static int DeriveOnboardingStep(GameHost host)
        {
            var profile = host.Profile;
            var region = profile.worldState.GetOrCreateRegionState(profile.worldState.currentRegionId);
            int step = profile.settings.onboardingStep;
            if (step < 2 && profile.lifetimeAcceptedSteps > 0) step = 2;
            if (step < 3 && region.completedProjectIds.Count > 0) step = 3;
            if (step < 4 && HasRestoredProducer(region)) step = 4;
            if (step < 5 && HasMovedBuilding(region)) step = 5;
            if (step < 6 && host.Modes.Current == GameMode.ExploreMode) step = 6;
            return step;
        }

        private void AdvanceOnboarding()
        {
            var host = GameHost.Current;
            if (host == null || host.Profile == null)
            {
                return;
            }

            // Advance one step beyond what is currently displayed (derived), so a
            // tap can never regress the card relative to real world progress.
            host.Profile.settings.onboardingStep = Math.Max(
                host.Profile.settings.onboardingStep, DeriveOnboardingStep(host)) + 1;
            if (host.Profile.settings.onboardingStep >= 6)
            {
                host.Profile.settings.onboardingCompleted = true;
            }

            if (!host.CommitChanges())
            {
                // Reverted; the onboarding card stays because progress did not stick.
                RefreshAll();
                return;
            }

            RefreshAll();
        }

        private void DismissOnboarding()
        {
            var host = GameHost.Current;
            if (host == null || host.Profile == null) return;
            host.Profile.settings.onboardingCompleted = true;
            if (host.CommitChanges())
            {
                RefreshAll();
                return;
            }

            // Dismissal could not be made durable: keep the card visible so the state
            // never lies about what was saved.
            RefreshAll();
        }

        private void StartExpedition(SessionType type)
        {
            _feedback.Play(FeedbackCue.ExpeditionStart);
            _expedition.StartExpedition(type);
        }

        private void FinishExpedition()
        {
            _feedback.QueueDurable(FeedbackCue.ExpeditionFinish);
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
            GameHost.Current?.CommitChanges();
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

            GameHost.Current?.CommitChanges();
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
            if (host?.Profile == null || host.Restoration == null)
            {
                return false;
            }

            bool completed = host.Restoration.TryComplete(projectId, out _);
            if (completed)
            {
                completed = host.CommitChanges();
                if (completed)
                {
                    _flow.Presenter?.Refresh();
                }
                else
                {
                    // Restoration was rolled back with the failed write; the project
                    // panel refresh shows it as available again instead of lying.
                    RefreshAll();
                }
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

            // Debug resets are durable mutations too: contain them in the same
            // transactional boundary so a failed write reverts the wipe.
            if (!host.CommitChanges())
            {
                RefreshAll();
                return;
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
