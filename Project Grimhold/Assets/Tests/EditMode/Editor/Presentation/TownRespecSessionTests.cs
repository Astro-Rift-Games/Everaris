#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;
using Tests.EditMode.Progression;

namespace Tests.EditMode.Presentation
{
    /// <summary>TASK-304 T3 (S1, S2, S3, S6, S8): Town respec intention, blocked states, confirmation and retries.</summary>
    public sealed class TownRespecSessionTests
    {
        private RespecTestProfile _profile;
        private AttributeRespecService _service;
        private TownRespecSession _session;
        private readonly List<string> _sentIds = new();
        private readonly Queue<(bool success, RespecResult data, BackendError error)> _responses = new();
        private TaskCompletionSource<bool> _gate;
        private int _reconciles;
        private Action _onReconcile;
        private bool _reconcileOk;
        private bool _canMutate;
        private bool _busy;

        [SetUp]
        public void SetUp()
        {
            _profile = new RespecTestProfile();
            _profile.Store.SetRespecCost(RespecTestProfile.Cost);
            Build();
        }

        [TearDown]
        public void TearDown()
        {
            _session?.Dispose();
            _profile.Dispose();
        }

        private void Build(RespecTestProfile profile = null)
        {
            _session?.Dispose();
            if (profile != null)
            {
                _profile.Dispose();
                _profile = profile;
            }

            _sentIds.Clear();
            _responses.Clear();
            _gate = null;
            _reconciles = 0;
            _onReconcile = null;
            _canMutate = true;
            _busy = false;
            _reconcileOk = true;
            _service = new AttributeRespecService(_profile.Store, Backend, () =>
            {
                _reconciles++;
                _onReconcile?.Invoke();
                return Task.FromResult(_reconcileOk);
            });
            _session = new TownRespecSession(_profile.Store, _service, () => _canMutate, () => _busy);
        }

        private async Task<(bool, RespecResult, BackendError)> Backend(string transactionId, int expectedRevision)
        {
            _sentIds.Add(transactionId);
            if (_gate != null) await _gate.Task;
            return _responses.Dequeue();
        }

        private void Succeed(bool replay = false) =>
            _responses.Enqueue((true, _profile.Result(alreadySecured: replay), default));

        private void Fail(string code) => _responses.Enqueue((false, default, new BackendError { error = code }));

        // ---- cost projection (S6) ----

        [Test]
        public void Presentation_ShowsTheServerCost_NeverAConstant()
        {
            Assert.That(_session.Presentation.Cost, Is.EqualTo(RespecTestProfile.Cost));
            Assert.That(_session.Presentation.CostText, Is.EqualTo("Costo: 100 de Oro"));

            _profile.Store.SetRespecCost(240);

            Assert.That(_session.Presentation.Cost, Is.EqualTo(240));
            Assert.That(_session.Presentation.CostText, Is.EqualTo("Costo: 240 de Oro"));
        }

        [Test]
        public void Presentation_WithAvailableGoldAndAssignedPoints_CanRespec()
        {
            TownRespecPresentation presentation = _session.Presentation;

            Assert.That(presentation.CanRespec, Is.True);
            Assert.That(presentation.BlockReason, Is.EqualTo(TownRespecBlockReason.None));
            Assert.That(presentation.BlockMessage, Is.Null);
            Assert.That(presentation.IsConfirming, Is.False);
            Assert.That(presentation.IsPending, Is.False);
        }

        [Test]
        public void Presentation_ExactlyEnoughGold_CanRespec()
        {
            Build(new RespecTestProfile(gold: RespecTestProfile.Cost));
            _profile.Store.SetRespecCost(RespecTestProfile.Cost);

            Assert.That(_session.Presentation.CanRespec, Is.True);
        }

        // ---- blocked states (S2, S8) ----

