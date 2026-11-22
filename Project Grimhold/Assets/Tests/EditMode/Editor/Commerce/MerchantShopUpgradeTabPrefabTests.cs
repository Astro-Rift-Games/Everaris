#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>TASK-303 T5 (S1, L2): the Mejoras tab lives inside MerchantShopUI, outside the header and footer.</summary>
public sealed class MerchantShopUpgradeTabPrefabTests
{
    private const string PrefabPath = "Assets/Prefabs/UI/MerchantShopUI.prefab";

    private static T Field<T>(object owner, string name) where T : class =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner) as T;

    private static MerchantShopUI Load()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null);
        return prefab.GetComponent<MerchantShopUI>();
    }

    [Test]
    public void TabBar_HasTradeAndUpgradeButtons_OutsideHeaderAndFooter_HiddenByDefault()
    {
        var ui = Load();
        var tabBar = Field<GameObject>(ui, "_tabBar");
        var trade = Field<Button>(ui, "_tradeTabButton");
        var upgrades = Field<Button>(ui, "_upgradesTabButton");
        Assert.That(tabBar, Is.Not.Null);
        Assert.That(tabBar.transform.parent, Is.EqualTo(ui.transform), "direct child of the shop root");
        Assert.That(tabBar.activeSelf, Is.False, "only the Blacksmith and Arcanist show the tabs");
        Assert.That(trade.transform.IsChildOf(tabBar.transform) && upgrades.transform.IsChildOf(tabBar.transform), Is.True);
        Assert.That(trade.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("Comerciar"));
        Assert.That(upgrades.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("Mejoras"));
        var header = Field<Button>(ui, "_closeButton").transform.parent;
        Assert.That(tabBar.transform.IsChildOf(header), Is.False);
        Assert.That(tabBar.transform.IsChildOf(ui.transform.Find("Footer")), Is.False);
    }

    [Test]
    public void TradeContent_IsTheThreeColumnsAndFooter()
    {
        var ui = Load();
        var content = Field<GameObject[]>(ui, "_tradeContent");
        Assert.That(content.Select(c => c.name), Is.EquivalentTo(new[] { "Player's Inventory", "MiddlePanel", "Merchant's Stock", "Footer" }));
    }

    [Test]
    public void UpgradePanel_IsWiredAndHiddenByDefault()
    {
        var ui = Load();
        var panel = Field<MerchantUpgradePanel>(ui, "_upgradePanel");
        Assert.That(panel, Is.Not.Null);
        Assert.That(panel.transform.parent, Is.EqualTo(ui.transform));
        Assert.That(panel.gameObject.activeSelf, Is.False);
        foreach (string name in new[] { "_rowContainer", "_rowTemplate", "_selectedTitleText", "_previewText", "_messageText", "_confirmButton", "_cancelButton" })
            Assert.That(panel.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel), Is.Not.Null, name);
        var operations = Field<Button[]>(panel, "_operationButtons");
        var labels = Field<TMP_Text[]>(panel, "_operationLabels");
        Assert.That(operations, Has.Length.EqualTo(2).And.All.Not.Null);
        Assert.That(labels, Has.Length.EqualTo(2).And.All.Not.Null);
        Assert.That(Field<Button>(panel, "_confirmButton").GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("Confirmar mejora"));
        Assert.That(Field<Button>(panel, "_cancelButton").GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("Cancelar"));
        Assert.That(Field<Button>(panel, "_rowTemplate").transform.IsChildOf(Field<Transform>(panel, "_rowContainer")), Is.True);
    }
}
#endif
