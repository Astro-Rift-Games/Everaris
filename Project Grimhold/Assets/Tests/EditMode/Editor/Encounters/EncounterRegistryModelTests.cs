using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// TASK-330 T4 (S1, S2, S5): the authority-side registry rules over every room, through the slots the registry replicates.
/// </summary>
public sealed class EncounterRegistryModelTests
{
    private const double TickSeconds = 0.5d;

    private static EncounterRoomDefinition Room(int id, float cooldownSeconds = 2f, int maxReactivations = 1) =>
        new EncounterRoomDefinition(new EncounterId(id), new EncounterLifecycleSettings(cooldownSeconds, maxReactivations));

    private static EncounterRegistryModel Create(params EncounterRoomDefinition[] rooms)
    {
        Assert.That(EncounterRegistryModel.TryCreate(rooms, TickSeconds, out EncounterRegistryModel model, out string failure), Is.True, failure);
        return model;
    }

    private static EncounterNetworkSlot Capture(EncounterRegistryModel model, int index)
    {
        Assert.That(model.TryCapture(index, out EncounterNetworkSlot slot), Is.True);
        return slot;
    }

    private static EncounterState StateOf(EncounterRegistryModel model, int index)
    {
        Assert.That(EncounterSlotCodec.TryGetStatus(Capture(model, index), out EncounterStatus status), Is.True);
        return status.State;
    }

    // Creation, identity and capacity (S1)

    [Test]
    public void TryCreate_RoomsKeepTheirSceneOrderAsIndexAndAreAddressableById()
    {
        EncounterRegistryModel model = Create(Room(30), Room(10), Room(20));

        Assert.That(model.RoomCount, Is.EqualTo(3));
        Assert.That(model.IdOf(0), Is.EqualTo(new EncounterId(30)));
        Assert.That(model.IdOf(2), Is.EqualTo(new EncounterId(20)));
        Assert.That(model.TryGetRoomIndex(new EncounterId(10), out int index), Is.True);
        Assert.That(index, Is.EqualTo(1));
        Assert.That(model.TryGetRoomIndex(new EncounterId(99), out _), Is.False);
    }

    [Test]
    public void TryCreate_EveryRoomStartsNotActivatedAndQueryable()
    {
        EncounterRegistryModel model = Create(Room(1), Room(2));

        for (int i = 0; i < 2; i++)
        {
            Assert.That(EncounterSlotCodec.TryGetStatus(Capture(model, i), out EncounterStatus status), Is.True);
            Assert.That(status.Id, Is.EqualTo(model.IdOf(i)));
            Assert.That(status.State, Is.EqualTo(EncounterState.NotActivated));
        }

        Assert.That(model.IsCooling, Is.False);
    }

    [Test]
    public void TryCreate_ThirtyTwoRooms_AreAcceptedAndThirtyThreeRejected()
    {
        var rooms = new List<EncounterRoomDefinition>();
        for (int i = 1; i <= EncounterLimits.MaxRooms; i++) rooms.Add(Room(i));
        Assert.That(EncounterRegistryModel.TryCreate(rooms, TickSeconds, out _, out string ok), Is.True, ok);

        rooms.Add(Room(EncounterLimits.MaxRooms + 1));
        Assert.That(EncounterRegistryModel.TryCreate(rooms, TickSeconds, out EncounterRegistryModel model, out string failure), Is.False);
        Assert.That(model, Is.Null);
        Assert.That(failure, Does.Contain("33").And.Contain("32"));
    }

    [Test]
    public void TryCreate_DuplicateInvalidOrNullRoomsAndBadTickLength_AreRejected()
    {
        Assert.That(EncounterRegistryModel.TryCreate(new[] { Room(1), Room(1) }, TickSeconds, out _, out string duplicate), Is.False);
        Assert.That(duplicate, Does.Contain("1"));
        Assert.That(EncounterRegistryModel.TryCreate(new[] { default(EncounterRoomDefinition) }, TickSeconds, out _, out _), Is.False);
        Assert.That(EncounterRegistryModel.TryCreate(null, TickSeconds, out _, out _), Is.False);
        Assert.That(EncounterRegistryModel.TryCreate(new[] { Room(1) }, 0d, out _, out _), Is.False);
    }