        [Test]
        public async Task Blocked_WhileReady_ExplainsTheLock_AndSendsNothing()
        {
            _canMutate = false;

            Assert.That(_session.Presentation.CanRespec, Is.False);
            Assert.That(_session.Presentation.BlockReason, Is.EqualTo(TownRespecBlockReason.BuildLocked));
            Assert.That(_session.RequestRespec(), Is.False);
            Assert.That(_session.Message, Is.EqualTo("Cancela Ready para reasignar atributos"));
            Assert.That(_session.IsConfirming, Is.False);
            await _session.ConfirmAsync();
            Assert.That(_sentIds, Is.Empty);
            _profile.AssertUnchanged();
        }

        [Test]
        public void Blocked_WithoutEnoughGold_NamesTheServerCost()
        {
            Build(new RespecTestProfile(gold: RespecTestProfile.Cost - 1));
            _profile.Store.SetRespecCost(RespecTestProfile.Cost);

            Assert.That(_session.Presentation.CanRespec, Is.False);
            Assert.That(_session.Presentation.BlockReason, Is.EqualTo(TownRespecBlockReason.InsufficientGold));
            Assert.That(_session.Presentation.BlockMessage,
                Is.EqualTo("No tenés Oro suficiente para reasignar (cuesta 100 de Oro)."));
            Assert.That(_session.RequestRespec(), Is.False);
            Assert.That(_session.Message, Is.EqualTo("No tenés Oro suficiente para reasignar (cuesta 100 de Oro)."));
            Assert.That(_sentIds, Is.Empty);
        }

        [Test]
        public void Blocked_InsufficientGoldMessage_FollowsAChangedServerCost()
        {
            _profile.Store.SetRespecCost((int)RespecTestProfile.StartingGold + 1);

            Assert.That(_session.Presentation.BlockMessage,
                Is.EqualTo("No tenés Oro suficiente para reasignar (cuesta 251 de Oro)."));
        }

        [Test]
        public void Blocked_WithoutAssignedPoints_ExplainsThereIsNothingToRespec()
        {
            Build(new RespecTestProfile(assigned: false));
            _profile.Store.SetRespecCost(RespecTestProfile.Cost);

            Assert.That(_session.Presentation.CanRespec, Is.False);
            Assert.That(_session.Presentation.BlockReason, Is.EqualTo(TownRespecBlockReason.NoAssignedPoints));
            Assert.That(_session.RequestRespec(), Is.False);
            Assert.That(_session.Message, Is.EqualTo("No hay puntos asignados para reasignar."));
            Assert.That(_sentIds, Is.Empty);
        }

        [Test]
        public void Blocked_WithAPendingRaidReservation_ExplainsIt()
        {
            LocalProfileSnapshot withReservation = _profile.Repository.Snapshot.Clone();
            withReservation.PendingReservation = new PendingLoadoutReservation("reservation", new List<StashItem>(), default);
            Assert.That(_profile.Repository.TrySave(withReservation, out _), Is.True);

            Assert.That(_session.Presentation.BlockReason, Is.EqualTo(TownRespecBlockReason.ReservationPending));
            Assert.That(_session.Presentation.BlockMessage,
                Is.EqualTo("No se puede reasignar mientras hay una reserva de Raid pendiente."));
        }

        [Test]
        public void Blocked_WhileAnotherAttributeOperationRuns_AsksToWait()
        {
            _busy = true;

            Assert.That(_session.Presentation.BlockReason, Is.EqualTo(TownRespecBlockReason.Busy));
            Assert.That(_session.RequestRespec(), Is.False);
            Assert.That(_session.Message, Is.EqualTo("Espera a que termine la operación en curso."));
        }

        [Test]
        public void Blocked_WhenTheServerCostIsNotKnown_NeverGuessesOne()
        {
            Build(new RespecTestProfile());

            Assert.That(_profile.Store.RespecCost, Is.Null);
            Assert.That(_session.Presentation.Cost, Is.Null);
            Assert.That(_session.Presentation.CostText, Is.EqualTo("Costo: —"));
            Assert.That(_session.Presentation.CanRespec, Is.False);
            Assert.That(_session.Presentation.BlockReason, Is.EqualTo(TownRespecBlockReason.CostUnavailable));
            Assert.That(_session.Presentation.BlockMessage, Is.EqualTo("No se pudo obtener el costo de la reasignación."));
        }

