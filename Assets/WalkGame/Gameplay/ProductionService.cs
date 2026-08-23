using System;
using System.Collections.Generic;
using WalkGame.Core;

namespace WalkGame.Gameplay
{
    public struct ProductionResult
    {
        public string producerId;
        public long produced;
        public bool cappedByOfflineWindow;
        public bool clockAnomaly;
    }

        public struct CollectResult
        {
            public string resourceId;
            public long collected;
        }

        /// <summary>Read-only view of one producer store waiting to be collected.</summary>
        public struct PendingCollect
        {
            public string producerId;
            public string resourceId;
            public long stored;
        }

    /// <summary>
    /// Checkpoint-based passive production (TECHNICAL_ARCHITECTURE 16). Nothing is
    /// simulated per frame or while offline: on resume, elapsed = clamp(now - checkpoint,
    /// 0, cap) and output derives deterministically from building tiers.
    /// </summary>
    public sealed class ProductionService
    {
        private readonly IContentCatalog _catalog;
        private readonly PlayerProfile _profile;
        private readonly RewardApplier _rewards;
        private readonly IClock _clock;
        private readonly Log _log;

        public ProductionService(IContentCatalog catalog, PlayerProfile profile, RewardApplier rewards, IClock clock, Log log)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _rewards = rewards ?? throw new ArgumentNullException(nameof(rewards));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log ?? Log.Disabled;
        }

        /// <summary>Accrues pending output into producer stores without granting resources yet.</summary>
        public ProductionResult Accrue(string regionId, ProducerState producer)
        {
            var result = new ProductionResult { producerId = producer.producerId };
            var definition = _catalog.GetProducer(producer.producerId);
            if (definition == null)
            {
                return result;
            }

            var region = _profile.worldState.GetOrCreateRegionState(regionId);
            if (!region.buildingStates.TryGetValue(producer.buildingInstanceId, out var building) || !building.IsRestored)
            {
                // Ruined producers generate nothing; keep checkpoint fresh so returning to
                // service later does not dump a huge retroactive backlog.
                producer.lastCheckpointUtc = _clock.UtcNow;
                return result;
            }

            DateTime now = _clock.UtcNow;
            TimeSpan elapsed = now - producer.lastCheckpointUtc;
            if (elapsed < TimeSpan.Zero)
            {
                // Device clock moved backward: clamp to zero, never produce negative value,
                // and re-baseline (PRIVACY_SAFETY_ANTI_CHEAT 9 Threat E).
                _log.Warning($"Clock anomaly for producer '{producer.producerId}': negative elapsed {elapsed}. Clamped.");
                result.clockAnomaly = true;
                elapsed = TimeSpan.Zero;
            }

            double capHours = definition.offlineCapHours;
            double elapsedHours = elapsed.TotalHours;
            if (elapsedHours > capHours)
            {
                elapsedHours = capHours;
                result.cappedByOfflineWindow = true;
            }

            int tier = Math.Max(1, building.upgradeTier);
            double rate = definition.baseRatePerHour * definition.MultiplierForTier(tier);
            long produced = (long)Math.Floor(rate * elapsedHours);

            long stored = producer.storedOutput + produced;
            if (stored > definition.storageCap)
            {
                stored = definition.storageCap;
            }

            result.produced = stored - producer.storedOutput;
            producer.storedOutput = stored;
            producer.lastCheckpointUtc = now;
            return result;
        }

        public void AccrueAll(string regionId)
        {
            var region = _profile.worldState.GetOrCreateRegionState(regionId);
            foreach (var producer in region.producerStates.Values)
            {
                if (producer == null)
                {
                    continue;
                }

                Accrue(regionId, producer);
            }
        }

        public CollectResult Collect(string regionId, string producerId)
        {
            var region = _profile.worldState.GetOrCreateRegionState(regionId);
            var result = new CollectResult();
            if (!region.producerStates.TryGetValue(producerId, out var producer))
            {
                return result;
            }

            Accrue(regionId, producer);
            if (producer.storedOutput <= 0)
            {
                return result;
            }

            var definition = _catalog.GetProducer(producerId);
            if (definition == null)
            {
                return result;
            }

            result.resourceId = definition.resourceId;
            result.collected = producer.storedOutput;
            _rewards.GrantResource(result.resourceId, result.collected);
            producer.storedOutput = 0;
            return result;
        }

        /// <summary>
        /// Stores currently holding output, for collection UI. Pure read: accrual must
        /// happen through Accrue/AccrueAll so callers control when checkpoints move.
        /// Leftovers from buildings that later became ruins remain collectible, matching
        /// Collect semantics.
        /// </summary>
        public List<PendingCollect> GetPendingCollectables(string regionId)
        {
            var region = _profile.worldState.GetOrCreateRegionState(regionId);
            var pending = new List<PendingCollect>();
            foreach (var producer in region.producerStates.Values)
            {
                if (producer == null || producer.storedOutput <= 0)
                {
                    continue;
                }

                var definition = _catalog.GetProducer(producer.producerId);
                if (definition == null)
                {
                    continue;
                }

                pending.Add(new PendingCollect
                {
                    producerId = producer.producerId,
                    resourceId = definition.resourceId,
                    stored = producer.storedOutput,
                });
            }

            pending.Sort((a, b) =>
            {
                int byResource = string.CompareOrdinal(a.resourceId, b.resourceId);
                return byResource != 0 ? byResource : string.CompareOrdinal(a.producerId, b.producerId);
            });
            return pending;
        }

        /// <summary>Collects every non-empty store; each entry goes through Collect.</summary>
        public List<CollectResult> CollectAll(string regionId)
        {
            var region = _profile.worldState.GetOrCreateRegionState(regionId);
            var results = new List<CollectResult>();
            foreach (var producerId in new List<string>(region.producerStates.Keys))
            {
                var result = Collect(regionId, producerId);
                if (result.collected > 0)
                {
                    results.Add(result);
                }
            }

            return results;
        }

        /// <summary>Ensures every restored building with a producer definition has state.</summary>
        public void EnsureProducerStates(string regionId)
        {
            var region = _profile.worldState.GetOrCreateRegionState(regionId);
            var definition = _catalog.GetRegion(regionId);
            if (definition == null)
            {
                return;
            }

            var seen = new HashSet<string>();
            foreach (var instance in definition.defaultBuildingInstances)
            {
                if (instance == null || string.IsNullOrEmpty(instance.producerId))
                {
                    continue;
                }

                seen.Add(instance.producerId);
                if (!region.producerStates.ContainsKey(instance.producerId))
                {
                    region.producerStates[instance.producerId] = new ProducerState
                    {
                        producerId = instance.producerId,
                        buildingInstanceId = instance.instanceId,
                        lastCheckpointUtc = _clock.UtcNow,
                    };
                }
            }

            foreach (var existing in new List<string>(region.producerStates.Keys))
            {
                if (!seen.Contains(existing))
                {
                    region.producerStates.Remove(existing);
                }
            }
        }
    }
}
