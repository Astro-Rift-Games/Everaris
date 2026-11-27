using NUnit.Framework;

/// <summary>TASK-330 T5 (S3, S5): the cycle carried inside the existing member key, so old bodies cannot be confused with the current cycle.</summary>
public sealed class EncounterMemberKeyCodecTests
{
    [Test]
    public void TryCompose_CycleZero_IsThePlainMemberIndex()
    {
        for (uint index = 1; index <= EncounterLimits.MaxMembersPerRoom; index++)
        {
            Assert.That(EncounterMemberKeyCodec.TryCompose(0, index, out uint key), Is.True);
            Assert.That(key, Is.EqualTo(index), "First-cycle keys must stay exactly what T3/T4 produced.");
        }
    }

    [Test]
    public void TryCompose_PutsTheCycleAboveTheIndexBits()
    {
        Assert.That(EncounterMemberKeyCodec.TryCompose(3, 7u, out uint key), Is.True);
        Assert.That(key, Is.EqualTo((3u << EncounterMemberKeyCodec.IndexBits) | 7u));
    }

    [Test]
    public void TryDecode_RoundTripsEveryCycleAndIndex()
    {
        foreach (int cycle in new[] { 0, 1, 2, 255, EncounterMemberKeyCodec.MaxCycle })
        {
            for (uint index = 1; index <= EncounterLimits.MaxMembersPerRoom; index++)
            {
                Assert.That(EncounterMemberKeyCodec.TryCompose(cycle, index, out uint key), Is.True, $"cycle {cycle} index {index}");
                Assert.That(EncounterMemberKeyCodec.TryDecode(key, out int decodedCycle, out uint decodedIndex), Is.True);
                Assert.That(decodedCycle, Is.EqualTo(cycle));
                Assert.That(decodedIndex, Is.EqualTo(index));
            }
        }
    }

    [Test]
    public void TryCompose_SameIndexInDifferentCycles_GivesDifferentKeys()
    {
        EncounterMemberKeyCodec.TryCompose(0, 1u, out uint first);
        EncounterMemberKeyCodec.TryCompose(1, 1u, out uint second);
        Assert.That(second, Is.Not.EqualTo(first));
    }

    [Test]
    public void TryCompose_RejectsIndexOrCycleOutOfRange()
    {
        Assert.That(EncounterMemberKeyCodec.TryCompose(0, 0u, out uint zero), Is.False);
        Assert.That(zero, Is.EqualTo(0u));
        Assert.That(EncounterMemberKeyCodec.TryCompose(0, EncounterLimits.MaxMembersPerRoom + 1u, out _), Is.False);
        Assert.That(EncounterMemberKeyCodec.TryCompose(-1, 1u, out _), Is.False);
        Assert.That(EncounterMemberKeyCodec.TryCompose(EncounterMemberKeyCodec.MaxCycle + 1, 1u, out _), Is.False);
    }

    [Test]
    public void TryDecode_RejectsAKeyThatNamesNoMember()
    {
        Assert.That(EncounterMemberKeyCodec.TryDecode(0u, out _, out _), Is.False);
        Assert.That(EncounterMemberKeyCodec.TryDecode(1u << EncounterMemberKeyCodec.IndexBits, out _, out _), Is.False, "A cycle without an index is no member.");
    }

    [Test]
    public void MaxCycle_FitsTheKeyAndTheRoomValidationBound()
    {
        Assert.That(EncounterMemberKeyCodec.TryCompose(EncounterMemberKeyCodec.MaxCycle, EncounterLimits.MaxMembersPerRoom, out uint key), Is.True);
        Assert.That(key, Is.GreaterThan(0u));
        Assert.That(EncounterLimits.MaxMembersPerRoom, Is.LessThanOrEqualTo((int)EncounterMemberKeyCodec.IndexMask));
    }
}
