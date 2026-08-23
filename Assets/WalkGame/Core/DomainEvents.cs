using System;
using System.Collections.Generic;

namespace WalkGame.Core
{
    /// <summary>
    /// Well-known persistent identifiers. Persistent IDs are API: never rename
    /// shipped IDs without a save migration (see AGENT_EXECUTION_GUIDE section 9).
    /// Content-specific IDs live with content; these cover system-level concepts.
    /// </summary>
    public static class WellKnownIds
    {
        public const string StartingRegionId = "region.ashfall";

        public static class Resources
        {
            public const string Water = "resource.water";
            public const string Biomass = "resource.biomass";
            public const string Salvage = "resource.salvage";
            public const string Components = "resource.components";
            public const string Knowledge = "resource.knowledge";
        }

        public static class ReasonCodes
        {
            public const string Steps = "activity.steps";
            public const string ExplorerBonus = "activity.explorer_bonus";
            public const string EnduranceBonus = "activity.endurance_bonus";
            public const string RhythmBonus = "activity.rhythm_bonus";
            public const string TempoBonus = "activity.tempo_bonus";
            public const string GrowthBonus = "activity.growth_bonus";
            public const string SessionBonusCapApplied = "activity.session_bonus_cap";
            public const string StepMilestone = "milestone.lifetime_steps";
            public const string ProjectRestore = "project.restore";
            public const string ProjectLandmark = "project.landmark";
            public const string DebugGrant = "debug.grant";
            public const string ProductionCollect = "production.collect";
        }

        public static class EnvironmentFlags
        {
            public const string RiverFlowing = "env.ashfall.river_flowing";
            public const string WetlandAlive = "env.ashfall.wetland_alive";
            public const string GroveRevived = "env.ashfall.grove_revived";
        }
    }

    /// <summary>
    /// Lightweight typed domain event hub. Systems publish facts; UI/audio/presentation
    /// subscribe. Deliberately not a heavyweight framework (TECHNICAL_ARCHITECTURE 11).
    /// </summary>
    public sealed class DomainEvents
    {
        private readonly Dictionary<Type, List<Delegate>> _subscribers = new Dictionary<Type, List<Delegate>>();

        public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : struct
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (!_subscribers.TryGetValue(typeof(TEvent), out var list))
            {
                list = new List<Delegate>();
                _subscribers[typeof(TEvent)] = list;
            }

            if (!list.Contains(handler))
            {
                list.Add(handler);
            }
        }

        public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : struct
        {
            if (_subscribers.TryGetValue(typeof(TEvent), out var list))
            {
                list.Remove(handler);
            }
        }

        public void Publish<TEvent>(TEvent evt) where TEvent : struct
        {
            if (!_subscribers.TryGetValue(typeof(TEvent), out var list))
            {
                return;
            }

            // Copy so handlers may unsubscribe while iterating.
            for (int i = 0; i < list.Count; i++)
            {
                ((Action<TEvent>)list[i]).Invoke(evt);
            }
        }
    }

    public struct VitalityCredited
    {
        public long Amount;
        public long ResultingBalance;
        public string ReasonCode;
    }

    public struct VitalitySpent
    {
        public long Amount;
        public long ResultingBalance;
        public string ReasonCode;
        public string RelatedEntityId;
    }

    public struct ProjectCompleted
    {
        public string RegionId;
        public string ProjectId;
    }

    public struct BuildingRestored
    {
        public string RegionId;
        public string BuildingInstanceId;
    }

    public struct BuildingMoved
    {
        public string RegionId;
        public string BuildingInstanceId;
    }

    public struct RegionStageChanged
    {
        public string RegionId;
        public int NewStage;
        public int OldStage;
    }

    public struct RegionUnlocked
    {
        public string RegionId;
    }

    public struct ActivityMilestoneReached
    {
        public long LifetimeSteps;
        public long VitalityAwarded;
    }

    public struct LoreDiscovered
    {
        public string RegionId;
        public string LoreId;
    }

    public struct EnvironmentFlagChanged
    {
        public string RegionId;
        public string FlagId;
    }

    public struct ProducerActivated
    {
        public string RegionId;
        public string ProducerId;
    }

    public struct OnboardingStepChanged
    {
        public int Step;
    }
}