    [Test]
    public void Matches_SameRoomsInTheSameOrder_IsTrue_AndAnyDifferenceIsFalse()
    {
        EncounterRegistryModel model = Create(Room(1), Room(2, cooldownSeconds: 5f));

        Assert.That(model.Matches(new[] { Room(1), Room(2, cooldownSeconds: 5f) }), Is.True);
        Assert.That(model.Matches(new[] { Room(2, cooldownSeconds: 5f), Room(1) }), Is.False);
        Assert.That(model.Matches(new[] { Room(1), Room(2, cooldownSeconds: 6f) }), Is.False);
        Assert.That(model.Matches(new[] { Room(1) }), Is.False);
    }

    // Activation (S1/S2)

    [Test]
    public void TryActivate_KnownRoom_BecomesActiveWithItsMemberKeys()
    {
        EncounterRegistryModel model = Create(Room(1), Room(2));

        Assert.That(model.TryActivate(new EncounterId(2), new uint[] { 1u, 2u, 3u }, out int index), Is.True);

        Assert.That(index, Is.EqualTo(1));
        Assert.That(StateOf(model, 1), Is.EqualTo(EncounterState.Active));
        Assert.That(Capture(model, 1).AliveMask, Is.EqualTo(0b111u));
        Assert.That(StateOf(model, 0), Is.EqualTo(EncounterState.NotActivated), "Other rooms are untouched.");
    }

    [Test]
    public void TryActivate_UnknownRoomKeysOutOfRangeOrRepeatedActivation_AreRejectedWithoutChange()
    {
        EncounterRegistryModel model = Create(Room(1));

        Assert.That(model.TryActivate(new EncounterId(9), new uint[] { 1u }, out _), Is.False);
        Assert.That(model.TryActivate(new EncounterId(1), new uint[] { 1u, 33u }, out _), Is.False);
        Assert.That(model.TryActivate(new EncounterId(1), new uint[] { 0u }, out _), Is.False);
        Assert.That(model.TryActivate(new EncounterId(1), new uint[0], out _), Is.False);
        Assert.That(StateOf(model, 0), Is.EqualTo(EncounterState.NotActivated));

        Assert.That(model.TryActivate(new EncounterId(1), new uint[] { 1u }, out _), Is.True);
        Assert.That(model.TryActivate(new EncounterId(1), new uint[] { 1u, 2u }, out _), Is.False, "An active encounter is not activated twice.");
        Assert.That(Capture(model, 0).AliveMask, Is.EqualTo(0b1u));
    }

    // Valid elimination and resolution (S2)

    [Test]
    public void RegisterValidElimination_PartialDeath_KeepsTheEncounterActive()
    {
        EncounterRegistryModel model = Create(Room(1));
        model.TryActivate(new EncounterId(1), new uint[] { 1u, 2u }, out _);

        Assert.That(model.RegisterValidElimination(new EncounterId(1), 1u, nowTick: 10, out int index), Is.True);

        Assert.That(index, Is.EqualTo(0));
        Assert.That(StateOf(model, 0), Is.EqualTo(EncounterState.Active));
        Assert.That(Capture(model, 0).AliveMask, Is.EqualTo(0b10u));
        Assert.That(model.IsCooling, Is.False);
    }

    [Test]
    public void RegisterValidElimination_LastDeath_ResolvesAndStartsTheCooldownAtThatTick()
    {
        EncounterRegistryModel model = Create(Room(1));
        model.TryActivate(new EncounterId(1), new uint[] { 1u, 2u }, out _);
        model.RegisterValidElimination(new EncounterId(1), 1u, 10, out _);

        Assert.That(model.RegisterValidElimination(new EncounterId(1), 2u, nowTick: 14, out _), Is.True);

        EncounterNetworkSlot slot = Capture(model, 0);
        Assert.That(slot.StateRaw, Is.EqualTo((byte)EncounterState.Resolved));
        Assert.That(slot.CooldownStartTick, Is.EqualTo(14));
        Assert.That(slot.AliveMask, Is.EqualTo(0u));
        Assert.That(slot.MemberMask, Is.EqualTo(0b11u), "The eliminated members still belong to the cycle.");
        Assert.That(model.IsCooling, Is.True);
    }

