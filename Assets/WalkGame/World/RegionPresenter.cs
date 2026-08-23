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
        private readonly Dictionary<string, NpcActor> _npcs = new Dictionary<string, NpcActor>();
        private readonly Dictionary<string, LoreActor> _lore = new Dictionary<string, LoreActor>();
        private RegionEnvironmentPresenter _environment;
        private MaterialPropertyBlock _groundPropertyBlock;
        private Transform _worldActorsRoot;

        public RegionDefinition Region { get; private set; }
        public RegionState State { get; private set; }
        public IReadOnlyDictionary<string, BuildingActor> Actors => _actors;
        public IReadOnlyDictionary<string, NpcActor> Npcs => _npcs;
        public IReadOnlyDictionary<string, LoreActor> LoreObjects => _lore;
        public RegionEnvironmentPresenter Environment => _environment;

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

            if (_environment == null)
            {
                var environmentGo = new GameObject("Environment");
                environmentGo.transform.SetParent(transform, false);
                _environment = environmentGo.AddComponent<RegionEnvironmentPresenter>();
            }

            _environment.Present(definition, state);

            foreach (var instance in definition.defaultBuildingInstances)
            {
                if (!state.buildingStates.TryGetValue(instance.instanceId, out var building))
                {
                    continue;
                }

                var actor = GetOrCreateActor(instance, building);
                actor.ApplyState(definition, building);
            }

            PresentWorldActors(definition, state);

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

            PresentWorldActors(Region, State);

            ApplyStageVisuals(State.restorationStage);
            _environment?.Refresh();
        }

        private void PresentWorldActors(RegionDefinition definition, RegionState state)
        {
            if (_worldActorsRoot == null)
            {
                _worldActorsRoot = new GameObject("WorldActors").transform;
                _worldActorsRoot.SetParent(transform, false);
            }

            foreach (var npcDefinition in definition.npcs)
            {
                if (npcDefinition == null || string.IsNullOrEmpty(npcDefinition.npcId))
                {
                    continue;
                }

                if (!_npcs.TryGetValue(npcDefinition.npcId, out var npc))
                {
                    var npcGo = new GameObject("NPC_" + npcDefinition.npcId);
                    npcGo.transform.SetParent(_worldActorsRoot, false);
                    npc = npcGo.AddComponent<NpcActor>();
                    npc.Bind(npcDefinition);
                    _npcs[npcDefinition.npcId] = npc;
                }

                npc.transform.localPosition = AnchorToWorld(npcDefinition.spawnAnchorId);
                npc.SetPresent(state.arrivedNpcIds.Contains(npcDefinition.npcId));
            }

            foreach (var loreDefinition in definition.loreObjects)
            {
                if (loreDefinition == null || string.IsNullOrEmpty(loreDefinition.loreId))
                {
                    continue;
                }

                if (!_lore.TryGetValue(loreDefinition.loreId, out var lore))
                {
                    var loreGo = new GameObject("Lore_" + loreDefinition.loreId);
                    loreGo.transform.SetParent(_worldActorsRoot, false);
                    lore = loreGo.AddComponent<LoreActor>();
                    lore.Bind(loreDefinition);
                    _lore[loreDefinition.loreId] = lore;
                }

                bool unlocked = string.IsNullOrEmpty(loreDefinition.prerequisiteProjectId) ||
                                state.completedProjectIds.Contains(loreDefinition.prerequisiteProjectId);
                lore.transform.localPosition = AnchorToWorld(loreDefinition.anchorId);
                lore.gameObject.SetActive(unlocked);
                lore.SetDiscovered(state.discoveredLoreIds.Contains(loreDefinition.loreId));
            }
        }

        private static Vector3 AnchorToWorld(string anchorId)
        {
            switch (anchorId)
            {
                case "anchor.wetland": return new Vector3(24.5f, 0f, 8.2f);
                case "anchor.transit_gate": return new Vector3(20f, 0f, 27f);
                case "anchor.settlement": return new Vector3(7.5f, 0f, 6f);
                case "anchor.aqueduct": return new Vector3(11.2f, 0f, 10f);
                case "anchor.water_station": return new Vector3(8f, 0f, 10f);
                case "anchor.riverside": return new Vector3(12.4f, 0f, 22f);
                case "anchor.greenhouse": return new Vector3(21f, 0f, 22.5f);
                default: return new Vector3(16f, 0f, 16f);
            }
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
            if (_groundPropertyBlock == null)
            {
                _groundPropertyBlock = new MaterialPropertyBlock();
            }

            groundRenderer.GetPropertyBlock(_groundPropertyBlock);
            _groundPropertyBlock.SetColor("_BaseColor", stageGroundTints[index]);
            _groundPropertyBlock.SetColor("_Color", stageGroundTints[index]);
            groundRenderer.SetPropertyBlock(_groundPropertyBlock);
        }

        /// <summary>Local world position of a grid cell (used for explore spawn).</summary>
        public Vector3 GridToWorld(int gridX, int gridY)
        {
            return new Vector3(gridX * BuildingActor.CellSize, 0f, gridY * BuildingActor.CellSize);
        }
    }
}
