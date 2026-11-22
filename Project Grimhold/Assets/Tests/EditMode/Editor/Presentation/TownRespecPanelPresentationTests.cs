#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;

namespace Tests.EditMode.Presentation
{
    /// <summary>TASK-304 T7 (S13): the Mentor panel projection of the confirmed attributes and the respec state.</summary>
    public sealed class TownRespecPanelPresentationTests
    {
        private static TownRespecPresentation Respec(int? cost = 100) =>
            new(cost, TownRespecBlockReason.None, null, true, false, "¿Reasignar?", null);

        [Test]
        public void Title_IsTheProvisionalMentorName()
        {
            Assert.That(TownRespecPanelPresentation.Title, Is.EqualTo("Mentor"));
        }

        [Test]
        public void Create_ProjectsEveryAttributeAndSumsThePointsThatReturnToThePool()
        {
            CharacterAttributeState attributes = AbilityTestFactory.CreateAttributes(5, 4, 3, 2, 1, 0);

            var presentation = TownRespecPanelPresentation.Create(attributes, Respec());

            Assert.That(presentation.HasAttributes, Is.True);
            Assert.That(presentation.ValueText(CharacterAttribute.Vitality), Is.EqualTo("5"));
            Assert.That(presentation.ValueText(CharacterAttribute.Resistance), Is.EqualTo("4"));
            Assert.That(presentation.ValueText(CharacterAttribute.Strength), Is.EqualTo("3"));
            Assert.That(presentation.ValueText(CharacterAttribute.Dexterity), Is.EqualTo("2"));
            Assert.That(presentation.ValueText(CharacterAttribute.Intelligence), Is.EqualTo("1"));
            Assert.That(presentation.ValueText(CharacterAttribute.Luck), Is.EqualTo("0"));
            Assert.That(presentation.PointsToReturn, Is.EqualTo(15));
            Assert.That(presentation.PointsToReturnText, Is.EqualTo("Puntos que vuelven al pool: 15"));
        }

        [Test]
        public void Create_DoesNotCountThePointsAlreadyAvailable()
        {
            Assert.That(CharacterAttributeState.TryCreate(2, 0, 0, 0, 0, 0, 7, out CharacterAttributeState attributes), Is.True);

            var presentation = TownRespecPanelPresentation.Create(attributes, Respec());

            Assert.That(presentation.PointsToReturn, Is.EqualTo(2), "the 7 free points are already in the pool");
        }

        [Test]
        public void Create_WithNothingAssigned_ReturnsNothing()
        {
            CharacterAttributeState attributes = AbilityTestFactory.CreateAttributes();

            var presentation = TownRespecPanelPresentation.Create(attributes, Respec());

            Assert.That(presentation.PointsToReturn, Is.Zero);
            Assert.That(presentation.PointsToReturnText, Is.EqualTo("Puntos que vuelven al pool: 0"));
        }

        [Test]
        public void Create_KeepsTheRespecStateUntouched()
        {
            TownRespecPresentation respec = Respec(250);

            var presentation = TownRespecPanelPresentation.Create(AbilityTestFactory.CreateAttributes(1), respec);

            Assert.That(presentation.Respec.Cost, Is.EqualTo(250));
            Assert.That(presentation.Respec.CostText, Is.EqualTo("Costo: 250 de Oro"));
            Assert.That(presentation.Respec.ConfirmationText, Is.EqualTo("¿Reasignar?"));
        }

        [Test]
        public void Unavailable_ShowsPlaceholdersInsteadOfGuessingTheBuild()
        {
            var presentation = TownRespecPanelPresentation.Unavailable(Respec(null));

            Assert.That(presentation.HasAttributes, Is.False);
            Assert.That(presentation.ValueText(CharacterAttribute.Strength), Is.EqualTo("—"));
            Assert.That(presentation.PointsToReturnText, Is.EqualTo("Puntos que vuelven al pool: —"));
            Assert.That(presentation.Respec.CostText, Is.EqualTo("Costo: —"));
        }
    }
}
#endif
