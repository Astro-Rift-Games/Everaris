#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using Grimhold.Backend;
using NUnit.Framework;
using UnityEditor;

public sealed class ApplicationStashServiceBootstrapperItemIdTests
{
    private static LootDefinitionCatalog Catalog => AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
        "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");

    [TestCase(2)]
    [TestCase(3)]
    public void TryNormalizeItems_StoresCanonicalIdForLegacyAlias(int schema)
    {
        var items = new[] { new InventoryItemData { lootId = "healthpotion", amount = 2, slot = 4 } };

        Assert.That(ApplicationStashServiceBootstrapper.TryNormalizeItems(items, Catalog, "loadout", out var result, schema), Is.True);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].LootId, Is.EqualTo(new LootId("CON_HEALTH_POTION")));
        Assert.That(result[0].Amount, Is.EqualTo(2));
        Assert.That(result[0].Slot, Is.EqualTo(4));
        Assert.That(items[0].lootId, Is.EqualTo("healthpotion"), "Hydration must not mutate the backend payload.");
    }

    [Test]
    public void TryNormalizeItems_KeepsCanonicalIdsAndSkipsUnknownIds()
    {
        var items = new[]
        {
            new InventoryItemData { lootId = "CON_HEALTH_POTION", amount = 1, slot = 0 },
            new InventoryItemData { lootId = "not_in_catalog", amount = 1, slot = 1 }
        };

        Assert.That(ApplicationStashServiceBootstrapper.TryNormalizeItems(items, Catalog, "stash", out var result, 2), Is.True);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].LootId, Is.EqualTo(new LootId("CON_HEALTH_POTION")));
    }
}
#endif
