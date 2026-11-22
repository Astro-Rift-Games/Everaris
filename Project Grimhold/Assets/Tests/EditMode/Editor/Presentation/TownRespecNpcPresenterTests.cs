#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using Grimhold.Backend;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Tests.EditMode.Progression;
using Assert = NUnit.Framework.Assert;
using Object = UnityEngine.Object;

namespace Tests.EditMode.Presentation
{
    /// <summary>
    /// TASK-304 T7 (S10-S13): the Mentor NPC presenter. It opens only for the respec NPC, applies the same gates the
    /// attribute presenter does, drives <see cref="TownRespecSession"/> through Confirmar/Cancelar, and owns input
    /// suppression and the NPC's facing while the panel is open.
    /// </summary>
    public sealed class TownRespecNpcPresenterTests
    {
        private const string PanelPath = "Assets/Prefabs/TownRespecPanel.prefab";

        private RespecTestProfile _profile;
        private AttributeRespecService _service;
        private TownAttributeAssignmentGate _gate;
        private GameObject _player;
        private GameObject _npc;
        private GameObject _holder;
        private TownRespecNpcPresenter _presenter;
        private PlayerInputReader _reader;
        private TownNpcDirectionalView _facing;
        private readonly List<string> _sentIds = new();
        private readonly Queue<(bool success, RespecResult data, BackendError error)> _responses = new();
        private TaskCompletionSource<bool> _hold;
        private bool _canMutate;

        [SetUp]
        public void SetUp()
        {
            _profile = new RespecTestProfile();
            _profile.Store.SetRespecCost(RespecTestProfile.Cost);
            _canMutate = true;
            _hold = null;
            _sentIds.Clear();
            _responses.Clear();
            _service = new AttributeRespecService(_profile.Store, Backend, () => Task.FromResult(true));
            _gate = new TownAttributeAssignmentGate(() => _canMutate);

            _holder = new GameObject("InputReaderHolder");
            _holder.SetActive(false);
            _reader = _holder.AddComponent<PlayerInputReader>();
            typeof(PlayerInputReader).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_reader, null);
            _holder.SetActive(true);

            _player = new GameObject("Player");
            _player.transform.position = new Vector3(0f, 3f, 0f);
            _presenter = _player.AddComponent<TownRespecNpcPresenter>();
            var serialized = new SerializedObject(_presenter);
            serialized.FindProperty("_viewPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath)?.GetComponent<TownRespecPanelView>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(serialized.FindProperty("_viewPrefab").objectReferenceValue, Is.Not.Null, "TownRespecPanel.prefab must exist");

            _npc = CreateNpc("Mentor", typeof(TownRespecNpcInteractable));
            _facing = _npc.GetComponentInChildren<TownNpcDirectionalView>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_player != null) Object.DestroyImmediate(_player);
            if (_npc != null) Object.DestroyImmediate(_npc);
            if (_holder != null) Object.DestroyImmediate(_holder);
            _profile.Dispose();
        }

        private async Task<(bool, RespecResult, BackendError)> Backend(string transactionId, int expectedRevision)
        {
            _sentIds.Add(transactionId);
            if (_hold != null) await _hold.Task;
            return _responses.Dequeue();
        }

        private void Bind() => _presenter.BindProfile(_profile.Store, _service, _gate, () => _reader);

        private static GameObject CreateNpc(string name, Type interactable, bool withDialogue = false)
        {
            var npc = new GameObject(name, typeof(NetworkObject));
            npc.SetActive(false);
            if (interactable != null) npc.AddComponent(interactable);
            if (withDialogue) npc.AddComponent<TownNpcDialogueTrigger>();
            var visual = new GameObject("VisualRoot").AddComponent<Animator>();
            visual.transform.SetParent(npc.transform, false);
            visual.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/Animations/NPCs/Shared/TownNpcIdle.controller");
            var view = npc.AddComponent<TownNpcDirectionalView>();
            var serializedView = new SerializedObject(view);
            serializedView.FindProperty("_animator").objectReferenceValue = visual;
            serializedView.ApplyModifiedPropertiesWithoutUndo();
            npc.SetActive(true);
            return npc;
        }

