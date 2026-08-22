using System.Collections.Generic;
using UnityEngine;
using WalkGame.Core;

namespace WalkGame.World
{
    /// <summary>
    /// Projects one canonical RegionState into a loaded scene (TECHNICAL_ARCHITECTURE 18).
    /// Builder and Explore modes share this presenter; neither view authors state, so a
    /// building moved in Builder View appears at exactly that position in Explore View.
    /// </summary>
    public sealed class RegionPresenter : MonoBehaviour
    {
        [SerializeField] private Transform buildingsRoot;
        [SerializeField] private Renderer groundRenderer;
        [SerializeField] private Color[] stageGroundTints =
        {
            new Color(0.28f, 0.27f, 0.26f), // 0 dead
            new Color(0.36f, 0.38f, 0.30f), // 1 first growth
            new Color(0.42f, 0.50f, 0.32f), // 2 recovering
            new Color(0.48f, 0.60f, 0.35f), // 3 rewilded
        };

        private readonly Dictionary<string, BuildingActor> _actors = new Dictionary<string, BuildingActor>();

        public RegionDefinition Region { get; private set; }
        public RegionState State { get; private set; }
        public IReadOnlyDictionary<string, BuildingActor> Actors => _actors;

        /// <summary>Wire the ground renderer when the rig is built programmatically.</summary>
        public void SetGroundRenderer(Renderer ground)
        {
            groundRenderer = ground;
        }

        public void Present(RegionDefinition definition, RegionState state)
        {
            Region = definition;
            State = state;

            if (buildingsRoot == null)
            {
                buildingsRoot = new GameObject("Buildings").transform;
                buildingsRoot.SetParent(transform, false);
            }

            foreach (var instance in definition.defaultBuildingInstances)
            {
                if (!state.buildingStates.TryGetValue(instance.instanceId, out var building))
                {
                    continue;
                }

                var actor = GetOrCreateActor(instance, building);
                actor.ApplyState(definition, building);
            }

            ApplyStageVisuals(state.restorationStage);
        }

        /// <summary>Re-projects every building (call after domain transactions or mode switches).</summary>
        public void Refresh()
        {
            if (Region == null || State == null)
            {
                return;
            }

            foreach (var pair in _actors)
            {
                if (State.buildingStates.TryGetValue(pair.Key, out var building))
                {
                    pair.Value.ApplyState(Region, building);
                }
            }

            ApplyStageVisuals(State.restorationStage);
        }

        private BuildingActor GetOrCreateActor(DefaultBuildingInstanceDefinition instance, BuildingState building)
        {
            if (_actors.TryGetValue(instance.instanceId, out var existing))
            {
                return existing;
            }

            var go = new GameObject($"Building_{instance.instanceId}");
            go.transform.SetParent(buildingsRoot, false);
            var actor = go.AddComponent<BuildingActor>();
            actor.Bind(instance.instanceId);

            var definition = WorldRegistry.CurrentCatalog != null
                ? WorldRegistry.CurrentCatalog.GetBuilding(building.definitionId)
                : null;
            actor.EnsureGrayBoxVisuals(definition != null ? definition.footprint : new FootprintDefinition());

            _actors[instance.instanceId] = actor;
            return actor;
        }

        private void ApplyStageVisuals(int stage)
        {
            if (groundRenderer == null || stageGroundTints == null || stageGroundTints.Length == 0)
            {
                return;
            }

            int index = Mathf.Clamp(stage, 0, stageGroundTints.Length - 1);
            groundRenderer.material.color = stageGroundTints[index];
        }

        /// <summary>Local world position of a grid cell (used for explore spawn).</summary>
        public Vector3 GridToWorld(int gridX, int gridY)
        {
            return new Vector3(gridX * BuildingActor.CellSize, 0f, gridY * BuildingActor.CellSize);
        }
    }
}
