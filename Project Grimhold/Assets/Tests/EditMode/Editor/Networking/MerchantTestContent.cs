using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Builds in-memory loot definitions and catalogs for merchant tests.
/// </summary>
public static class MerchantTestContent
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public static LootDefinition CreateDefinition(string id, int buyValue, int sellValue, int extractionValue = 0, LootCategory category = LootCategory.None)
    {
        var definition = ScriptableObject.CreateInstance<LootDefinition>();
        SetField(definition, "_id", id);
        if (category == LootCategory.None)
        {
            if (id != null && (id.Contains("helmet") || id.Contains("hat"))) category = LootCategory.Helmet;
            else if (id != null && id.Contains("armor")) category = LootCategory.Armor;
            else if (id != null && id.Contains("potion")) category = LootCategory.Consumable;
            else category = LootCategory.Resource;
        }
        SetField(definition, "_category", category);
        SetField(definition, "_buyValuePerUnit", buyValue);
        SetField(definition, "_sellValuePerUnit", sellValue);
        SetField(definition, "_extractionValuePerUnit", extractionValue);
        SetField(definition, "_objectValue", sellValue);
        SetField(definition, "_tradable", true);
        return definition;
    }

    public static LootDefinitionCatalog CreateCatalog(params LootDefinition[] definitions)
    {
        var catalog = ScriptableObject.CreateInstance<LootDefinitionCatalog>();
        SetField(catalog, "_definitions", new List<LootDefinition>(definitions));
        return catalog;
    }

    public static MerchantStockItem Offer(LootDefinition definition, int initialQuantity)
    {
        return new MerchantStockItem { Item = definition, InitialQuantity = initialQuantity };
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, PrivateInstance);
        Assert.That(field, Is.Not.Null, name);
        field.SetValue(target, value);
    }
}
