using System;
using System.Collections.Generic;

namespace WalkGame.Core
{
    /// <summary>Reserved rectangle inside the placement area (road/water/spawn/landmark).</summary>
    [Serializable]
    public sealed class ReservedArea
    {
        public int originX;
        public int originY;
        public int widthCells = 1;
        public int depthCells = 1;

        public bool Contains(int x, int y)
        {
            return x >= originX && x < originX + widthCells && y >= originY && y < originY + depthCells;
        }
    }

    /// <summary>
    /// Static authored data describing one bounded region. DATA_MODEL.md 18.
    /// Regions are self-contained; travel between them is via map/transit only.
    /// </summary>
    public sealed class RegionDefinition
    {
        public string regionId = string.Empty;
        public string displayNameKey = string.Empty;
        public string sceneReference = string.Empty;

        public List<DefaultBuildingInstanceDefinition> defaultBuildingInstances = new List<DefaultBuildingInstanceDefinition>();
        public List<StageThresholdDefinition> stageThresholds = new List<StageThresholdDefinition>();

        /// <summary>Legal placement area expressed as an inclusive cell rectangle.</summary>
        public int placementOriginX;
        public int placementOriginY;
        public int placementWidthCells = 32;
        public int placementDepthCells = 32;
        public List<ReservedArea> reservedAreas = new List<ReservedArea>();

        public string exploreSpawnId = "spawn.explore.default";
        public int exploreSpawnGridX;
        public int exploreSpawnGridY;

        /// <summary>Authored presentation/content hooks for the contained region.</summary>
        public List<NpcDefinition> npcs = new List<NpcDefinition>();
        public List<LoreDefinition> loreObjects = new List<LoreDefinition>();

        /// <summary>Number of authored region visual stages (dead -> flourishing).</summary>
        public int visualStageCount = 4;

        public bool IsInsidePlacementArea(int x, int y)
        {
            return x >= placementOriginX && x < placementOriginX + placementWidthCells &&
                   y >= placementOriginY && y < placementOriginY + placementDepthCells;
        }

        public bool IsReserved(int x, int y)
        {
            foreach (var area in reservedAreas)
            {
                if (area != null && area.Contains(x, y))
                {
                    return true;
                }
            }

            return false;
        }

        public DefaultBuildingInstanceDefinition FindDefaultInstance(string instanceId)
        {
            foreach (var instance in defaultBuildingInstances)
            {
                if (instance != null && instance.instanceId == instanceId)
                {
                    return instance;
                }
            }

            return null;
        }
    }
}
