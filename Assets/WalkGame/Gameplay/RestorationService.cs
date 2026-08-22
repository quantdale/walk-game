using System;
using System.Collections.Generic;
using WalkGame.Core;

namespace WalkGame.Gameplay
{
    public enum RestorationFailure
    {
        None = 0,
        UnknownProject,
        AlreadyCompleted,
        MissingPrerequisite,
        RegionStageTooLow,
        LifetimeStepsTooLow,
        InsufficientVitality,
        InsufficientResources
    }

    /// <summary>
    /// Validates and commits restoration project transactions (TECHNICAL_ARCHITECTURE 10).
    /// Validate -> deduct -> mark complete -> apply rewards -> advance stage -> persist happens
    /// through one call so a failure cannot leave half-applied state in memory.
    /// </summary>
    public sealed class RestorationService
    {
        private readonly IContentCatalog _catalog;
        private readonly PlayerProfile _profile;
        private readonly VitalityLedger _ledger;
        private readonly RewardApplier _rewards;
        private readonly DomainEvents _events;
        private readonly Log _log;

        public RestorationService(
            IContentCatalog catalog,
            PlayerProfile profile,
            VitalityLedger ledger,
            RewardApplier rewards,
            DomainEvents events,
            Log log)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _rewards = rewards ?? throw new ArgumentNullException(nameof(rewards));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _log = log ?? Log.Disabled;
        }

        public RestorationFailure Evaluate(string projectId, out RestorationProjectDefinition project, out RegionState region)
        {
            project = null;
            region = null;

            foreach (var candidate in _catalog.GetProjectsForRegion(_profile.worldState.currentRegionId))
            {
                if (candidate != null && candidate.projectId == projectId)
                {
                    project = candidate;
                    break;
                }
            }

            if (project == null)
            {
                return RestorationFailure.UnknownProject;
            }

            if (!_profile.worldState.TryGetRegionState(project.regionId, out region))
            {
                return RestorationFailure.UnknownProject;
            }

            if (region.completedProjectIds.Contains(projectId))
            {
                return RestorationFailure.AlreadyCompleted;
            }

            foreach (var prerequisite in project.prerequisiteProjectIds)
            {
                if (!region.completedProjectIds.Contains(prerequisite))
                {
                    return RestorationFailure.MissingPrerequisite;
                }
            }

            if (project.requiredRegionStage.HasValue && region.restorationStage < project.requiredRegionStage.Value)
            {
                return RestorationFailure.RegionStageTooLow;
            }

            if (project.requiredLifetimeSteps.HasValue && _profile.lifetimeAcceptedSteps < project.requiredLifetimeSteps.Value)
            {
                return RestorationFailure.LifetimeStepsTooLow;
            }

            if (_ledger.GetBalance() < project.vitalityCost)
            {
                return RestorationFailure.InsufficientVitality;
            }

            foreach (var cost in project.resourceCosts)
            {
                _profile.resources.TryGetValue(cost.Key, out var owned);
                if (owned < cost.Value)
                {
                    return RestorationFailure.InsufficientResources;
                }
            }

            return RestorationFailure.None;
        }

        public bool TryComplete(string projectId, out RestorationFailure failure)
        {
            failure = Evaluate(projectId, out var project, out var region);
            if (failure != RestorationFailure.None)
            {
                return false;
            }

            var spend = new VitalitySpend
            {
                amount = project.vitalityCost,
                reasonCode = project.IsLandmark ? WellKnownIds.ReasonCodes.ProjectLandmark : WellKnownIds.ReasonCodes.ProjectRestore,
                relatedEntityId = project.projectId,
            };

            if (!_ledger.TrySpend(spend, out _))
            {
                failure = RestorationFailure.InsufficientVitality;
                return false;
            }

            DeductResources(project);

            region.completedProjectIds.Add(project.projectId);
            _rewards.Apply(project.regionId, project.rewardActions);
            AdvanceStagesIfEligible(region);

            _events.Publish(new ProjectCompleted { RegionId = project.regionId, ProjectId = project.projectId });
            _log.Info($"Project completed: {project.projectId}");
            return true;
        }

        public bool TryAdvanceStageManually(RegionState region)
        {
            return AdvanceStagesIfEligible(region);
        }

        private void DeductResources(RestorationProjectDefinition project)
        {
            foreach (var cost in project.resourceCosts)
            {
                if (_profile.resources.TryGetValue(cost.Key, out var owned))
                {
                    _profile.resources[cost.Key] = Math.Max(0, owned - cost.Value);
                }
            }
        }

        private bool AdvanceStagesIfEligible(RegionState region)
        {
            bool advancedAny = false;
            var definition = _catalog.GetRegion(region.regionId);
            if (definition == null)
            {
                return false;
            }

            bool advancing = true;
            while (advancing)
            {
                advancing = false;
                int nextStage = region.restorationStage + 1;
                foreach (var threshold in definition.stageThresholds)
                {
                    if (threshold == null || threshold.stage != nextStage)
                    {
                        continue;
                    }

                    if (!MeetsThreshold(region, threshold))
                    {
                        continue;
                    }

                    int oldStage = region.restorationStage;
                    region.restorationStage = nextStage;
                    advancedAny = true;
                    advancing = true;
                    _events.Publish(new RegionStageChanged
                    {
                        RegionId = region.regionId,
                        OldStage = oldStage,
                        NewStage = nextStage,
                    });
                    break;
                }
            }

            return advancedAny;
        }

        private static bool MeetsThreshold(RegionState region, StageThresholdDefinition threshold)
        {
            int totalScore = region.ecologyScore + region.infrastructureScore + region.communityScore + region.knowledgeScore;
            if (totalScore < threshold.totalScoreRequired)
            {
                return false;
            }

            foreach (var requiredProject in threshold.requiredProjectIds)
            {
                if (!region.completedProjectIds.Contains(requiredProject))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
