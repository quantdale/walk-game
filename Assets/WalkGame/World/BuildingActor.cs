using UnityEngine;
using WalkGame.Building;
using WalkGame.Core;

namespace WalkGame.World
{
    /// <summary>
    /// Scene-side representation of one canonical BuildingState (TECHNICAL_ARCHITECTURE 17).
    /// The actor never authors state: it projects definition + RegionState into visuals and
    /// re-applies itself whenever the canonical placement version or lifecycle changes.
    /// Gray-box visuals are primitive children swapped by lifecycle state; real art replaces
    /// them without touching this contract.
    /// </summary>
    public sealed class BuildingActor : MonoBehaviour
    {
        public const float CellSize = 1f;

        public string InstanceId { get; private set; }
        public int AppliedPlacementVersion { get; private set; } = -1;
        public BuildingLifecycleState AppliedLifecycle { get; private set; } = (BuildingLifecycleState)(-1);

        [SerializeField] private Transform ruinVisual;
        [SerializeField] private Transform restoredVisual;
        [SerializeField] private Renderer tintTarget;
        [SerializeField] private Color ruinTint = new Color(0.35f, 0.34f, 0.32f);
        [SerializeField] private Color restoredTint = new Color(0.65f, 0.78f, 0.55f);

        public void Bind(string instanceId)
        {
            InstanceId = instanceId;
            name = $"Building_{instanceId}";
        }

        /// <summary>Applies canonical state; idempotent per (placementVersion, lifecycle).</summary>
        public void ApplyState(RegionDefinition definition, BuildingState state)
        {
            if (state == null || definition == null)
            {
                return;
            }

            if (AppliedPlacementVersion != state.placement.placementVersion ||
                AppliedLifecycle != state.lifecycleState)
            {
                ApplyPlacement(definition, state);
                ApplyLifecycleVisuals(state);
                AppliedPlacementVersion = state.placement.placementVersion;
                AppliedLifecycle = state.lifecycleState;
            }
        }

        private void ApplyPlacement(RegionDefinition definition, BuildingState state)
        {
            // Grid cell (0,0) sits at the region origin corner; center the footprint on its cell rect.
            var building = WorldRegistry.CurrentCatalog != null
                ? WorldRegistry.CurrentCatalog.GetBuilding(state.definitionId)
                : null;

            int width = 1, depth = 1;
            if (building != null)
            {
                BuildingPlacementService.GetFootprintExtent(building, state.placement.rotationQuarterTurns, out width, out depth);
            }

            float x = (definition.placementOriginX + state.placement.gridX + width * 0.5f) * CellSize;
            float z = (definition.placementOriginY + state.placement.gridY + depth * 0.5f) * CellSize;
            transform.position = new Vector3(x, 0f, z);
            transform.rotation = Quaternion.Euler(0f, state.placement.rotationQuarterTurns * 90f, 0f);
        }

        private void ApplyLifecycleVisuals(BuildingState state)
        {
            bool restored = state.IsRestored;
            if (ruinVisual != null)
            {
                ruinVisual.gameObject.SetActive(!restored);
            }

            if (restoredVisual != null)
            {
                restoredVisual.gameObject.SetActive(restored);
            }

            if (tintTarget != null)
            {
                tintTarget.material.color = restored ? restoredTint : ruinTint;
            }
        }

        /// <summary>Builds default gray-box visuals when no authored prefab is assigned.</summary>
        public void EnsureGrayBoxVisuals(FootprintDefinition footprint)
        {
            if (ruinVisual == null)
            {
                var ruinGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ruinGo.name = "Ruin";
                ruinGo.transform.SetParent(transform, false);
                ruinGo.transform.localScale = new Vector3(
                    Mathf.Max(0.6f, footprint.widthCells * 0.9f),
                    0.5f,
                    Mathf.Max(0.6f, footprint.depthCells * 0.9f));
                ruinGo.transform.localPosition = new Vector3(0f, 0.25f, 0f);
                ruinVisual = ruinGo.transform;
                tintTarget = ruinGo.GetComponent<Renderer>();
            }

            if (restoredVisual == null)
            {
                var restoredGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                restoredGo.name = "Restored";
                restoredGo.transform.SetParent(transform, false);
                restoredGo.transform.localScale = new Vector3(
                    Mathf.Max(0.8f, footprint.widthCells * 0.95f),
                    1.2f,
                    Mathf.Max(0.8f, footprint.depthCells * 0.95f));
                restoredGo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                restoredVisual = restoredGo.transform;
                restoredGo.SetActive(false);
            }
        }
    }
}