        [Test]
        public async Task RefreshCost_LoadsTheServerCost_AndNotifiesThePanel()
        {
            var profile = new RespecTestProfile();
            Build(profile);
            _service = new AttributeRespecService(profile.Store, Backend, () => Task.FromResult(true),
                () => Task.FromResult<int?>(130));
            _session.Dispose();
            _session = new TownRespecSession(profile.Store, _service, () => true);
            int changes = 0;
            _session.Changed += () => changes++;

            await _session.RefreshCostAsync();

            Assert.That(_session.Presentation.Cost, Is.EqualTo(130));
            Assert.That(_session.Presentation.CanRespec, Is.True);
            Assert.That(changes, Is.EqualTo(1));
        }

        // ---- explicit confirmation (S6) ----

        [Test]
        public void Request_OpensTheConfirmation_WithoutSendingOrChangingAnything()
        {
            int changes = 0;
            _session.Changed += () => changes++;

            Assert.That(_session.RequestRespec(), Is.True);

            Assert.That(_session.IsConfirming, Is.True);
            Assert.That(_session.Presentation.IsConfirming, Is.True);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(_sentIds, Is.Empty);
            _profile.AssertUnchanged();
        }

        [Test]
        public void Confirmation_StatesTheCost_TheReset_ThePointsReturn_AndTheUnequip()
        {
            _session.RequestRespec();

            string text = _session.Presentation.ConfirmationText;

            Assert.That(text, Does.Contain("100 de Oro"));
            Assert.That(text, Does.Contain("todos tus atributos volverán a 0").IgnoreCase);
            Assert.That(text, Does.Contain("puntos").And.Contain("pool disponible"));
            Assert.That(text, Does.Contain("desequiparán").And.Contain("armas").And.Contain("habilidades"));
            Assert.That(_session.Presentation.CanRespec, Is.True);
        }

        [Test]
        public void Cancel_ClosesTheConfirmation_WithoutSending()
        {
            _session.RequestRespec();

            _session.Cancel();

            Assert.That(_session.IsConfirming, Is.False);
            Assert.That(_session.Presentation.ConfirmationText, Is.Null);
            Assert.That(_sentIds, Is.Empty);
            _profile.AssertUnchanged();
        }

        [Test]
        public async Task Confirm_WithoutARequest_SendsNothing()
        {
            await _session.ConfirmAsync();

            Assert.That(_sentIds, Is.Empty);
            _profile.AssertUnchanged();
        }

        // ---- success (S1) ----

        [Test]
        public async Task Confirm_AppliesTheSnapshotInOneCommit_AndReportsIt()
        {
            _session.RequestRespec();
            Succeed();

            await _session.ConfirmAsync();

            Assert.That(_sentIds, Has.Count.EqualTo(1));
            Assert.That(Regex.IsMatch(_sentIds[0], "^[0-9]+_[0-9a-f]{32}$"), Is.True, _sentIds[0]);
            Assert.That(_profile.Commits, Is.EqualTo(1));
            _profile.AssertAttributes(0, RespecTestProfile.OwnedPoints);
            Assert.That(_profile.Store.GetCurrency(), Is.EqualTo(RespecTestProfile.StartingGold - RespecTestProfile.Cost));
            Assert.That(_profile.Store.GetPreparedEquipment().Get(EquipmentSlot.WeaponSetAMainHand).IsValid, Is.False);
            Assert.That(_profile.Store.GetPreparedAbilities().Slot1.IsValid, Is.False);
            Assert.That(_session.Message, Is.EqualTo("Atributos reasignados"));
            Assert.That(_session.IsConfirming || _session.IsPending, Is.False);
            Assert.That(_session.Presentation.BlockReason, Is.EqualTo(TownRespecBlockReason.NoAssignedPoints),
                "after the respec nothing is left to redistribute");
        }

        // ---- pending guard ----

