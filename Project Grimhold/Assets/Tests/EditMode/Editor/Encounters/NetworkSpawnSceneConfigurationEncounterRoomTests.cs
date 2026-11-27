using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Spawning;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class NetworkSpawnSceneConfigurationEncounterRoomTests
{
    private readonly EncounterRoomFactory _factory = new EncounterRoomFactory();
    private readonly List<GameObject> _objects = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        _factory.Destroy();
        foreach (GameObject go in _objects)
        {
            if (go != null) Object.DestroyImmediate(go);
        }

        _objects.Clear();
    }

    private EncounterRoom ValidRoom(int id, Transform[] markers = null) =>
        _factory.Room(id, markers ?? _factory.Markers(3, "R" + id));

    /// <summary>A Required configuration that is valid before any rooms are added.</summary>
    private NetworkSpawnSceneConfiguration Configuration(SceneSpawnPointPolicy policy = SceneSpawnPointPolicy.Required, params EncounterRoom[] rooms)
    {
        var go = new GameObject("SpawnConfiguration");
        _objects.Add(go);
        var configuration = go.AddComponent<NetworkSpawnSceneConfiguration>();
        Transform[] playerPoints = _factory.Markers(1, "Player");
        Transform[] enemyPoints = _factory.Markers(4, "Enemy");
        var serialized = new SerializedObject(configuration);
        serialized.FindProperty("_spawnPointPolicy").enumValueIndex = (int)policy;
        if (policy == SceneSpawnPointPolicy.Required)
        {
            SerializedProperty areas = serialized.FindProperty("_playerSpawnAreas");
            areas.arraySize = 1;
            SerializedProperty areaPoints = areas.GetArrayElementAtIndex(0).FindPropertyRelative("_spawnPoints");
            areaPoints.arraySize = 1;
            areaPoints.GetArrayElementAtIndex(0).objectReferenceValue = playerPoints[0];

            SerializedProperty groups = serialized.FindProperty("_spawnGroups");
            groups.arraySize = 1;
            SerializedProperty group = groups.GetArrayElementAtIndex(0);
            group.FindPropertyRelative(nameof(SpawnGroupDefinition.Group)).enumValueIndex = (int)SpawnGroupType.Enemies;
            group.FindPropertyRelative(nameof(SpawnGroupDefinition.Amount)).intValue = 2;
            SerializedProperty points = group.FindPropertyRelative(nameof(SpawnGroupDefinition.SpawnPoints));
            points.arraySize = enemyPoints.Length;
            for (int i = 0; i < enemyPoints.Length; i++) points.GetArrayElementAtIndex(i).objectReferenceValue = enemyPoints[i];
        }

        SerializedProperty roomsProperty = serialized.FindProperty("_encounterRooms");
        roomsProperty.arraySize = rooms.Length;
        for (int i = 0; i < rooms.Length; i++) roomsProperty.GetArrayElementAtIndex(i).objectReferenceValue = rooms[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return configuration;
    }

    // Room set

    [Test]
    public void ValidateSet_NullOrEmpty_Passes()
    {
        Assert.That(EncounterRoomSetValidation.Validate(null, out string failure), Is.True, failure);
        Assert.That(EncounterRoomSetValidation.Validate(System.Array.Empty<EncounterRoom>(), out failure), Is.True, failure);
        Assert.That(failure, Is.Empty);
    }

    [Test]
    public void ValidateSet_DistinctRoomsWithDistinctMarkers_Passes()
    {
        Assert.That(EncounterRoomSetValidation.Validate(new[] { ValidRoom(1), ValidRoom(2) }, out string failure), Is.True, failure);
    }

    [Test]
    public void ValidateSet_RejectsNullRoom()
    {
        Assert.That(EncounterRoomSetValidation.Validate(new[] { ValidRoom(1), null }, out string failure), Is.False);
        Assert.That(failure, Does.Contain("null"));
    }

    [Test]
    public void ValidateSet_RejectsDuplicateEncounterIds()
    {
        Assert.That(EncounterRoomSetValidation.Validate(new[] { ValidRoom(5), ValidRoom(5) }, out string failure), Is.False);
        Assert.That(failure, Does.Contain("Duplicate").And.Contain("5"));
    }

    [Test]
    public void ValidateSet_RejectsTheSameRoomListedTwice()
    {
        EncounterRoom room = ValidRoom(5);
        Assert.That(EncounterRoomSetValidation.Validate(new[] { room, room }, out string failure), Is.False);
        Assert.That(failure, Does.Contain("Duplicate"));
    }

    [Test]
    public void ValidateSet_RejectsMarkerOwnedByTwoRooms()
    {
        Transform[] shared = _factory.Markers(3);
        EncounterRoom first = ValidRoom(1, new[] { shared[0], shared[1] });
        EncounterRoom second = ValidRoom(2, new[] { shared[1], shared[2] });
        Assert.That(EncounterRoomSetValidation.Validate(new[] { first, second }, out string failure), Is.False);
        Assert.That(failure, Does.Contain(shared[1].name).And.Contain(first.name).And.Contain(second.name));
    }

    [Test]
    public void ValidateSet_RejectsAnInvalidRoom()
    {
        EncounterRoom invalid = _factory.Room(3, _factory.Markers(2), withZone: false);
        Assert.That(EncounterRoomSetValidation.Validate(new[] { ValidRoom(1), invalid }, out string failure), Is.False);
        Assert.That(failure, Does.Contain("generation zone"));
    }

    // Configuration

    [Test]
    public void Configuration_Defaults_HaveNoRoomsAndStayValid()
    {
        NetworkSpawnSceneConfiguration configuration = Configuration();
        Assert.That(configuration.EncounterRooms, Is.Empty);
        Assert.That(configuration.Validate(out string failure), Is.True, failure);
    }

    [Test]
    public void Configuration_WithValidDistinctRooms_Passes()
    {
        NetworkSpawnSceneConfiguration configuration = Configuration(SceneSpawnPointPolicy.Required, ValidRoom(1), ValidRoom(2));
        Assert.That(configuration.EncounterRooms.Count, Is.EqualTo(2));
        Assert.That(configuration.Validate(out string failure), Is.True, failure);
    }

    [Test]
    public void Configuration_RejectsDuplicateEncounterIds()
    {
        NetworkSpawnSceneConfiguration configuration = Configuration(SceneSpawnPointPolicy.Required, ValidRoom(7), ValidRoom(7));
        Assert.That(configuration.Validate(out string failure), Is.False);
        Assert.That(failure, Does.Contain("Duplicate"));
    }

    [Test]
    public void Configuration_RejectsNullRoom()
    {
        NetworkSpawnSceneConfiguration configuration = Configuration(SceneSpawnPointPolicy.Required, ValidRoom(1), null);
        Assert.That(configuration.Validate(out string failure), Is.False);
        Assert.That(failure, Does.Contain("null"));
    }

    [Test]
    public void Configuration_RejectsMarkerOwnedByTwoRooms()
    {
        Transform[] shared = _factory.Markers(2);
        NetworkSpawnSceneConfiguration configuration = Configuration(SceneSpawnPointPolicy.Required,
            ValidRoom(1, new[] { shared[0] }), ValidRoom(2, new[] { shared[0], shared[1] }));
        Assert.That(configuration.Validate(out string failure), Is.False);
        Assert.That(failure, Does.Contain(shared[0].name));
    }

    [Test]
    public void Configuration_NotRequiredPolicy_RejectsRooms()
    {
        NetworkSpawnSceneConfiguration configuration = Configuration(SceneSpawnPointPolicy.NotRequired, ValidRoom(1));
        Assert.That(configuration.Validate(out string failure), Is.False);
        Assert.That(failure, Does.Contain("NotRequired"));
    }

    [Test]
    public void Configuration_NotRequiredPolicy_WithoutRooms_StillPasses()
    {
        NetworkSpawnSceneConfiguration configuration = Configuration(SceneSpawnPointPolicy.NotRequired);
        Assert.That(configuration.Validate(out string failure), Is.True, failure);
    }
}

