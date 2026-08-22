using System;
using WalkGame.Core;

namespace WalkGame.Building
{
    public enum PlacementFailure
    {
        None = 0,
        UnknownBuilding,
        NotMovable,
        OutsidePlacementArea,
        ReservedArea,
        OverlapsBuilding,
        NotRestoredYet
    }

    /// <summary>
    /// Validates and commits building placement mutations (AGENT_EXECUTION_GUIDE 13).
    /// Presentation never writes transforms directly into canonical state; a move is
    /// previewed, validated against footprints/mask, then committed transactionally.
    /// </summary>
    public sealed class BuildingPlacementService
    {
        private readonly IContentCatalog _catalog;
        private readonly DomainEvents _events;

        private RegionState _region;
        private RegionDefinition _definition;
        private string _movingInstanceId;
        private int _originalGridX;
        private int _originalGridY;
        private int _originalRotationQuarterTurns;

        public bool IsMoving => _movingInstanceId != null;

        public BuildingPlacementService(IContentCatalog catalog, DomainEvents events)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        /// <summary>Validates an arbitrary candidate without mutating state.</summary>
        public static PlacementFailure Validate(
            RegionDefinition definition,
            RegionState region,
            IContentCatalog catalog,
            string movingInstanceId,
            BuildingPlacement candidate)
        {
            if (definition == null || region == null || catalog == null || candidate == null)
            {
                return PlacementFailure.OutsidePlacementArea;
            }

            if (!region.buildingStates.TryGetValue(movingInstanceId, out var moving))
            {
                return PlacementFailure.UnknownBuilding;
            }

            var buildingDefinition = catalog.GetBuilding(moving.definitionId);
            if (buildingDefinition == null)
            {
                return PlacementFailure.UnknownBuilding;
            }

            if (IsFixed(definition, movingInstanceId) || !buildingDefinition.movableAfterRestore)
            {
                return PlacementFailure.NotMovable;
            }

            if (!moving.IsRestored)
            {
                return PlacementFailure.NotRestoredYet;
            }

            GetFootprintExtent(buildingDefinition, candidate.rotationQuarterTurns, out int width, out int depth);

            for (int dx = 0; dx < width; dx++)
            {
                for (int dy = 0; dy < depth; dy++)
                {
                    int x = candidate.gridX + dx;
                    int y = candidate.gridY + dy;

                    if (!definition.IsInsidePlacementArea(x, y))
                    {
                        return PlacementFailure.OutsidePlacementArea;
                    }

                    if (definition.IsReserved(x, y))
                    {
                        return PlacementFailure.ReservedArea;
                    }
                }
            }

            foreach (var other in region.buildingStates.Values)
            {
                if (other == null || other.instanceId == movingInstanceId)
                {
                    continue;
                }

                if (Overlaps(catalog, definition, other, candidate.gridX, candidate.gridY, width, depth))
                {
                    return PlacementFailure.OverlapsBuilding;
                }
            }

            return PlacementFailure.None;
        }

        public PlacementFailure BeginMove(RegionDefinition definition, RegionState region, string instanceId)
        {
            if (IsMoving)
            {
                CancelMove();
            }

            var failure = Validate(definition, region, _catalog, instanceId,
                region.buildingStates.TryGetValue(instanceId, out var state) ? state.placement : null);
            if (failure == PlacementFailure.UnknownBuilding)
            {
                return failure;
            }

            // BeginMove is allowed even while invalid at the *current* spot only when the
            // building itself is movable; validation above covers movability via Validate.
            if (failure == PlacementFailure.NotMovable || failure == PlacementFailure.NotRestoredYet)
            {
                return failure;
            }

            _region = region;
            _definition = definition;
            _movingInstanceId = instanceId;
            _originalGridX = state.placement.gridX;
            _originalGridY = state.placement.gridY;
            _originalRotationQuarterTurns = state.placement.rotationQuarterTurns;
            return PlacementFailure.None;
        }

        public PlacementFailure PreviewCandidate(BuildingPlacement candidate)
        {
            if (!IsMoving)
            {
                return PlacementFailure.UnknownBuilding;
            }

            return Validate(_definition, _region, _catalog, _movingInstanceId, candidate);
        }

        public bool ConfirmMove(BuildingPlacement candidate, out PlacementFailure failure)
        {
            failure = PreviewCandidate(candidate);
            if (failure != PlacementFailure.None || !IsMoving)
            {
                return false;
            }

            var state = _region.buildingStates[_movingInstanceId];
            state.placement.gridX = candidate.gridX;
            state.placement.gridY = candidate.gridY;
            state.placement.rotationQuarterTurns = ((candidate.rotationQuarterTurns % 4) + 4) % 4;
            state.placement.placementVersion++;

            // Capture event payload before clearing the in-flight transaction.
            var regionId = _region.regionId;
            var instanceId = _movingInstanceId;
            ClearTransaction();
            _events.Publish(new BuildingMoved { RegionId = regionId, BuildingInstanceId = instanceId });
            return true;
        }

        public void CancelMove()
        {
            if (!IsMoving)
            {
                return;
            }

            var state = _region.buildingStates[_movingInstanceId];
            state.placement.gridX = _originalGridX;
            state.placement.gridY = _originalGridY;
            state.placement.rotationQuarterTurns = _originalRotationQuarterTurns;
            ClearTransaction();
        }

        private void ClearTransaction()
        {
            _region = null;
            _definition = null;
            _movingInstanceId = null;
        }

        private static bool IsFixed(RegionDefinition definition, string instanceId)
        {
            var defaultInstance = definition.FindDefaultInstance(instanceId);
            return defaultInstance != null && defaultInstance.fixedPlacement;
        }

        /// <summary>Footprint extents after rotation (odd quarter turns swap width/depth).</summary>
        public static void GetFootprintExtent(BuildingDefinition definition, int rotationQuarterTurns, out int width, out int depth)
        {
            bool rotated = ((rotationQuarterTurns % 2) + 2) % 2 == 1;
            width = rotated ? definition.footprint.depthCells : definition.footprint.widthCells;
            depth = rotated ? definition.footprint.widthCells : definition.footprint.depthCells;
        }

        private static bool Overlaps(
            IContentCatalog catalog,
            RegionDefinition definition,
            BuildingState other,
            int candidateX,
            int candidateY,
            int candidateWidth,
            int candidateDepth)
        {
            var otherDefinition = catalog.GetBuilding(other.definitionId);
            if (otherDefinition == null)
            {
                return false;
            }

            GetFootprintExtent(otherDefinition, other.placement.rotationQuarterTurns, out int otherWidth, out int otherDepth);

            int ox = other.placement.gridX;
            int oy = other.placement.gridY;

            return candidateX < ox + otherWidth &&
                   candidateX + candidateWidth > ox &&
                   candidateY < oy + otherDepth &&
                   candidateY + candidateDepth > oy;
        }
    }
}
