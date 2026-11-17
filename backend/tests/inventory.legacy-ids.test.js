'use strict';
const { test, describe, before, after } = require('node:test');
const assert = require('node:assert/strict');
const mongoose = require('mongoose');
const { startMemoryMongo } = require('./support/memoryMongo');
const Character = require('../src/models/Character');
const Account = require('../src/models/Account');
const SessionTicketService = require('../src/services/SessionTicketService');
const InventoryService = require('../src/services/InventoryService');
const { createInstance } = require('../src/services/ItemInstances');

// Stored data may still hold ids that became aliases (healthpotion -> CON_HEALTH_POTION).
// v3 tickets emit canonical ids; neither tickets nor reads repair stored documents (documented
// read contract: reads return stored ids, the explicit migration script rewrites them).
describe('legacy item ids in v3 raid admission tickets', () => {
  let mongoServer;
  let accountId;
  const sword = createInstance('arming_sword', { gemSocketCount: 0, gems: [] });
  const stored = {
    stash: [
      { slot: 0, lootId: 'healthpotion', amount: 4 },
      { slot: 1, lootId: 'CON_HEALTH_POTION', amount: 2 },
      { slot: 2, lootId: 'forged_dragon_scale', amount: 1 }
    ],
    loadout: [{ slot: 0, lootId: 'healthpotion', amount: 3 }],
    reservationItems: [
      { slot: 0, lootId: 'healthpotion', amount: 3 },
      { slot: 1, lootId: 'CON_HEALTH_POTION', amount: 2 },
      { slot: 2, lootId: 'forged_dragon_scale', amount: 1 }
    ]
  };

  before(async () => {
    mongoServer = await startMemoryMongo();
    await mongoose.connect(mongoServer.getUri());
    accountId = (await Account.create({ username: 'legacy_ids', passwordHash: 'x' }))._id;
    await Character.create({
      accountId,
      name: 'LegacyHero',
      inventory: {
        inventorySchemaVersion: 3,
        stash: stored.stash,
        loadout: stored.loadout,
        pendingReservation: {
          reservationId: 'res_legacy',
          items: stored.reservationItems,
          preparedEquipment: { weaponSlot1: { lootId: 'longsword', instance: sword } }
        }
      }
    });
  });

  after(async () => {
    await mongoose.disconnect();
    await mongoServer.stop();
  });

  test('v3 raid ticket emits canonical ids with the same quantities, leaves unknown ids and canonical ids as stored', async () => {
    const { snapshot } = await SessionTicketService.issueRaidAdmissionTicket(accountId, '123456', 'res_legacy');
    assert.deepEqual(snapshot.items, [
      { lootId: 'CON_HEALTH_POTION', amount: 3 },
      { lootId: 'CON_HEALTH_POTION', amount: 2 },
      { lootId: 'forged_dragon_scale', amount: 1 }
    ]);
    assert.deepEqual(snapshot.preparedEquipment.weaponSlot1,
      { lootId: 'arming_sword', modifiers: { gemSocketCount: 0, gems: [] } });
  });

  test('ticket issuance and inventory reads do not write canonical ids back to the stored document', async () => {
    await InventoryService.getInventory(accountId);
    const raw = await Character.collection.findOne({ accountId });
    assert.deepEqual(raw.inventory.stash.map(item => item.lootId), ['healthpotion', 'CON_HEALTH_POTION', 'forged_dragon_scale']);
    assert.deepEqual(raw.inventory.loadout.map(item => item.lootId), ['healthpotion']);
    assert.deepEqual(raw.inventory.pendingReservation.items.map(item => item.lootId),
      ['healthpotion', 'CON_HEALTH_POTION', 'forged_dragon_scale']);
    assert.equal(raw.inventory.pendingReservation.preparedEquipment.weaponSlot1.lootId, 'longsword');
  });
});
