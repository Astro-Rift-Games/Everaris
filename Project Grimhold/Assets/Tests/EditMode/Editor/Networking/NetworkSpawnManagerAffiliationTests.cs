using System;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;
using Object = UnityEngine.Object;

/// <summary>
/// FIX-110: presentation reads the initial affiliation projection every frame, so the
/// projection of one immutable launch context must be built once and reused.
/// </summary>
public sealed class NetworkSpawnManagerAffiliationTests
{
    private static readonly ProfileId HostProfile = new ProfileId("11111111111111111111111111111111");
    private static readonly ProfileId GuestProfile = new ProfileId("22222222222222222222222222222222");

    private GameObject _owner;
    private NetworkSpawnManager _spawnManager;

    [TearDown]
    public void TearDown()
    {
        if (_owner != null)
        {
            Object.DestroyImmediate(_owner);
        }
    }

    [Test]
    public void TryGetRaidInitialAffiliations_RepeatedCalls_ReturnSameSnapshot()
    {
        InitializeWithLaunchContext(CreateTwoTeamContext());

        Assert.That(_spawnManager.TryGetRaidInitialAffiliations(out RaidInitialAffiliationSnapshot first), Is.True);
        Assert.That(_spawnManager.TryGetRaidInitialAffiliations(out RaidInitialAffiliationSnapshot second), Is.True);

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public void TryGetRaidInitialAffiliations_RepeatedCalls_DoNotAllocate()
    {
        InitializeWithLaunchContext(CreateTwoTeamContext());
        Assert.That(_spawnManager.TryGetRaidInitialAffiliations(out _), Is.True);

        NetworkSpawnManager spawnManager = _spawnManager;
        TestDelegate repeatedRead = () => spawnManager.TryGetRaidInitialAffiliations(out _);
        Assert.That(
            repeatedRead,
            UnityEngine.TestTools.Constraints.ConstraintExtensions.AllocatingGCMemory(Is.Not));
    }

    [Test]
    public void TryGetRaidInitialAffiliations_CachedSnapshot_KeepsLaunchTeams()
    {
        InitializeWithLaunchContext(CreateTwoTeamContext());
        Assert.That(_spawnManager.TryGetRaidInitialAffiliations(out _), Is.True);

        Assert.That(_spawnManager.TryGetRaidInitialAffiliations(out RaidInitialAffiliationSnapshot affiliations), Is.True);
        ProfileId[] profiles = { HostProfile, GuestProfile };
        Assert.That(RaidParticipantIdAssignment.TryResolve(profiles, HostProfile, out RaidParticipantId hostId), Is.True);
        Assert.That(RaidParticipantIdAssignment.TryResolve(profiles, GuestProfile, out RaidParticipantId guestId), Is.True);

        Assert.That(affiliations.ParticipantCount, Is.EqualTo(2));
        Assert.That(affiliations.TryGetTeam(hostId, out RaidTeamId hostTeam), Is.True);
        Assert.That(affiliations.TryGetTeam(guestId, out RaidTeamId guestTeam), Is.True);
        Assert.That(hostTeam.Value, Is.EqualTo(1));
        Assert.That(guestTeam.Value, Is.EqualTo(2));
    }

    [Test]
    public void TryGetRaidInitialAffiliations_WithoutLaunchContext_ReturnsFalseAndNull()
    {
        InitializeWithLaunchContext(null);

        Assert.That(_spawnManager.TryGetRaidInitialAffiliations(out RaidInitialAffiliationSnapshot affiliations), Is.False);
        Assert.That(affiliations, Is.Null);
    }

    private void InitializeWithLaunchContext(RaidLaunchContext launchContext)
    {
        _owner = new GameObject("FIX-110 affiliation cache test");
        NetworkRunner runner = _owner.AddComponent<NetworkRunner>();
        _spawnManager = _owner.AddComponent<NetworkSpawnManager>();
        Assert.That(
            _spawnManager.InitializeForRunner(
                runner,
                NetworkPrefabRef.Empty,
                default,
                Array.Empty<NetworkPrefabRef>(),
                SessionStartupContext.HostMigrationResume,
                launchContext),
            Is.True);
    }

    private static RaidLaunchContext CreateTwoTeamContext()
    {
        Assert.That(RaidCode.TryParse("123456", out RaidCode code), Is.True);
        Assert.That(RaidTeamId.TryCreate(1, out RaidTeamId hostTeam), Is.True);
        Assert.That(RaidTeamId.TryCreate(2, out RaidTeamId guestTeam), Is.True);
        Assert.That(
            RaidLaunchContext.TryCreate(
                code,
                HostProfile,
                new[]
                {
                    new RaidLaunchParticipant(HostProfile, hostTeam),
                    new RaidLaunchParticipant(GuestProfile, guestTeam)
                },
                HostProfile,
                7,
                out RaidLaunchContext context),
            Is.True);
        return context;
    }
}
