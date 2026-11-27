using NUnit.Framework;

/// <summary>TASK-330 T4 (S1, S5): the replicated slot layout, its bitmasks and bounds.</summary>
public sealed class EncounterSlotCodecTests
{
    private const double TickSeconds = 0.02d;
    private static readonly EncounterId Room = new EncounterId(7);

    private static EncounterLifecycleSnapshot Snapshot(
        EncounterState state, int reactivations = 0, double cooldownStart = 0d, uint[] members = null, uint[] alive = null) =>
        new EncounterLifecycleSnapshot(state, reactivations, cooldownStart, members ?? new uint[0], alive ?? new uint[0]);

    [TestCase(1u, 0x00000001u)]
    [TestCase(2u, 0x00000002u)]
    [TestCase(32u, 0x80000000u)]
    public void TryKeyToBit_KeysInRange_MapToBitKeyMinusOne(uint key, uint expected)
    {
        Assert.That(EncounterSlotCodec.TryKeyToBit(key, out uint bit), Is.True);
        Assert.That(bit, Is.EqualTo(expected));
    }

    [TestCase(0u)]
    [TestCase(33u)]
    [TestCase(uint.MaxValue)]
    public void TryKeyToBit_KeysOutOfRange_AreRejected(uint key)
    {
        Assert.That(EncounterSlotCodec.TryKeyToBit(key, out uint bit), Is.False);
        Assert.That(bit, Is.EqualTo(0u));
    }

    [Test]
    public void Limits_MatchTheMaskWidth()
    {
        Assert.That(EncounterLimits.MaxMembersPerRoom, Is.EqualTo(32));
        Assert.That(EncounterLimits.MaxRooms, Is.EqualTo(32));
    }

    [Test]
    public void Empty_IsInUseAndNotActivated()
    {
        EncounterNetworkSlot slot = EncounterSlotCodec.Empty(Room);

        Assert.That(EncounterSlotCodec.IsInUse(slot), Is.True);
        Assert.That(slot.EncounterId, Is.EqualTo(7));
        Assert.That(EncounterSlotCodec.TryGetStatus(slot, out EncounterStatus status), Is.True);
        Assert.That(status.State, Is.EqualTo(EncounterState.NotActivated));
        Assert.That(status.Id, Is.EqualTo(Room));
        Assert.That(status.AliveMemberCount, Is.EqualTo(0));
    }

    [Test]
    public void IsInUse_DefaultSlot_IsFalseAndHasNoStatus()
    {
        Assert.That(EncounterSlotCodec.IsInUse(default), Is.False);
        Assert.That(EncounterSlotCodec.TryGetStatus(default, out _), Is.False);
    }

    [Test]
    public void TryEncode_ActiveSnapshot_WritesStateCountMasksAndTick()
    {
        var snapshot = Snapshot(EncounterState.Active, reactivations: 2, members: new uint[] { 1u, 3u, 32u }, alive: new uint[] { 3u, 32u });

        Assert.That(EncounterSlotCodec.TryEncode(Room, snapshot, TickSeconds, out EncounterNetworkSlot slot), Is.True);

        Assert.That(slot.EncounterId, Is.EqualTo(7));
        Assert.That(slot.StateRaw, Is.EqualTo((byte)EncounterState.Active));
        Assert.That(slot.ReactivationCount, Is.EqualTo(2));
        Assert.That(slot.MemberMask, Is.EqualTo(0x80000005u));
        Assert.That(slot.AliveMask, Is.EqualTo(0x80000004u));
    }

    [Test]
    public void TryEncode_CooldownStart_IsStoredAsRegistryTicks()
    {
        var snapshot = Snapshot(EncounterState.Resolved, cooldownStart: 7 * TickSeconds, members: new uint[] { 1u });

        Assert.That(EncounterSlotCodec.TryEncode(Room, snapshot, TickSeconds, out EncounterNetworkSlot slot), Is.True);

        Assert.That(slot.CooldownStartTick, Is.EqualTo(7));
    }

    [Test]
    public void TryEncode_AllThirtyTwoMembersAlive_FillsTheMask()
    {
        var keys = new uint[32];
        for (uint i = 0; i < 32; i++) keys[i] = i + 1;

        Assert.That(EncounterSlotCodec.TryEncode(Room, Snapshot(EncounterState.Active, members: keys, alive: keys), TickSeconds, out EncounterNetworkSlot slot), Is.True);

        Assert.That(slot.MemberMask, Is.EqualTo(uint.MaxValue));
        Assert.That(slot.AliveMask, Is.EqualTo(uint.MaxValue));
        Assert.That(EncounterSlotCodec.TryGetStatus(slot, out EncounterStatus status), Is.True);
        Assert.That(status.MemberCount, Is.EqualTo(32));
        Assert.That(status.AliveMemberCount, Is.EqualTo(32));
    }

