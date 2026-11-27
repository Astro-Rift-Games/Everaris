using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.Networking
{
    /// <summary>
    /// TASK-309: the local client detects, before the expedition starts, that its own admission payload
    /// would be refused by the Raid host for MaxTransportable and reports it once, analytics only.
    /// </summary>
    public sealed class RaidAdmissionTransportBlockDetectionTests
    {
        private const string CatalogPath =
            "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset";

        private static readonly string[] RestrictedUtilities =
        {
            "UTL_RUNE_COMPASS", "UTL_ARCANE_DETECTOR", "CON_SELF_REVIVE"
        };

        private static readonly Regex WarningPattern =
            new Regex("Transport block report");

        private LootDefinitionCatalog _catalog;
        private int _sends;

        [SetUp]
        public void SetUp()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(CatalogPath);
            Assert.That(_catalog, Is.Not.Null);
            _sends = 0;
        }

        private static RaidAdmissionData Admission(params LootEntry[] entries)
        {
            Assert.That(RaidCode.TryParse("038271", out RaidCode code), Is.True);
            return new RaidAdmissionData(
                code,
                new ProfileId("profile-code"),
                "reservation-1",
                entries,
                default,
                new[] { 1, 0, 0, 0, 0, 0 });
        }

        private static RaidAdmissionData WithUtility(string lootId, int amount) =>
            Admission(new LootEntry(new LootId("arming_sword"), 1),
                new LootEntry(new LootId(lootId), amount));

        private RaidAdmissionTransportBlockReporter Reporter(Func<Task<bool>> send = null) =>
            new RaidAdmissionTransportBlockReporter(send ?? (() =>
            {
                _sends++;
                return Task.FromResult(true);
            }));

        [Test]
        public void ErrorConstant_MatchesTheMessageTheRaidHostReturns()
        {
            RaidAdmissionData admission = WithUtility("UTL_RUNE_COMPASS", 2);

            bool valid = admission.TryProjectLoadout(
                _catalog, LocalProfileSnapshot.MaxLoadoutSlots, out _, out _, out string error);

            Assert.That(valid, Is.False);
            Assert.That(error, Is.EqualTo(RaidAdmissionData.TransportLimitExceededError));
            Assert.That(RaidAdmissionData.TransportLimitExceededError,
                Is.EqualTo("Loadout exceeds item transport limits."));
        }

        [TestCaseSource(nameof(RestrictedUtilities))]
        public void Classifier_FlagsRestrictedUtilityAboveItsLimit(string lootId)
        {
            RaidAdmissionData admission = WithUtility(lootId, 2);

            Assert.That(RaidAdmissionTransportBlock.IsTransportLimitBlock(in admission, _catalog), Is.True);
        }

        [TestCaseSource(nameof(RestrictedUtilities))]
        public void Classifier_AllowsRestrictedUtilityAtItsLimit(string lootId)
        {
            RaidAdmissionData admission = WithUtility(lootId, 1);

            Assert.That(RaidAdmissionTransportBlock.IsTransportLimitBlock(in admission, _catalog), Is.False);
        }

        [Test]
        public void Classifier_IgnoresPhysicalBackpackCapacityRefusal()
        {
            RaidAdmissionData admission = Admission(
                new LootEntry(new LootId("arming_sword"), LocalProfileSnapshot.MaxLoadoutSlots + 2));
            bool valid = admission.TryProjectLoadout(
                _catalog, LocalProfileSnapshot.MaxLoadoutSlots, out _, out _, out string error);
            Assert.That(valid, Is.False, "precondition: the host refuses this loadout");
            Assert.That(error, Is.EqualTo("Loadout exceeds physical backpack capacity."));

            Assert.That(RaidAdmissionTransportBlock.IsTransportLimitBlock(in admission, _catalog), Is.False);
        }

        [Test]
        public void Classifier_IgnoresStackValidationRefusal()
        {
            RaidAdmissionData admission = Admission(
                new LootEntry(new LootId("arming_sword"), RaidLoadoutRules.MaximumEntries + 1));
            bool valid = admission.TryProjectLoadout(
                _catalog, LocalProfileSnapshot.MaxLoadoutSlots, out _, out _, out string error);
            Assert.That(valid, Is.False, "precondition: the host refuses this loadout");
            Assert.That(error, Is.EqualTo(
                "Loadout exceeds physical stack capacity or item transport limits."));

            Assert.That(RaidAdmissionTransportBlock.IsTransportLimitBlock(in admission, _catalog), Is.False);
        }

        [Test]
        public void Classifier_KeepsTheHostCheckOrder_TransportBeforePhysicalCapacity()
        {
            RaidAdmissionData admission = Admission(
                new LootEntry(new LootId("arming_sword"), LocalProfileSnapshot.MaxLoadoutSlots + 2),
                new LootEntry(new LootId("UTL_RUNE_COMPASS"), 2));

            Assert.That(RaidAdmissionTransportBlock.IsTransportLimitBlock(in admission, _catalog), Is.True);
        }

        [Test]
        public void Classifier_RejectsInvalidAdmissionAndMissingCatalog()
        {
            RaidAdmissionData invalid = default;
            RaidAdmissionData restricted = WithUtility("UTL_RUNE_COMPASS", 2);

            Assert.That(RaidAdmissionTransportBlock.IsTransportLimitBlock(in invalid, _catalog), Is.False);
            Assert.That(RaidAdmissionTransportBlock.IsTransportLimitBlock(in restricted, null), Is.False);
        }

        [Test]
        public void Reporter_SendsExactlyOnce_WhenTheAdmissionExceedsMaxTransportable()
        {
            RaidAdmissionData admission = WithUtility("UTL_RUNE_COMPASS", 2);

            bool reported = Reporter().ReportIfBlocked(in admission, _catalog);

            Assert.That(reported, Is.True);
            Assert.That(_sends, Is.EqualTo(1));
        }

        [Test]
        public void Reporter_DoesNotSend_WhenTheLoadoutIsAllowed()
        {
            RaidAdmissionData admission = WithUtility("UTL_RUNE_COMPASS", 1);

            bool reported = Reporter().ReportIfBlocked(in admission, _catalog);

            Assert.That(reported, Is.False);
            Assert.That(_sends, Is.EqualTo(0));
        }

        [Test]
        public void Reporter_DoesNotSend_WhenRefusedForAnotherReason()
        {
            RaidAdmissionData capacity = Admission(
                new LootEntry(new LootId("arming_sword"), LocalProfileSnapshot.MaxLoadoutSlots + 2));
            RaidAdmissionData stack = Admission(
                new LootEntry(new LootId("arming_sword"), RaidLoadoutRules.MaximumEntries + 1));
            RaidAdmissionData invalid = default;
            RaidAdmissionTransportBlockReporter reporter = Reporter();

            Assert.That(reporter.ReportIfBlocked(in capacity, _catalog), Is.False);
            Assert.That(reporter.ReportIfBlocked(in stack, _catalog), Is.False);
            Assert.That(reporter.ReportIfBlocked(in invalid, _catalog), Is.False);
            Assert.That(_sends, Is.EqualTo(0));
        }

        [Test]
        public void Reporter_DoesNotSend_WithoutACatalog()
        {
            RaidAdmissionData admission = WithUtility("UTL_RUNE_COMPASS", 2);

            Assert.That(Reporter().ReportIfBlocked(in admission, null), Is.False);
            Assert.That(_sends, Is.EqualTo(0));
        }

        [Test]
        public void Reporter_WithoutASendDelegate_SkipsSilently()
        {
            RaidAdmissionData admission = WithUtility("UTL_RUNE_COMPASS", 2);
            var reporter = new RaidAdmissionTransportBlockReporter(null);

            Assert.That(reporter.ReportIfBlocked(in admission, _catalog), Is.False);
        }

        [Test]
        public void Reporter_ThrowingSend_NeverEscapesAndIsLoggedOnce()
        {
            RaidAdmissionData admission = WithUtility("UTL_RUNE_COMPASS", 2);
            RaidAdmissionTransportBlockReporter reporter = Reporter(() =>
            {
                _sends++;
                throw new InvalidOperationException("boom");
            });
            LogAssert.Expect(LogType.Warning, WarningPattern);

            bool reported = reporter.ReportIfBlocked(in admission, _catalog);

            Assert.That(reported, Is.True);
            Assert.That(_sends, Is.EqualTo(1));
        }

        [Test]
        public void Reporter_FaultedTask_NeverEscapesAndIsLoggedOnce()
        {
            RaidAdmissionData admission = WithUtility("UTL_RUNE_COMPASS", 2);
            RaidAdmissionTransportBlockReporter reporter = Reporter(() =>
            {
                _sends++;
                return Task.FromException<bool>(new InvalidOperationException("boom"));
            });
            LogAssert.Expect(LogType.Warning, WarningPattern);

            bool reported = reporter.ReportIfBlocked(in admission, _catalog);

            Assert.That(reported, Is.True);
            Assert.That(_sends, Is.EqualTo(1));
        }

        [Test]
        public void Reporter_RejectedReport_NeverEscapesAndIsLoggedOnce()
        {
            RaidAdmissionData admission = WithUtility("UTL_RUNE_COMPASS", 2);
            RaidAdmissionTransportBlockReporter reporter = Reporter(() =>
            {
                _sends++;
                return Task.FromResult(false);
            });
            LogAssert.Expect(LogType.Warning, WarningPattern);

            bool reported = reporter.ReportIfBlocked(in admission, _catalog);

            Assert.That(reported, Is.True);
            Assert.That(_sends, Is.EqualTo(1));
        }

        [Test]
        public void Reporter_DoesNotBlockOnAPendingSend()
        {
            RaidAdmissionData admission = WithUtility("UTL_RUNE_COMPASS", 2);
            var pending = new TaskCompletionSource<bool>();
            RaidAdmissionTransportBlockReporter reporter = Reporter(() =>
            {
                _sends++;
                return pending.Task;
            });

            bool reported = reporter.ReportIfBlocked(in admission, _catalog);

            Assert.That(reported, Is.True);
            Assert.That(_sends, Is.EqualTo(1));
            pending.SetResult(true);
        }

        [Test]
        public void Reporter_ForAnUnauthenticatedSession_NeverSends()
        {
            RaidAdmissionData admission = WithUtility("UTL_RUNE_COMPASS", 2);
            RaidAdmissionTransportBlockReporter reporter = RaidAdmissionTransportBlockReporter.ForSession(null);

            Assert.That(reporter.ReportIfBlocked(in admission, _catalog), Is.False);
        }

        [Test]
        public void TelemetryClient_BodyCarriesOnlyTheSource()
        {
            Assert.That(TelemetryClient.BuildTransportBlockBody(),
                Is.EqualTo("{\"source\":\"raid_admission\"}"));
        }
    }
}

