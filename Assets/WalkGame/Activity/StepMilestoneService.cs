using System;
using WalkGame.Core;

namespace WalkGame.Activity
{
    /// <summary>
    /// Permanent lifetime-step milestones (GAME_DESIGN 13). Each milestone fires once,
    /// is celebratory, and never resets - rest days cannot destroy progress.
    /// </summary>
    public sealed class StepMilestoneService
    {
        private readonly IContentCatalog _catalog;
        private readonly PlayerProfile _profile;
        private readonly VitalityLedger _ledger;
        private readonly DomainEvents _events;

        public StepMilestoneService(IContentCatalog catalog, PlayerProfile profile, VitalityLedger ledger, DomainEvents events)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        public void CheckAndAward()
        {
            foreach (var milestone in _catalog.GetMilestones())
            {
                if (milestone == null || string.IsNullOrEmpty(milestone.milestoneId))
                {
                    continue;
                }

                if (_profile.lifetimeAcceptedSteps < milestone.lifetimeStepsRequired)
                {
                    continue;
                }

                if (!_profile.achievementState.reachedMilestoneIds.Add(milestone.milestoneId))
                {
                    continue;
                }

                long awarded = 0;
                if (milestone.vitalityReward > 0)
                {
                    _ledger.Credit(new VitalityCredit
                    {
                        amount = milestone.vitalityReward,
                        reasonCode = WellKnownIds.ReasonCodes.StepMilestone,
                        relatedEntityId = milestone.milestoneId,
                    });
                    awarded = milestone.vitalityReward;
                }

                _events.Publish(new ActivityMilestoneReached
                {
                    LifetimeSteps = milestone.lifetimeStepsRequired,
                    VitalityAwarded = awarded,
                });
            }
        }
    }
}
