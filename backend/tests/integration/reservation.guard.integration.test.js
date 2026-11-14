'use strict';
// FIX-19: real-DB proof that a pending reservation locks Stash/Loadout/Equipment mutations
// (including the atomic write filter on a stale read) and that replacing or rolling back an
// unadmitted reservation keeps every unit.
const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('crypto');
const mongoose = require('mongoose');
const db = require('./testDb');
const Character = require('../../src/models/Character');
const Inventory = require('../../src/services/InventoryService');

const instance = n => ({ instanceId: n.toString(16).padStart(24, '0'), modifiers: { gemSocketCount: 0, gems: [] } });
const characters = () => mongoose.connection.collection('characters');
const raw = accountId => characters().findOne({ accountId });
const hash = doc => crypto.createHash('sha256').update(JSON.stringify(doc)).digest('hex');

function totalsByLootId(doc) {
  const totals = {};
  const add = (lootId, amount) => { totals[lootId] = (totals[lootId] || 0) + amount; };
  const addEquipment = equipment => Object.values(equipment || {}).filter(item => item && item !== 'null')
    .forEach(item => add(item.lootId ?? item, 1));
  const { inventory } = doc;
  [...inventory.stash, ...inventory.loadout, ...(inventory.pendingReservation?.items || [])].forEach(item => add(item.lootId, item.amount));
  addEquipment(inventory.preparedEquipment); addEquipment(inventory.pendingReservation?.preparedEquipment);
  return totals;
}

async function seed(version, withReservation = true) {
  const accountId = new mongoose.Types.ObjectId();
  const weapon = n => (version === 3 ? { lootId: 'arming_sword', instance: instance(n) } : 'arming_sword');
  const at = (slot, lootId, amount) => (version === 1 ? { lootId, amount } : { slot, lootId, amount });
  await characters().insertOne({ accountId, name: 'Reserved', revision: 7, unlockedAbilities: [],
    inventory: { inventorySchemaVersion: version, currency: 0, stash: [at(0, 'bone', 2)],
      loadout: [at(2, 'stick', 1), at(5, 'bone', 1)], preparedEquipment: { weaponSlot1: weapon(1) },
      pendingReservation: withReservation ? { reservationId: 'old', raidId: null, admittedAt: null,
        items: [at(2, 'bone', 3), at(4, 'stick', 2)], preparedEquipment: { weaponSlot1: weapon(2), weaponSlot2: weapon(3) } } : null } });
  return accountId;
}

test('FIX-19 reservation lock and preservation against a real database', async t => {
  await db.connect();
  t.after(() => db.closeDatabase());

  for (const version of [1, 2, 3]) {
    const mutations = {
      moveToLoadout: accountId => Inventory.moveToLoadout(accountId, 'bone', 1, 7, version === 1 ? undefined : 0),
      moveToStash: accountId => Inventory.moveToStash(accountId, 'stick', 1, 7, version === 1 ? undefined : 2),
      updatePreparedEquipment: accountId => Inventory.updatePreparedEquipment(accountId, { weaponSlot1: version === 3 ? null : '' }, 7),
    };
    for (const [name, mutate] of Object.entries(mutations)) {
      await t.test(`v${version} ${name} rejects a pending reservation and leaves the document unchanged`, async () => {
        const accountId = await seed(version);
        const before = hash(await raw(accountId));
        await assert.rejects(mutate(accountId), { statusCode: 409, errorCode: 'RESERVATION_PENDING' });
        assert.equal(hash(await raw(accountId)), before);
      });

      await t.test(`v${version} ${name} write filter blocks a reservation created after the read`, async () => {
        const accountId = await seed(version, false);
        const stale = await Character.findOne({ accountId });
        await characters().updateOne({ accountId }, { $set: { 'inventory.pendingReservation':
          { reservationId: 'race', raidId: null, admittedAt: null, items: [], preparedEquipment: {} } } });
        const before = hash(await raw(accountId));
        const findOne = Character.findOne;
        Character.findOne = async () => stale;
        try {
          await assert.rejects(mutate(accountId), { statusCode: 409 });
        } finally {
          Character.findOne = findOne;
        }
        assert.equal(hash(await raw(accountId)), before);
      });
    }

    const endings = {
      replacement: accountId => Inventory.savePendingReservation(accountId, 'new'),
      rollback: accountId => Inventory.clearPendingReservation(accountId, { rollback: true, expectedReservationId: 'old' }),
    };
    for (const [name, end] of Object.entries(endings)) {
      await t.test(`v${version} ${name} of an unadmitted reservation keeps per-lootId totals`, async () => {
        const accountId = await seed(version);
        const before = totalsByLootId(await raw(accountId));
        await end(accountId);
        assert.deepEqual(totalsByLootId(await raw(accountId)), before);
      });
    }
  }
});
