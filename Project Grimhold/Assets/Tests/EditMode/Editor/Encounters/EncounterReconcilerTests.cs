using System.Collections.Generic;
using NUnit.Framework;

/// <summary>TASK-330 T4 (S5): comparison of the restored registry with the restored enemies after a Host Migration.</summary>
public sealed class EncounterReconcilerTests
{
    private static EncounterNetworkSlot Active(int id, uint aliveMask, uint memberMask = 0u) => new EncounterNetworkSlot
    {
        EncounterId = id,
        StateRaw = (byte)EncounterState.Active,
        MemberMask = memberMask | aliveMask,
        AliveMask = aliveMask,
    };

    private static EncounterNetworkSlot Resolved(int id, uint memberMask) => new EncounterNetworkSlot
    {
        EncounterId = id,
        StateRaw = (byte)EncounterState.Resolved,
        MemberMask = memberMask,
        AliveMask = 0u,
    };

    private static EncounterMemberObservation Alive(int id, uint key) => new EncounterMemberObservation(id, key, true);

    private static EncounterMemberObservation Dead(int id, uint key) => new EncounterMemberObservation(id, key, false);

    private static EncounterReconcileResult Compare(EncounterNetworkSlot[] slots, params EncounterMemberObservation[] observed) =>
        EncounterReconciler.Compare(slots, observed);

    [Test]
    public void Compare_EveryAliveMemberHasItsLivingEnemy_IsConsistent()
    {
        EncounterReconcileResult result = Compare(
            new[] { Active(1, 0b11u), default, Resolved(2, 0b1u) },
            Alive(1, 1u), Alive(1, 2u), Dead(2, 1u));

        Assert.That(result.IsConsistent, Is.True);
        Assert.That(result.Log, Does.Contain("successful"));
    }

    [Test]
    public void Compare_EliminatedMemberRestoredDead_IsConsistent()
    {
        EncounterReconcileResult result = Compare(
            new[] { Active(1, aliveMask: 0b10u, memberMask: 0b01u) },
            Dead(1, 1u), Alive(1, 2u));

        Assert.That(result.IsConsistent, Is.True);
    }

    [Test]
    public void Compare_NoEncountersAndNoEnemies_IsConsistent()
    {
        Assert.That(Compare(new EncounterNetworkSlot[3]).IsConsistent, Is.True);
        Assert.That(EncounterReconciler.Compare(null, null).IsConsistent, Is.True);
    }

    [Test]
    public void Compare_AliveInTheRegistryButRestoredDead_IsAnUnrecordedDeath()
    {
        EncounterReconcileResult result = Compare(new[] { Active(4, 0b11u) }, Alive(4, 1u), Dead(4, 2u));

        Assert.That(result.IsConsistent, Is.False);
        Assert.That(result.UnrecordedDeaths, Has.Count.EqualTo(1));
        Assert.That(result.UnrecordedDeaths[0].EncounterId, Is.EqualTo(4));
        Assert.That(result.UnrecordedDeaths[0].MemberKey, Is.EqualTo(2u));
        Assert.That(result.MissingMembers, Is.Empty);
        Assert.That(result.UnexpectedLiving, Is.Empty);
        Assert.That(result.Log, Does.Contain("mismatch").And.Contain("Encounter#4").And.Contain("2"));
    }

    [Test]
    public void Compare_AliveInTheRegistryWithNoRestoredEnemy_IsAMissingMember()
    {
        EncounterReconcileResult result = Compare(new[] { Active(4, 0b101u) }, Alive(4, 1u));

        Assert.That(result.MissingMembers, Has.Count.EqualTo(1));
        Assert.That(result.MissingMembers[0].EncounterId, Is.EqualTo(4));
        Assert.That(result.MissingMembers[0].MemberKey, Is.EqualTo(3u));
        Assert.That(result.UnrecordedDeaths, Is.Empty);
        Assert.That(result.Log, Does.Contain("Encounter#4"));
    }

    [Test]
    public void Compare_LivingEnemyOfUnknownOrInactiveRoom_IsUnexpected()
    {
        EncounterReconcileResult result = Compare(
            new[] { Resolved(2, 0b1u), Active(3, 0b1u) },
            Alive(9, 1u), Alive(2, 1u), Alive(3, 1u));

        Assert.That(result.UnexpectedLiving, Has.Count.EqualTo(2));
        Assert.That(result.UnexpectedLiving[0].EncounterId, Is.EqualTo(9));
        Assert.That(result.UnexpectedLiving[1].EncounterId, Is.EqualTo(2));
        Assert.That(result.UnrecordedDeaths, Is.Empty);
    }

    [Test]
    public void Compare_LivingEnemyWhoseKeyIsNotAliveInTheRegistry_IsUnexpected()
    {
        EncounterReconcileResult result = Compare(new[] { Active(3, aliveMask: 0b1u, memberMask: 0b10u) }, Alive(3, 1u), Alive(3, 2u), Alive(3, 40u));

        Assert.That(result.UnexpectedLiving, Has.Count.EqualTo(2));
        Assert.That(result.UnexpectedLiving[0].MemberKey, Is.EqualTo(2u));
        Assert.That(result.UnexpectedLiving[1].MemberKey, Is.EqualTo(40u), "A key outside the mask is never alive in the registry.");
    }

    [Test]
    public void Compare_DeadEnemyOfUnknownRoom_IsIgnored()
    {
        Assert.That(Compare(new[] { Active(3, 0b1u) }, Alive(3, 1u), Dead(8, 1u)).IsConsistent, Is.True);
    }

