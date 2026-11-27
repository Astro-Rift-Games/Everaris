using System;

/// <summary>Static data the registry needs about one room: its identity and Balance parameters.</summary>
public readonly struct EncounterRoomDefinition : IEquatable<EncounterRoomDefinition>
{
    public EncounterRoomDefinition(EncounterId id, EncounterLifecycleSettings settings)
    {
        Id = id;
        Settings = settings;
    }

    public EncounterId Id { get; }
    public EncounterLifecycleSettings Settings { get; }

    public bool Equals(EncounterRoomDefinition other) =>
        Id == other.Id &&
        Settings.CooldownSeconds == other.Settings.CooldownSeconds &&
        Settings.MaxReactivations == other.Settings.MaxReactivations;

    public override bool Equals(object obj) => obj is EncounterRoomDefinition other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();
}