    [Test]
    public void RegisterValidElimination_IsIdempotentAndRejectsStrangers()
    {
        EncounterRegistryModel model = Create(Room(1), Room(2));
        model.TryActivate(new EncounterId(1), new uint[] { 1u, 2u }, out _);
        model.RegisterValidElimination(new EncounterId(1), 1u, 10, out _);
        EncounterNetworkSlot before = Capture(model, 0);

        Assert.That(model.RegisterValidElimination(new EncounterId(1), 1u, 11, out _), Is.False, "Repeated death.");
        Assert.That(model.RegisterValidElimination(new EncounterId(1), 9u, 11, out _), Is.False, "Not a member.");
        Assert.That(model.RegisterValidElimination(new EncounterId(77), 1u, 11, out _), Is.False, "Unknown room.");
        Assert.That(model.RegisterValidElimination(new EncounterId(2), 1u, 11, out _), Is.False, "Room that was never activated.");

        Assert.That(Capture(model, 0), Is.EqualTo(before));
        Assert.That(StateOf(model, 1), Is.EqualTo(EncounterState.NotActivated));
    }

    [Test]
    public void RegisterValidElimination_AfterResolution_DoesNotRestartTheCooldown()
    {
        EncounterRegistryModel model = Create(Room(1));
        model.TryActivate(new EncounterId(1), new uint[] { 1u }, out _);
        model.RegisterValidElimination(new EncounterId(1), 1u, 10, out _);

        Assert.That(model.RegisterValidElimination(new EncounterId(1), 1u, 50, out _), Is.False);

        Assert.That(Capture(model, 0).CooldownStartTick, Is.EqualTo(10));
    }

    // Cooldown on the registry clock (S2)

    [Test]
    public void Advance_ResolvedRoom_PassesThroughCooldownToReactivable_ReportingEachChange()
    {
        // Cooldown 2 s at 0.5 s per tick = 4 ticks.
        EncounterRegistryModel model = Create(Room(1, cooldownSeconds: 2f));
        model.TryActivate(new EncounterId(1), new uint[] { 1u }, out _);
        model.RegisterValidElimination(new EncounterId(1), 1u, 10, out _);
        var changed = new List<int>();

        model.Advance(11, changed);
        Assert.That(StateOf(model, 0), Is.EqualTo(EncounterState.Cooldown));
        Assert.That(changed, Is.EqualTo(new[] { 0 }));
        Assert.That(model.IsCooling, Is.True);

        changed.Clear();
        model.Advance(13, changed);
        Assert.That(StateOf(model, 0), Is.EqualTo(EncounterState.Cooldown), "Three ticks of four.");
        Assert.That(changed, Is.Empty);

        model.Advance(14, changed);
        Assert.That(StateOf(model, 0), Is.EqualTo(EncounterState.Reactivable));
        Assert.That(changed, Is.EqualTo(new[] { 0 }));
        Assert.That(model.IsCooling, Is.False);
    }

    [Test]
    public void Advance_TwoRoomsResolvedAtDifferentTicks_EachEndsAfterItsOwnCooldown()
    {
        // Cooldown 50 s at 0.5 s per tick = 100 ticks, counted from each room's own resolution.
        EncounterRegistryModel model = Create(Room(1, cooldownSeconds: 50f), Room(2, cooldownSeconds: 50f));
        model.TryActivate(new EncounterId(1), new uint[] { 1u }, out _);
        model.TryActivate(new EncounterId(2), new uint[] { 1u }, out _);
        model.RegisterValidElimination(new EncounterId(1), 1u, nowTick: 10, out _);
        model.RegisterValidElimination(new EncounterId(2), 1u, nowTick: 50, out _);
        var changed = new List<int>();

        model.Advance(109, changed);
        Assert.That(StateOf(model, 0), Is.EqualTo(EncounterState.Cooldown));
        Assert.That(StateOf(model, 1), Is.EqualTo(EncounterState.Cooldown));

        model.Advance(110, changed);
        Assert.That(StateOf(model, 0), Is.EqualTo(EncounterState.Reactivable), "A resolved at tick 10.");
        Assert.That(StateOf(model, 1), Is.EqualTo(EncounterState.Cooldown), "B has 60 of 100 ticks.");
        Assert.That(model.IsCooling, Is.True, "B still needs the clock.");

        model.Advance(149, changed);
        Assert.That(StateOf(model, 1), Is.EqualTo(EncounterState.Cooldown));
        model.Advance(150, changed);
        Assert.That(StateOf(model, 1), Is.EqualTo(EncounterState.Reactivable), "B resolved at tick 50.");
        Assert.That(model.IsCooling, Is.False);
    }

    [Test]
    public void Advance_ZeroCooldown_ReachesReactivableInOneStep()
    {
        EncounterRegistryModel model = Create(Room(1, cooldownSeconds: 0f));
        model.TryActivate(new EncounterId(1), new uint[] { 1u }, out _);
        model.RegisterValidElimination(new EncounterId(1), 1u, 5, out _);

        model.Advance(5, new List<int>());

        Assert.That(StateOf(model, 0), Is.EqualTo(EncounterState.Reactivable));
    }

