using UnityEngine;
using WalkGame.Core;
using WalkGame.Building;
using WalkGame.World;
using WalkGame.UI;

namespace WalkGame.App
{
    /// <summary>
    /// Builds and drives the Builder <-> Explore presentation split inside one loaded
    /// region scene (TECHNICAL_ARCHITECTURE 6/7). Rigs are constructed programmatically so
    /// scenes stay content-only; ModeStateMachine remains the only mode authority.
    /// </summary>
    public sealed class AppFlowController : MonoBehaviour
    {
        private RegionPresenter _presenter;
        private BuilderCameraController _builderRig;
        private ExploreCharacterController _exploreCharacter;
        private Transform _exploreCamera;
        private BuildingActor _selectedBuilding;
        private BuildingActor _movingBuilding;
        private BuildingPlacement _previewPlacement;
        private PlacementFailure _previewFailure = PlacementFailure.None;
        private string _dialogueMessage = string.Empty;
        private float _dialogueUntil;
        private bool _rigReady;

        public event System.Action PresentationChanged;
        public event System.Action<PlacementFailure> PlacementFeedback;

        public RegionPresenter Presenter => _presenter;

        private void Start()
        {
            var host = GameHost.Current;
            if (host == null)
            {
                Debug.LogError("AppFlowController requires GameHost; add GameHost to the bootstrap scene."); // hygiene-allow: no host/log exists yet
                enabled = false;
                return;
            }

            if (host.PersistenceBlocked)
            {
                // Fail-closed recovery mode composes no playable rig at all (ADR 0007).
                enabled = false;
                return;
            }

            host.Events.Subscribe<ModeChanged>(OnModeChanged);
            BuildRuntimeRig();

            var region = host.Profile.worldState.GetOrCreateRegionState(host.Profile.worldState.currentRegionId);
            _presenter.Present(WorldRegistry.CurrentRegion, region);
            _presenter.Environment?.SetReducedMotion(host.Profile.settings.reducedMotion);
            _builderRig.BuildingTapped += OnBuildingTapped;
            _builderRig.GroundTapped += OnGroundTapped;
            _exploreCharacter.InteractionChanged += OnExploreInteractionChanged;

            // Vertical slice boots straight into the region's Builder View.
            host.Modes.TryTransition(GameMode.LoadingRegion);
            host.Modes.TryTransition(GameMode.BuilderMode);
        }

        public void EnterExplore()
        {
            GameHost.Current?.Modes.TryTransition(GameMode.ExploreMode);
        }

        public void EnterBuilder()
        {
            GameHost.Current?.Modes.TryTransition(GameMode.BuilderMode);
        }

        private void OnModeChanged(ModeChanged change)
        {
            if (!_rigReady)
            {
                return;
            }

            bool explore = change.Current == GameMode.ExploreMode;

            _builderRig.gameObject.SetActive(!explore);
            _exploreCharacter.gameObject.SetActive(explore);
            _exploreCamera.gameObject.SetActive(explore);

            if (explore)
            {
                CancelBuildingMove();
                var definition = WorldRegistry.CurrentRegion;
                _exploreCharacter.TeleportTo(_presenter.GridToWorld(definition.exploreSpawnGridX, definition.exploreSpawnGridY));
            }
            else
            {
                // Re-project canonical state after any exploration-time discoveries.
                _presenter.Refresh();
            }
        }

        public BuilderSelectionView GetBuilderSelection()
        {
            var view = new BuilderSelectionView();
            if (_selectedBuilding == null || GameHost.Current == null)
            {
                return view;
            }

            var host = GameHost.Current;
            var region = host.Profile.worldState.GetOrCreateRegionState(host.Profile.worldState.currentRegionId);
            if (!region.buildingStates.TryGetValue(_selectedBuilding.InstanceId, out var state))
            {
                return view;
            }

            var definition = host.Catalog.GetBuilding(state.definitionId);
            var defaultInstance = host.Catalog.Ashfall.FindDefaultInstance(state.instanceId);
            bool movable = definition != null && definition.movableAfterRestore && state.IsRestored &&
                           (defaultInstance == null || !defaultInstance.fixedPlacement);
            view.hasSelection = true;
            view.title = Humanize(_selectedBuilding.InstanceId);
            view.status = state.IsRestored
                ? (state.upgradeTier >= 2 ? "Improved" : "Restored")
                : "Ruin — restore it to unlock movement";
            view.placement = $"Grid {state.placement.gridX}, {state.placement.gridY} · {state.placement.rotationQuarterTurns * 90}°";
            view.canMove = movable;
            view.isMoving = host.Placement.IsMoving;
            view.previewValid = _previewFailure == PlacementFailure.None;
            view.previewStatus = host.Placement.IsMoving ? FriendlyPlacementFailure(_previewFailure) : string.Empty;
            return view;
        }

        public void SetExploreMoveInput(Vector2 input)
        {
            _exploreCharacter?.SetVirtualInput(input);
        }

