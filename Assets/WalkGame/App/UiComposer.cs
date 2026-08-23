using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WalkGame.Activity;
using WalkGame.Core;
using WalkGame.Gameplay;
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
        private MotionPermissionCoordinator _motionPermissions;
        private bool _permissionRequestInFlight;

        public void Compose(AppFlowController flow, ActivityTicker ticker)
        {
            _flow = flow;
            _ticker = ticker;

            var host = GameHost.Current;

            var hudGo = new GameObject("Hud");
            hudGo.transform.SetParent(transform, false);
            _hud = hudGo.AddComponent<HudController>();

            _motionPermissions = new MotionPermissionCoordinator(host.Provider, host.Log);
            _motionPermissions.StateChanged += _ => RefreshAll();
            _ = _motionPermissions.RefreshAsync(); // reconcile with platform on boot

            _hud.Bind(new UiContext
            {
                GetProfile = () => host.Profile,
                GetIsExplore = () => host.Modes.Current == GameMode.ExploreMode,
                ToggleExploreRequested = ToggleMode,
                GetCollectables = GetCollectables,
                CollectProducerRequested = CollectProducer,
                GetMotionPermission = () => _motionPermissions.CurrentState,
                EnableMotionAccessRequested = BeginMotionPermissionRequest,
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
                        if (host.Provider is DebugActivityProvider debug)
                        {
                            var error = debug.DebugBeginVehicleLikeSession(out var driver);
                            if (error == SessionStartError.None)
                            {
                                driver.Drive(minutes: 30);
                                var result = debug.StopSessionAsync().Result;
                                var trust = new TrustEvaluator(RewardPolicy.Default);
                                result.trustScore = trust.EvaluateSession(new ActiveSessionState
                                {
                                    accumulatedSteps = result.acceptedSteps,
                                    accumulatedDistanceMeters = result.verifiedDistanceMeters,
                                    movingSeconds = result.verifiedMovingSeconds,
                                }, true, false, false);
                                host.Activity.ProcessSessionResult(result, growthEligible: false);
                                host.Persist();
                            }
                        }
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

            if (_ticker != null)
            {
                _ticker.ActivityProcessed += RefreshAll;
            }

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
            _flow.Presenter?.Refresh();
            RefreshAll();
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

        private IEnumerator MotionPermissionRoutine()
        {
            var request = _motionPermissions.RequestAsync();
            while (!request.IsCompleted)
            {
                yield return null;
            }

            _permissionRequestInFlight = false;
            if (request.IsFaulted)
            {
                GameHost.Current.Log.Warning("Motion permission request faulted; state left unchanged.");
            }
            else
            {
                GameHost.Current.Log.Info($"Motion permission outcome: {request.Result}.");
            }

            RefreshAll();
        }

        private IReadOnlyList<RestorationProjectView> BuildProjectViews()
        {
            var host = GameHost.Current;
            var views = new List<RestorationProjectView>();
            foreach (var project in host.Catalog.GetProjectsForRegion(host.Profile.worldState.currentRegionId))
            {
                string resourceCost = string.Empty;
                foreach (var cost in project.resourceCosts)
                {
                    resourceCost += $"{cost.Key.Replace("resource.", string.Empty)} {cost.Value} ";
                }

                views.Add(new RestorationProjectView
                {
                    ProjectId = project.projectId,
                    Title = project.titleKey.Replace('.', ' '),
                    VitalityCost = project.vitalityCost,
                    ResourceCost = resourceCost.TrimEnd(),
                });
            }

            return views;
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
