using System;

/// <summary>Stable identifier of a Dungeon Encounter within one raid (Doc 10 §9.2).</summary>
public readonly struct EncounterId : IEquatable<EncounterId>
{
    public static readonly EncounterId None = default;

    public EncounterId(int value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "EncounterId must be positive; 0 is reserved for None.");
        }

        Value = value;
    }

    public int Value { get; }

    public bool IsValid => Value > 0;

    public bool Equals(EncounterId other) => Value == other.Value;

    public override bool Equals(object obj) => obj is EncounterId other && Equals(other);

    public override int GetHashCode() => Value;

    public override string ToString() => $"Encounter#{Value}";

    public static bool operator ==(EncounterId left, EncounterId right) => left.Equals(right);

    public static bool operator !=(EncounterId left, EncounterId right) => !left.Equals(right);
}
