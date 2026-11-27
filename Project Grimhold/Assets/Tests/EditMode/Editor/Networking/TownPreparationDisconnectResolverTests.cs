using Fusion;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// FIX-92: A member (or Host) disconnecting from Town after an authority migration before completing
/// ticket re-registration must still be cleaned up from its preparation, while an unverified disconnect
/// declaring another connected player's ProfileId must NEVER affect that connected player.
/// </summary>
[TestFixture]
public sealed class TownPreparationDisconnectResolverTests
{
    private static readonly PlayerRef HostPlayer = PlayerRef.FromIndex(1);
    private static readonly PlayerRef MemberPlayer = PlayerRef.FromIndex(2);
    private static readonly PlayerRef SpooferPlayer = PlayerRef.FromIndex(3);
    private static readonly ProfileId HostProfile = new ProfileId("host_profile_fix92");
    private static readonly ProfileId MemberProfile = new ProfileId("member_profile_fix92");

    [Test]
    public void PostMigrationDisconnect_UnverifiedMember_ResolvesUniqueDeclaredProfileForCleanup()
    {
        var resolver = new TownPreparationDisconnectResolver();
        // Authority migrated: verified map cleared, but declared profiles of active players were observed.
        resolver.ObserveActiveDeclaredProfiles(
            new[] { HostPlayer, MemberPlayer },
            p => p == HostPlayer ? HostProfile : p == MemberPlayer ? MemberProfile : default);

        // Member disconnects before finishing ticket re-registration; their PlayerObject is already despawned.
        bool resolved = resolver.TryResolveDisconnectedProfile(
            disconnectedPlayer: MemberPlayer,
            remainingActivePlayers: new[] { HostPlayer },
            tryResolveLiveVerified: p => p == HostPlayer ? HostProfile : default,
            tryReadLiveDeclared: p => p == HostPlayer ? HostProfile : default,
            out ProfileId disconnectedProfile);

        Assert.IsTrue(resolved, "Unverified member disconnecting after authority migration must be resolved for cleanup.");
        Assert.AreEqual(MemberProfile, disconnectedProfile);
    }

    [Test]
    public void PostMigrationDisconnect_UnverifiedHost_ResolvesHostProfileSoPreparationDissolves()
    {
        var resolver = new TownPreparationDisconnectResolver();
        resolver.ObserveActiveDeclaredProfiles(
            new[] { HostPlayer, MemberPlayer },
            p => p == HostPlayer ? HostProfile : p == MemberPlayer ? MemberProfile : default);

        bool resolved = resolver.TryResolveDisconnectedProfile(
            disconnectedPlayer: HostPlayer,
            remainingActivePlayers: new[] { MemberPlayer },
            tryResolveLiveVerified: _ => default,
            tryReadLiveDeclared: p => p == MemberPlayer ? MemberProfile : default,
            out ProfileId disconnectedProfile);

        Assert.IsTrue(resolved, "Unverified Host disconnecting after migration must resolve so the preparation dissolves.");
        Assert.AreEqual(HostProfile, disconnectedProfile);
    }

    [Test]
    public void PostMigrationDisconnect_UnverifiedSpooferSharingActivePlayerDeclaredProfile_DoesNotRemoveConnectedPlayer()
    {
        var resolver = new TownPreparationDisconnectResolver();
        // Both legitimate MemberPlayer and SpooferPlayer declare MemberProfile while unverified.
        resolver.ObserveActiveDeclaredProfiles(
            new[] { HostPlayer, MemberPlayer, SpooferPlayer },
            p => p == HostPlayer ? HostProfile : MemberProfile);

        // Spoofer disconnects: MemberPlayer is STILL connected and declares MemberProfile!
        bool resolved = resolver.TryResolveDisconnectedProfile(
            disconnectedPlayer: SpooferPlayer,
            remainingActivePlayers: new[] { HostPlayer, MemberPlayer },
            tryResolveLiveVerified: _ => default,
            tryReadLiveDeclared: p => p == HostPlayer ? HostProfile : p == MemberPlayer ? MemberProfile : default,
            out ProfileId disconnectedProfile);

        Assert.IsFalse(resolved,
            "An unverified disconnect declaring the same ProfileId as a connected player must NOT remove the connected player.");
        Assert.AreEqual(default(ProfileId), disconnectedProfile);
    }

    [Test]
    public void PostMigrationDisconnect_UnverifiedSpooferSharingActiveVerifiedPlayerProfile_DoesNotRemoveConnectedPlayer()
    {
        var resolver = new TownPreparationDisconnectResolver();
        resolver.RecordVerified(MemberPlayer, MemberProfile);
        resolver.RecordDeclared(SpooferPlayer, MemberProfile);

        bool resolved = resolver.TryResolveDisconnectedProfile(
            disconnectedPlayer: SpooferPlayer,
            remainingActivePlayers: new[] { MemberPlayer },
            tryResolveLiveVerified: p => p == MemberPlayer ? MemberProfile : default,
            tryReadLiveDeclared: _ => default,
            out ProfileId disconnectedProfile);

        Assert.IsFalse(resolved,
            "An unverified spoofer disconnecting must never affect an active verified player.");
        Assert.AreEqual(default(ProfileId), disconnectedProfile);
    }

    [Test]
    public void NormalDisconnect_WithoutMigration_VerifiedMember_PreservesExactExistingBehavior()
    {
        var resolver = new TownPreparationDisconnectResolver();
        resolver.RecordVerified(MemberPlayer, MemberProfile);

        bool resolved = resolver.TryResolveDisconnectedProfile(
            disconnectedPlayer: MemberPlayer,
            remainingActivePlayers: new[] { HostPlayer },
            tryResolveLiveVerified: _ => default,
            tryReadLiveDeclared: _ => default,
            out ProfileId disconnectedProfile);

        Assert.IsTrue(resolved);
        Assert.AreEqual(MemberProfile, disconnectedProfile);
        Assert.IsFalse(resolver.TryGetVerifiedProfile(MemberPlayer, out _), "Verified binding is removed on disconnect.");
    }
}
