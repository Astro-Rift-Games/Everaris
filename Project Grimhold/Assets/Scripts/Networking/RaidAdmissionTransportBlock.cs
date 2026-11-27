using System;

/// <summary>
/// Detects, on the local client, whether its own Raid admission would be refused by the Raid host
/// because an item exceeds MaxTransportable. It reuses the host projection
/// (<see cref="RaidAdmissionData.TryProjectLoadout"/>) so the rule is never duplicated; every other
/// refusal reason (capacity, shape, invalid admission) is deliberately not a transport block.
/// </summary>
public static class RaidAdmissionTransportBlock
{
    public static bool IsTransportLimitBlock(in RaidAdmissionData admission, LootDefinitionCatalog catalog)
    {
        if (catalog == null || !admission.IsValid) return false;

        bool projected = admission.TryProjectLoadout(
            catalog,
            LocalProfileSnapshot.MaxLoadoutSlots,
            out _,
            out _,
            out string error);
        return !projected &&
               string.Equals(error, RaidAdmissionData.TransportLimitExceededError, StringComparison.Ordinal);
    }
}