    [Test]
    public void Advance_OnlyReportsRoomsThatChanged()
    {
        EncounterRegistryModel model = Create(Room(1), Room(2), Room(3));
        model.TryActivate(new EncounterId(1), new uint[] { 1u }, out _);
        model.TryActivate(new EncounterId(3), new uint[] { 1u }, out _);
        model.RegisterValidElimination(new EncounterId(3), 1u, 0, out _);
        var changed = new List<int>();

        model.Advance(1, changed);

        Assert.That(changed, Is.EqualTo(new[] { 2 }));
        Assert.That(StateOf(model, 0), Is.EqualTo(EncounterState.Active));
        Assert.That(StateOf(model, 1), Is.EqualTo(EncounterState.NotActivated));
    }

    // Host Migration: restoring from replicated slots (S5)

    [Test]
    public void TryRestoreRoom_FromTheSlotsOfAnotherModel_ContinuesTheCooldownOnTheSameClock()
    {
        EncounterRegistryModel original = Create(Room(1, cooldownSeconds: 2f), Room(2));
        original.TryActivate(new EncounterId(1), new uint[] { 1u, 2u }, out _);
        original.TryActivate(new EncounterId(2), new uint[] { 1u, 2u, 3u }, out _);
        original.RegisterValidElimination(new EncounterId(1), 1u, 100, out _);
        original.RegisterValidElimination(new EncounterId(1), 2u, 102, out _);
        original.RegisterValidElimination(new EncounterId(2), 2u, 103, out _);
        original.Advance(103, new List<int>());

        // The new Host builds a fresh model from the scene and adopts what the registry replicated.
        EncounterRegistryModel migrated = Create(Room(1, cooldownSeconds: 2f), Room(2));
        for (int i = 0; i < 2; i++)
        {
            Assert.That(migrated.TryRestoreRoom(i, Capture(original, i), out string failure), Is.True, failure);
        }

        Assert.That(StateOf(migrated, 0), Is.EqualTo(EncounterState.Cooldown));
        Assert.That(Capture(migrated, 0), Is.EqualTo(Capture(original, 0)));
        Assert.That(Capture(migrated, 1), Is.EqualTo(Capture(original, 1)));
        Assert.That(migrated.IsCooling, Is.True);

        migrated.Advance(105, new List<int>());
        Assert.That(StateOf(migrated, 0), Is.EqualTo(EncounterState.Cooldown), "Cooldown started on tick 102; 4 ticks end on 106.");
        migrated.Advance(106, new List<int>());
        Assert.That(StateOf(migrated, 0), Is.EqualTo(EncounterState.Reactivable));
        Assert.That(migrated.RegisterValidElimination(new EncounterId(2), 1u, 107, out _), Is.True, "The partially eliminated room keeps going.");
        Assert.That(migrated.RegisterValidElimination(new EncounterId(2), 2u, 107, out _), Is.False, "A restored elimination is not repeated.");
    }

    [Test]
    public void TryRestoreRoom_SlotOfAnotherRoomOrInconsistentSlot_IsRejectedAndTheRoomKeepsItsState()
    {
        EncounterRegistryModel model = Create(Room(1), Room(2));
        model.TryActivate(new EncounterId(1), new uint[] { 1u, 2u }, out _);
        EncounterNetworkSlot before = Capture(model, 0);

        EncounterNetworkSlot foreign = EncounterSlotCodec.Empty(new EncounterId(2));
        Assert.That(model.TryRestoreRoom(0, foreign, out string mismatch), Is.False);
        Assert.That(mismatch, Does.Contain("2").And.Contain("1"));

        var activeWithoutSurvivors = new EncounterNetworkSlot { EncounterId = 1, StateRaw = (byte)EncounterState.Active, MemberMask = 1u, AliveMask = 0u };
        Assert.That(model.TryRestoreRoom(0, activeWithoutSurvivors, out _), Is.False);
        Assert.That(model.TryRestoreRoom(0, default, out _), Is.False, "An unused slot is not a restorable room.");
        Assert.That(model.TryRestoreRoom(5, before, out _), Is.False, "Room index out of range.");

        Assert.That(Capture(model, 0), Is.EqualTo(before));
    }

    [Test]
    public void TryCapture_IndexOutOfRange_IsFalse()
    {
        EncounterRegistryModel model = Create(Room(1));

        Assert.That(model.TryCapture(1, out _), Is.False);
        Assert.That(model.TryCapture(-1, out _), Is.False);
    }