    [Test]
    public void Compare_ReportsEveryDiscrepancyKindTogether()
    {
        EncounterReconcileResult result = Compare(
            new[] { Active(1, 0b111u) },
            Dead(1, 1u), Alive(1, 2u), Alive(5, 1u));

        Assert.That(result.UnrecordedDeaths, Has.Count.EqualTo(1));
        Assert.That(result.MissingMembers, Has.Count.EqualTo(1));
        Assert.That(result.UnexpectedLiving, Has.Count.EqualTo(1));
        Assert.That(result.Log, Does.Contain("unrecorded deaths=1").And.Contain("missing members=1").And.Contain("unexpected living=1"));
    }

    [Test]
    public void Compare_DoesNotModifyItsInputs()
    {
        EncounterNetworkSlot[] slots = { Active(1, 0b11u) };
        EncounterMemberObservation[] observed = { Dead(1, 1u) };
        EncounterNetworkSlot before = slots[0];

        EncounterReconciler.Compare(slots, observed);

        Assert.That(slots[0], Is.EqualTo(before));
        Assert.That(observed, Has.Length.EqualTo(1));
    }

    [Test]
    public void Compare_LongDiscrepancyLists_AreCappedInTheLogButNotInTheResult()
    {
        var observed = new List<EncounterMemberObservation>();
        for (uint key = 1; key <= 32; key++) observed.Add(Dead(1, key));

        EncounterReconcileResult result = EncounterReconciler.Compare(new[] { Active(1, uint.MaxValue) }, observed);

        Assert.That(result.UnrecordedDeaths, Has.Count.EqualTo(32));
        Assert.That(result.Log.Length, Is.LessThan(600));
        Assert.That(result.Log, Does.Contain("unrecorded deaths=32"));
    }


    // Reactivation cycles (TASK-330 T5, S3/S5): the cycle travels in the member key, bodies of earlier cycles are not members.

    private static EncounterNetworkSlot ActiveInCycle(int id, int cycle, uint aliveMask) => new EncounterNetworkSlot
    {
        EncounterId = id,
        StateRaw = (byte)EncounterState.Active,
        ReactivationCount = cycle,
        MemberMask = aliveMask,
        AliveMask = aliveMask,
    };

    private static uint Key(int cycle, uint index)
    {
        Assert.That(EncounterMemberKeyCodec.TryCompose(cycle, index, out uint key), Is.True);
        return key;
    }

    [Test]
    public void Compare_CurrentCycleKeys_MatchTheRegistryByMemberIndex()
    {
        EncounterReconcileResult result = Compare(
            new[] { ActiveInCycle(4, 2, 0b11u) },
            Alive(4, Key(2, 1u)), Alive(4, Key(2, 2u)));

        Assert.That(result.IsConsistent, Is.True, result.Log);
    }

    [Test]
    public void Compare_DeadBodiesOfEarlierCycles_AreIgnoredAndNeverRecordedAsDeaths()
    {
        // Cycle 1 is current: member 1 and 2 alive. The two bodies of cycle 0 carry the same indices and are dead.
        EncounterReconcileResult result = Compare(
            new[] { ActiveInCycle(4, 1, 0b11u) },
            Dead(4, Key(0, 1u)), Dead(4, Key(0, 2u)), Alive(4, Key(1, 1u)), Alive(4, Key(1, 2u)));

        Assert.That(result.IsConsistent, Is.True, result.Log);
        Assert.That(result.UnrecordedDeaths, Is.Empty, "An old body is not the death of the current member with the same index.");
        Assert.That(result.IgnoredOtherCycle, Is.EqualTo(2));
        Assert.That(result.Log, Does.Contain("successful").And.Contain("2"));
    }

    [Test]
    public void Compare_OldCycleBodyMustNotHideAMissingCurrentMember()
    {
        EncounterReconcileResult result = Compare(
            new[] { ActiveInCycle(4, 1, 0b11u) },
            Dead(4, Key(0, 1u)), Dead(4, Key(0, 2u)), Alive(4, Key(1, 1u)));

        Assert.That(result.MissingMembers, Has.Count.EqualTo(1));
        Assert.That(result.MissingMembers[0].MemberKey, Is.EqualTo(Key(1, 2u)), "Reported with the cycle-aware key.");
    }

    [Test]
    public void Compare_CurrentMemberRestoredDead_IsAnUnrecordedDeathWithItsCycleAwareKey()
    {
        EncounterReconcileResult result = Compare(
            new[] { ActiveInCycle(4, 1, 0b11u) },
            Dead(4, Key(1, 1u)), Alive(4, Key(1, 2u)), Dead(4, Key(0, 2u)));

        Assert.That(result.UnrecordedDeaths, Has.Count.EqualTo(1));
        Assert.That(result.UnrecordedDeaths[0].MemberKey, Is.EqualTo(Key(1, 1u)));
        Assert.That(result.IgnoredOtherCycle, Is.EqualTo(1));
    }

    [Test]
    public void Compare_LivingBodyOfAnEarlierCycle_IsIgnoredNotUnexpected()
    {
        EncounterReconcileResult result = Compare(new[] { ActiveInCycle(4, 1, 0b1u) }, Alive(4, Key(1, 1u)), Alive(4, Key(0, 1u)));

        Assert.That(result.UnexpectedLiving, Is.Empty);
        Assert.That(result.IgnoredOtherCycle, Is.EqualTo(1));
    }

    [Test]
    public void Compare_FirstCycle_KeepsTheLogOfTheFirstCycleUnchanged()
    {
        EncounterReconcileResult result = Compare(new[] { Active(1, 0b1u) }, Alive(1, 1u));

        Assert.That(result.IgnoredOtherCycle, Is.EqualTo(0));
        Assert.That(result.Log, Is.EqualTo("Host Migration encounter reconciliation successful. Encounters: 1, living members: 1."));
    }
}
