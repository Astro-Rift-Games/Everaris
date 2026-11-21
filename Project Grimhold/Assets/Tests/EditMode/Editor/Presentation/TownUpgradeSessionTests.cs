#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Grimhold.Backend;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode.Presentation
{
    /// <summary>TASK-303 T4 (S1, S4, S5, S6, S9): Town NPC upgrade intention, single confirmation and reconciliation.</summary>
    public sealed class TownUpgradeSessionTests
    {
        private const string ArmorId = "123456789012345678901234";
        private const string RapierId = "223456789012345678901234";
        private const string WandId = "323456789012345678901234";
        private readonly LootId _armor = new("heavy_armor_breastplate");
        private readonly LootId _rapier = new("rapier");
        private readonly LootId _wand = new("magic_wand");
        private readonly LootId _iron = new("RES_BLACK_IRON");
        private readonly LootId _rune = new("RES_RUNE_FRAGMENT");
        private LootDefinitionCatalog _catalog;
        private InMemoryLocalProfileRepository _repository;
        private LocalProfileStore _store;
        private GameObject _services;
        private FakeRemote _remote;
        private StashTransferCoordinator _coordinator;
        private TownUpgradeSession _session;
        private EquipmentInstance _armorInstance;
        private int _commits;

        [SetUp]
        public void SetUp()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>("Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");
            var profile = new ProfileId("49494949494949494949494949494949");
            _repository = new InMemoryLocalProfileRepository();
            Assert.That(_repository.Initialize(profile, _catalog), Is.True);
            _armorInstance = new EquipmentInstance(ArmorId);
            var next = _repository.Snapshot.Clone();
            next.InventorySchemaVersion = 3;
            next.RemoteRevision = 7;
            next.Currency = 100;
            next.Loadout.Add(new StashItem(_rapier, 1, 4, new EquipmentInstance(RapierId)));
            next.Loadout.Add(new StashItem(_wand, 1, 5, new EquipmentInstance(WandId)));
            next.Loadout.Add(new StashItem(_iron, 2, 6));
            next.Loadout.Add(new StashItem(_rune, 1, 7));
            next.Stash.Add(new StashItem(_rune, 5, 19));
            next.PreparedEquipment = default(PreparedEquipmentLoadout).With(EquipmentSlot.Armor, _armor, _armorInstance);
            Assert.That(_repository.TrySave(next, out _), Is.True);
            _store = new LocalProfileStore(_repository, profile, _catalog);
            _services = new GameObject("Upgrade session test services");
            var stash = _services.AddComponent<InMemoryPlayerStashService>(); stash.Initialize(_store);
            var loadout = _services.AddComponent<InMemoryPlayerLoadoutService>(); loadout.Initialize(_store);
            _remote = new FakeRemote();
            // A working backend reread, as in Town; tests needing a failing or counted reread build their own.
            _coordinator = new StashTransferCoordinator(profile, stash, loadout, _remote, new FakeReconcile());
            _session = new TownUpgradeSession(_store, _coordinator, _catalog, TownMerchantType.Blacksmith);
            _store.ProfileCommitted += _ => _commits++;
            _commits = 0;
        }

        [TearDown] public void TearDown() { _session?.Dispose(); UnityEngine.Object.DestroyImmediate(_services); }

        private TownUpgradeTarget Target(LootId lootId) => _session.Targets().Single(t => t.LootId == lootId);

        [Test]
        public void Targets_BlacksmithListsEquippedArmorAndInventoryPhysicalWeapons_Only()
        {
            var targets = _session.Targets();
            Assert.That(targets.Select(t => t.LootId.Value), Is.EquivalentTo(new[] { "heavy_armor_breastplate", "rapier" }));
            var armor = Target(_armor);
            Assert.That(armor.IsEquipped, Is.True);
            Assert.That(armor.EquipmentSlot, Is.EqualTo(EquipmentSlot.Armor));
            Assert.That(Target(_rapier).IsEquipped, Is.False);
            Assert.That(Target(_rapier).LoadoutSlot, Is.EqualTo(4));

            using var arcanist = new TownUpgradeSession(_store, _coordinator, _catalog, TownMerchantType.Arcanist);
            Assert.That(arcanist.Targets().Select(t => t.LootId.Value), Is.EqualTo(new[] { "magic_wand" }));
        }

        [Test]
        public void Preview_CountsInventoryOnly_FlagsStashAndNeverMutates()
        {
            var preview = _session.Preview(Target(_rapier), NpcUpgradeOperation.PrimaryScaling);
            Assert.That(preview.GoldAvailable, Is.EqualTo(100));
            Assert.That(preview.Materials[0].InInventory, Is.EqualTo(1));
            Assert.That(preview.Materials[0].InStash, Is.EqualTo(5));
            Assert.That(preview.BlockReason, Is.EqualTo(NpcUpgradeBlockReason.MissingMaterials));
            Assert.That(_session.Select(Target(_rapier), NpcUpgradeOperation.PrimaryScaling), Is.False);
            Assert.That(_session.Message, Is.EqualTo("Faltan materiales en el Inventario; los del Stash no se usan para mejorar."));
            Assert.That(_session.IsConfirming, Is.False);
            AssertUnchanged();
        }

        [Test]
        public void Select_EquippedArmor_BuildsExactBackendIntent()
        {
            Assert.That(_session.Select(Target(_armor), NpcUpgradeOperation.ArmorLevel), Is.True);
            Assert.That(_session.IsConfirming, Is.True);
            var request = _session.Request;
            Assert.That(request.merchant, Is.EqualTo("blacksmith"));
            Assert.That(request.operation, Is.EqualTo("armor_level"));
            Assert.That(request.instanceId, Is.EqualTo(ArmorId));
            Assert.That(request.location, Is.EqualTo("equipment"));
            Assert.That(request.equipmentSlot, Is.EqualTo(EquipmentSlot.Armor));
            Assert.That(request.expectedRevision, Is.EqualTo(7));
            Assert.That(Regex.IsMatch(request.transactionId, "^[0-9]+_[0-9a-f]{32}$"), Is.True, request.transactionId);
            var json = Newtonsoft.Json.Linq.JObject.Parse(InventoryRequestJson.Encode(request));
            Assert.That(json["location"].ToString(), Is.EqualTo("equipment"));
            Assert.That(json["slot"].ToString(), Is.EqualTo("armor"));
            AssertUnchanged();
        }

        [Test]
        public void LoadoutRequest_SendsNumericSlot()
        {
            _store.ReconcileRemoteState(InventoryWith(currency: 999, runes: 2), null, _catalog, null);
            Assert.That(_session.Select(Target(_rapier), NpcUpgradeOperation.PrimaryScaling), Is.True);
            var json = Newtonsoft.Json.Linq.JObject.Parse(InventoryRequestJson.Encode(_session.Request));
            Assert.That(json["location"].ToString(), Is.EqualTo("loadout"));
            Assert.That(json["slot"].Type, Is.EqualTo(Newtonsoft.Json.Linq.JTokenType.Integer));
            Assert.That((int)json["slot"], Is.EqualTo(4));
        }

        [Test]
        public async Task Confirm_AppliesAuthoritativeResult_KeepsArmorEquippedWithNewLevel_AndCallsOnce()
        {
            Assert.That(_session.Select(Target(_armor), NpcUpgradeOperation.ArmorLevel), Is.True);
            _remote.Result = SuccessResult();
            var pending = _session.ConfirmAsync();
            await _session.ConfirmAsync(); // a second confirmation while pending never sends again
            Assert.That(_session.IsPending, Is.True);
            _remote.Gate.SetResult(true);
            await pending;

            Assert.That(_remote.Calls, Is.EqualTo(1));
            var equipped = _store.GetPreparedEquipment();
            Assert.That(equipped.Get(EquipmentSlot.Armor), Is.EqualTo(_armor));
            Assert.That(equipped.GetInstance(EquipmentSlot.Armor).InstanceId, Is.EqualTo(ArmorId));
            Assert.That(equipped.GetInstance(EquipmentSlot.Armor).ArmorUpgradeLevel, Is.EqualTo(1));
            Assert.That(_store.GetCurrency(), Is.EqualTo(60));
            Assert.That(_store.RemoteRevision, Is.EqualTo(8));
            Assert.That(_store.GetLoadout().Any(i => i.LootId == _iron), Is.False);
            Assert.That(_session.Message, Is.EqualTo("Mejora aplicada"));
            Assert.That(_session.IsPending || _session.IsConfirming, Is.False);
        }

        [TestCase("INSUFFICIENT_FUNDS", "Oro insuficiente para esta mejora")]
        [TestCase("INSUFFICIENT_MATERIALS", "Faltan materiales en el Inventario; los del Stash no se usan para mejorar.")]
        [TestCase("UPGRADE_LIMIT_REACHED", NpcUpgradeMessages.NpcLimit)]
        [TestCase("REVISION_CONFLICT", "El inventario cambió. Vuelve a intentarlo")]
        [TestCase("SOMETHING_UNKNOWN", "El servidor rechazó la mejora")]
        public async Task Rejection_LeavesLocalStateUnchanged_WithExplicitMessage(string code, string message)
        {
            Assert.That(_session.Select(Target(_armor), NpcUpgradeOperation.ArmorLevel), Is.True);
            _remote.Error = new BackendError { error = code };
            var pending = _session.ConfirmAsync();
            _remote.Gate.SetResult(false);
            await pending;

            Assert.That(_remote.Calls, Is.EqualTo(1));
            Assert.That(_session.Message, Is.EqualTo(message));
            AssertUnchanged();
        }

        [Test]
        public async Task StaleRevision_IsRejectedLocally_WithoutCallingTheBackend()
        {
            Assert.That(_session.Select(Target(_armor), NpcUpgradeOperation.ArmorLevel), Is.True);
            var request = _session.Request;
            var outcome = await _coordinator.UpgradeItemAsync(new UpgradeItemRequest
            {
                transactionId = request.transactionId, expectedRevision = 6, merchant = request.merchant, operation = request.operation,
                instanceId = request.instanceId, location = request.location, equipmentSlot = request.equipmentSlot
            }, _store);
            Assert.That(outcome.RemoteErrorCode, Is.EqualTo("REVISION_CONFLICT"));
            Assert.That(_remote.Calls, Is.Zero);
            AssertUnchanged();
        }

        [TestCase("TIMEOUT")]
        [TestCase("NETWORK_ERROR")]
        [TestCase("INVALID_INVENTORY")]
        [TestCase("REVISION_CONFLICT")]
        public async Task UnconfirmedOrConflictingResult_ReconcilesOnce_NeverResends(string code)
        {
            var reconcile = new FakeReconcile();
            var coordinator = new StashTransferCoordinator(_store.ProfileId, _services.GetComponent<InMemoryPlayerStashService>(),
                _services.GetComponent<InMemoryPlayerLoadoutService>(), _remote, reconcile);
            using var session = new TownUpgradeSession(_store, coordinator, _catalog, TownMerchantType.Blacksmith);
            Assert.That(session.Select(session.Targets().Single(t => t.LootId == _armor), NpcUpgradeOperation.ArmorLevel), Is.True);
            _remote.Error = new BackendError { error = code };
            var pending = session.ConfirmAsync();
            _remote.Gate.SetResult(false);
            await pending;

            Assert.That(_remote.Calls, Is.EqualTo(1));
            Assert.That(reconcile.Calls, Is.EqualTo(1));
            Assert.That(session.Message, Is.EqualTo(code == "REVISION_CONFLICT"
                ? "El inventario cambió. Vuelve a intentarlo" : "No se pudo confirmar la mejora; el inventario se sincronizó"));
        }

        [Test]
        public async Task TerminalRejection_DoesNotReconcile()
        {
            var reconcile = new FakeReconcile();
            var coordinator = new StashTransferCoordinator(_store.ProfileId, _services.GetComponent<InMemoryPlayerStashService>(),
                _services.GetComponent<InMemoryPlayerLoadoutService>(), _remote, reconcile);
            using var session = new TownUpgradeSession(_store, coordinator, _catalog, TownMerchantType.Blacksmith);
            Assert.That(session.Select(session.Targets().Single(t => t.LootId == _armor), NpcUpgradeOperation.ArmorLevel), Is.True);
            _remote.Error = new BackendError { error = "INSUFFICIENT_FUNDS" };
            var pending = session.ConfirmAsync();
            _remote.Gate.SetResult(false);
            await pending;
            Assert.That(reconcile.Calls, Is.Zero);
            AssertUnchanged();
        }

        [Test]
        public void ReplayedResult_AppliesOnce_SecondApplyIsRejectedWithoutCommit()
        {
            var replay = SuccessResult();
            replay.alreadySecured = true;
            Assert.That(_store.ApplyConfirmedUpgrade(replay, 7), Is.EqualTo(StashOperationResult.Success));
            int commits = _commits;
            Assert.That(_store.ApplyConfirmedUpgrade(replay, 7), Is.EqualTo(StashOperationResult.InvalidInventory));
            Assert.That(_commits, Is.EqualTo(commits));
            Assert.That(_store.GetPreparedEquipment().GetInstance(EquipmentSlot.Armor).ArmorUpgradeLevel, Is.EqualTo(1));
        }

        [TestCase("TIMEOUT")]
        [TestCase("NETWORK_ERROR")]
        public async Task UnconfirmedResult_ReconcileFails_DoesNotClaimSync(string code)
        {
            var reconcile = new FakeReconcile { Succeeds = false };
            var coordinator = new StashTransferCoordinator(_store.ProfileId, _services.GetComponent<InMemoryPlayerStashService>(),
                _services.GetComponent<InMemoryPlayerLoadoutService>(), _remote, reconcile);
            using var session = new TownUpgradeSession(_store, coordinator, _catalog, TownMerchantType.Blacksmith);
            Assert.That(session.Select(session.Targets().Single(t => t.LootId == _armor), NpcUpgradeOperation.ArmorLevel), Is.True);
            _remote.Error = new BackendError { error = code };
            var pending = session.ConfirmAsync();
            _remote.Gate.SetResult(false);
            await pending;

            Assert.That(_remote.Calls, Is.EqualTo(1));
            Assert.That(reconcile.Calls, Is.EqualTo(1));
            Assert.That(session.Message, Is.EqualTo(TownUpgradeSession.UnsyncedMessage));
            Assert.That(session.Message, Does.Not.Contain("se sincronizó"));
            AssertUnchanged();
        }

        [Test]
        public void FailureMessage_BackendInternalServerError_IsUnconfirmed() =>
            Assert.That(TownUpgradeSession.FailureMessage("INTERNAL_SERVER_ERROR"), Is.EqualTo(TownUpgradeSession.UnconfirmedMessage));

        [Test]
        public void OutcomeMessage_UsesStatusBeforeCode()
        {
            Assert.That(TownUpgradeSession.OutcomeMessage(StashTransferOutcome.Success()), Is.EqualTo("Mejora aplicada"));
            Assert.That(TownUpgradeSession.OutcomeMessage(StashTransferOutcome.Diverged(StashOperationResult.InvalidInventory)),
                Is.EqualTo("La mejora se confirmó; el inventario se sincronizó con el servidor"));
            Assert.That(TownUpgradeSession.OutcomeMessage(StashTransferOutcome.Unavailable()),
                Is.EqualTo("El servicio de mejoras no está disponible"));
            Assert.That(TownUpgradeSession.OutcomeMessage(
                    StashTransferOutcome.RecoveryFailed(StashOperationResult.Success, null, null)),
                Is.EqualTo(TownUpgradeSession.UnsyncedMessage));
            Assert.That(TownUpgradeSession.OutcomeMessage(StashTransferOutcome.RemoteFailed(null, "INSUFFICIENT_FUNDS")),
                Is.EqualTo("Oro insuficiente para esta mejora"));
        }

        [TestCase("INTERNAL_SERVER_ERROR")]
        [TestCase("REMOTE_RESULT_UNKNOWN")]
        [TestCase("TRANSACTION_ID_REUSED")]
        [TestCase(null)]
        public void FailureMessage_PossiblyCommitted_IsUnconfirmed(string code) =>
            Assert.That(TownUpgradeSession.FailureMessage(code), Is.EqualTo(TownUpgradeSession.UnconfirmedMessage));

        [Test]
        public void OutcomeMessage_RecoveryFailed_IgnoresCode() =>
            Assert.That(TownUpgradeSession.OutcomeMessage(
                    StashTransferOutcome.RecoveryFailed(StashOperationResult.Success, null, "INTERNAL_SERVER_ERROR")),
                Does.Not.Contain("se sincronizó"));

        [Test]
        public async Task Success_LocalApplyFails_ReconcilesOnce_ReportsConfirmed()
        {
            var reconcile = new FakeReconcile();
            var coordinator = new StashTransferCoordinator(_store.ProfileId, _services.GetComponent<InMemoryPlayerStashService>(),
                _services.GetComponent<InMemoryPlayerLoadoutService>(), _remote, reconcile);
            using var session = new TownUpgradeSession(_store, coordinator, _catalog, TownMerchantType.Blacksmith);
            Assert.That(session.Select(session.Targets().Single(t => t.LootId == _armor), NpcUpgradeOperation.ArmorLevel), Is.True);
            var stale = SuccessResult();
            stale.revision = 7; // does not advance, so the store refuses it
            _remote.Result = stale;
            var pending = session.ConfirmAsync();
            _remote.Gate.SetResult(true);
            await pending;

            Assert.That(_remote.Calls, Is.EqualTo(1));
            Assert.That(reconcile.Calls, Is.EqualTo(1));
            Assert.That(session.Message, Is.EqualTo("La mejora se confirmó; el inventario se sincronizó con el servidor"));
        }

        private sealed class FakeReconcile : IProfileReconciliation
        {
            public int Calls;
            public bool Succeeds = true;
            public Task<bool> ReconcileAsync() { Calls++; return Task.FromResult(Succeeds); }
        }

        private InventoryData InventoryWith(long currency, int runes) => new InventoryData
        {
            inventorySchemaVersion = 3, revision = 7, currency = currency,
            loadout = new[]
            {
                Item(4, _rapier, 1, RapierId), Item(5, _wand, 1, WandId), Item(6, _iron, 2, null), Item(7, _rune, runes, null)
            },
            stash = new[] { Item(19, _rune, 5, null) },
            preparedEquipment = PreparedEquipmentData.FromLoadout(_store.GetPreparedEquipment())
        };

        private UpgradeItemResult SuccessResult() => new UpgradeItemResult
        {
            revision = 8, currency = 60, alreadySecured = false,
            loadout = new[] { Item(4, _rapier, 1, RapierId), Item(5, _wand, 1, WandId), Item(7, _rune, 1, null) },
            stash = new[] { Item(19, _rune, 5, null) },
            preparedEquipment = PreparedEquipmentData.FromLoadout(
                default(PreparedEquipmentLoadout).With(EquipmentSlot.Armor, _armor, new EquipmentInstance(ArmorId, null, 1)))
        };

        private static InventoryItemData Item(int slot, LootId id, int amount, string instanceId) => new InventoryItemData
        {
            slot = slot, lootId = id.Value, amount = amount,
            instance = instanceId == null ? null : new EquipmentInstanceData { instanceId = instanceId }
        };

        private void AssertUnchanged()
        {
            Assert.That(_store.RemoteRevision, Is.EqualTo(7));
            Assert.That(_store.GetCurrency(), Is.EqualTo(100));
            Assert.That(_store.GetPreparedEquipment().GetInstance(EquipmentSlot.Armor), Is.EqualTo(_armorInstance));
            Assert.That(_store.GetLoadout().Count, Is.EqualTo(4));
            Assert.That(_store.GetStash(), Is.EqualTo(new[] { new StashItem(_rune, 5, 19) }));
            Assert.That(_commits, Is.Zero);
        }

        private sealed class FakeRemote : IRemoteInventoryService, IRemoteUpgradeService
        {
            public int Calls;
            public UpgradeItemResult Result;
            public BackendError Error;
            public readonly TaskCompletionSource<bool> Gate = new();
            public async Task<(bool success, UpgradeItemResult data, BackendError error)> UpgradeItemAsync(UpgradeItemRequest request)
            { Calls++; return (await Gate.Task, Result, Error); }
            public Task<(bool success, BackendError error)> OrderAsync(InventoryList list, IReadOnlyList<LootId> ids) => throw new NotSupportedException();
            public Task<(bool success, BackendError error)> MoveToLoadoutAsync(LootId id, int amount) => throw new NotSupportedException();
            public Task<(bool success, BackendError error)> MoveToStashAsync(LootId id, int amount) => throw new NotSupportedException();
            public Task<(bool success, BackendError error)> UpdatePreparedEquipmentAsync(PreparedEquipmentLoadout equipment) => throw new NotSupportedException();
        }
    }
}
#endif
