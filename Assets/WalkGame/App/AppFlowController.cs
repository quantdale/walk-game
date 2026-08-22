using UnityEngine;
using WalkGame.Core;
using WalkGame.World;

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
        private bool _rigReady;

        public RegionPresenter Presenter => _presenter;

        private void Start()
        {
            var host = GameHost.Current;
            if (host == null)
            {
                Debug.LogError("AppFlowController requires GameHost; add GameHost to the bootstrap scene.");
                enabled = false;
                return;
            }

            host.Events.Subscribe<ModeChanged>(OnModeChanged);
            BuildRuntimeRig();

            var region = host.Profile.worldState.GetOrCreateRegionState(host.Profile.worldState.currentRegionId);
            _presenter.Present(WorldRegistry.CurrentRegion, region);

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
                var definition = WorldRegistry.CurrentRegion;
                _exploreCharacter.TeleportTo(_presenter.GridToWorld(definition.exploreSpawnGridX, definition.exploreSpawnGridY));
            }
            else
            {
                // Re-project canonical state after any exploration-time discoveries.
                _presenter.Refresh();
            }
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

            // Presenter root.
            var presenterGo = new GameObject("RegionPresenter");
            var presenter = presenterGo.AddComponent<RegionPresenter>();
            presenter.SetGroundRenderer(ground.GetComponent<Renderer>());

            // Builder rig: elevated angled camera over the region.
            var builderGo = new GameObject("BuilderRig");
            var builderCamera = builderGo.AddComponent<Camera>();
            builderCamera.clearFlags = CameraClearFlags.SolidColor;
            builderCamera.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            builderCamera.tag = "MainCamera";
            var builderController = builderGo.AddComponent<BuilderCameraController>();

            // Explore rig: capsule player with its own follow camera.
            var exploreGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            exploreGo.name = "ExplorePlayer";
            Object.Destroy(exploreGo.GetComponent<Collider>());
            var controller = exploreGo.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            var exploreController = exploreGo.AddComponent<ExploreCharacterController>();
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
