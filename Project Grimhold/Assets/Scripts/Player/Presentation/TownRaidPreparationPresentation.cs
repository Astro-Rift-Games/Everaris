using System;
using System.Collections.Generic;

/// <summary>Pure local presentation projection for one player's current Town preparation.</summary>
public readonly struct TownRaidPreparationPresentation
{
    public readonly struct Member
    {
        private readonly LootDefinition[] _equipment;

        public ProfileId ProfileId { get; }
        public bool IsLocal { get; }
        public bool HasEquipment { get; }
        public int EquipmentRevision { get; }
        public string DisplayName { get; }
        public bool IsHost { get; }
        public bool IsReady { get; }
        public bool LaunchAcknowledged { get; }

        public Member(string displayName, bool isHost, bool isReady)
            : this(default, false, displayName, isHost, isReady, null, false)
        {
        }

        public Member(ProfileId profileId, bool isLocal, string displayName,
            bool isHost, bool isReady, IPreparedEquipmentReader equipmentReader)
            : this(profileId, isLocal, displayName, isHost, isReady, equipmentReader, false)
        {
        }

        public Member(ProfileId profileId, bool isLocal, string displayName,
            bool isHost, bool isReady, IPreparedEquipmentReader equipmentReader,
            bool launchAcknowledged)
        {
            ProfileId = profileId;
            IsLocal = isLocal;
            DisplayName = displayName;
            IsHost = isHost;
            IsReady = isReady;
            LaunchAcknowledged = launchAcknowledged;
            HasEquipment = equipmentReader != null;
            EquipmentRevision = equipmentReader?.ObservedEquipmentRevision ?? 0;
            _equipment = null;
            if (equipmentReader != null)
            {
                _equipment = new LootDefinition[EquipmentSlotRules.SlotCount + 1];
                foreach (EquipmentSlot slot in EquipmentSlotRules.AllSlots)
                {
                    if (equipmentReader.TryGetPreparedSlotDefinition(slot, out LootDefinition definition))
                        _equipment[(int)slot] = definition;
                }
            }
        }

        public bool TryGetEquipment(EquipmentSlot slot, out LootDefinition definition)
        {
            definition = null;
            if (_equipment == null || !EquipmentSlotRules.IsEquipmentSlot(slot)) return false;
            definition = _equipment[(int)slot];
            return definition != null;
        }
    }

    public delegate bool DisplayNameResolver(ProfileId profileId, out string displayName);
    public delegate bool PreparedEquipmentResolver(ProfileId profileId, out IPreparedEquipmentReader reader);

    public TownRaidPreparationSnapshot Snapshot { get; }
    public IReadOnlyList<Member> Members { get; }
    public bool IsHost { get; }
    public bool LocalReady { get; }
    public bool CanStart { get; }
    public bool IsLaunching => Snapshot.State == TownRaidPreparationState.Launching;
    public bool CanLeave => Snapshot.State == TownRaidPreparationState.Waiting;
    public bool CanSetReady => Snapshot.State == TownRaidPreparationState.Waiting;
    public bool CanToggleFriendlyFire => IsHost && Snapshot.State == TownRaidPreparationState.Waiting;
    public bool FriendlyFireEnabled => Snapshot.FriendlyFireEnabled;
    public int AcknowledgedCount
    {
        get
        {
            if (Members == null) return 0;
            int count = 0;
            for (int index = 0; index < Members.Count; index++)
            {
                if (Members[index].LaunchAcknowledged) count++;
            }
            return count;
        }
    }

    private TownRaidPreparationPresentation(
        in TownRaidPreparationSnapshot snapshot,
        IReadOnlyList<Member> members,
        bool isHost,
        bool localReady,
        bool canStart)
    {
        Snapshot = snapshot;
        Members = members;
        IsHost = isHost;
        LocalReady = localReady;
        CanStart = canStart;
    }

    public static bool TryCreate(
        in TownRaidPreparationSnapshot snapshot,
        ProfileId localProfileId,
        out TownRaidPreparationPresentation presentation)
    {
        return TryCreate(snapshot, localProfileId, null, out presentation);
    }

    public static bool TryCreate(
        in TownRaidPreparationSnapshot snapshot,
        ProfileId localProfileId,
        DisplayNameResolver displayNameResolver,
        out TownRaidPreparationPresentation presentation)
    {
        return TryCreate(snapshot, localProfileId, displayNameResolver, null, out presentation);
    }

    public static bool TryCreate(
        in TownRaidPreparationSnapshot snapshot,
        ProfileId localProfileId,
        DisplayNameResolver displayNameResolver,
        PreparedEquipmentResolver equipmentResolver,
        out TownRaidPreparationPresentation presentation)
    {
        presentation = default;
        if (!localProfileId.IsValid || !TownRaidPreparationRules.IsValidSnapshot(snapshot))
        {
            return false;
        }

        IReadOnlyList<TownRaidPreparationMember> members = snapshot.Members;
        var presentedMembers = new Member[members.Count];
        bool found = false;
        bool localReady = false;
        for (int index = 0; index < members.Count; index++)
        {
            TownRaidPreparationMember member = members[index];
            string displayName = member.ProfileId.Value;
            if (displayNameResolver != null &&
                displayNameResolver(member.ProfileId, out string resolvedName) &&
                !string.IsNullOrWhiteSpace(resolvedName))
            {
                displayName = resolvedName;
            }

            IPreparedEquipmentReader equipmentReader = null;
            if (equipmentResolver != null && equipmentResolver(member.ProfileId, out var resolvedReader))
                equipmentReader = resolvedReader;

            presentedMembers[index] = new Member(
                member.ProfileId,
                member.ProfileId == localProfileId,
                displayName,
                member.ProfileId == snapshot.HostProfileId,
                member.IsReady,
                equipmentReader,
                member.LaunchAcknowledged);

            if (member.ProfileId != localProfileId)
            {
                continue;
            }

            found = true;
            localReady = member.IsReady;
        }

        if (!found)
        {
            return false;
        }

        bool isHost = snapshot.HostProfileId == localProfileId;
        presentation = new TownRaidPreparationPresentation(
            snapshot,
            Array.AsReadOnly(presentedMembers),
            isHost,
            localReady,
            TownRaidPreparationRules.CanStart(snapshot, localProfileId));
        return true;
    }
}