        [Test]
        public async Task Confirm_WhilePending_NeverSendsASecondRequest()
        {
            _session.RequestRespec();
            _gate = new TaskCompletionSource<bool>();
            Succeed();

            Task first = _session.ConfirmAsync();
            Assert.That(_session.IsPending, Is.True);
            Assert.That(_session.Presentation.IsPending, Is.True);
            Assert.That(_session.Presentation.CanRespec, Is.False);
            Assert.That(_session.RequestRespec(), Is.False);
            await _session.ConfirmAsync();
            _gate.SetResult(true);
            await first;

            Assert.That(_sentIds, Has.Count.EqualTo(1));
            Assert.That(_profile.Commits, Is.EqualTo(1));
            Assert.That(_session.IsPending, Is.False);
        }

        // ---- idempotent retry (S3) ----

        [Test]
        public async Task LostResponse_RetryReusesTheTransactionId_AndTheReplayChargesOnce()
        {
            _session.RequestRespec();
            _reconcileOk = false; // the server applied the respec but the reread did not arrive either
            Fail(BackendErrorUtility.Timeout);

            await _session.ConfirmAsync();

            Assert.That(_session.Message, Is.EqualTo(TownRespecMessages.UnsyncedMessage));
            _profile.AssertUnchanged();

            _reconcileOk = true;
            Succeed(replay: true);
            Assert.That(_session.RequestRespec(), Is.True);
            await _session.ConfirmAsync();

            Assert.That(_sentIds, Has.Count.EqualTo(2));
            Assert.That(_sentIds[1], Is.EqualTo(_sentIds[0]));
            Assert.That(_profile.Commits, Is.EqualTo(1));
            Assert.That(_profile.Store.GetCurrency(), Is.EqualTo(RespecTestProfile.StartingGold - RespecTestProfile.Cost));
            _profile.AssertAttributes(0, RespecTestProfile.OwnedPoints);
            Assert.That(_session.Message, Is.EqualTo("Atributos reasignados"));
        }

        [Test]
        public async Task LostResponse_WhenTheRereadShowsTheRespec_NothingIsLeftToRetry()
        {
            _session.RequestRespec();
            Fail(BackendErrorUtility.Timeout);
            _onReconcile = () => _profile.Store.ApplyConfirmedRespec(_profile.Result()); // the server did apply it

            await _session.ConfirmAsync();

            Assert.That(_session.Message, Is.EqualTo(TownRespecMessages.UnconfirmedMessage));
            Assert.That(_reconciles, Is.EqualTo(1));
            Assert.That(_session.Presentation.BlockReason, Is.EqualTo(TownRespecBlockReason.NoAssignedPoints));
            Assert.That(_profile.Store.GetCurrency(), Is.EqualTo(RespecTestProfile.StartingGold - RespecTestProfile.Cost));
        }

        [Test]
        public async Task LostResponseThatDidNotApply_RetryReusesTheIdAndApplies()
        {
            _session.RequestRespec();
            Fail(BackendErrorUtility.NetworkError);
            await _session.ConfirmAsync();
            Assert.That(_session.Message, Is.EqualTo(TownRespecMessages.UnconfirmedMessage));

            Succeed();
            Assert.That(_session.RequestRespec(), Is.True);
            await _session.ConfirmAsync();

            Assert.That(_sentIds[1], Is.EqualTo(_sentIds[0]));
            Assert.That(_profile.Store.GetCurrency(), Is.EqualTo(RespecTestProfile.StartingGold - RespecTestProfile.Cost));
        }

        [Test]
        public async Task UnconfirmedThenCancelled_StillRetriesWithTheSameId()
        {
            _session.RequestRespec();
            Fail(BackendErrorUtility.Timeout);
            await _session.ConfirmAsync();
            _session.RequestRespec();
            _session.Cancel();

            Succeed();
            _session.RequestRespec();
            await _session.ConfirmAsync();

            Assert.That(_sentIds[1], Is.EqualTo(_sentIds[0]));
        }

