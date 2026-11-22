#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using Tests.EditMode.Progression;

namespace Tests.EditMode.Presentation
{
    /// <summary>TASK-304 T3 (S1, S8): the respec commit refreshes the attribute panel and the derived statistics at once.</summary>
    public sealed class TownRespecBindingTests
    {
        private RespecTestProfile _profile;
        private TownAttributeAssignmentBinding _binding;
        private readonly List<TownAttributeAssignmentPresentation> _attributes = new();
        private readonly List<TownCharacterStatisticsPresentation> _statistics = new();

        [SetUp]
        public void SetUp()
        {
            _profile = new RespecTestProfile();
            LocalProfileStore store = _profile.Store;
            _binding = new TownAttributeAssignmentBinding(
                _profile.ProfileId,
                ProgressionBalanceDefaults.InitialMaximumAttributeValue,
                store.TryGetCharacterAttributeState,
                handler => store.ProfileCommitted += handler,
                handler => store.ProfileCommitted -= handler,
                _attributes.Add,
                () => Assert.Fail("The attributes must stay readable."),
                TryBuildStatistics,
                _statistics.Add,
                () => Assert.Fail("The statistics must stay buildable."));
        }

        [TearDown]
        public void TearDown()
        {
            _binding.Dispose();
            _profile.Dispose();
        }

        private bool TryBuildStatistics(in CharacterAttributeState state, out TownCharacterStatisticsPresentation statistics) =>
            TownCharacterStatisticsBuilder.TryBuild(
                state,
                _profile.Store.GetPreparedEquipment(),
                id => _profile.LootCatalog.TryGet(id.Value, out LootDefinition definition) ? definition : null,
                ProgressionBalanceDefaults.InitialCharacterDerivedStatisticsConfiguration,
                TownCharacterStatisticsBuilder.DefaultDefenseMitigationConstant,
                out statistics,
                out _);

        [Test]
        public void ApplyConfirmedRespec_RefreshesTheAttributesAndDerivedStatisticsImmediately()
        {
            Assert.That(_attributes, Has.Count.EqualTo(1));
            TownAttributeAssignmentPresentation before = _attributes[0];
            TownCharacterStatisticsPresentation statsBefore = _statistics[0];
            Assert.That(before.Vitality, Is.EqualTo(5));
            Assert.That(statsBefore.Weapons.Count, Is.EqualTo(1), "the rapier is equipped before the respec");

            Assert.That(_profile.Store.ApplyConfirmedRespec(_profile.Result()), Is.EqualTo(StashOperationResult.Success));

            Assert.That(_attributes, Has.Count.EqualTo(2), "one commit refreshes the panel once");
            TownAttributeAssignmentPresentation after = _attributes[1];
            Assert.That(
                new[] { after.Vitality, after.Resistance, after.Strength, after.Dexterity, after.Intelligence, after.Luck },
                Is.All.Zero);
            Assert.That(after.AvailablePoints, Is.EqualTo(RespecTestProfile.OwnedPoints));
            Assert.That(after.CanAssignVitality, Is.True, "the returned points can be assigned again");

            Assert.That(_statistics, Has.Count.EqualTo(2));
            TownCharacterStatisticsPresentation statsAfter = _statistics[1];
            Assert.That(statsAfter.MaximumHealth.FromAttributes, Is.LessThan(statsBefore.MaximumHealth.FromAttributes));
            Assert.That(statsAfter.Weapons.Count, Is.Zero, "the unequipped weapon leaves the sheet");
        }
    }
}
#endif
