using System;
using NUnit.Framework;
using WalkGame.Core;
using WalkGame.Gameplay;

namespace WalkGame.Tests
{
    public sealed class M88VitalityAndRewardIntegrityTests
    {
        private MutableClock _clock;
        private PlayerProfile _profile;

        [SetUp]
        public void SetUp()
        {
            _clock = new MutableClock(new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc));
            _profile = new PlayerProfile();
        }

        // --- M1: Vitality spend reason code invariant ---

        [Test]
        public void TrySpend_EmptyReason_Throws_AndDoesNotMutate()
        {
            _profile.vitalityBalance = 100;
            var ledger = new VitalityLedger(_profile, _clock, new DomainEvents(), Log.Disabled);
            int txCountBefore = _profile.recentVitalityTransactions.Count;

            Assert.Throws<ArgumentException>(() =>
                ledger.TrySpend(new VitalitySpend { amount = 10, reasonCode = "" }, out _));

            Assert.AreEqual(100, _profile.vitalityBalance, "balance must not change on invalid reason");
            Assert.AreEqual(txCountBefore, _profile.recentVitalityTransactions.Count, "no transaction appended");
        }

        [Test]
        public void TrySpend_NullReason_Throws_AndDoesNotMutate()
        {
            _profile.vitalityBalance = 100;
            var ledger = new VitalityLedger(_profile, _clock, new DomainEvents(), Log.Disabled);
            Assert.Throws<ArgumentException>(() =>
                ledger.TrySpend(new VitalitySpend { amount = 10, reasonCode = null }, out _));
            Assert.AreEqual(100, _profile.vitalityBalance);
        }

        [Test]
        public void TrySpend_ValidReason_Succeeds_AndReasonPreserved()
        {
            _profile.vitalityBalance = 50;
            var ledger = new VitalityLedger(_profile, _clock, new DomainEvents(), Log.Disabled);
            bool ok = ledger.TrySpend(new VitalitySpend { amount = 20, reasonCode = WellKnownIds.ReasonCodes.ProjectRestore }, out var tx);
            Assert.IsTrue(ok);
            Assert.AreEqual(30, _profile.vitalityBalance);
            Assert.AreEqual(WellKnownIds.ReasonCodes.ProjectRestore, tx.reasonCode);
            Assert.AreEqual(LedgerTransactionType.Spend, tx.type);
        }

        [Test]
        public void Credit_EmptyReason_Throws()
        {
            var ledger = new VitalityLedger(_profile, _clock, new DomainEvents(), Log.Disabled);
            var credit = new VitalityCredit { amount = 10, reasonCode = "" };
            Assert.Throws<ArgumentException>(() => ledger.Credit(credit));
        }

        // --- M2: Reward overflow invariants ---

        [Test]
        public void GrantResource_Overflow_SaturatesToMax_NotWrappedToZero()
        {
            var applier = new RewardApplier(_profile, _clock, new DomainEvents(), Log.Disabled);
            _profile.resources["wood"] = long.MaxValue - 10;
            applier.GrantResource("wood", 20);
            Assert.AreEqual(long.MaxValue, _profile.resources["wood"], "positive overflow must saturate to Max, not wrap to negative/zero");
        }

        [Test]
        public void GrantResource_LargeNegative_SaturatesToZero()
        {
            var applier = new RewardApplier(_profile, _clock, new DomainEvents(), Log.Disabled);
            _profile.resources["wood"] = 5;
            applier.GrantResource("wood", -10);
            Assert.AreEqual(0, _profile.resources["wood"], "negative overflow must clamp to 0");
        }

        [Test]
        public void GrantResource_HalfMaxPlusHalfMax_Saturates()
        {
            var applier = new RewardApplier(_profile, _clock, new DomainEvents(), Log.Disabled);
            _profile.resources["stone"] = long.MaxValue / 2 + 1;
            applier.GrantResource("stone", long.MaxValue / 2 + 10);
            Assert.AreEqual(long.MaxValue, _profile.resources["stone"]);
        }

        [Test]
        public void GrantResource_NormalSmallAmount_UnchangedBehavior()
        {
            var applier = new RewardApplier(_profile, _clock, new DomainEvents(), Log.Disabled);
            _profile.resources["wood"] = 10;
            applier.GrantResource("wood", 5);
            Assert.AreEqual(15, _profile.resources["wood"]);
            applier.GrantResource("wood", -3);
            Assert.AreEqual(12, _profile.resources["wood"]);
        }

        [Test]
        public void AddRegionScore_Overflow_SaturatesToIntMax()
        {
            var applier = new RewardApplier(_profile, _clock, new DomainEvents(), Log.Disabled);
            var region = _profile.worldState.GetOrCreateRegionState(WellKnownIds.StartingRegionId);
            region.ecologyScore = int.MaxValue - 5;
            applier.ApplyOne(region, new RewardActionDefinition { kind = RewardActionKind.AddRegionScore, targetId = "ecology", amount = 10 });
            Assert.AreEqual(int.MaxValue, region.ecologyScore, "positive int overflow must saturate to Max");
        }

        [Test]
        public void AddRegionScore_NegativeOverflow_SaturatesToIntMin()
        {
            var applier = new RewardApplier(_profile, _clock, new DomainEvents(), Log.Disabled);
            var region = _profile.worldState.GetOrCreateRegionState(WellKnownIds.StartingRegionId);
            region.infrastructureScore = int.MinValue + 5;
            applier.ApplyOne(region, new RewardActionDefinition { kind = RewardActionKind.AddRegionScore, targetId = "infrastructure", amount = -10 });
            Assert.AreEqual(int.MinValue, region.infrastructureScore);
        }

        [Test]
        public void AddRegionScore_NormalAmount_Unchanged()
        {
            var applier = new RewardApplier(_profile, _clock, new DomainEvents(), Log.Disabled);
            var region = _profile.worldState.GetOrCreateRegionState(WellKnownIds.StartingRegionId);
            region.communityScore = 100;
            applier.ApplyOne(region, new RewardActionDefinition { kind = RewardActionKind.AddRegionScore, targetId = "community", amount = 25 });
            Assert.AreEqual(125, region.communityScore);
        }

        [Test]
        public void AddRegionScore_LargeLongAmount_ClampedToIntRange_AndSaturated()
        {
            var applier = new RewardApplier(_profile, _clock, new DomainEvents(), Log.Disabled);
            var region = _profile.worldState.GetOrCreateRegionState(WellKnownIds.StartingRegionId);
            region.knowledgeScore = int.MaxValue - 1;
            // amount larger than int.MaxValue: delta clamped to int.MaxValue then saturated
            applier.ApplyOne(region, new RewardActionDefinition { kind = RewardActionKind.AddRegionScore, targetId = "knowledge", amount = (long)int.MaxValue * 10 });
            Assert.AreEqual(int.MaxValue, region.knowledgeScore);
        }
    }
}
