using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>TASK-330 T5 (S3): "no hay jugadores dentro de la zona de generación".</summary>
public sealed class EncounterZoneOccupancyTests
{
    private GameObject _zoneObject;
    private BoxCollider2D _zone;

    [SetUp]
    public void SetUp()
    {
        _zoneObject = new GameObject("Zone");
        _zoneObject.transform.position = new Vector3(10f, 0f, 0f);
        _zone = _zoneObject.AddComponent<BoxCollider2D>();
        _zone.size = new Vector2(4f, 4f);
        Physics2D.SyncTransforms();
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_zoneObject);

    private static List<EncounterPlayerPresence> Players(params EncounterPlayerPresence[] players) => new List<EncounterPlayerPresence>(players);

    private static EncounterPlayerPresence At(float x, float y, bool alive = true) => new EncounterPlayerPresence(new Vector2(x, y), alive);

    [Test]
    public void HasPlayerInside_PlayerInsideTheZone_IsOccupied()
    {
        Assert.That(EncounterZoneOccupancy.HasPlayerInside(_zone, Players(At(10f, 1f))), Is.True);
    }

    [Test]
    public void HasPlayerInside_PlayersOnlyOutsideTheZone_IsFree()
    {
        Assert.That(EncounterZoneOccupancy.HasPlayerInside(_zone, Players(At(0f, 0f), At(13f, 0f), At(10f, 3f))), Is.False);
    }

    [Test]
    public void HasPlayerInside_OneOfSeveralPlayersInside_IsOccupied()
    {
        Assert.That(EncounterZoneOccupancy.HasPlayerInside(_zone, Players(At(0f, 0f), At(9f, -1f), At(30f, 5f))), Is.True);
    }

    [Test]
    public void HasPlayerInside_DeadAvatarInsideTheZone_DoesNotCount()
    {
        Assert.That(EncounterZoneOccupancy.HasPlayerInside(_zone, Players(At(10f, 0f, alive: false))), Is.False);
    }

    [Test]
    public void HasPlayerInside_DeadAvatarAndLivingPlayerInside_IsOccupied()
    {
        Assert.That(EncounterZoneOccupancy.HasPlayerInside(_zone, Players(At(10f, 0f, alive: false), At(11f, 0f))), Is.True);
    }

    [Test]
    public void HasPlayerInside_NoPlayers_IsFree()
    {
        Assert.That(EncounterZoneOccupancy.HasPlayerInside(_zone, Players()), Is.False);
    }

    [Test]
    public void HasPlayerInside_NullOrDisabledZoneOrNoList_IsNeverOccupied()
    {
        Assert.That(EncounterZoneOccupancy.HasPlayerInside(null, Players(At(10f, 0f))), Is.False);
        Assert.That(EncounterZoneOccupancy.HasPlayerInside(_zone, null), Is.False);
        _zone.enabled = false;
        Assert.That(EncounterZoneOccupancy.HasPlayerInside(_zone, Players(At(10f, 0f))), Is.False);
    }
}
