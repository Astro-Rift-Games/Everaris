using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>TASK-330 T5 (S3): reactivation must not be disabled silently by a missing pressure config or controller.</summary>
public sealed class EncounterReactivationDependenciesTests
{
    private const string PrefabPath = "Assets/Prefabs/NetworkMatchController.prefab";

    private DungeonPressureConfig _config;

    [TearDown]
    public void TearDown()
    {
        if (_config != null) Object.DestroyImmediate(_config);
    }

    private DungeonPressureConfig Config(int maxGlobalEnemies)
    {
        _config = ScriptableObject.CreateInstance<DungeonPressureConfig>();
        var serialized = new SerializedObject(_config);
        serialized.FindProperty("_maxGlobalEnemies").intValue = maxGlobalEnemies;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return _config;
    }

    [Test]
    public void TryValidate_ConfigWithCapacityAndController_IsValid()
    {
        Assert.That(EncounterReactivationDependencies.TryValidate(Config(40), true, out string failure), Is.True, failure);
        Assert.That(failure, Is.Empty);
    }

    [Test]
    public void TryValidate_MissingConfig_FailsNamingTheFieldAndTheEffect()
    {
        Assert.That(EncounterReactivationDependencies.TryValidate(null, true, out string failure), Is.False);
        Assert.That(failure, Does.Contain("_pressureConfig").And.Contain("MaxGlobalEnemies").And.Contain("blocked"));
    }

    [TestCase(0)]
    [TestCase(-3)]
    public void TryValidate_NoGlobalCapacity_Fails(int maxGlobalEnemies)
    {
        Assert.That(EncounterReactivationDependencies.TryValidate(Config(maxGlobalEnemies), true, out string failure), Is.False);
        Assert.That(failure, Does.Contain("MaxGlobalEnemies").And.Contain(maxGlobalEnemies.ToString()));
    }

    [Test]
    public void TryValidate_MissingPressureController_FailsNamingTheComponent()
    {
        Assert.That(EncounterReactivationDependencies.TryValidate(Config(40), false, out string failure), Is.False);
        Assert.That(failure, Does.Contain(nameof(DungeonPressureController)));
    }

    [Test]
    public void Prefab_EncounterRegistryUsesTheSamePressureConfigAsTheDungeonPressureController()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null, PrefabPath);
        var registry = prefab.GetComponent<EncounterRegistry>();
        var controller = prefab.GetComponent<DungeonPressureController>();
        Assert.That(registry, Is.Not.Null);
        Assert.That(controller, Is.Not.Null);

        Object registryConfig = new SerializedObject(registry).FindProperty("_pressureConfig").objectReferenceValue;
        Object controllerConfig = new SerializedObject(controller).FindProperty("_config").objectReferenceValue;

        Assert.That(registryConfig, Is.Not.Null, "The registry needs the pressure config for MaxGlobalEnemies.");
        Assert.That(registryConfig, Is.SameAs(controllerConfig));
        Assert.That(EncounterReactivationDependencies.TryValidate((DungeonPressureConfig)registryConfig, true, out string failure), Is.True, failure);
    }
}
