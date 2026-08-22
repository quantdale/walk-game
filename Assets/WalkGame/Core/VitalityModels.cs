using System;
using System.Collections.Generic;

namespace WalkGame.Core
{
    /// <summary>Credit/spend requests accepted by the VitalityLedger (TECHNICAL_ARCHITECTURE 5).</summary>
    public sealed class VitalityCredit
    {
        public long amount;
        public string reasonCode = string.Empty;
        public string relatedEntityId;

        public static VitalityCredit Steps(long amount)
        {
            return new VitalityCredit { amount = amount, reasonCode = WellKnownIds.ReasonCodes.Steps };
        }
    }

    public sealed class VitalitySpend
    {
        public long amount;
        public string reasonCode = string.Empty;
        public string relatedEntityId;
    }

    /// <summary>Bounded audit entry for every balance mutation. DATA_MODEL.md 14.</summary>
    public sealed class VitalityTransaction
    {
        public string transactionId = Guid.NewGuid().ToString("D");
        public DateTime timestampUtc = DateTime.MinValue;
        public LedgerTransactionType type = LedgerTransactionType.Credit;
        public long amount;
        public string reasonCode = string.Empty;
        public string relatedEntityId;
        public long resultingBalance;
    }

    public enum LedgerTransactionType
    {
        Credit = 0,
        Spend = 1
    }
}