    // Reactivation (TASK-330 T5, S3)

    private static EncounterReactivationContext Open => EncounterReactivationRules.BuildContext(false, 10, 2, true);

    private static uint Key(int cycle, uint index)
    {
        Assert.That(EncounterMemberKeyCodec.TryCompose(cycle, index, out uint key), Is.True);
        return key;
    }

    /// <summary>A room resolved and cooled down at tick 10 (cooldown 0), with its first cycle's members 1 and 2.</summary>
    private static EncounterRegistryModel ReactivableRoom(int maxReactivations = 2)
    {
        EncounterRegistryModel model = Create(Room(1, cooldownSeconds: 0f, maxReactivations: maxReactivations));
        Assert.That(model.TryActivate(new EncounterId(1), new uint[] { 1u, 2u }, out _), Is.True);
        Assert.That(model.RegisterValidElimination(new EncounterId(1), 1u, 9, out _), Is.True);
        Assert.That(model.RegisterValidElimination(new EncounterId(1), 2u, 10, out _), Is.True);
        model.Advance(10, new List<int>());
        Assert.That(model.IsReactivable(0), Is.True);
        return model;
    }

    [Test]
    public void IsReactivable_FollowsTheLifecycleState()
    {
        EncounterRegistryModel model = Create(Room(1, cooldownSeconds: 0f));
        Assert.That(model.IsReactivable(0), Is.False);
        Assert.That(model.StateOf(0), Is.EqualTo(EncounterState.NotActivated));
        model.TryActivate(new EncounterId(1), new uint[] { 1u }, out _);
        Assert.That(model.IsReactivable(0), Is.False);
        model.RegisterValidElimination(new EncounterId(1), 1u, 4, out _);
        Assert.That(model.IsReactivable(0), Is.False, "Resolved is not Reactivable until the cooldown elapsed.");
        model.Advance(4, new List<int>());
        Assert.That(model.IsReactivable(0), Is.True);
        Assert.That(model.IsReactivable(5), Is.False, "Out of range.");
    }

    [Test]
    public void EvaluateReactivation_ReportsTheFirstBlockedCondition()
    {
        EncounterRegistryModel model = ReactivableRoom();

        Assert.That(model.EvaluateReactivation(new EncounterId(1), Open), Is.EqualTo(EncounterReactivationBlock.None));
        Assert.That(model.EvaluateReactivation(new EncounterId(1), EncounterReactivationRules.BuildContext(true, 10, 2, true)), Is.EqualTo(EncounterReactivationBlock.PlayersInGenerationZone));
        Assert.That(model.EvaluateReactivation(new EncounterId(1), EncounterReactivationRules.BuildContext(false, 1, 2, true)), Is.EqualTo(EncounterReactivationBlock.NoGlobalCapacity));
        Assert.That(model.EvaluateReactivation(new EncounterId(1), EncounterReactivationRules.BuildContext(false, 10, 2, false)), Is.EqualTo(EncounterReactivationBlock.DungeonPhaseForbidsGeneration));
        Assert.That(model.EvaluateReactivation(new EncounterId(99), Open), Is.EqualTo(EncounterReactivationBlock.NotResolved), "An unknown room is never reactivable.");
    }

    [Test]
    public void TryReactivate_StartsTheNextCycleWithTheNewMembers()
    {
        EncounterRegistryModel model = ReactivableRoom();

        Assert.That(model.TryReactivate(new EncounterId(1), Open, new uint[] { 1u, 2u, 3u }, out int index), Is.True);

        Assert.That(index, Is.EqualTo(0));
        EncounterNetworkSlot slot = Capture(model, 0);
        Assert.That(slot.StateRaw, Is.EqualTo((byte)EncounterState.Active));
        Assert.That(slot.ReactivationCount, Is.EqualTo(1));
        Assert.That(slot.MemberMask, Is.EqualTo(0b111u));
        Assert.That(slot.AliveMask, Is.EqualTo(0b111u));
        Assert.That(model.IsCooling, Is.False);
    }

