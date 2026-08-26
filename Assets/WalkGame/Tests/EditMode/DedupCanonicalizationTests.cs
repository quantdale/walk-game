using System;
using NUnit.Framework;
using WalkGame.Core;

namespace WalkGame.Tests
{
    /// <summary>
    /// M8.5 dedup canonicalization (OpenSpec runtime-ownership, I8): after
    /// <see cref="CreditedActivityKeys.Rebuild"/>, entries are deterministic, unique,
    /// bounded by capacity in most-recent-occurrence order, and the membership index is
    /// exactly equal to the surviving entries. Capacity compaction must never make a
    /// surviving credited key appear uncredited (H9 corruption regression).
    /// </summary>
    public sealed class DedupCanonicalizationTests
    {
        [Test]
        public void DuplicateBeforeCapacity_CollapsesToMostRecentOccurrence_Once()
        {
            var keys = new CreditedActivityKeys(capacity: 10);
            keys.entries.AddRange(new[] { "A", "B", "A", "C" });

            keys.Rebuild();

            CollectionAssert.AreEqual(new[] { "B", "A", "C" }, keys.entries,
                "duplicates collapse onto their most recent occurrence");
            Assert.IsTrue(keys.Contains("A"));
            Assert.IsTrue(keys.Contains("B"));
            Assert.IsTrue(keys.Contains("C"));
            Assert.AreEqual(3, keys.Count);
        }

        [Test]
        public void DuplicateCrossingEvictionBoundary_NeverReopensSurvivingKey()
        {
            // The H9 corruption: trimming removed A's OLDER duplicate from entries and
            // also dropped A from the membership set while A's newer entry survived.
            var keys = new CreditedActivityKeys(capacity: 3);
            keys.entries.AddRange(new[] { "A", "B", "A", "C", "D" });

            keys.Rebuild();

            CollectionAssert.AreEqual(new[] { "A", "C", "D" }, keys.entries);
            Assert.IsTrue(keys.Contains("A"), "a surviving credited key must stay credited");
            Assert.IsFalse(keys.TryMarkCredited("A"),
                "the surviving durable key MUST be rejected as already credited");
            Assert.IsFalse(keys.Contains("B"), "the evicted oldest unique key is gone");
        }

        [Test]
        public void AllDuplicateInput_CollapsesToSingleMostRecentEntry()
        {
            var keys = new CreditedActivityKeys(capacity: 5);
            keys.entries.AddRange(new[] { "K", "K", "K", "K" });

            keys.Rebuild();

            CollectionAssert.AreEqual(new[] { "K" }, keys.entries);
            Assert.AreEqual(1, keys.Count);
            Assert.IsFalse(keys.TryMarkCredited("K"));
        }

        [Test]
        public void NullAndEmptyEntries_AreRemoved_WithoutTouchingValidMembership()
        {
            var keys = new CreditedActivityKeys(capacity: 5);
            keys.entries.AddRange(new[] { null, "", "valid.1", null, "valid.2" });

            keys.Rebuild();

            CollectionAssert.AreEqual(new[] { "valid.1", "valid.2" }, keys.entries);
            Assert.IsTrue(keys.Contains("valid.1"));
            Assert.IsTrue(keys.Contains("valid.2"));
        }

        [Test]
        public void OverCapacityUniqueInput_TrimsOldestUniqueEntries()
        {
            var keys = new CreditedActivityKeys(capacity: 3);
            keys.entries.AddRange(new[] { "A", "B", "C", "D", "E" });

            keys.Rebuild();

            CollectionAssert.AreEqual(new[] { "C", "D", "E" }, keys.entries);
            Assert.AreEqual(3, keys.Count);
            Assert.IsFalse(keys.Contains("A"));
            Assert.IsFalse(keys.Contains("B"));
        }

        [Test]
        public void SaveLoadReplay_SurvivingKeyIsRejectedAsAlreadyCredited()
        {
            // Corruption reaching the load path (SaveValidator calls Rebuild): a
            // hand-edited duplicate list must canonicalize so exactly-once survives.
            string payload =
                "{\"schemaVersion\":3,\"entries\":[\"session:a\",\"session:b\",\"session:a\",\"session:c\",\"session:d\"]}";

            var keys = new CreditedActivityKeys(capacity: 3);
            var parsed = Newtonsoft.Json.JsonConvert.DeserializeObject<SerializationShape>(payload);
            keys.entries = parsed.entries;
            keys.Rebuild();

            Assert.IsFalse(keys.TryMarkCredited("session:a"),
                "reload must not reopen a key that survived compaction");
            Assert.IsTrue(keys.TryMarkCredited("session:e"));
            CollectionAssert.AreEqual(new[] { "session:c", "session:d", "session:e" }, keys.entries);
        }

        [Test]
        public void TryMarkCredited_AfterCanonicalRebuild_EvictsOldestExactly()
        {
            var keys = new CreditedActivityKeys(capacity: 3);
            keys.entries.AddRange(new[] { "A", "B", "A", "C" });
            keys.Rebuild(); // -> B, A, C

            Assert.IsTrue(keys.TryMarkCredited("D")); // evicts B
            CollectionAssert.AreEqual(new[] { "A", "C", "D" }, keys.entries);
            Assert.IsTrue(keys.Contains("A"), "eviction removes only the true oldest unique key");
            Assert.IsFalse(keys.Contains("B"));

            Assert.IsTrue(keys.TryMarkCredited("E")); // evicts A
            CollectionAssert.AreEqual(new[] { "C", "D", "E" }, keys.entries);
            Assert.IsFalse(keys.Contains("A"));
        }

        private sealed class SerializationShape
        {
            public int schemaVersion = 3;
            public System.Collections.Generic.List<string> entries = new System.Collections.Generic.List<string>();
        }
    }
}
