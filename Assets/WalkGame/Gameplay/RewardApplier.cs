using System;
using WalkGame.Core;

namespace WalkGame.Gameplay
{
    /// <summary>
    /// Applies data-driven reward actions to canonical state.
    /// Presentation listens to the published domain events; this class never touches scene objects.
    /// </summary>
    public sealed class RewardApplier
    {
        private readonly PlayerProfile _profile;
        private readonly IClock _clock;
        private readonly DomainEvents _events;
        private readonly Log _log;

        public RewardApplier(PlayerProfile profile, IClock clock, DomainEvents events, Log log)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _log = log ?? Log.Disabled;
        }

        public void Apply(string regionId, System.Collections.Generic.IReadOnlyList<RewardActionDefinition> actions)
        {
            if (actions == null)
            {
                return;
            }

            var region = _profile.worldState.GetOrCreateRegionState(regionId);
            foreach (var action in actions)
            {
                if (action == null)
                {
                    continue;
                }

                ApplyOne(region, action);
            }
        }

        public void ApplyOne(RegionState region, RewardActionDefinition action)
        {
            switch (action.kind)
            {
                case RewardActionKind.SetBuildingRestored:
                    if (region.buildingStates.TryGetValue(action.targetId, out var restored))
                    {
                        restored.lifecycleState = BuildingLifecycleState.Restored;
                        restored.restorationCompletedAtUtc = _clock.UtcNow;
                        _events.Publish(new BuildingRestored { RegionId = region.regionId, BuildingInstanceId = action.targetId });
                    }
                    else
                    {
                        _log.Warning($"SetBuildingRestored skipped: unknown instance '{action.targetId}' in '{region.regionId}'.");
                    }
                    break;

                case RewardActionKind.UnlockProject:
                    if (region.unlockedProjectIds.Add(action.targetId))
                    {
                        _log.Info($"Project unlocked: {action.targetId}");
                    }
                    break;

                case RewardActionKind.AddRegionScore:
                    AddScore(region, action.targetId, action.amount);
                    break;

                case RewardActionKind.UnlockNpc:
                    region.arrivedNpcIds.Add(action.targetId);
                    break;

                case RewardActionKind.SetEnvironmentFlag:
                    region.discoveredLoreIds.Add("flag:" + action.targetId);
                    break;

                case RewardActionKind.UnlockRegion:
                    if (_profile.worldState.unlockedRegionIds.Add(action.targetId))
                    {
                        _events.Publish(new RegionUnlocked { RegionId = action.targetId });
                    }
                    break;

                case RewardActionKind.GrantResource:
                    GrantResource(action.secondaryId, action.amount);
                    break;

                case RewardActionKind.DiscoverLore:
                    if (region.discoveredLoreIds.Add(action.targetId))
                    {
                        _events.Publish(new LoreDiscovered { RegionId = region.regionId, LoreId = action.targetId });
                    }
                    break;

                case RewardActionKind.UnlockBuilding:
                    // Ruins are visible from first visit; unlocking ensures a state entry exists.
                    if (!region.buildingStates.ContainsKey(action.targetId))
                    {
                        _log.Warning($"UnlockBuilding skipped: unknown instance '{action.targetId}' in '{region.regionId}'.");
                    }
                    break;

                default:
                    _log.Warning($"Unknown reward action kind {action.kind}.");
                    break;
            }
        }

        private void AddScore(RegionState region, string scoreType, long amount)
        {
            int delta = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, amount));
            switch (scoreType)
            {
                case "ecology": region.ecologyScore += delta; break;
                case "infrastructure": region.infrastructureScore += delta; break;
                case "community": region.communityScore += delta; break;
                case "knowledge": region.knowledgeScore += delta; break;
                default: _log.Warning($"Unknown score type '{scoreType}'."); break;
            }
        }

        public void GrantResource(string resourceId, long amount)
        {
            if (string.IsNullOrEmpty(resourceId) || amount == 0)
            {
                return;
            }

            _profile.resources.TryGetValue(resourceId, out var current);
            current += amount;
            if (current < 0)
            {
                current = 0;
            }

            _profile.resources[resourceId] = current;
        }
    }
}
