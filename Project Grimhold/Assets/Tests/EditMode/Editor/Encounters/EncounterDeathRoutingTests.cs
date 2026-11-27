using NUnit.Framework;

/// <summary>TASK-330 T4 (S2): which enemy deaths are reported to the Encounter registry.</summary>
public sealed class EncounterDeathRoutingTests
{
    [Test]
    public void Resolve_EncounterEnemy_NotifiesWithItsId()
    {
        Assert.That(EncounterDeathRouting.Resolve(5, 3u, out EncounterId id), Is.EqualTo(EncounterDeathRoute.Notify));
        Assert.That(id, Is.EqualTo(new EncounterId(5)));
    }

    [TestCase(0, 0u)]
    [TestCase(0, 4u)]
    [TestCase(-1, 4u)]
    public void Resolve_EnemyWithoutEncounter_IsNotAMember(int encounterIdValue, uint memberKey)
    {
        Assert.That(EncounterDeathRouting.Resolve(encounterIdValue, memberKey, out EncounterId id), Is.EqualTo(EncounterDeathRoute.NotEncounterMember));
        Assert.That(id.IsValid, Is.False);
    }

    [Test]
    public void Resolve_EncounterEnemyWithoutMemberKey_IsInvalid()
    {
        Assert.That(EncounterDeathRouting.Resolve(5, 0u, out EncounterId id), Is.EqualTo(EncounterDeathRoute.InvalidMember));
        Assert.That(id.IsValid, Is.False);
    }
}