    [TestCase(0u)]
    [TestCase(33u)]
    public void TryEncode_MemberKeyOutsideTheMaskRange_IsRejectedAndSlotIsDefault(uint key)
    {
        var snapshot = Snapshot(EncounterState.Active, members: new uint[] { 1u, key }, alive: new uint[] { 1u, key });

        Assert.That(EncounterSlotCodec.TryEncode(Room, snapshot, TickSeconds, out EncounterNetworkSlot slot), Is.False);

        Assert.That(slot.EncounterId, Is.EqualTo(0));
        Assert.That(slot.MemberMask, Is.EqualTo(0u));
    }

    [Test]
    public void TryEncode_InvalidIdOrTickLength_IsRejected()
    {
        EncounterLifecycleSnapshot snapshot = Snapshot(EncounterState.NotActivated);

        Assert.That(EncounterSlotCodec.TryEncode(EncounterId.None, snapshot, TickSeconds, out _), Is.False);
        Assert.That(EncounterSlotCodec.TryEncode(Room, snapshot, 0d, out _), Is.False);
        Assert.That(EncounterSlotCodec.TryEncode(Room, Snapshot((EncounterState)99), TickSeconds, out _), Is.False);
    }

    [Test]
    public void EncodeThenDecode_RoundTripsTheSnapshot()
    {
        var snapshot = Snapshot(
            EncounterState.Cooldown, reactivations: 1, cooldownStart: 150 * TickSeconds, members: new uint[] { 2u, 4u, 6u });

        Assert.That(EncounterSlotCodec.TryEncode(Room, snapshot, TickSeconds, out EncounterNetworkSlot slot), Is.True);
        Assert.That(EncounterSlotCodec.TryDecode(slot, TickSeconds, out EncounterId id, out EncounterLifecycleSnapshot decoded), Is.True);

        Assert.That(id, Is.EqualTo(Room));
        Assert.That(decoded.State, Is.EqualTo(EncounterState.Cooldown));
        Assert.That(decoded.ReactivationCount, Is.EqualTo(1));
        Assert.That(decoded.CooldownStartTime, Is.EqualTo(150 * TickSeconds).Within(1e-9));
        Assert.That(decoded.MemberKeys, Is.EqualTo(new uint[] { 2u, 4u, 6u }));
        Assert.That(decoded.AliveMemberKeys, Is.Empty);
    }

    [Test]
    public void TryDecode_ListsKeysAscendingFromTheMasks()
    {
        var slot = new EncounterNetworkSlot
        {
            EncounterId = 3,
            StateRaw = (byte)EncounterState.Active,
            MemberMask = 0x80000005u,
            AliveMask = 0x80000001u,
        };

        Assert.That(EncounterSlotCodec.TryDecode(slot, TickSeconds, out _, out EncounterLifecycleSnapshot decoded), Is.True);

        Assert.That(decoded.MemberKeys, Is.EqualTo(new uint[] { 1u, 3u, 32u }));
        Assert.That(decoded.AliveMemberKeys, Is.EqualTo(new uint[] { 1u, 32u }));
    }

    [Test]
    public void TryDecode_UnusedSlotOrUndefinedStateOrBadTick_IsRejected()
    {
        Assert.That(EncounterSlotCodec.TryDecode(default, TickSeconds, out _, out _), Is.False);
        var undefined = new EncounterNetworkSlot { EncounterId = 3, StateRaw = 9 };
        Assert.That(EncounterSlotCodec.TryDecode(undefined, TickSeconds, out _, out _), Is.False);
        Assert.That(EncounterSlotCodec.TryDecode(EncounterSlotCodec.Empty(Room), 0d, out _, out _), Is.False);
    }

    [Test]
    public void TryGetStatus_ReportsCountsFromTheMasks()
    {
        var slot = new EncounterNetworkSlot
        {
            EncounterId = 9,
            StateRaw = (byte)EncounterState.Active,
            ReactivationCount = 1,
            MemberMask = 0b1111u,
            AliveMask = 0b0101u,
        };

        Assert.That(EncounterSlotCodec.TryGetStatus(slot, out EncounterStatus status), Is.True);

        Assert.That(status.Id, Is.EqualTo(new EncounterId(9)));
        Assert.That(status.State, Is.EqualTo(EncounterState.Active));
        Assert.That(status.ReactivationCount, Is.EqualTo(1));
        Assert.That(status.MemberCount, Is.EqualTo(4));
        Assert.That(status.AliveMemberCount, Is.EqualTo(2));
    }

    [Test]
    public void IsAliveMember_ChecksTheAliveMaskBit()
    {
        var slot = new EncounterNetworkSlot { EncounterId = 9, StateRaw = (byte)EncounterState.Active, MemberMask = 0b110u, AliveMask = 0b100u };

        Assert.That(EncounterSlotCodec.IsAliveMember(slot, 3u), Is.True);
        Assert.That(EncounterSlotCodec.IsAliveMember(slot, 2u), Is.False, "Member but eliminated.");
        Assert.That(EncounterSlotCodec.IsAliveMember(slot, 1u), Is.False);
        Assert.That(EncounterSlotCodec.IsAliveMember(slot, 0u), Is.False);
        Assert.That(EncounterSlotCodec.IsAliveMember(slot, 33u), Is.False);
    }
}
