#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.Stash
{
    public sealed class LocalProfileShopReceiptRecoveryTests
    {
        private const string SkipWarning = "[LocalProfileSaveCodec] Skipping invalid, duplicate or foreign-profile shop transaction receipt.";
        private readonly ProfileId _profile = new("60606060606060606060606060606060");
        private LootDefinitionCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
                "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");
            Assert.That(_catalog, Is.Not.Null);
        }

        [Test]
        public void Codec_SkipsBadReceiptAndPreservesCompleteProfile(
            [Values("null", "malformedId", "emptyGuid", "zeroTimestamp", "negativeTimestamp", "duplicate", "foreign")] string kind)
        {
            LocalProfileSnapshot expected = CreateSnapshot();
            string json = WithBadReceipt(expected, kind);
            LogAssert.Expect(LogType.Warning, SkipWarning);

            Assert.That(LocalProfileSaveCodec.TryDecode(json, _profile, _catalog,
                out var decoded, out var status, out string error), Is.True, error);

            Assert.That(status, Is.EqualTo(LocalProfilePersistenceStatus.Ready));
            Assert.That(error, Is.Null);
            AssertPreserved(expected, decoded);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Repository_KeepsMainWithoutBackupRepairOrCorruptReset(
            [Values("null", "malformedId", "emptyGuid", "zeroTimestamp", "negativeTimestamp", "duplicate", "foreign")] string kind,
            [Values(false, true)] bool backupHasBadReceipt)
        {
            LocalProfileSnapshot expected = CreateSnapshot();
            string main = WithBadReceipt(expected, kind);
            var oldSnapshot = new LocalProfileSnapshot { ProfileId = _profile, Currency = 1 };
            string backup = backupHasBadReceipt ? WithBadReceipt(oldSnapshot, kind) : LocalProfileSaveCodec.Encode(oldSnapshot);
            string mainPath = Path.Combine(".", $"grimhold-profile-{_profile.Value}.json");
            var files = new LocalProfilePersistenceEditModeTests.MemoryFileStore();
            files.Files[mainPath] = main;
            files.Files[mainPath + ".bak"] = backup;
            var repository = new LocalProfileRepository(files, ".");
            LogAssert.Expect(LogType.Warning, SkipWarning);

            Assert.That(repository.Initialize(_profile, _catalog), Is.True, repository.LastError);

            Assert.That(repository.Status, Is.EqualTo(LocalProfilePersistenceStatus.Ready));
            Assert.That(repository.LastError, Is.Null);
            Assert.That(repository.CanRecoverCorruptCache, Is.False);
            AssertPreserved(expected, repository.Snapshot);
            Assert.That(files.Files[mainPath], Is.EqualTo(main), "Main must not be replaced with older backup state.");
            Assert.That(files.Files[mainPath + ".bak"], Is.EqualTo(backup));
            Assert.That(files.WriteCount, Is.Zero);
            Assert.That(files.Files.Count, Is.EqualTo(2), "Initialization must not create a temporary file.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Codec_ValidShopReceiptsRoundTripUnchangedWithoutWarnings()
        {
            LocalProfileSnapshot expected = CreateSnapshot();
            Assert.That(LocalProfileSaveCodec.TryDecode(LocalProfileSaveCodec.Encode(expected), _profile, _catalog,
                out var decoded, out var status, out string error), Is.True, error);
            Assert.That(status, Is.EqualTo(LocalProfilePersistenceStatus.Ready));
            Assert.That(error, Is.Null);
            AssertPreserved(expected, decoded);
            LogAssert.NoUnexpectedReceived();
        }

        private LocalProfileSnapshot CreateSnapshot()
        {
            var snapshot = new LocalProfileSnapshot
            {
                ProfileId = _profile,
                Currency = 999,
                GuildReputation = 17,
                InventorySchemaVersion = 3,
                ShopIdempotencyWatermark = 70,
                PendingExtractionCommit = new PendingExtractionCommit(
                    new ExtractionReceipt("raid-shop-recovery", _profile, 3),
                    new[] { new StashItem(new LootId("bone"), 4) }, default, 150, 2)
            };
            snapshot.Stash.Add(new StashItem(new LootId("healthpotion"), 2, 5));
            snapshot.Loadout.Add(new StashItem(new LootId("bone"), 4, 8));
            snapshot.AppliedExtractionReceipts.Add(snapshot.PendingExtractionCommit.Receipt);
            snapshot.AppliedShopTransactionReceipts.Add(new ShopTransactionReceipt(
                new ShopTransactionId(100, Guid.Parse("11111111111111111111111111111111")), _profile));
            snapshot.AppliedShopTransactionReceipts.Add(new ShopTransactionReceipt(
                new ShopTransactionId(101, Guid.Parse("22222222222222222222222222222222")), _profile));
            return snapshot;
        }

        private string WithBadReceipt(LocalProfileSnapshot snapshot, string kind)
        {
            var document = JObject.Parse(LocalProfileSaveCodec.Encode(snapshot));
            var receipts = (JArray)document["appliedShopTransactionReceipts"];
            // Old backup can contain the same offending entry without the newer local state.
            var bad = new JObject
            {
                ["timestamp"] = 100,
                ["transactionId"] = "11111111111111111111111111111111",
                ["profileId"] = _profile.Value
            };
            switch (kind)
            {
                case "null": receipts.Insert(0, JValue.CreateNull()); break;
                case "malformedId": bad["transactionId"] = "not-a-guid"; receipts.Insert(0, bad); break;
                case "emptyGuid": bad["transactionId"] = Guid.Empty.ToString("N"); receipts.Insert(0, bad); break;
                case "zeroTimestamp": bad["timestamp"] = 0; receipts.Insert(0, bad); break;
                case "negativeTimestamp": bad["timestamp"] = -1; receipts.Insert(0, bad); break;
                case "duplicate":
                    if (receipts.Count == 0) receipts.Add(bad.DeepClone());
                    receipts.Insert(1, bad);
                    break;
                case "foreign": bad["profileId"] = "70707070707070707070707070707070"; receipts.Insert(0, bad); break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            return document.ToString();
        }

        private static void AssertPreserved(LocalProfileSnapshot expected, LocalProfileSnapshot actual)
        {
            Assert.That(actual.PendingExtractionCommit, Is.Not.Null);
            Assert.That(actual.PendingExtractionCommit.Receipt, Is.EqualTo(expected.PendingExtractionCommit.Receipt));
            CollectionAssert.AreEqual(expected.PendingExtractionCommit.Items, actual.PendingExtractionCommit.Items);
            Assert.That(LocalProfileSaveCodec.Encode(actual), Is.EqualTo(LocalProfileSaveCodec.Encode(expected)),
                "Only the offending shop receipt may be removed; pending extraction and local-only fields must survive.");
        }
    }
}
#endif
