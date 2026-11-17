using System.Collections.Generic;

/// <summary>Compares admission units with replicated aggregate ownership without losing instance modifiers.</summary>
public static class RaidAdmissionLoadoutMatcher
{
    public static bool Matches(RaidAdmissionData expected, PlayerExpeditionLootSnapshot actual,
        PlayerWeaponEquipmentNetworkController equipment, LootDefinitionCatalog catalog)
    {
        if (actual == null || equipment == null) return false;
        var slots = new LootEntry?[EquipmentSlotRules.AllSlots.Length];
        var modifiers = new WeaponInstanceModifiersNetworkData[slots.Length];
        for (int index = 0; index < slots.Length; index++)
        {
            EquipmentSlot slot = EquipmentSlotRules.AllSlots[index];
            slots[index] = equipment.TryGetSlotLoot(slot, out LootEntry entry) ? entry : null;
            if (EquipmentSlotRules.IsHandSlot(slot) &&
                !equipment.TryGetSlotModifiers(slot, out modifiers[index])) return false;
        }
        return Matches(expected, actual.Combined, actual.ModifiedWeaponUnits, slots, modifiers,
            equipment.ActiveWeaponSetSlot, catalog);
    }

    /// <summary>Detached confirmation interface; slot arrays follow EquipmentSlotRules.AllSlots order.</summary>
    public static bool Matches(RaidAdmissionData expected, IReadOnlyList<LootEntry> combined,
        IReadOnlyList<ExpeditionWeaponUnit> modifiedUnits, IReadOnlyList<LootEntry?> slots,
        IReadOnlyList<WeaponInstanceModifiersNetworkData> slotModifiers, WeaponSetSlot activeSet,
        LootDefinitionCatalog catalog)
    {
        int slotCount = EquipmentSlotRules.AllSlots.Length;
        if (!expected.IsValid ||
            !RaidLoadoutRules.TryValidate(expected.ReservedLoadout, catalog, RaidLoadoutRules.MaximumEntries, out _) ||
            combined == null || modifiedUnits == null || slots == null || slots.Count != slotCount ||
            slotModifiers == null || slotModifiers.Count != slotCount || activeSet != expected.ActiveWeaponSet)
            return false;

        // The admission owns all units, including separately appended prepared instances.
        // Runtime ownership folds them by template, while modifiers remain per weapon unit.
        var totals = new List<StashItem>();
        var expectedUnits = new List<ExpeditionWeaponUnit>();
        var packed = new WeaponInstanceModifiersNetworkData[expected.ReservedLoadout.Count];
        // Reserved ids may be legacy aliases (for example "healthpotion"); materialized ownership always
        // carries the catalog's canonical id, so both sides are compared by canonical identity.
        var canonicalIds = new LootId[expected.ReservedLoadout.Count];
        for (int index = 0; index < expected.ReservedLoadout.Count; index++)
        {
            LootEntry entry = expected.ReservedLoadout[index];
            if (!catalog.TryGet(entry.LootId.Value, out LootDefinition definition)) return false;
            canonicalIds[index] = definition.LootId;
            totals.Add(new StashItem(canonicalIds[index], entry.Amount, index));
            if (entry.Modifiers.HasValue &&
                !WeaponInstanceModifiersNetworkData.TryCreate(entry.Modifiers.Value,
                    definition.WeaponDefinition.NaturalScalingAttribute, catalog, out packed[index], out _))
                return false;
            if (!packed[index].IsEmpty)
                expectedUnits.Add(new ExpeditionWeaponUnit(canonicalIds[index], RaidLootOrigin.Dungeon, packed[index]));
        }
        if (!ProfileInventoryRules.TryAggregate(totals, out List<LootEntry> expectedTotals) ||
            expectedTotals.Count != combined.Count) return false;
        var remaining = new List<LootEntry>(combined);
        foreach (LootEntry entry in expectedTotals)
            if (!remaining.Remove(entry)) return false;

        var currentUnits = new List<ExpeditionWeaponUnit>(modifiedUnits.Count);
        foreach (ExpeditionWeaponUnit unit in modifiedUnits)
            currentUnits.Add(new ExpeditionWeaponUnit(unit.LootId, RaidLootOrigin.Dungeon, unit.Modifiers));
        if (!ExpeditionWeaponUnit.SequenceEquivalent(expectedUnits, currentUnits)) return false;

        for (int index = 0; index < slotCount; index++)
        {
            int reference = expected.EntryIndicesPlusOne[index];
            if (reference == 0)
            {
                if (slots[index].HasValue || !slotModifiers[index].IsEmpty) return false;
                continue;
            }
            if (!slots[index].HasValue || slots[index].Value.LootId != canonicalIds[reference - 1] ||
                slots[index].Value.Amount != 1 || !slotModifiers[index].Equals(packed[reference - 1]))
                return false;
        }
        return true;
    }
}
