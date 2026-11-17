#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;

namespace Tests.EditMode.Equipment
{
    /// <summary>Proves the independent Doc 09 oracle rejects every illegal shape it is meant to catch.</summary>
    public sealed class Doc09WeaponLegalityValidatorTests
    {
        private const CharacterAttribute Str = CharacterAttribute.Strength;
        private const CharacterAttribute Dex = CharacterAttribute.Dexterity;
        private const CharacterAttribute Vit = CharacterAttribute.Vitality;

        [Test]
        public void Audit_Doc09ExampleStrengthBPlusVitalityD_IsLegal()
        {
            WeaponObservation weapon = new(Str, Scaling(Str, WeaponScalingGrade.B, 0.70f), Scaling(Vit, WeaponScalingGrade.D, 0.40f), 2);

            Assert.That(Doc09WeaponLegalityValidator.Audit(weapon), Is.Empty);
        }

        [Test]
        public void Audit_UnscaledWeaponWithoutSockets_IsLegal()
        {
            Assert.That(Doc09WeaponLegalityValidator.Audit(new WeaponObservation(Dex, null, null, 0)), Is.Empty);
        }

        [TestCase(WeaponScalingGrade.S, 1.00f)]
        [TestCase(WeaponScalingGrade.A, 0.85f)]
        [TestCase(WeaponScalingGrade.B, 0.70f)]
        [TestCase(WeaponScalingGrade.C, 0.55f)]
        [TestCase(WeaponScalingGrade.D, 0.40f)]
        [TestCase(WeaponScalingGrade.E, 0.25f)]
        public void Audit_EveryDoc09GradeWithItsCoefficient_IsLegal(WeaponScalingGrade grade, float coefficient)
        {
            WeaponObservation weapon = new(Str, Scaling(Str, grade, coefficient), null, 0);

            Assert.That(Doc09WeaponLegalityValidator.Audit(weapon), Is.Empty);
        }

        [TestCase(CharacterAttribute.Vitality)]
        [TestCase(CharacterAttribute.Resistance)]
        [TestCase(CharacterAttribute.Luck)]
        public void Audit_NaturalAttributeOutsideStrDexInt_IsIllegal(CharacterAttribute natural)
        {
            AssertSingleViolation(new WeaponObservation(natural, null, null, 0), "natural");
        }

        [Test]
        public void Audit_PrimaryOtherThanNaturalCompetence_IsIllegal()
        {
            AssertSingleViolation(new WeaponObservation(Str, Scaling(Dex, WeaponScalingGrade.B, 0.70f), null, 0), "primary");
        }

        [Test]
        public void Audit_SecondaryWithoutPrimary_IsIllegal()
        {
            AssertSingleViolation(new WeaponObservation(Str, null, Scaling(Vit, WeaponScalingGrade.D, 0.40f), 0), "without a primary");
        }

        [Test]
        public void Audit_Doc09ExampleStrengthBPlusStrengthD_IsIllegal()
        {
            WeaponObservation weapon = new(Str, Scaling(Str, WeaponScalingGrade.B, 0.70f), Scaling(Str, WeaponScalingGrade.D, 0.40f), 0);

            AssertSingleViolation(weapon, "repeats");
        }

        [TestCase(WeaponScalingGrade.None)]
        [TestCase((WeaponScalingGrade)7)]
        public void Audit_GradeOutsideEThroughS_IsIllegal(WeaponScalingGrade grade)
        {
            AssertSingleViolation(new WeaponObservation(Str, Scaling(Str, grade, 0f), null, 0), "grade");
        }

        [Test]
        public void Audit_ReportedCoefficientDifferentFromDoc09_IsIllegal()
        {
            AssertSingleViolation(new WeaponObservation(Str, Scaling(Str, WeaponScalingGrade.B, 0.75f), null, 0), "coefficient");
        }

        [Test]
        public void Audit_SecondaryAttributeOutsideDoc09Attributes_IsIllegal()
        {
            WeaponObservation weapon = new(Str, Scaling(Str, WeaponScalingGrade.B, 0.70f), Scaling((CharacterAttribute)6, WeaponScalingGrade.D, 0.40f), 0);

            AssertSingleViolation(weapon, "secondary");
        }

        [TestCase(-1)]
        [TestCase(3)]
        public void Audit_GemSocketCountOutsideZeroToTwo_IsIllegal(int socketCount)
        {
            AssertSingleViolation(new WeaponObservation(Str, null, null, socketCount), "socket");
        }

        private static ScalingObservation Scaling(CharacterAttribute attribute, WeaponScalingGrade grade, float coefficient) =>
            new(attribute, grade, coefficient);

        private static void AssertSingleViolation(in WeaponObservation weapon, string expectedFragment)
        {
            var violations = Doc09WeaponLegalityValidator.Audit(weapon);

            Assert.That(violations, Has.Count.EqualTo(1), string.Join(" | ", violations));
            Assert.That(violations[0], Does.Contain(expectedFragment).IgnoreCase);
        }
    }
}
#endif
