using System;
using System.Collections.Generic;

namespace WalkGame.Core
{
    /// <summary>
    /// The only component allowed to credit/spend Vitality (TECHNICAL_ARCHITECTURE 5).
    /// Balance can never go negative; every mutation appends a bounded audit entry
    /// with reason codes so future cloud reconciliation stays possible.
    /// </summary>
    public sealed class VitalityLedger
    {
        public const int MaxRetainedTransactions = 100;

        private readonly PlayerProfile _profile;
        private readonly IClock _clock;
        private readonly DomainEvents _events;
        private readonly Log _log;

        public VitalityLedger(PlayerProfile profile, IClock clock, DomainEvents events, Log log)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _log = log ?? Log.Disabled;
        }

        public long GetBalance()
        {
            return _profile.vitalityBalance;
        }

        public IReadOnlyList<VitalityTransaction> RecentTransactions => _profile.recentVitalityTransactions;

        public VitalityTransaction Credit(VitalityCredit credit)
        {
            if (credit == null)
            {
                throw new ArgumentNullException(nameof(credit));
            }

            if (credit.amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(credit), "Credit amount must be positive.");
            }

            if (string.IsNullOrEmpty(credit.reasonCode))
            {
                throw new ArgumentException("A credit requires a reason code.", nameof(credit));
            }

            _profile.vitalityBalance = checked(_profile.vitalityBalance + credit.amount);
            var transaction = AppendTransaction(LedgerTransactionType.Credit, credit.amount, credit.reasonCode, credit.relatedEntityId);
            _events.Publish(new VitalityCredited
            {
                Amount = credit.amount,
                ResultingBalance = transaction.resultingBalance,
                ReasonCode = credit.reasonCode,
            });
            return transaction;
        }

        public bool TrySpend(VitalitySpend spend, out VitalityTransaction transaction)
        {
            transaction = null;
            if (spend == null)
            {
                throw new ArgumentNullException(nameof(spend));
            }

            if (spend.amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(spend), "Spend amount must be positive.");
            }

            if (_profile.vitalityBalance < spend.amount)
            {
                _log.Debug($"Vitality spend rejected: need {spend.amount}, have {_profile.vitalityBalance}.");
                return false;
            }

            _profile.vitalityBalance -= spend.amount;
            transaction = AppendTransaction(LedgerTransactionType.Spend, spend.amount, spend.reasonCode, spend.relatedEntityId);
            _events.Publish(new VitalitySpent
            {
                Amount = spend.amount,
                ResultingBalance = transaction.resultingBalance,
                ReasonCode = spend.reasonCode,
                RelatedEntityId = spend.relatedEntityId,
            });
            return true;
        }

        private VitalityTransaction AppendTransaction(LedgerTransactionType type, long amount, string reasonCode, string relatedEntityId)
        {
            var transaction = new VitalityTransaction
            {
                timestampUtc = _clock.UtcNow,
                type = type,
                amount = amount,
                reasonCode = reasonCode,
                relatedEntityId = relatedEntityId,
                resultingBalance = _profile.vitalityBalance,
            };

            _profile.recentVitalityTransactions.Add(transaction);
            while (_profile.recentVitalityTransactions.Count > MaxRetainedTransactions)
            {
                _profile.recentVitalityTransactions.RemoveAt(0);
            }

            return transaction;
        }
    }
}
