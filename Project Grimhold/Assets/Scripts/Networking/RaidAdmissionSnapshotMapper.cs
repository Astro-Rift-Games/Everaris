using System;
using Grimhold.Backend;

/// <summary>One admission projection for both ticket decoding and backend verification responses.</summary>
public static class RaidAdmissionSnapshotMapper
{
    public static bool TryProject(in AuthoritativeRaidAdmissionSnapshotDto snapshot,
        out CharacterAttributeState attributes, out PreparedAbilityLoadout abilities,
        out LootEntry[] items, out PreparedEquipmentLoadout equipment)
    {
        attributes = default; abilities = default; items = Array.Empty<LootEntry>(); equipment = default;
        var state = snapshot.characterAttributes;
        if (!CharacterAttributeState.TryCreate(state.vitality, state.resistance, state.strength, state.dexterity,
            state.intelligence, state.luck, state.availablePoints, out attributes)) return false;
        AbilityId first = default, second = default;
        if (!string.IsNullOrEmpty(snapshot.preparedAbilities.slot1) && !AbilityId.TryCreate(snapshot.preparedAbilities.slot1, out first) ||
            !string.IsNullOrEmpty(snapshot.preparedAbilities.slot2) && !AbilityId.TryCreate(snapshot.preparedAbilities.slot2, out second)) return false;
        abilities = new PreparedAbilityLoadout(first, second);
        if (!PreparedAbilityLoadout.TryValidateTransportShape(abilities, out _)) return false;
        var source = snapshot.items ?? Array.Empty<LootEntryDto>();
        if (source.Length > RaidLoadoutRules.MaximumEntries) return false;
        items = new LootEntry[source.Length];
        for (int index = 0; index < source.Length; index++)
        {
            if (!TryProjectWeaponModifiers(source[index].modifiers, out var modifiers)) return false;
            items[index] = new LootEntry(new LootId(source[index].lootId), source[index].amount, modifiers);
            if (!items[index].IsValid) return false;
        }
        foreach (var slot in EquipmentSlotRules.AllSlots)
        {
            string id = snapshot.preparedEquipment.Definitions.Get(slot);
            if (string.IsNullOrEmpty(id)) continue;
            WeaponInstanceModifiers? modifiers = null;
            bool isUnit = snapshot.preparedEquipment.Units != null && snapshot.preparedEquipment.Units.ContainsKey(slot);
            if (isUnit && !TryProjectWeaponModifiers(snapshot.preparedEquipment.Units[slot], out modifiers)) return false;
            equipment = equipment.With(slot, new LootId(id), isUnit ? new EquipmentInstance(null, modifiers) : null);
        }
        return true;
    }

    // Armor upgrade levels are validated but not carried into the Raid yet (TASK-303 L6 follow-up).
    private static bool TryProjectWeaponModifiers(WeaponInstanceModifiersData data, out WeaponInstanceModifiers? modifiers)
    {
        modifiers = null;
        if (data == null) return true;
        if (data.IsArmorShape) return data.TryToArmorLevel(out _, out _);
        if (!data.TryToTransportModifiers(out var parsed, out _)) return false;
        modifiers = parsed;
        return true;
    }
}