        [Test]
        public async Task UnconfirmedThenProfileCommit_NextConfirmUsesANewTransactionId()
        {
            // The server applied the respec but the answer and the reread were lost, so the id is kept for a retry.
            _session.RequestRespec();
            Fail(BackendErrorUtility.Timeout);
            await _session.ConfirmAsync();
            Assert.That(_session.Message, Is.EqualTo(TownRespecMessages.UnconfirmedMessage));

            // The player then assigns a point: the profile commits, so the authoritative state moved on.
            Assert.That(CharacterAttributeState.TryCreate(6, 5, 5, 5, 5, 5, 2, out CharacterAttributeState assigned), Is.True);
            _profile.Store.ForceCharacterAttributeState(assigned);
            Assert.That(_profile.Commits, Is.EqualTo(1));

            Succeed();
            Assert.That(_session.RequestRespec(), Is.True);
            await _session.ConfirmAsync();

            Assert.That(_sentIds, Has.Count.EqualTo(2));
            Assert.That(_sentIds[1], Is.Not.EqualTo(_sentIds[0]),
                "a replayed id would make the server return the old receipt and reset nothing");
        }

        [Test]
        public async Task DefinitiveRejection_ReleasesTheId_SoTheNextIntentUsesANewOne()
        {
            _session.RequestRespec();
            Fail("REVISION_CONFLICT");
            await _session.ConfirmAsync();

            Succeed();
            Assert.That(_session.RequestRespec(), Is.True);
            await _session.ConfirmAsync();

            Assert.That(_sentIds, Has.Count.EqualTo(2));
            Assert.That(_sentIds[1], Is.Not.EqualTo(_sentIds[0]));
        }

        // ---- rejections (S2) ----

        [TestCase("NO_ASSIGNED_POINTS", "No hay puntos asignados para reasignar.")]
        [TestCase("INSUFFICIENT_FUNDS", "No tenés Oro suficiente para reasignar.")]
        [TestCase("RESERVATION_PENDING", "No se puede reasignar mientras hay una reserva de Raid pendiente.")]
        [TestCase("REVISION_CONFLICT", "El perfil cambió. Vuelve a intentarlo.")]
        [TestCase("INVENTORY_FULL", "No hay lugar en el Inventario para desequipar las armas que dejan de cumplir sus requisitos.")]
        [TestCase("TRANSACTION_ID_REUSED", TownRespecMessages.UnconfirmedMessage)]
        [TestCase("TRANSACTION_RETENTION_EXCEEDED", TownRespecMessages.UnconfirmedMessage)]
        [TestCase("SOMETHING_UNKNOWN", "El servidor rechazó la reasignación.")]
        public async Task Rejection_LeavesTheProfileUntouched_WithAnExplicitSpanishMessage(string code, string message)
        {
            _session.RequestRespec();
            Fail(code);

            await _session.ConfirmAsync();

            Assert.That(_session.Message, Is.EqualTo(message));
            Assert.That(TownRespecMessages.FailureMessage(code), Is.EqualTo(message));
            Assert.That(_session.IsConfirming || _session.IsPending, Is.False);
            _profile.AssertUnchanged();
        }

        [Test]
        public async Task ReadyAfterTheRequest_BlocksTheConfirmation_WithoutSending()
        {
            _session.RequestRespec();
            _canMutate = false;

            await _session.ConfirmAsync();

            Assert.That(_sentIds, Is.Empty);
            Assert.That(_session.Message, Is.EqualTo("Cancela Ready para reasignar atributos"));
            Assert.That(_session.IsConfirming, Is.False);
            _profile.AssertUnchanged();
        }

        [Test]
        public void ACommitThatLeavesNothingToRespec_ClosesTheOpenConfirmation()
        {
            _session.RequestRespec();

            // e.g. a reconciliation brought the zeroed build while the dialog was open
            _profile.Store.ApplyConfirmedRespec(_profile.Result());

            Assert.That(_session.IsConfirming, Is.False);
            Assert.That(_session.Message, Is.EqualTo("No hay puntos asignados para reasignar."));
        }

        [Test]
        public async Task Unavailable_WithoutAService_BlocksTheIntentSilently()
        {
            using var session = new TownRespecSession(_profile.Store, null, () => true);

            Assert.That(session.Presentation.CanRespec, Is.False);
            Assert.That(session.Presentation.BlockReason, Is.EqualTo(TownRespecBlockReason.Unavailable));
            Assert.That(session.RequestRespec(), Is.False);
            await session.ConfirmAsync();
            _profile.AssertUnchanged();
        }
    }
}
#endif
