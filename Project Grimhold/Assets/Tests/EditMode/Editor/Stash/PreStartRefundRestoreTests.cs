#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEditor;

namespace Tests.EditMode.Stash
{
    public sealed class PreStartRefundRestoreTests
    {
        private readonly ProfileId _profile = new("47474747474747474747474747474747");
        private LootDefinitionCatalog _catalog;
        private InMemoryLocalProfileRepository _repository;
        private LocalProfileStore _store;
        private PendingLoadoutReservation _admitted;

        [SetUp]
        public void SetUp()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<LootDefinitionCatalog>(
                "Assets/Scriptable Objects/Loot/Catalogs/LootDefinitionCatalog.asset");
            Assert.That(_catalog, Is.Not.Null);
            _repository = new InMemoryLocalProfileRepository();
            Assert.That(_repository.Initialize(_profile, _catalog), Is.True);
            LocalProfileSnapshot snapshot = _repository.Snapshot.Clone();
            snapshot.Loadout.Add(new StashItem(new LootId("bone"), 2, 0));
            snapshot.PreparedEquipment = new PreparedEquipmentLoadout(new LootId("rapier"), default);
            Assert.That(_repository.TrySave(snapshot, out string error), Is.True, error);
            _store = new LocalProfileStore(_repository, _profile, _catalog);
            Assert.That(_store.TryCreateLoadoutReservation("reservation-a", out _admitted), Is.EqualTo(StashOperationResult.Success));
            // Admission: the local store consumes the reservation and the player owns nothing in Town.
            Assert.That(_store.TryConfirmLoadoutReservation("reservation-a"), Is.EqualTo(StashOperationResult.Success));
        }

        [Test]
        public void Restore_ReturnsAdmittedItemsAndEquipmentExactlyOnce()
        {
            Assert.That(_store.GetLoadout(), Is.Empty);
            Assert.That(_store.GetPreparedEquipment().HasAnyEquipment, Is.False);
            int commits = 0;
            _store.ProfileCommitted += _ => commits++;

            Assert.That(_store.TryRestoreRefundedReservation(_admitted), Is.EqualTo(StashOperationResult.Success));

            Assert.That(_store.GetLoadout(), Is.EqualTo(new[] { new StashItem(new LootId("bone"), 2, 0) }));
            Assert.That(_store.GetPreparedEquipment().WeaponSetAMainHand, Is.EqualTo(new LootId("rapier")));
            Assert.That(_store.PendingReservation, Is.Null);
            Assert.That(commits, Is.EqualTo(1));
            Assert.That(LocalProfileSaveCodec.TryDecode(LocalProfileSaveCodec.Encode(_repository.Snapshot),
                _profile, _catalog, out _, out _, out string error), Is.True, error);
        }

        [Test]
        public void Restore_Repeated_DoesNotDuplicateOrCommit()
        {
            Assert.That(_store.TryRestoreRefundedReservation(_admitted), Is.EqualTo(StashOperationResult.Success));
            string before = LocalProfileSaveCodec.Encode(_repository.Snapshot);
            int commits = 0;
            _store.ProfileCommitted += _ => commits++;

            Assert.That(_store.TryRestoreRefundedReservation(_admitted), Is.Not.EqualTo(StashOperationResult.Success));

            Assert.That(LocalProfileSaveCodec.Encode(_repository.Snapshot), Is.EqualTo(before));
            Assert.That(_store.GetLoadout(), Is.EqualTo(new[] { new StashItem(new LootId("bone"), 2, 0) }));
            Assert.That(commits, Is.Zero);
        }

        [Test]
        public void Restore_WithAnActiveReservation_RejectsWithoutChanges()
        {
            Assert.That(_store.TryRestoreRefundedReservation(_admitted), Is.EqualTo(StashOperationResult.Success));
            Assert.That(_store.TryCreateLoadoutReservation("reservation-b", out _), Is.EqualTo(StashOperationResult.Success));
            string before = LocalProfileSaveCodec.Encode(_repository.Snapshot);

            Assert.That(_store.TryRestoreRefundedReservation(_admitted), Is.EqualTo(StashOperationResult.InvalidInventory));

            Assert.That(LocalProfileSaveCodec.Encode(_repository.Snapshot), Is.EqualTo(before));
        }

        [Test]
        public void Restore_Null_Rejects()
        {
            string before = LocalProfileSaveCodec.Encode(_repository.Snapshot);
            Assert.That(_store.TryRestoreRefundedReservation(null), Is.EqualTo(StashOperationResult.InvalidInventory));
            Assert.That(LocalProfileSaveCodec.Encode(_repository.Snapshot), Is.EqualTo(before));
        }

        [Test]
        public void ConfirmedReservation_StillCannotBeRolledBack()
        {
            // Regression: refund is a separate operation; rollback after admission stays rejected.
            string before = LocalProfileSaveCodec.Encode(_repository.Snapshot);
            Assert.That(_store.TryRollbackLoadoutReservation("reservation-a"), Is.EqualTo(StashOperationResult.InvalidInventory));
            Assert.That(LocalProfileSaveCodec.Encode(_repository.Snapshot), Is.EqualTo(before));
        }
    }
}
#endif
