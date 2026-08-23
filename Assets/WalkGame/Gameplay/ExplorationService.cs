using System;
using WalkGame.Core;

namespace WalkGame.Gameplay
{
    /// <summary>Owns lightweight Explore discoveries so scene actors never become save authority.</summary>
    public sealed class ExplorationService
    {
        private readonly IContentCatalog _catalog;
        private readonly PlayerProfile _profile;
        private readonly DomainEvents _events;

        public ExplorationService(IContentCatalog catalog, PlayerProfile profile, DomainEvents events)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public bool TryDiscoverLore(string regionId, string loreId)
        {
            var region = _profile.worldState.GetOrCreateRegionState(regionId);
            var definition = _catalog.GetRegion(regionId);
            if (definition == null)
            {
                return false;
            }

            foreach (var lore in definition.loreObjects)
            {
                if (lore == null || lore.loreId != loreId)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(lore.prerequisiteProjectId) &&
                    !region.completedProjectIds.Contains(lore.prerequisiteProjectId))
                {
                    return false;
                }

                if (!region.discoveredLoreIds.Add(loreId))
                {
                    return false;
                }

                _events.Publish(new LoreDiscovered { RegionId = regionId, LoreId = loreId });
                return true;
            }

            return false;
        }
    }
}
