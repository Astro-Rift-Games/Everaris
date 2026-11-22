#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Every Town service NPC greets the player with an intro dialogue before its panel opens, so none of them
/// answers an interaction with a bare menu.
/// </summary>
public sealed class TownNpcIntroDialoguePrefabTests
{
    [TestCase("Assets/Prefabs/TownAbilityVendorNPC.prefab", "TownAbilityVendorNpcDialogue")]
    [TestCase("Assets/Prefabs/TownMissionNPC.prefab", "TownMissionNpcDialogue")]
    [TestCase("Assets/Prefabs/TownMerchantNPC.prefab", "TownMerchantNpcDialogue")]
    [TestCase("Assets/Prefabs/TownRaidNpc.prefab", "TownRaidNpcDialogue")]
    [TestCase("Assets/Prefabs/TownRespecNpc.prefab", "TownRespecNpcDialogue")]
    [TestCase("Assets/Prefabs/TownStashNpc.prefab", "TownStashNpcDialogue")]
    [TestCase("Assets/Prefabs/TownTrainerNpc.prefab", "TownTrainerNpcDialogue")]
    public void TownNpcPrefab_HasAnIntroDialogueWithSpokenLines(string prefabPath, string sequenceName)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.That(prefab, Is.Not.Null, prefabPath);

        TownNpcDialogueTrigger trigger = prefab.GetComponent<TownNpcDialogueTrigger>();
        Assert.That(trigger, Is.Not.Null, $"{prefab.name} must carry a {nameof(TownNpcDialogueTrigger)}.");

        DialogueSequence sequence = trigger.PrimarySequence;
        Assert.That(sequence, Is.Not.Null, $"{prefab.name} needs a primary dialogue sequence.");
        Assert.That(sequence.name, Is.EqualTo(sequenceName));
        Assert.That(sequence.Lines, Is.Not.Null.And.Not.Empty, $"{sequenceName} needs at least one line.");
        foreach (DialogueLine line in sequence.Lines)
        {
            Assert.That(line.SpeakerName, Is.Not.Null.And.Not.Empty, $"{sequenceName} line speaker");
            Assert.That(line.Text, Is.Not.Null.And.Not.Empty, $"{sequenceName} line text");
        }
    }
}
#endif
