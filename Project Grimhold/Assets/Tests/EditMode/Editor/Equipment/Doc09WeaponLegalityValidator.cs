#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;

namespace Tests.EditMode.Equipment
{
    /// <summary>One observed scaling slot: attribute, grade and the coefficient production reports for it.</summary>
    internal readonly struct ScalingObservation
    {
        public readonly CharacterAttribute Attribute;
        public readonly WeaponScalingGrade Grade;
        public readonly float ReportedCoefficient;

        public ScalingObservation(CharacterAttribute attribute, WeaponScalingGrade grade, float reportedCoefficient)
        {
            Attribute = attribute;
            Grade = grade;
            ReportedCoefficient = reportedCoefficient;
        }
    }

    /// <summary>Plain observation of a weapon instance, decoupled from production validation.</summary>
    internal readonly struct WeaponObservation
    {
        public readonly CharacterAttribute NaturalAttribute;
        public readonly ScalingObservation? Primary;
        public readonly ScalingObservation? Secondary;
        public readonly int GemSocketCount;

        public WeaponObservation(
            CharacterAttribute naturalAttribute,
            ScalingObservation? primary,
            ScalingObservation? secondary,
            int gemSocketCount)
        {
            NaturalAttribute = naturalAttribute;
            Primary = primary;
            Secondary = secondary;
            GemSocketCount = gemSocketCount;
        }
    }

    /// <summary>
    /// Test-side oracle for Doc 09 §8.1 (grades and coefficients), §8.2 (legal combinations) and
    /// §22 (0-2 gem sockets). It restates the design rules independently and never calls
    /// <see cref="WeaponInstanceModifiers.TryValidate"/>.
    /// </summary>
    internal static class Doc09WeaponLegalityValidator
    {
        public const int MaxGemSocketCount = 2;
        private const float CoefficientTolerance = 0.0001f;

        // Doc 09 §5 and §8.2: FUE, DES and INT are the natural competences of weapon families.
        private static readonly CharacterAttribute[] NaturalCompetences =
        {
            CharacterAttribute.Strength,
            CharacterAttribute.Dexterity,
            CharacterAttribute.Intelligence
        };

        // Doc 09 §8.2: the secondary may use VIT, RES, FUE, DES, INT or SUE.
        private static readonly CharacterAttribute[] SecondaryCandidates =
        {
            CharacterAttribute.Vitality,
            CharacterAttribute.Resistance,
            CharacterAttribute.Strength,
            CharacterAttribute.Dexterity,
            CharacterAttribute.Intelligence,
            CharacterAttribute.Luck
        };

        // Doc 09 §8.1: grade coefficients.
        private static readonly Dictionary<WeaponScalingGrade, float> Coefficients = new()
        {
            { WeaponScalingGrade.S, 1.00f },
            { WeaponScalingGrade.A, 0.85f },
            { WeaponScalingGrade.B, 0.70f },
            { WeaponScalingGrade.C, 0.55f },
            { WeaponScalingGrade.D, 0.40f },
            { WeaponScalingGrade.E, 0.25f }
        };

        /// <summary>Reads a generated instance through its public data only, never through its validator.</summary>
        public static WeaponObservation Observe(CharacterAttribute naturalAttribute, in WeaponInstanceModifiers instance) =>
            new(
                naturalAttribute,
                instance.HasPrimary ? ObserveScaling(instance.Primary) : null,
                instance.HasSecondary ? ObserveScaling(instance.Secondary) : null,
                instance.GemSocketCount);

        /// <summary>Returns every Doc 09 rule the weapon breaks; an empty list means legal.</summary>
        public static List<string> Audit(in WeaponObservation weapon)
        {
            var violations = new List<string>();
            if (System.Array.IndexOf(NaturalCompetences, weapon.NaturalAttribute) < 0)
            {
                violations.Add($"Natural attribute '{weapon.NaturalAttribute}' must be Strength, Dexterity or Intelligence.");
            }

            if (weapon.Primary.HasValue)
            {
                ScalingObservation primary = weapon.Primary.Value;
                AuditGrade("Primary", primary, violations);
                if (primary.Attribute != weapon.NaturalAttribute)
                {
                    violations.Add($"Primary scaling '{primary.Attribute}' must match the natural competence '{weapon.NaturalAttribute}'.");
                }
            }

            if (weapon.Secondary.HasValue)
            {
                ScalingObservation secondary = weapon.Secondary.Value;
                AuditGrade("Secondary", secondary, violations);
                if (System.Array.IndexOf(SecondaryCandidates, secondary.Attribute) < 0)
                {
                    violations.Add($"Secondary scaling attribute '{(int)secondary.Attribute}' is not a Doc 09 attribute.");
                }

                if (!weapon.Primary.HasValue)
                {
                    violations.Add("Secondary scaling cannot exist without a primary scaling.");
                }
                else if (secondary.Attribute == weapon.Primary.Value.Attribute)
                {
                    violations.Add($"Secondary scaling repeats the primary attribute '{secondary.Attribute}'.");
                }
            }

            if (weapon.GemSocketCount < 0 || weapon.GemSocketCount > MaxGemSocketCount)
            {
                violations.Add($"Gem socket count {weapon.GemSocketCount} is outside 0-{MaxGemSocketCount}.");
            }

            return violations;
        }

        private static ScalingObservation ObserveScaling(WeaponScalingModifier modifier)
        {
            modifier.TryGetCoefficient(out float coefficient);
            return new ScalingObservation(modifier.Attribute, modifier.Grade, coefficient);
        }

        private static void AuditGrade(string slot, in ScalingObservation scaling, List<string> violations)
        {
            if (!Coefficients.TryGetValue(scaling.Grade, out float expected))
            {
                violations.Add($"{slot} scaling grade '{scaling.Grade}' must be one of E through S.");
                return;
            }

            if (System.Math.Abs(scaling.ReportedCoefficient - expected) > CoefficientTolerance)
            {
                violations.Add($"{slot} scaling grade {scaling.Grade} reports coefficient {scaling.ReportedCoefficient} instead of {expected}.");
            }
        }
    }
}
#endif
