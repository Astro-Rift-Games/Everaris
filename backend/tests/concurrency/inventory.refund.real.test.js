'use strict';
// FIX-95: concurrent refund / start-marker calls against a real database.
const test = require('node:test');
const assert = require('node:assert/strict');
const mongoose = require('mongoose');
const { startMemoryMongo } = require('../support/memoryMongo');

const Character = require('../../src/models/Character');
const InventoryService = require('../../src/services/InventoryService');

const raw = accountId => mongoose.connection.collection('characters').findOne({ accountId });

async function seedAdmitted(raidId = 'raid-A', { admit = true } = {}) {
  const accountId = new mongoose.Types.ObjectId();
  await mongoose.connection.collection('characters').insertOne({
    accountId, name: `Race${accountId}`, revision: 3, unlockedAbilities: [],
    inventory: {
      inventorySchemaVersion: 2, currency: 0,
      stash: [{ slot: 0, lootId: 'bone', amount: 2 }],
      loadout: [{ slot: 1, lootId: 'stick', amount: 4 }],
      preparedEquipment: { weaponSlot1: 'arming_sword' },
      pendingReservation: null, appliedExtractionReceipts: []
    }
  });
  await InventoryService.savePendingReservation(accountId, 'res-1');
  await mongoose.connection.collection('characters').updateOne({ accountId },
    { $set: { 'inventory.pendingReservation.raidId': raidId } });
  if (admit) await InventoryService.admitPendingReservation(accountId, 'res-1');
  return accountId;
}

test('FIX-95 refund concurrency', async t => {
  let mongoServer;
  t.before(async () => {
    mongoServer = await startMemoryMongo();
    await mongoose.connect(mongoServer.getUri());
  });
  t.after(async () => {
    await mongoose.disconnect();
    await mongoServer.stop();
  });
  t.afterEach(async () => { await Character.deleteMany({}); });

  await t.test('concurrent refunds restore the reserved items exactly once', async () => {
    const accountId = await seedAdmitted();
    const admitted = await raw(accountId);

    const results = await Promise.allSettled(Array.from({ length: 6 },
      () => InventoryService.refundPendingReservation(accountId, 'res-1')));

    const fulfilled = results.filter(r => r.status === 'fulfilled');
    const rejected = results.filter(r => r.status === 'rejected');
    assert.ok(fulfilled.length >= 1);
    for (const failure of rejected) {
      assert.equal(failure.reason.statusCode, 409);
      assert.equal(failure.reason.errorCode, 'REVISION_CONFLICT');
    }
    assert.equal(fulfilled.filter(r => r.value.revision === admitted.revision + 1).length, fulfilled.length,
      'every success reports the single refund revision');

    const after = await raw(accountId);
    assert.equal(after.revision, admitted.revision + 1, 'exactly one write');
    assert.equal(after.inventory.pendingReservation, null);
    assert.deepEqual(after.inventory.loadout, [{ slot: 1, lootId: 'stick', amount: 4 }]);
    assert.equal(after.inventory.preparedEquipment.weaponSlot1, 'arming_sword');
    assert.deepEqual(after.inventory.stash, [{ slot: 0, lootId: 'bone', amount: 2 }]);
  });

  await t.test('admission racing the start marker always ends marked started (A1)', async () => {
    for (let round = 0; round < 8; round++) {
      const raidId = `raid-admit-${round}`;
      const host = await seedAdmitted(raidId);
      const late = await seedAdmitted(raidId, { admit: false });

      const [started, admitted] = await Promise.allSettled([
        InventoryService.markRaidStarted(host, raidId),
        InventoryService.admitPendingReservation(late, 'res-1')
      ]);

      assert.equal(started.status, 'fulfilled', `round ${round}`);
      // A revision conflict between the two writes is a retryable rejection, not a lost marker.
      if (admitted.status === 'rejected') {
        assert.equal(admitted.reason.errorCode, 'REVISION_CONFLICT');
        await InventoryService.admitPendingReservation(late, 'res-1');
      }
      const after = await raw(late);
      assert.ok(after.inventory.pendingReservation.admittedAt instanceof Date, `round ${round}`);
      assert.ok(after.inventory.pendingReservation.startedAt instanceof Date,
        `round ${round}: a reservation admitted around the marker must not stay refundable`);
    }
  });

  await t.test('refund racing the start marker ends in exactly one outcome', async () => {
    for (let round = 0; round < 8; round++) {
      // One raid per round: an admission into a raid already marked started inherits the marker (A1).
      const raidId = `raid-race-${round}`;
      const accountId = await seedAdmitted(raidId);
      const admitted = await raw(accountId);

      const [refund, started] = await Promise.allSettled([
        InventoryService.refundPendingReservation(accountId, 'res-1'),
        InventoryService.markRaidStarted(accountId, raidId)
      ]);
      const after = await raw(accountId);

      if (refund.status === 'fulfilled') {
        // Refund won: items are back and the reservation is gone; the marker either ran first
        // (impossible, it would have blocked the refund) or found no reservation to mark.
        assert.equal(after.inventory.pendingReservation, null, `round ${round}`);
        assert.deepEqual(after.inventory.loadout, [{ slot: 1, lootId: 'stick', amount: 4 }]);
        assert.equal(after.inventory.preparedEquipment.weaponSlot1, 'arming_sword');
        if (started.status === 'fulfilled') assert.equal(started.value.marked, 0);
        else assert.equal(started.reason.errorCode, 'HOST_NOT_IN_RAID');
      } else {
        // Marker won: the raid started, the refund is rejected and the items stay reserved.
        assert.equal(started.status, 'fulfilled', `round ${round}`);
        assert.equal(started.value.marked, 1);
        assert.ok(['REVISION_CONFLICT', 'RAID_ALREADY_STARTED'].includes(refund.reason.errorCode), refund.reason.errorCode);
        assert.ok(after.inventory.pendingReservation.startedAt instanceof Date);
        assert.deepEqual(after.inventory.loadout, []);
        assert.equal(after.revision, admitted.revision + 1);
      }
    }
  });
});
