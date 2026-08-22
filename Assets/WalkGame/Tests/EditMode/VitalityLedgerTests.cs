using System;
using NUnit.Framework;
using WalkGame.Core;
using WalkGame.Gameplay;

namespace WalkGame.Tests
{
    public sealed class VitalityLedgerTests
    {
        private MutableClock _clock;
        private PlayerProfile _profile;
        private DomainEvents _events;
        private VitalityLedger _ledger;

        [SetUp]
        public void SetUp()
        {
            _clock = new MutableClock(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
            _profile = new PlayerProfile();
            _events = new DomainEvents();
            _ledger = new VitalityLedger(_profile, _clock, _events, Log.Disabled);
        }

        [Test]
        public void Credit_IncreasesBalance_AndRecordsTransaction()
        {
            var transaction = _ledger.Credit(VitalityCredit.Steps(100));

            Assert.AreEqual(100, _ledger.GetBalance());
            Assert.AreEqual(LedgerTransactionType.Credit, transaction.type);
            Assert.AreEqual(100, transaction.resultingBalance);
            Assert.AreEqual(WellKnownIds.ReasonCodes.Steps, transaction.reasonCode);
            Assert.AreEqual(1, _profile.recentVitalityTransactions.Count);
        }

        [Test]
        public void Spend_RejectsWhenInsufficient_BalanceNeverNegative()
        {
            _ledger.Credit(VitalityCredit.Steps(50));

            bool spent = _ledger.TrySpend(new VitalitySpend { amount = 51, reasonCode = WellKnownIds.ReasonCodes.ProjectRestore }, out _);

            Assert.IsFalse(spent);
            Assert.AreEqual(50, _ledger.GetBalance());
        }

        [Test]
        public void Spend_DecreasesBalance_WhenSufficient()
        {
            _ledger.Credit(VitalityCredit.Steps(200));
            bool spent = _ledger.TrySpend(
                new VitalitySpend { amount = 150, reasonCode = WellKnownIds.ReasonCodes.ProjectRestore },
                out var transaction);

            Assert.IsTrue(spent);
            Assert.AreEqual(50, transaction.resultingBalance);
            Assert.AreEqual(50, _ledger.GetBalance());
        }

        [Test]
        public void TransactionLog_StaysBounded()
        {
            for (int i = 0; i < VitalityLedger.MaxRetainedTransactions + 25; i++)
            {
                _ledger.Credit(VitalityCredit.Steps(1));
            }

            Assert.AreEqual(VitalityLedger.MaxRetainedTransactions, _profile.recentVitalityTransactions.Count);
            // Oldest entries are dropped; newest balance is preserved.
            Assert.AreEqual(_ledger.GetBalance(), _profile.recentVitalityTransactions[^1].resultingBalance);
        }

        [Test]
        public void Credit_PublishesDomainEvent()
        {
            VitalityCredited received = default;
            _events.Subscribe<VitalityCredited>(evt => received = evt);

            _ledger.Credit(VitalityCredit.Steps(10));

            Assert.AreEqual(10, received.Amount);
            Assert.AreEqual(WellKnownIds.ReasonCodes.Steps, received.ReasonCode);
        }

        [Test]
        public void Credit_ZeroOrNegativeAmount_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ledger.Credit(VitalityCredit.Steps(0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ledger.Credit(VitalityCredit.Steps(-5)));
        }
    }
}
