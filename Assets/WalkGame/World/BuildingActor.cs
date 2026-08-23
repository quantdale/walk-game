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
        public string DefinitionId { get; private set; }
        public int AppliedPlacementVersion { get; private set; } = -1;
        public BuildingLifecycleState AppliedLifecycle { get; private set; } = (BuildingLifecycleState)(-1);
        public int AppliedUpgradeTier { get; private set; } = -1;
        public BuildingLifecycleState Lifecycle => AppliedLifecycle;
        public bool IsSelected { get; private set; }
        public bool IsPlacementPreview { get; private set; }

        [SerializeField] private Transform ruinVisual;
        [SerializeField] private Transform restoredVisual;
        [SerializeField] private Renderer tintTarget;
        [SerializeField] private Color ruinTint = new Color(0.35f, 0.34f, 0.32f);
        [SerializeField] private Color restoredTint = new Color(0.65f, 0.78f, 0.55f);

        private Transform _upgradeVisual;
        private Transform _selectionRing;
        private MaterialPropertyBlock _propertyBlock;
        private RegionDefinition _lastDefinition;

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

            DefinitionId = state.definitionId;
            _lastDefinition = definition;
            if (AppliedPlacementVersion != state.placement.placementVersion ||
                AppliedLifecycle != state.lifecycleState ||
                AppliedUpgradeTier != state.upgradeTier ||
                IsPlacementPreview)
            {
                ApplyPlacement(definition, state);
                ApplyLifecycleVisuals(state);
                AppliedPlacementVersion = state.placement.placementVersion;
                AppliedLifecycle = state.lifecycleState;
                AppliedUpgradeTier = state.upgradeTier;
                IsPlacementPreview = false;
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

            ApplyPlacement(definition, state.placement, width, depth);
        }

        private void ApplyPlacement(RegionDefinition definition, BuildingPlacement placement, int width, int depth)
        {
            float x = (definition.placementOriginX + placement.gridX + width * 0.5f) * CellSize;
            float z = (definition.placementOriginY + placement.gridY + depth * 0.5f) * CellSize;
            transform.position = new Vector3(x, 0f, z);
            transform.rotation = Quaternion.Euler(0f, placement.rotationQuarterTurns * 90f, 0f);
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

            ApplyTint(restored ? restoredTint : ruinTint);

            if (_upgradeVisual != null)
            {
                _upgradeVisual.gameObject.SetActive(restored && state.upgradeTier >= 2);
            }

            SetSelected(IsSelected);
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

            if (_upgradeVisual == null)
            {
                var upgradeGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                upgradeGo.name = "UpgradeAccent";
                upgradeGo.transform.SetParent(restoredVisual, false);
                upgradeGo.transform.localScale = new Vector3(0.22f, 0.38f, 0.22f);
                upgradeGo.transform.localPosition = new Vector3(0f, 0.7f, 0f);
                var renderer = upgradeGo.GetComponent<Renderer>();
                renderer.sharedMaterial = CreateLocalMaterial(new Color(0.95f, 0.65f, 0.22f));
                RemoveCollider(upgradeGo);
                _upgradeVisual = upgradeGo.transform;
                _upgradeVisual.gameObject.SetActive(false);
            }

            if (_selectionRing == null)
            {
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.name = "SelectionRing";
                ring.transform.SetParent(transform, false);
                ring.transform.localPosition = new Vector3(0f, 0.035f, 0f);
                ring.transform.localScale = new Vector3(
                    Mathf.Max(1.2f, footprint.widthCells * 1.05f),
                    0.02f,
                    Mathf.Max(1.2f, footprint.depthCells * 1.05f));
                var renderer = ring.GetComponent<Renderer>();
                renderer.sharedMaterial = CreateLocalMaterial(new Color(0.96f, 0.78f, 0.25f, 0.8f));
                RemoveCollider(ring);
                _selectionRing = ring.transform;
                _selectionRing.gameObject.SetActive(false);
            }
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (_selectionRing == null)
            {
                return;
            }

            _selectionRing.gameObject.SetActive(selected || IsPlacementPreview);
            ApplyRingColor(IsPlacementPreview ? restoredTint : new Color(0.96f, 0.78f, 0.25f, 0.8f));
        }

        /// <summary>Moves only the scene projection while a placement transaction is open.</summary>
        public void SetPlacementPreview(RegionDefinition definition, BuildingPlacement candidate, bool valid)
        {
            if (definition == null || candidate == null || WorldRegistry.CurrentCatalog == null)
            {
                return;
            }

            var building = WorldRegistry.CurrentCatalog.GetBuilding(DefinitionId);
            if (building == null)
            {
                return;
            }

            BuildingPlacementService.GetFootprintExtent(building, candidate.rotationQuarterTurns, out int width, out int depth);
            ApplyPlacement(definition, candidate, width, depth);
            IsPlacementPreview = true;
            if (_selectionRing != null)
            {
                _selectionRing.gameObject.SetActive(true);
            }

            ApplyRingColor(valid ? new Color(0.35f, 0.9f, 0.5f, 0.85f) : new Color(0.95f, 0.28f, 0.25f, 0.9f));
        }

        public void ClearPlacementPreview(RegionDefinition definition, BuildingState state)
        {
            IsPlacementPreview = false;
            if (definition != null && state != null)
            {
                ApplyState(definition, state);
            }
            else if (_selectionRing != null)
            {
                _selectionRing.gameObject.SetActive(IsSelected);
            }
        }

        private void ApplyTint(Color color)
        {
            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            ApplyTintToRenderer(tintTarget, color);
            if (restoredVisual != null)
            {
                ApplyTintToRenderer(restoredVisual.GetComponent<Renderer>(), color);
            }
        }

        private void ApplyRingColor(Color color)
        {
            if (_selectionRing == null)
            {
                return;
            }

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            ApplyTintToRenderer(_selectionRing.GetComponent<Renderer>(), color);
        }

        private void ApplyTintToRenderer(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor("_BaseColor", color);
            _propertyBlock.SetColor("_Color", color);
            renderer.SetPropertyBlock(_propertyBlock);
        }

        private static Material CreateLocalMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            return shader == null ? null : new Material(shader) { color = color };
        }

        private static void RemoveCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }
        }
    }
}
