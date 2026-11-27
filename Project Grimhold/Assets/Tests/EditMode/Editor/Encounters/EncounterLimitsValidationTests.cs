using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>TASK-330 T4 (S1, S5): authored data cannot exceed the replicated capacity of the Encounter registry.</summary>
public sealed class EncounterLimitsValidationTests
{
    private readonly EncounterRoomFactory _factory = new EncounterRoomFactory();

    [TearDown]
    public void TearDown() => _factory.Destroy();

    private EncounterRoom RoomWithMax(int id, int maxEnemies, int spawnPoints)
    {
        EncounterRoom room = _factory.Room(id, _factory.Markers(spawnPoints, "R" + id), min: 1, max: maxEnemies);
        return room;
    }

    [Test]
    public void Validate_MaxEnemiesAtTheMemberCap_Passes()
    {
        EncounterRoom room = RoomWithMax(1, EncounterLimits.MaxMembersPerRoom, EncounterLimits.MaxMembersPerRoom);

        Assert.That(room.Validate(out string failure), Is.True, failure);
    }

    [Test]
    public void Validate_MaxEnemiesAboveTheMemberCap_FailsNamingTheRoomAndTheCap()
    {
        EncounterRoom room = RoomWithMax(1, EncounterLimits.MaxMembersPerRoom + 1, 40);

        Assert.That(room.Validate(out string failure), Is.False);

        Assert.That(failure, Does.Contain("Room_1"));
        Assert.That(failure, Does.Contain("MaxEnemies (33)"));
        Assert.That(failure, Does.Contain(EncounterLimits.MaxMembersPerRoom.ToString()));
    }

    [Test]
    public void ValidateSet_MaxRoomsRooms_Passes()
    {
        List<EncounterRoom> rooms = Rooms(EncounterLimits.MaxRooms);

        Assert.That(EncounterRoomSetValidation.Validate(rooms, out string failure), Is.True, failure);
    }

    [Test]
    public void ValidateSet_MoreThanMaxRooms_FailsWithTheCount()
    {
        List<EncounterRoom> rooms = Rooms(EncounterLimits.MaxRooms + 1);

        Assert.That(EncounterRoomSetValidation.Validate(rooms, out string failure), Is.False);

        Assert.That(failure, Does.Contain("33"));
        Assert.That(failure, Does.Contain(EncounterLimits.MaxRooms.ToString()));
    }

    private List<EncounterRoom> Rooms(int count)
    {
        var rooms = new List<EncounterRoom>();
        for (int i = 1; i <= count; i++)
        {
            rooms.Add(_factory.Room(i, _factory.Markers(1, "S" + i)));
        }

        return rooms;
    }
}