        public string GetInteractionPrompt()
        {
            if (Time.unscaledTime < _dialogueUntil && !string.IsNullOrEmpty(_dialogueMessage))
            {
                return _dialogueMessage;
            }

            if (_exploreCharacter == null)
            {
                return string.Empty;
            }

            if (_exploreCharacter.NearbyNpc != null)
            {
                return $"Talk to {_exploreCharacter.NearbyNpc.DisplayName} · {_exploreCharacter.NearbyNpc.Role}";
            }

            if (_exploreCharacter.NearbyLore != null)
            {
                return $"Inspect {_exploreCharacter.NearbyLore.Title}";
            }

            return string.Empty;
        }

        public void Interact()
        {
            var host = GameHost.Current;
            if (host == null || _exploreCharacter == null)
            {
                return;
            }

            if (_exploreCharacter.NearbyNpc != null)
            {
                _dialogueMessage = $"{_exploreCharacter.NearbyNpc.DisplayName}: {_exploreCharacter.NearbyNpc.Dialogue}";
                _dialogueUntil = Time.unscaledTime + 7f;
                PresentationChanged?.Invoke();
                return;
            }

            if (_exploreCharacter.NearbyLore != null)
            {
                var lore = _exploreCharacter.NearbyLore;
                bool discovered = host.Exploration.TryDiscoverLore(
                    host.Profile.worldState.currentRegionId, lore.LoreId);
                _dialogueMessage = discovered
                    ? $"{lore.Title}: {lore.Body}"
                    : $"{lore.Title}: already recorded";
                if (discovered && !host.CommitChanges())
                {
                    // The discovery was rolled back with the failed write; the dialogue
                    // must not present it as recorded (M8.2 feedback-truthfulness).
                    _dialogueMessage = $"{lore.Title}: discovery could not be saved this session.";
                }

                _dialogueUntil = Time.unscaledTime + 8f;
                _presenter.Refresh();
                PresentationChanged?.Invoke();
            }
        }

        private void OnExploreInteractionChanged()
        {
            _dialogueMessage = string.Empty;
            _dialogueUntil = 0f;
            PresentationChanged?.Invoke();
        }

        public void BeginSelectedBuildingMove()
        {
            var host = GameHost.Current;
            if (host == null || _selectedBuilding == null)
            {
                return;
            }

            var region = host.Profile.worldState.GetOrCreateRegionState(host.Profile.worldState.currentRegionId);
            var failure = host.Placement.BeginMove(host.Catalog.Ashfall, region, _selectedBuilding.InstanceId);
            if (failure != PlacementFailure.None)
            {
                _previewFailure = failure;
                PlacementFeedback?.Invoke(failure);
                PresentationChanged?.Invoke();
                return;
            }

            _movingBuilding = _selectedBuilding;
            var state = region.buildingStates[_selectedBuilding.InstanceId];
            _previewPlacement = new BuildingPlacement
            {
                gridX = state.placement.gridX,
                gridY = state.placement.gridY,
                rotationQuarterTurns = state.placement.rotationQuarterTurns,
            };
            _previewFailure = PlacementFailure.None;
            _movingBuilding.SetPlacementPreview(host.Catalog.Ashfall, _previewPlacement, true);
            PresentationChanged?.Invoke();
        }

        public void RotateSelectedBuilding()
        {
            if (GameHost.Current == null || !GameHost.Current.Placement.IsMoving)
            {
                return;
            }

            _previewPlacement.rotationQuarterTurns = (_previewPlacement.rotationQuarterTurns + 1) % 4;
            UpdatePreview(_previewPlacement);
        }

        public void ConfirmBuildingMove()
        {
            var host = GameHost.Current;
            if (host == null || !host.Placement.IsMoving)
            {
                return;
            }

            if (host.Placement.ConfirmMove(_previewPlacement, out _previewFailure))
            {
                bool saved = host.CommitChanges();
                _movingBuilding?.ClearPlacementPreview(host.Catalog.Ashfall,
                    host.Profile.worldState.GetOrCreateRegionState(host.Profile.worldState.currentRegionId).buildingStates[_movingBuilding.InstanceId]);
                _presenter.Refresh();
                _movingBuilding?.SetSelected(true);
                _movingBuilding = null;
                if (saved)
                {
                    // A failed commit reverted the move above; firing the confirm cue
                    // would celebrate a placement that no longer exists.
                    PlacementFeedback?.Invoke(PlacementFailure.None);
                }
            }
            else
            {
                PlacementFeedback?.Invoke(_previewFailure);
            }

            PresentationChanged?.Invoke();
        }

        public void CancelBuildingMove()
        {
            var host = GameHost.Current;
            if (host == null || !host.Placement.IsMoving)
            {
                return;
            }

            host.Placement.CancelMove();
            _presenter.Refresh();
            _movingBuilding?.SetSelected(true);
            _movingBuilding = null;
            _previewFailure = PlacementFailure.None;
            PresentationChanged?.Invoke();
        }

        public void ResetBuildingPreview()
        {
            if (GameHost.Current == null || !GameHost.Current.Placement.IsMoving || _movingBuilding == null)
            {
                return;
            }

            var state = GameHost.Current.Profile.worldState.GetOrCreateRegionState(GameHost.Current.Profile.worldState.currentRegionId)
                .buildingStates[_movingBuilding.InstanceId];
            _previewPlacement = new BuildingPlacement
            {
                gridX = state.placement.gridX,
                gridY = state.placement.gridY,
                rotationQuarterTurns = state.placement.rotationQuarterTurns,
            };
            UpdatePreview(_previewPlacement);
        }

