#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// State-Authority boundary of <see cref="TownRaidPreparationDirectory"/> Create through a real Single runner.
/// The awaiting-party state flows through its real path: the session coordinator publishes it on the player's
/// replicated <see cref="SocialPlayerIdentity"/>, which <see cref="TownPartyDirectory"/> serves to the directory.
/// </summary>
public sealed class TownRaidPreparationDirectoryTests
{
    private const string SocialPlayerPrefabGuid = "b58bec13d63beb74ca61349f7d983c36";
    private const string TownRaidNpcPrefabGuid = "a9ffa00805946954c9c2e987819f1857";
    private const int SettleFrames = 30;

    private NetworkRunner _runner;
    private Tests.PlayMode.PlayModeMerchantComposition _identity;
    private GameObject _coordinatorOwner;
    private SessionConnectionCoordinator _coordinator;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        _identity?.Dispose();
        _identity = null;
        LogAssert.ignoreFailingMessages = false;
        if (_coordinator != null) _coordinator.SimulatePartyMemberInDungeon = false;
        if (_coordinatorOwner != null) UnityEngine.Object.DestroyImmediate(_coordinatorOwner);
        _coordinatorOwner = null;
        _coordinator = null;
        if (_runner != null && _runner.IsRunning)
        {
            var shutdown = _runner.Shutdown();
            while (!shutdown.IsCompleted) yield return null;
        }
        if (_runner != null) UnityEngine.Object.DestroyImmediate(_runner.gameObject);
    }

    [UnityTest]
    public IEnumerator Create_WhenNotAwaitingPartyCompletion_SpawnsPreparation()
    {
        var fixture = new Fixture();
        yield return Compose(fixture, awaitingPartyCompletion: false);

        Assert.That(fixture.Directory.RequestCreate(), Is.True, "The Town client request must be accepted for sending.");
        int framesRemaining = 120;
        while (!fixture.Directory.TryGetPreparation(fixture.LocalProfile, out _) && framesRemaining-- > 0) yield return null;

        Assert.That(fixture.Directory.TryGetPreparation(fixture.LocalProfile, out TownRaidPreparationNetworkController preparation), Is.True);
        Assert.That(preparation.HostProfileId, Is.EqualTo(fixture.LocalProfile));
        Assert.That(fixture.Directory.PreparationCount, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator Create_WhenAwaitingPartyCompletion_IsRejectedSilently()
    {
        var fixture = new Fixture();
        yield return Compose(fixture, awaitingPartyCompletion: true);

        Assert.That(fixture.Parties.IsAwaitingPartyCompletion(_runner.LocalPlayer), Is.True, "Precondition: the replicated identity publishes the wait.");
        Assert.That(fixture.Directory.RequestCreate(), Is.True, "The request reaches the State Authority; only the authority may refuse it.");
        for (int frame = 0; frame < SettleFrames; frame++) yield return null;

        Assert.That(fixture.Directory.TryGetPreparation(fixture.LocalProfile, out _), Is.False);
        Assert.That(fixture.Directory.PreparationCount, Is.EqualTo(0));
    }

    private IEnumerator Compose(Fixture fixture, bool awaitingPartyCompletion)
    {
        LogAssert.ignoreFailingMessages = true;
        Assert.That(SessionConnectionCoordinator.Instance, Is.Null, "A leaked coordinator singleton would replace the fixture's.");
        _coordinatorOwner = new GameObject("TownRaidPreparationDirectoryTests coordinator");
        _coordinatorOwner.AddComponent<HubSessionLauncher>();
        _coordinatorOwner.AddComponent<FusionSessionLauncher>();
        _coordinator = _coordinatorOwner.AddComponent<SessionConnectionCoordinator>();
        // The client-side request path requires an honest Town session: walk the state machine as a real connect would.
        Assert.That(TransitionCoordinator(SessionConnectionState.ConnectingTown), Is.True);
        Assert.That(TransitionCoordinator(SessionConnectionState.Town), Is.True);
        _coordinator.SimulatePartyMemberInDungeon = awaitingPartyCompletion;

        var runnerObject = new GameObject("TownRaidPreparationDirectoryRunner");
        _runner = runnerObject.AddComponent<NetworkRunner>();
        runnerObject.AddComponent<EntityRegistry>();
        runnerObject.AddComponent<LocalInputContext>();
        runnerObject.AddComponent<TownRaidPreparationDirectoryContext>();
        runnerObject.AddComponent<TownPartyDirectoryContext>();
        runnerObject.AddComponent<TownSenderIdentityContext>();
        var joinContext = runnerObject.AddComponent<LocalPlayerJoinContext>();
        // Directory and Party only serve senders whose Town session ticket verifies, so the fixture composes one.
        _identity = new Tests.PlayMode.PlayModeMerchantComposition(0);
        fixture.LocalProfile = _identity.ProfileId;
        joinContext.Initialize(new PlayerJoinData(fixture.LocalProfile));

        var start = _runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Single,
            SessionName = $"town-raid-preparation-{Guid.NewGuid():N}",
            SceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>(),
            ObjectProvider = runnerObject.AddComponent<NetworkObjectProviderDefault>()
        });
        while (!start.IsCompleted) yield return null;
        Assert.That(start.Result.Ok, Is.True, start.Result.ShutdownReason.ToString());

        NetworkObject npc = _runner.Spawn(LoadPrefab(TownRaidNpcPrefabGuid));
        NetworkObject localPlayer = _runner.Spawn(LoadPrefab(SocialPlayerPrefabGuid), Vector3.zero, Quaternion.identity, _runner.LocalPlayer);
        _runner.SetPlayerObject(_runner.LocalPlayer, localPlayer);
        fixture.Directory = npc.GetComponent<TownRaidPreparationDirectory>();
        fixture.Parties = npc.GetComponent<TownPartyDirectory>();

        int framesRemaining = 240;
        while (!IsReady(fixture, awaitingPartyCompletion) && framesRemaining-- > 0) yield return null;
        Assert.That(IsReady(fixture, awaitingPartyCompletion), Is.True, "The directory index, solo party and wait state must settle.");
    }

    private bool IsReady(Fixture fixture, bool awaitingPartyCompletion) =>
        fixture.Directory.IsIndexReady && fixture.Parties.TryGetParty(fixture.LocalProfile, out _) &&
        fixture.Parties.IsAwaitingPartyCompletion(_runner.LocalPlayer) == awaitingPartyCompletion;

    private bool TransitionCoordinator(SessionConnectionState next) =>
        (bool)typeof(SessionConnectionCoordinator)
            .GetMethod("TransitionTo", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(_coordinator, new object[] { next });

    private NetworkObject LoadPrefab(string guid)
    {
        NetworkPrefabId prefabId = _runner.Config.PrefabTable.GetId(NetworkObjectGuid.Parse(guid));
        return _runner.Config.PrefabTable.Load(prefabId, true);
    }

    private sealed class Fixture
    {
        public ProfileId LocalProfile;
        public TownRaidPreparationDirectory Directory;
        public TownPartyDirectory Parties;
    }
}
#endif
