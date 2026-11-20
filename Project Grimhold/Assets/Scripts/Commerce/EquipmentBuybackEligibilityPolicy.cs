/// <summary>Advisory equipment and special consumable eligibility; the backend owns acceptance (Doc 23 §8.3).</summary>
public sealed class EquipmentBuybackEligibilityPolicy : IBuybackEligibilityPolicy
{
    public bool IsEligible(LootDefinition definition)
    {
        if (definition == null || !definition.Tradable || definition.ObjectValue <= 0)
        {
            return false;
        }

        if (definition.Category is LootCategory.Weapon or LootCategory.Helmet or LootCategory.Armor or
            LootCategory.Gloves or LootCategory.Boots or LootCategory.Shield)
        {
            return true;
        }

        string id = definition.Id;
        return id == "CON_EXPLOSIVE_BOMB" || id == "CON_SMOKE_BOMB" ||
               id == "CON_THROWING_DAGGER" || id == "CON_SELF_REVIVE";
    }
}