        private void OnBuildingTapped(BuildingActor actor)
        {
            if (_movingBuilding != null && actor == null)
            {
                return;
            }

            if (_movingBuilding != null && actor != null && actor != _movingBuilding)
            {
                return;
            }

            if (_selectedBuilding != null && _selectedBuilding != actor)
            {
                _selectedBuilding.SetSelected(false);
            }

            _selectedBuilding = actor;
            _selectedBuilding?.SetSelected(true);
            PresentationChanged?.Invoke();
        }

        private void OnGroundTapped(Vector3 point)
        {
            var host = GameHost.Current;
            if (host == null || !host.Placement.IsMoving || _movingBuilding == null)
            {
                return;
            }

            _previewPlacement.gridX = Mathf.FloorToInt(point.x / BuildingActor.CellSize);
            _previewPlacement.gridY = Mathf.FloorToInt(point.z / BuildingActor.CellSize);
            UpdatePreview(_previewPlacement);
        }

        private void UpdatePreview(BuildingPlacement candidate)
        {
            var host = GameHost.Current;
            if (host == null || _movingBuilding == null)
            {
                return;
            }

            _previewFailure = host.Placement.PreviewCandidate(candidate);
            _movingBuilding.SetPlacementPreview(host.Catalog.Ashfall, candidate, _previewFailure == PlacementFailure.None);
            PresentationChanged?.Invoke();
        }

        private static string FriendlyPlacementFailure(PlacementFailure failure)
        {
            switch (failure)
            {
                case PlacementFailure.OutsidePlacementArea: return "Move inside the basin boundary";
                case PlacementFailure.ReservedArea: return "That route or landmark is reserved";
                case PlacementFailure.OverlapsBuilding: return "That footprint overlaps another building";
                case PlacementFailure.NotMovable: return "This landmark stays anchored";
                case PlacementFailure.NotRestoredYet: return "Restore this building first";
                default: return "Placement ready";
            }
        }

        private static string Humanize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            int lastDot = value.LastIndexOf('.');
            string text = lastDot >= 0 ? value.Substring(lastDot + 1) : value;
            return text.Replace('_', ' ').Replace('-', ' ');
        }

        private void OnDestroy()
        {
            // The composition root outlives this controller; a stale ModeChanged handler
            // would otherwise fire into a destroyed rig after a scene reload (M8 audit).
            GameHost.Current?.Events?.Unsubscribe<ModeChanged>(OnModeChanged);
        }

        private void BuildRuntimeRig()
        {
            if (_rigReady)
            {
                return;
            }

            var definition = WorldRegistry.CurrentRegion;
            float extent = Mathf.Max(definition.placementWidthCells, definition.placementDepthCells) * BuildingActor.CellSize + 4f;

            // Ground.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "AshfallGround";
            ground.transform.localScale = new Vector3(extent / 10f, 1f, extent / 10f); // Plane primitive is 10 units
            var groundRenderer = ground.GetComponent<Renderer>();
            if (groundRenderer != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader != null)
                {
                    groundRenderer.sharedMaterial = new Material(shader)
                    {
                        color = new Color(0.18f, 0.19f, 0.19f),
                    };
                }
            }

            // Presenter root.
            var presenterGo = new GameObject("RegionPresenter");
            var presenter = presenterGo.AddComponent<RegionPresenter>();
            presenter.SetGroundRenderer(groundRenderer);

            // Builder rig: elevated angled camera over the region.
            var builderGo = new GameObject("BuilderRig");
            var builderCamera = builderGo.AddComponent<Camera>();
            builderCamera.clearFlags = CameraClearFlags.SolidColor;
            builderCamera.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            builderCamera.tag = "MainCamera";
            var builderController = builderGo.AddComponent<BuilderCameraController>();
            builderController.SetAreaBounds(new Vector2(extent, extent));

            // Explore rig: capsule player with its own follow camera.
            var exploreGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            exploreGo.name = "ExplorePlayer";
            Object.Destroy(exploreGo.GetComponent<Collider>());
            var controller = exploreGo.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            var exploreController = exploreGo.AddComponent<ExploreCharacterController>();
            exploreController.SetBounds(new Vector2(extent - 1f, extent - 1f));
            exploreGo.SetActive(false);

            var exploreCamGo = new GameObject("ExploreCamera");
            var exploreCam = exploreCamGo.AddComponent<Camera>();
            exploreCam.clearFlags = CameraClearFlags.SolidColor;
            exploreCam.backgroundColor = new Color(0.20f, 0.24f, 0.28f);
            exploreCam.enabled = false;
            exploreCamGo.SetActive(false);

            exploreController.AttachCamera(exploreCamGo.transform);

            _presenter = presenter;
            _builderRig = builderController;
            _exploreCharacter = exploreController;
            _exploreCamera = exploreCamGo.transform;
            _rigReady = true;
        }
    }
}