    [Test]
    public void TryReactivate_BlockedContextOrBadKeys_ChangesNothingAndConsumesNoReactivation()
    {
        EncounterRegistryModel model = ReactivableRoom();
        EncounterNetworkSlot before = Capture(model, 0);

        Assert.That(model.TryReactivate(new EncounterId(1), EncounterReactivationRules.BuildContext(true, 10, 2, true), new uint[] { 1u }, out _), Is.False);
        Assert.That(model.TryReactivate(new EncounterId(1), Open, new uint[] { 1u, 33u }, out _), Is.False, "A key the slot cannot replicate.");
        Assert.That(model.TryReactivate(new EncounterId(1), Open, new uint[] { 0u }, out _), Is.False);
        Assert.That(model.TryReactivate(new EncounterId(1), Open, new uint[0], out _), Is.False);
        Assert.That(model.TryReactivate(new EncounterId(1), Open, null, out _), Is.False);
        Assert.That(model.TryReactivate(new EncounterId(77), Open, new uint[] { 1u }, out _), Is.False, "Unknown room.");

        Assert.That(Capture(model, 0), Is.EqualTo(before));
    }

    [Test]
    public void TryReactivate_AfterMaxReactivations_IsRefused()
    {
        EncounterRegistryModel model = ReactivableRoom(maxReactivations: 1);
        Assert.That(model.TryReactivate(new EncounterId(1), Open, new uint[] { 1u }, out _), Is.True);
        model.RegisterValidElimination(new EncounterId(1), Key(1, 1u), 20, out _);
        model.Advance(20, new List<int>());
        Assert.That(model.IsReactivable(0), Is.True);

        Assert.That(model.EvaluateReactivation(new EncounterId(1), Open), Is.EqualTo(EncounterReactivationBlock.MaxReactivationsReached));
        Assert.That(model.TryReactivate(new EncounterId(1), Open, new uint[] { 1u }, out _), Is.False);
        Assert.That(Capture(model, 0).ReactivationCount, Is.EqualTo(1));
    }

    [Test]
    public void TryReactivate_MaxReactivationsZero_NeverReactivates()
    {
        EncounterRegistryModel model = ReactivableRoom(maxReactivations: 0);
        Assert.That(model.TryReactivate(new EncounterId(1), Open, new uint[] { 1u }, out _), Is.False);
        Assert.That(StateOf(model, 0), Is.EqualTo(EncounterState.Reactivable));
    }

    [Test]
    public void RegisterValidElimination_DeathOfAnEarlierCycleBody_DoesNotTouchTheCurrentCycle()
    {
        EncounterRegistryModel model = ReactivableRoom();
        model.TryReactivate(new EncounterId(1), Open, new uint[] { 1u, 2u }, out _);
        EncounterNetworkSlot before = Capture(model, 0);

        // Member keys 1 and 2 of cycle 0 name bodies of the previous cycle, whose indices collide with the current members.
        Assert.That(model.RegisterValidElimination(new EncounterId(1), Key(0, 1u), 30, out _), Is.False);
        Assert.That(model.RegisterValidElimination(new EncounterId(1), Key(0, 2u), 30, out _), Is.False);

        Assert.That(Capture(model, 0), Is.EqualTo(before));
        Assert.That(StateOf(model, 0), Is.EqualTo(EncounterState.Active));
    }

    [Test]
    public void RegisterValidElimination_CurrentCycleKeys_ResolveTheReactivatedRoom()
    {
        EncounterRegistryModel model = ReactivableRoom();
        model.TryReactivate(new EncounterId(1), Open, new uint[] { 1u, 2u }, out _);

        Assert.That(model.RegisterValidElimination(new EncounterId(1), Key(1, 1u), 30, out _), Is.True);
        Assert.That(StateOf(model, 0), Is.EqualTo(EncounterState.Active));
        Assert.That(model.RegisterValidElimination(new EncounterId(1), Key(1, 2u), 31, out _), Is.True);

        Assert.That(StateOf(model, 0), Is.EqualTo(EncounterState.Resolved));
        Assert.That(Capture(model, 0).CooldownStartTick, Is.EqualTo(31));
    }

    [Test]
    public void RegisterValidElimination_KeyOfALaterCycleOrWithoutAMember_IsRejected()
    {
        EncounterRegistryModel model = ReactivableRoom();
        model.TryReactivate(new EncounterId(1), Open, new uint[] { 1u }, out _);

        Assert.That(model.RegisterValidElimination(new EncounterId(1), Key(2, 1u), 30, out _), Is.False, "A cycle that has not started.");
        Assert.That(model.RegisterValidElimination(new EncounterId(1), 1u << EncounterMemberKeyCodec.IndexBits, 30, out _), Is.False, "No member index.");
        Assert.That(StateOf(model, 0), Is.EqualTo(EncounterState.Active));
    }
}