/// <summary>Existing behavior preserved: the shipped CatacombsMap has no rooms, so the global groups are untouched.</summary>
public sealed class CatacombsEncounterRoomPreservationTests
{
    private const string ScenePath = "Assets/Scenes/CatacombsMap.unity";
    private Scene _scene;

    [SetUp]
    public void OpenSavedSceneReadOnly()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            Assert.That(SceneManager.GetSceneAt(i).isDirty, Is.False, "Composition tests require clean open scenes.");
        _scene = EditorSceneManager.OpenPreviewScene(ScenePath);
    }

    [TearDown]
    public void Close()
    {
        if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
    }

    [Test]
    public void CatacombsMap_HasNoRoomsAndTheEnemiesGroupPointSetIsIdentical()
    {
        NetworkSpawnSceneConfiguration configuration = _scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<NetworkSpawnSceneConfiguration>(true)).Single();
        Assert.That(configuration.EncounterRooms, Is.Empty);
        Assert.That(configuration.Validate(out string failure), Is.True, failure);

        SpawnGroupDefinition enemies = configuration.SpawnGroups.Single(group => group.Group == SpawnGroupType.Enemies);
        Transform[] selected = EncounterMarkerOwnership.ExcludeOwned(enemies.SpawnPoints, configuration.EncounterRooms);
        Assert.That(selected, Is.SameAs(enemies.SpawnPoints));
        Assert.That(selected, Has.Length.EqualTo(64));
        Assert.That(EncounterMarkerOwnership.EffectiveGroupAmount(enemies.Amount, enemies.SpawnPoints.Length, selected.Length), Is.EqualTo(enemies.Amount));
    }
}
