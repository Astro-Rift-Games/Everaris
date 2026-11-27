using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class EncounterRoomValidationTests
{
    private readonly EncounterRoomFactory _factory = new EncounterRoomFactory();

    [TearDown]
    public void TearDown() => _factory.Destroy();

    private EncounterRoom ValidRoom(int id = 1) => _factory.Room(id, _factory.Markers(3, "Room" + id), min: 1, max: 2);

    private static void AssertRejected(EncounterRoom room, string expectedFragment)
    {
        Assert.That(room.Validate(out string failure), Is.False);
        Assert.That(failure, Does.Contain(expectedFragment));
    }

    private static string TooltipOf(string field)
    {
        FieldInfo info = typeof(EncounterRoom).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
        var attribute = (TooltipAttribute)System.Attribute.GetCustomAttribute(info, typeof(TooltipAttribute));
        return attribute?.tooltip ?? string.Empty;
    }

    [Test]
    public void Validate_ValidRoom_PassesWithEmptyFailure()
    {
        Assert.That(ValidRoom(4).Validate(out string failure), Is.True, failure);
        Assert.That(failure, Is.Empty);
    }

    [Test]
    public void Defaults_AreNeutral_NeverReactivateAndMarkedPendingBalance()
    {
        var go = new GameObject("DefaultRoom");
        try
        {
            var room = go.AddComponent<EncounterRoom>();
            Assert.That(room.LifecycleSettings.MaxReactivations, Is.EqualTo(0));
            Assert.That(room.LifecycleSettings.CooldownSeconds, Is.GreaterThanOrEqualTo(0f));
            Assert.That(room.Id.IsValid, Is.False, "A room has no implicit identity until it is authored.");
            Assert.That(TooltipOf("_cooldownSeconds"), Does.Contain("non-GD default, pending Balance"));
            Assert.That(TooltipOf("_maxReactivations"), Does.Contain("non-GD default, pending Balance"));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void Id_ExposesTheAuthoredPositiveValue()
    {
        Assert.That(ValidRoom(9).Id, Is.EqualTo(new EncounterId(9)));
    }

    [Test]
    public void LifecycleSettings_ExposeAuthoredBalanceParameters()
    {
        EncounterRoom room = ValidRoom();
        EncounterRoomFactory.Set(room, "_cooldownSeconds", 45f);
        EncounterRoomFactory.Set(room, "_maxReactivations", 2);
        Assert.That(room.LifecycleSettings.CooldownSeconds, Is.EqualTo(45f));
        Assert.That(room.LifecycleSettings.MaxReactivations, Is.EqualTo(2));
    }

    [Test]
    public void Validate_RejectsInvalidEncounterId()
    {
        EncounterRoom room = ValidRoom();
        EncounterRoomFactory.Set(room, "_encounterId", 0);
        AssertRejected(room, "EncounterId");
        EncounterRoomFactory.Set(room, "_encounterId", -3);
        AssertRejected(room, "EncounterId");
    }

    [Test]
    public void Validate_RejectsMissingGenerationZone()
    {
        EncounterRoom room = _factory.Room(1, _factory.Markers(2), withZone: false);
        AssertRejected(room, "generation zone");
    }

    [Test]
    public void Validate_RejectsNullSpawnPoint()
    {
        EncounterRoom room = ValidRoom();
        EncounterRoomFactory.SetPoint(room, 1, null);
        AssertRejected(room, "null spawn point");
    }

    [Test]
    public void Validate_RejectsDuplicateSpawnPoint()
    {
        Transform[] markers = _factory.Markers(3);
        EncounterRoom room = _factory.Room(1, new[] { markers[0], markers[1], markers[0] });
        AssertRejected(room, "duplicate spawn point");
    }

    [Test]
    public void Validate_RejectsRoomWithoutSpawnPoints()
    {
        EncounterRoom room = _factory.Room(1, System.Array.Empty<Transform>());
        AssertRejected(room, "no spawn points");
    }

    [Test]
    public void Validate_RejectsMinGreaterThanMax()
    {
        EncounterRoom room = _factory.Room(1, _factory.Markers(4), min: 3, max: 2);
        AssertRejected(room, "MinEnemies");
    }

    [Test]
    public void Validate_RejectsMinEnemiesAboveSpawnPointCapacity()
    {
        EncounterRoom room = _factory.Room(1, _factory.Markers(2), min: 3, max: 4);
        AssertRejected(room, "spawn points");
    }

    [Test]
    public void Validate_AcceptsMinEnemiesEqualToSpawnPointCapacity()
    {
        EncounterRoom room = _factory.Room(1, _factory.Markers(3), min: 3, max: 4);
        Assert.That(room.Validate(out string failure), Is.True, failure);
    }

    [Test]
    public void Validate_RejectsMinBelowOne()
    {
        EncounterRoom room = _factory.Room(1, _factory.Markers(4), min: 0, max: 2);
        AssertRejected(room, "MinEnemies");
    }

    [Test]
    public void Validate_RejectsEncounterChanceOutsideUnitRange()
    {
        EncounterRoom room = ValidRoom();
        EncounterRoomFactory.Set(room, "_composition._encounterChance", float.NaN);
        AssertRejected(room, "EncounterChance");
    }

    [Test]
    public void Validate_RejectsInvalidBalanceParameters()
    {
        EncounterRoom room = ValidRoom();
        EncounterRoomFactory.Set(room, "_cooldownSeconds", -1f);
        AssertRejected(room, "cooldown");
        EncounterRoomFactory.Set(room, "_cooldownSeconds", 10f);
        EncounterRoomFactory.Set(room, "_maxReactivations", -1);
        AssertRejected(room, "MaxReactivations");
    }

    [Test]
    public void Validate_FailureNamesTheRoom()
    {
        EncounterRoom room = ValidRoom(12);
        EncounterRoomFactory.Set(room, "_encounterId", 0);
        room.Validate(out string failure);
        Assert.That(failure, Does.Contain(room.name));
    }
}
