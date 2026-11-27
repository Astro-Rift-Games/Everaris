using NUnit.Framework;
using UnityEngine;

public sealed class EncounterMarkerOwnershipTests
{
    private readonly EncounterRoomFactory _factory = new EncounterRoomFactory();

    [TearDown]
    public void TearDown() => _factory.Destroy();

    [Test]
    public void ExcludeOwned_NoRooms_ReturnsTheSameGroupArray()
    {
        Transform[] group = _factory.Markers(6);
        Assert.That(EncounterMarkerOwnership.ExcludeOwned(group, null), Is.SameAs(group));
        Assert.That(EncounterMarkerOwnership.ExcludeOwned(group, System.Array.Empty<EncounterRoom>()), Is.SameAs(group));
    }

    [Test]
    public void ExcludeOwned_RoomOwnsNoGroupMarker_ReturnsTheSameGroupArray()
    {
        Transform[] group = _factory.Markers(4);
        EncounterRoom room = _factory.Room(1, _factory.Markers(2, "Other"));
        Assert.That(EncounterMarkerOwnership.ExcludeOwned(group, new[] { room }), Is.SameAs(group));
    }

    [Test]
    public void ExcludeOwned_RemovesMarkersOwnedByAnyRoomAndKeepsTheRestInOrder()
    {
        Transform[] group = _factory.Markers(6);
        EncounterRoom first = _factory.Room(1, new[] { group[1], group[2] });
        EncounterRoom second = _factory.Room(2, new[] { group[4] });
        Transform[] remaining = EncounterMarkerOwnership.ExcludeOwned(group, new[] { first, second });
        Assert.That(remaining, Is.EqualTo(new[] { group[0], group[3], group[5] }));
        Assert.That(group, Has.Length.EqualTo(6), "The authored group array is not mutated.");
    }

    [Test]
    public void ExcludeOwned_NullGroup_ReturnsNull()
    {
        Assert.That(EncounterMarkerOwnership.ExcludeOwned(null, System.Array.Empty<EncounterRoom>()), Is.Null);
    }

    [Test]
    public void ExcludeOwned_IgnoresNullRoomsAndNullMarkers()
    {
        Transform[] group = _factory.Markers(3);
        EncounterRoom room = _factory.Room(1, new[] { group[0], group[1] });
        EncounterRoomFactory.SetPoint(room, 1, null);
        Assert.That(EncounterMarkerOwnership.ExcludeOwned(group, new[] { null, room }), Is.EqualTo(new[] { group[1], group[2] }));
    }

    [Test]
    public void EffectiveGroupAmount_NothingExcluded_KeepsTheConfiguredAmountUnchanged()
    {
        Assert.That(EncounterMarkerOwnership.EffectiveGroupAmount(64, 64, 64), Is.EqualTo(64));
        Assert.That(EncounterMarkerOwnership.EffectiveGroupAmount(70, 64, 64), Is.EqualTo(70), "Existing over-configuration behavior is preserved.");
    }

    [Test]
    public void EffectiveGroupAmount_MarkersExcluded_NeverExceedsTheRemainingMarkers()
    {
        Assert.That(EncounterMarkerOwnership.EffectiveGroupAmount(64, 64, 50), Is.EqualTo(50));
        Assert.That(EncounterMarkerOwnership.EffectiveGroupAmount(10, 64, 50), Is.EqualTo(10));
        Assert.That(EncounterMarkerOwnership.EffectiveGroupAmount(64, 64, 0), Is.EqualTo(0));
    }
}