        private TownRespecPanelView View => _player.GetComponentInChildren<TownRespecPanelView>(true);

        private T Field<T>(string name) where T : Object
        {
            var serialized = new SerializedObject(View);
            return (T)serialized.FindProperty(name).objectReferenceValue;
        }

        private string Text(string field) => Field<TMP_Text>(field).text;
        private Button ConfirmButton => Field<Button>("_confirmButton");
        private Button CancelButton => Field<Button>("_cancelButton");

        private bool Open() => _presenter.OpenForTarget(_npc.GetComponent<NetworkObject>());

        private void SucceedOnce() => _responses.Enqueue((true, _profile.Result(), default));

        private void RaiseInteract() => Raise<Action>("InteractPressedLocally")?.Invoke();

        private bool RaiseCloseRequested()
        {
            Func<bool> handlers = Raise<Func<bool>>("InventoryCloseRequested");
            bool consumed = false;
            if (handlers == null) return false;
            foreach (Func<bool> handler in handlers.GetInvocationList()) consumed |= handler();
            return consumed;
        }

        private T Raise<T>(string eventName) where T : Delegate =>
            (T)typeof(PlayerInputReader).GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_reader);

        // ---- opening (S11, S13) ----

        [Test]
        public void Open_ForTheRespecNpc_ShowsTheMentorPanelWithTheCurrentBuildAndTheCost()
        {
            Bind();

            Assert.That(Open(), Is.True);

            Assert.That(_presenter.IsOpen, Is.True);
            Assert.That(Text("_titleText"), Is.EqualTo("Mentor"));
            Assert.That(Text("_costText"), Is.EqualTo("Costo: 100 de Oro"));
            Assert.That(Text("_pointsToReturnText"), Is.EqualTo("Puntos que vuelven al pool: 30"));
            var values = new SerializedObject(View).FindProperty("_attributeValueTexts");
            for (int index = 0; index < 6; index++)
            {
                Assert.That(((TMP_Text)values.GetArrayElementAtIndex(index).objectReferenceValue).text, Is.EqualTo("5"));
            }

            Assert.That(Text("_confirmationText"), Is.EqualTo(TownRespecMessages.ConfirmationText(RespecTestProfile.Cost)),
                "the panel opens on the existing explicit confirmation");
            Assert.That(ConfirmButton.interactable, Is.True);
            Assert.That(CancelButton.interactable, Is.True);
            _profile.AssertUnchanged();
        }

        [Test]
        public void Open_ForAnotherNpc_DoesNothing()
        {
            Bind();
            GameObject other = CreateNpc("Trainer", typeof(TownTrainerNpcInteractable));
            try
            {
                Assert.That(_presenter.OpenForTarget(other.GetComponent<NetworkObject>()), Is.False);
                Assert.That(_presenter.OpenForTarget(null), Is.False);
            }
            finally { Object.DestroyImmediate(other); }

            Assert.That(_presenter.IsOpen, Is.False);
            Assert.That(_reader.IsGameplayInputSuppressed, Is.False);
            Assert.That(_sentIds, Is.Empty);
        }

        [Test]
        public void Open_WithoutABoundProfile_WarnsAndStaysClosed()
        {
            LogAssert.Expect(UnityEngine.LogType.Warning, "Mentor is unavailable because the local profile is not bound.");

            Assert.That(Open(), Is.False);

            Assert.That(_presenter.IsOpen, Is.False);
            Assert.That(_reader.IsGameplayInputSuppressed, Is.False);
        }

        [Test]
        public void Interaction_WaitsForTheDialogue_AndOpensWhenItCompletes()
        {
            Bind();
            GameObject talking = CreateNpc("Mentor", typeof(TownRespecNpcInteractable), withDialogue: true);
            try
            {
                var target = talking.GetComponent<NetworkObject>();

                Assert.That(_presenter.HandleInteraction(target), Is.False, "the dialogue plays first");
                Assert.That(_presenter.IsOpen, Is.False);

                Assert.That(_presenter.HandleDialogueCompleted(target), Is.True);
                Assert.That(_presenter.IsOpen, Is.True);
            }
            finally { Object.DestroyImmediate(talking); }
        }

        [Test]
        public void Interaction_WithoutADialogueTrigger_OpensImmediately()
        {
            Bind();

            Assert.That(_presenter.HandleInteraction(_npc.GetComponent<NetworkObject>()), Is.True);
            Assert.That(_presenter.IsOpen, Is.True);
        }

        [Test]
        public void DialogueCompleted_ForAnotherNpc_DoesNotOpenTheMentor()
        {
            Bind();
            GameObject other = CreateNpc("Trainer", typeof(TownTrainerNpcInteractable), withDialogue: true);
            try
            {
                Assert.That(_presenter.HandleDialogueCompleted(other.GetComponent<NetworkObject>()), Is.False);
            }
            finally { Object.DestroyImmediate(other); }

            Assert.That(_presenter.IsOpen, Is.False);
        }

        [Test]
        public void Open_FacesThePlayer_AndClosingRestoresTheInitialFacing()
        {
            Bind();
            Assert.That(_facing.CurrentFacing, Is.EqualTo(CharacterVisualDirection.South));

            Open();
            Assert.That(_facing.CurrentFacing, Is.EqualTo(CharacterVisualDirection.North), "the player stands to the north");

            _presenter.ClosePanel();
            Assert.That(_facing.CurrentFacing, Is.EqualTo(CharacterVisualDirection.South));
        }

        // ---- gates (S11, S8) ----

        [Test]
        public void Open_WhileReady_ShowsTheLockAndNeverSends()
        {
            Bind();
            _canMutate = false;

            Open();

            Assert.That(Text("_messageText"), Is.EqualTo(TownRespecMessages.BuildLockedMessage));
            Assert.That(ConfirmButton.interactable, Is.False);
            ConfirmButton.onClick.Invoke();
            Assert.That(_sentIds, Is.Empty);
            _profile.AssertUnchanged();
        }

        [Test]
        public void Open_WhileAnAttributeCommitIsInFlight_ShowsBusyAndNeverSends()
        {
            Bind();
            _gate.IsCommitInFlight = true;

            Open();

            Assert.That(Text("_messageText"), Is.EqualTo(TownRespecMessages.BusyMessage));
            Assert.That(ConfirmButton.interactable, Is.False);
            ConfirmButton.onClick.Invoke();
            Assert.That(_sentIds, Is.Empty);
            _profile.AssertUnchanged();
        }

        [Test]
        public void ReadyPressedWhileTheConfirmationIsOpen_RejectsTheConfirmWithoutSending()
        {
            Bind();
            Open();
            Assert.That(ConfirmButton.interactable, Is.True);

            _canMutate = false;
            ConfirmButton.onClick.Invoke();

            Assert.That(_sentIds, Is.Empty);
            Assert.That(Text("_messageText"), Is.EqualTo(TownRespecMessages.BuildLockedMessage));
            _profile.AssertUnchanged();
        }

        [Test]
        public void Open_WithoutEnoughGold_ShowsTheShortfall()
        {
            _profile.Dispose();
            _profile = new RespecTestProfile(gold: 40);
            _profile.Store.SetRespecCost(RespecTestProfile.Cost);
            _service = new AttributeRespecService(_profile.Store, Backend, () => Task.FromResult(true));
            Bind();

            Open();

            Assert.That(Text("_messageText"), Is.EqualTo(TownRespecMessages.InsufficientGoldMessage(RespecTestProfile.Cost)));
            Assert.That(ConfirmButton.interactable, Is.False);
        }

        [Test]
        public void Open_WithNothingAssigned_ShowsThereIsNothingToReturn()
        {
            _profile.Dispose();
            _profile = new RespecTestProfile(assigned: false);
            _profile.Store.SetRespecCost(RespecTestProfile.Cost);
            _service = new AttributeRespecService(_profile.Store, Backend, () => Task.FromResult(true));
            Bind();

            Open();

            Assert.That(Text("_messageText"), Is.EqualTo(TownRespecMessages.NoAssignedPointsMessage));
            Assert.That(Text("_pointsToReturnText"), Is.EqualTo("Puntos que vuelven al pool: 0"));
            Assert.That(ConfirmButton.interactable, Is.False);
        }

        [Test]
        public async Task Open_WithAnUnknownCost_ReadsTheServerPriceBeforeOfferingTheConfirmation()
        {
            _profile.Dispose();
            _profile = new RespecTestProfile();
            var priceGate = new TaskCompletionSource<int?>();
            _service = new AttributeRespecService(_profile.Store, Backend, () => Task.FromResult(true), () => priceGate.Task);
            Bind();

            Open();
            Assert.That(Text("_costText"), Is.EqualTo("Costo: —"));
            Assert.That(ConfirmButton.interactable, Is.False);

            priceGate.SetResult(RespecTestProfile.Cost);
            await Task.Yield();

            Assert.That(Text("_costText"), Is.EqualTo("Costo: 100 de Oro"));
            Assert.That(Text("_confirmationText"), Is.EqualTo(TownRespecMessages.ConfirmationText(RespecTestProfile.Cost)));
            Assert.That(ConfirmButton.interactable, Is.True);
        }

        // ---- confirm / cancel (S1, S3, S13) ----

        [Test]
        public void Confirm_SendsOnce_AppliesTheAuthoritativeSnapshotAndShowsTheResult()
        {
            Bind();
            SucceedOnce();
            Open();

            ConfirmButton.onClick.Invoke();

            Assert.That(_sentIds, Has.Count.EqualTo(1));
            Assert.That(Text("_messageText"), Is.EqualTo(TownRespecMessages.AppliedMessage));
            Assert.That(Text("_pointsToReturnText"), Is.EqualTo("Puntos que vuelven al pool: 0"));
            var values = new SerializedObject(View).FindProperty("_attributeValueTexts");
            for (int index = 0; index < 6; index++)
            {
                Assert.That(((TMP_Text)values.GetArrayElementAtIndex(index).objectReferenceValue).text, Is.EqualTo("0"));
            }

            Assert.That(_profile.Store.GetCurrency(), Is.EqualTo(RespecTestProfile.StartingGold - RespecTestProfile.Cost));
            _profile.AssertAttributes(0, RespecTestProfile.OwnedPoints);
            Assert.That(ConfirmButton.interactable, Is.False, "nothing is left to confirm");
            Assert.That(_presenter.IsOpen, Is.True, "the result stays on screen until the player closes it");
        }

        [Test]
        public async Task Confirm_WhilePending_BlocksAttributeAssignmentAndIgnoresASecondClick()
        {
            Bind();
            _hold = new TaskCompletionSource<bool>();
            SucceedOnce();
            Open();

            ConfirmButton.onClick.Invoke();
            ConfirmButton.onClick.Invoke();

            Assert.That(_presenter.IsRespecPending, Is.True);
            Assert.That(_gate.IsRespecPending, Is.True);
            Assert.That(_gate.IsAssignmentBlocked, Is.True, "the attribute panel cannot assign while the respec runs");
            Assert.That(_sentIds, Has.Count.EqualTo(1));
            Assert.That(Text("_messageText"), Is.EqualTo(TownRespecMessages.PendingMessage));
            Assert.That(ConfirmButton.interactable, Is.False);
            Assert.That(CancelButton.interactable, Is.False);

            _hold.SetResult(true);
            await Task.Yield();

            Assert.That(_gate.IsRespecPending, Is.False);
            Assert.That(_gate.IsAssignmentBlocked, Is.False);
            Assert.That(Text("_messageText"), Is.EqualTo(TownRespecMessages.AppliedMessage));
        }

        [Test]
        public void Confirm_WhenTheServerRejects_ShowsTheFailureAndKeepsTheProfile()
        {
            Bind();
            _responses.Enqueue((false, default, new BackendError { error = "INSUFFICIENT_FUNDS" }));
            Open();

            ConfirmButton.onClick.Invoke();

            Assert.That(Text("_messageText"), Is.EqualTo(TownRespecMessages.FailureMessage("INSUFFICIENT_FUNDS")));
            Assert.That(_gate.IsRespecPending, Is.False);
            Assert.That(_profile.Store.GetCurrency(), Is.EqualTo(RespecTestProfile.StartingGold));
            _profile.AssertAttributes(5, 3);
        }

        [Test]
        public void Cancel_ClosesThePanelWithoutSendingAnything()
        {
            Bind();
            Open();

            CancelButton.onClick.Invoke();

            Assert.That(_presenter.IsOpen, Is.False);
            Assert.That(_sentIds, Is.Empty);
            Assert.That(_reader.IsGameplayInputSuppressed, Is.False);
            Assert.That(_facing.CurrentFacing, Is.EqualTo(CharacterVisualDirection.South));
            _profile.AssertUnchanged();
        }

        [Test]
        public void Reopen_AfterCancel_OffersTheConfirmationAgain()
        {
            Bind();
            Open();
            CancelButton.onClick.Invoke();

            Open();

            Assert.That(Text("_confirmationText"), Is.EqualTo(TownRespecMessages.ConfirmationText(RespecTestProfile.Cost)));
            Assert.That(ConfirmButton.interactable, Is.True);
        }

        // ---- closing and input ownership ----

        [Test]
        public void Open_SuppressesGameplayInput_AndCloseButtonReleasesIt()
        {
            Bind();

            Open();
            Assert.That(_reader.IsGameplayInputSuppressed, Is.True);

            Field<Button>("_closeButton").onClick.Invoke();
            Assert.That(_presenter.IsOpen, Is.False);
            Assert.That(_reader.IsGameplayInputSuppressed, Is.False);
        }

        [Test]
        public void CloseRequestedFromTheKeyboard_ClosesAndConsumesTheKey()
        {
            Bind();
            Open();

            Assert.That(RaiseCloseRequested(), Is.True);
            Assert.That(_presenter.IsOpen, Is.False);
            Assert.That(_reader.IsGameplayInputSuppressed, Is.False);
            Assert.That(RaiseCloseRequested(), Is.False, "a closed panel no longer owns the key");
        }

        [Test]
        public void InteractPressedWhileOpen_ClosesThePanel()
        {
            Bind();
            Open();

            RaiseInteract();

            Assert.That(_presenter.IsOpen, Is.False);
            Assert.That(_reader.IsGameplayInputSuppressed, Is.False);
        }

        [Test]
        public void Close_WhileThePersistenceIsPending_KeepsTheRequestAndItsInterlock()
        {
            Bind();
            _hold = new TaskCompletionSource<bool>();
            SucceedOnce();
            Open();
            ConfirmButton.onClick.Invoke();

            _presenter.ClosePanel();

            Assert.That(_presenter.IsOpen, Is.False);
            Assert.That(_reader.IsGameplayInputSuppressed, Is.False);
            Assert.That(_gate.IsRespecPending, Is.True, "the request is still in flight, so assignment keeps waiting");

            _hold.SetResult(true);
        }

        [Test]
        public void Unbind_ClosesThePanel_AndReleasesTheInterlock()
        {
            Bind();
            Open();

            _presenter.UnbindProfile();

            Assert.That(_presenter.IsOpen, Is.False);
            Assert.That(_reader.IsGameplayInputSuppressed, Is.False);
            Assert.That(_gate.IsRespecPending, Is.False);
        }

        [Test]
        public void ProfileCommittedWhileOpen_RefreshesTheBuild()
        {
            Bind();
            Open();

            Assert.That(_profile.Store.ApplyConfirmedRespec(_profile.Result()), Is.EqualTo(StashOperationResult.Success));

            Assert.That(Text("_pointsToReturnText"), Is.EqualTo("Puntos que vuelven al pool: 0"));
            Assert.That(Text("_messageText"), Is.EqualTo(TownRespecMessages.NoAssignedPointsMessage));
        }
    }
}
#endif
