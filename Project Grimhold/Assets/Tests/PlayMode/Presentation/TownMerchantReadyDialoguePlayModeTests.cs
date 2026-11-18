#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using Tests.PlayMode;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// The Town merchant dialogue flow through a real Single runner: the real player prefab's
/// <see cref="DialoguePresenter"/> and <see cref="TownMerchantPresenter"/> react to an authoritative
/// interaction result on the real merchant NPC. A Ready player (build gate false) hears the Ready-lock
/// dialogue and the shop stays closed; an unlocked player hears the normal greeting and the shop opens.
/// </summary>
public sealed class TownMerchantReadyDialoguePlayModeTests
{
    private const string SocialPlayerPrefabGuid = "b58bec13d63beb74ca61349f7d983c36";
    private const string MerchantPrefabGuid = "2b48446013b1f154b878ae1793dab811";
    private static readonly Vector3 PlayerPosition = new Vector3(2f, 0f, 0f);

    private NetworkRunner _runner;
    private PlayModeMerchantComposition _profile;
    private NetworkObject _player;
    private NetworkObject _merchantObject;
    private DialoguePresenter _dialogue;
    private TownMerchantPresenter _merchantPresenter;
    private DialogueController _dialogueController;
    private TownNpcDirectionalView _facing;
    private readonly List<string> _lines = new List<string>();
    private bool _canMutateBuild = true;
    private int _interactionSequence;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (_runner != null && _runner.IsRunning)
        {
            var shutdown = _runner.Shutdown();
            while (!shutdown.IsCompleted) yield return null;
        }
        if (_runner != null) UnityEngine.Object.DestroyImmediate(_runner.gameObject);
        _profile?.Dispose();
        _profile = null;
    }

    [UnityTest]
    public IEnumerator ReadyLockedPlayer_PlaysReadyDialogue_AndTheShopStaysClosed()
    {
        yield return Start(canMutateBuild: false);

        yield return Interact();
        Assert.That(_dialogue.IsDialogueActive, Is.True, "The interaction starts the merchant dialogue.");
        Assert.That(_facing.CurrentFacing, Is.Not.EqualTo(_facing.InitialFacing), "Precondition: the merchant turned to the player.");
        yield return AdvanceUntilDialogueEnds();

        Assert.That(_lines, Is.EqualTo(new[]
        {
            "Your pack's already strapped tight for the descent, friend.",
            "Unbuckle it first if you mean to haggle — I don't trade with those halfway out the gate."
        }));
        Assert.That(ShopIsOpen(), Is.False, "The shop does not open when the Ready-lock dialogue completes.");
        Assert.That(_facing.CurrentFacing, Is.EqualTo(_facing.InitialFacing), "The merchant restores its facing without a panel.");
    }

    [UnityTest]
    public IEnumerator UnlockedPlayer_PlaysGreeting_ThenTheShopOpens()
    {
        yield return Start(canMutateBuild: true);

        yield return Interact();
        yield return AdvanceUntilDialogueEnds();

        Assert.That(_lines, Is.EqualTo(new[] { "Let's see what's for sale." }));
        Assert.That(ShopIsOpen(), Is.True, "Positive control: the same flow opens the shop when the player is not Ready.");
    }

    private IEnumerator Start(bool canMutateBuild)
    {
        _canMutateBuild = canMutateBuild;
        _lines.Clear();
        _interactionSequence = 0;
        var merchantPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/TownMerchantNPC.prefab");
        var owned = merchantPrefab.GetComponent<TownMerchantNetworkController>().Stock[0].Item.LootId;
        _profile = new PlayModeMerchantComposition(10000, owned, 0);
        var owner = new GameObject("Town merchant dialogue runner");
        _runner = owner.AddComponent<NetworkRunner>();
        owner.AddComponent<EntityRegistry>();
        owner.AddComponent<LocalInputContext>();
        owner.AddComponent<TownSenderIdentityContext>();
        owner.AddComponent<LocalPlayerJoinContext>().Initialize(new PlayerJoinData(_profile.ProfileId));
        var start = _runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Single,
            SessionName = "merchant-dialogue-test-" + Guid.NewGuid().ToString("N"),
            SceneManager = owner.AddComponent<NetworkSceneManagerDefault>(),
            ObjectProvider = owner.AddComponent<NetworkObjectProviderDefault>()
        });
        yield return WaitFor(() => start.IsCompleted, "Runner startup");
        Assert.That(start.Result.Ok, Is.True, start.Result.ShutdownReason.ToString());

        _player = Spawn(SocialPlayerPrefabGuid, PlayerPosition);
        _runner.SetPlayerObject(_runner.LocalPlayer, _player);
        _merchantObject = Spawn(MerchantPrefabGuid, Vector3.zero);
        _merchantObject.GetComponent<TownMerchantNetworkController>().TicketVerifier = _profile;

        _dialogue = _player.GetComponent<DialoguePresenter>();
        _merchantPresenter = _player.GetComponent<TownMerchantPresenter>();
        _facing = _merchantObject.GetComponentInChildren<TownNpcDirectionalView>();
        Assert.That(_dialogue, Is.Not.Null);
        Assert.That(_merchantPresenter, Is.Not.Null);
        Assert.That(_facing, Is.Not.Null);
        Assert.That(_player.HasInputAuthority, Is.True);

        // Same composition the Town binder performs: the profile source carries the Ready gate.
        _merchantPresenter.BindProfile(new TownMerchantProfileSource(
            _profile.ProfileId, _profile.Inventory, _profile.Currency, _profile.Shop,
            _profile.BuybackLedger, _profile.Shop, () => _canMutateBuild));

        yield return null;
        _dialogueController = _player.GetComponentInChildren<DialogueController>(true);
        Assert.That(_dialogueController, Is.Not.Null, "The presenter instantiates its real dialogue UI.");
        _dialogueController.LineStarted += (line, _, __) => _lines.Add(line.Text);
    }

    private NetworkObject Spawn(string guid, Vector3 position)
    {
        var id = _runner.Config.PrefabTable.GetId(NetworkObjectGuid.Parse(guid));
        var prefab = _runner.Config.PrefabTable.Load(id, true);
        Assert.That(prefab, Is.Not.Null, "Required prefab is absent from the Fusion PrefabTable: " + guid);
        return _runner.Spawn(prefab, position, Quaternion.identity, _runner.LocalPlayer);
    }

    /// <summary>
    /// Delivers the authoritative interaction result State Authority records for the local player's
    /// successful interaction with the merchant through the controller's own delivery method;
    /// both real presenters consume it on Render.
    /// </summary>
    private IEnumerator Interact()
    {
        var controller = _player.GetComponent<PlayerInteractionNetworkController>();
        MethodInfo enqueue = typeof(PlayerInteractionNetworkController).GetMethod(
            "EnqueueInteractionResult", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(enqueue, Is.Not.Null, "The interaction result delivery seam changed.");
        enqueue.Invoke(controller, new object[]
        {
            ++_interactionSequence, unchecked((int)_merchantObject.Id.Raw), 0,
            true, true, (int)InteractionFailureReason.None, (int)LootTransferFailureReason.None
        });
        yield return null;
    }

    /// <summary>Interacting again while talking advances the dialogue, as in production.</summary>
    private IEnumerator AdvanceUntilDialogueEnds()
    {
        for (int step = 0; step < 20 && _dialogue.IsDialogueActive; step++)
        {
            yield return Interact();
        }
        Assert.That(_dialogue.IsDialogueActive, Is.False, "The dialogue completed.");
    }

    private bool ShopIsOpen()
    {
        Transform shop = _player.transform.Find("MerchantShopUI");
        Assert.That(shop, Is.Not.Null, "The merchant presenter created its real shop view.");
        return UiPanelVisibility.IsVisible(shop.gameObject);
    }

    private static IEnumerator WaitFor(Func<bool> ready, string operation)
    {
        for (int frame = 0; frame < 120 && !ready(); frame++) yield return null;
        Assert.That(ready(), Is.True, operation + " did not complete within 120 frames");
    }
}
#endif
