/// <summary>
/// Fixed capacities of the replicated Encounter state. They bound the networked layout (Fusion arrays and bitmasks
/// have a compile-time size) and are enforced on authored data: <see cref="EncounterRoom.Validate"/> and
/// <see cref="EncounterRoomSetValidation.Validate"/>.
/// </summary>
public static class EncounterLimits
{
    /// <summary>Rooms with an Encounter per raid scene. A room's index is its position in the scene configuration.</summary>
    public const int MaxRooms = 32;

    /// <summary>
    /// Members per Encounter cycle. Member keys are 1..MaxMembersPerRoom and occupy bit (key - 1) of the replicated
    /// member and alive masks, so the cap is the width of the mask.
    /// </summary>
    public const int MaxMembersPerRoom = 32;
}
