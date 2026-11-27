'use strict';

const { describe, test, before, after, beforeEach } = require('node:test');
const assert = require('node:assert/strict');
const mongoose = require('mongoose');
const { startMemoryMongo } = require('../support/memoryMongo');
const Character = require('../../src/models/Character');
const InventoryService = require('../../src/services/InventoryService');

describe('FIX-33 concurrency between legacy extraction and shop operations', () => {
  let mongo;

  before(async () => {
    mongo = await startMemoryMongo();
    await mongoose.connect(mongo.getUri());
    await Character.createCollection();
  });

  after(async () => {
    await mongoose.disconnect();
    if (mongo) await mongo.stop();
  });

  beforeEach(async () => {
    await Character.deleteMany({});
  });

  test('v1 legacy commitExtraction interleaved between shopBuy read and $set does not lose extracted loot', async () => {
    const accountId = new mongoose.Types.ObjectId().toString();
    await Character.create({
      accountId,
      name: 'LegacyTrader',
      revision: 5,
      inventory: {
        inventorySchemaVersion: 1,
        currency: 200,
        stash: [],
        loadout: [],
        preparedEquipment: { weaponSlot1: 'arming_sword', weaponSlot2: '', helmet: '', armor: '', gloves: '', boots: '' },
        pendingReservation: null,
        appliedExtractionReceipts: []
      }
    });

    const originalFindOneAndUpdate = Character.findOneAndUpdate;
    let interleaved = false;
    Character.findOneAndUpdate = async function (filter, update, options) {
      // Intercept shopBuy's atomic update right after shopBuy has read revision=5,
      // and run legacy commitExtraction before shopBuy writes.
      if (!interleaved && update?.$push?.appliedShopReceipts) {
        interleaved = true;
        // Restore temporarily so commitExtraction uses real DB operations.
        Character.findOneAndUpdate = originalFindOneAndUpdate;
        const extractResult = await InventoryService.commitExtraction(
          accountId,
          'raid-legacy-race-1',
          1,
          [{ lootId: 'CON_HEALTH_POTION', amount: 3 }]
        );
        assert.equal(extractResult.alreadySecured, false);
        return originalFindOneAndUpdate.call(this, filter, update, options);
      }
      return originalFindOneAndUpdate.call(this, filter, update, options);
    };

    try {
      await assert.rejects(
        () => InventoryService.shopBuy(accountId, 'CON_MANA_FRAGMENT', 1, undefined, 5, 'tx-race-buy-1'),
        (err) => err && err.statusCode === 409 && err.errorCode === 'REVISION_CONFLICT',
        'shopBuy reading stale revision=5 before legacy commitExtraction must fail with 409 REVISION_CONFLICT'
      );
    } finally {
      Character.findOneAndUpdate = originalFindOneAndUpdate;
    }

    const finalChar = await Character.findOne({ accountId }).lean();
    assert.equal(finalChar.revision, 6, 'Legacy commitExtraction must increment character revision');
    assert.equal(finalChar.inventory.currency, 200, 'Rejected shopBuy must not debit currency');
    assert.deepEqual(
      finalChar.inventory.loadout.map(i => ({ lootId: i.lootId, amount: i.amount })),
      [{ lootId: 'CON_HEALTH_POTION', amount: 3 }],
      'Extracted loot must survive intact without being overwritten by stale shopBuy'
    );
    assert.equal(finalChar.inventory.appliedExtractionReceipts.length, 1);
  });

  test('v2 legacy commitExtraction interleaved between shopBuy read and $set rejects stale buy with REVISION_CONFLICT', async () => {
    const accountId = new mongoose.Types.ObjectId().toString();
    await Character.create({
      accountId,
      name: 'V2Trader',
      revision: 3,
      inventory: {
        inventorySchemaVersion: 2,
        currency: 200,
        stash: [],
        loadout: [],
        preparedEquipment: { weaponSlot1: 'arming_sword', weaponSlot2: '', helmet: '', armor: '', gloves: '', boots: '' },
        pendingReservation: null,
        appliedExtractionReceipts: []
      }
    });

    const originalFindOneAndUpdate = Character.findOneAndUpdate;
    let interleaved = false;
    Character.findOneAndUpdate = async function (filter, update, options) {
      if (!interleaved && update?.$push?.appliedShopReceipts) {
        interleaved = true;
        Character.findOneAndUpdate = originalFindOneAndUpdate;
        await InventoryService.commitExtraction(
          accountId,
          'raid-legacy-race-v2',
          1,
          [{ lootId: 'CON_HEALTH_POTION', amount: 2 }]
        );
        return originalFindOneAndUpdate.call(this, filter, update, options);
      }
      return originalFindOneAndUpdate.call(this, filter, update, options);
    };

    try {
      await assert.rejects(
        () => InventoryService.shopBuy(accountId, 'CON_MANA_FRAGMENT', 1, undefined, 3, 'tx-race-buy-v2'),
        (err) => err && err.statusCode === 409 && err.errorCode === 'REVISION_CONFLICT'
      );
    } finally {
      Character.findOneAndUpdate = originalFindOneAndUpdate;
    }

    const finalChar = await Character.findOne({ accountId }).lean();
    assert.equal(finalChar.revision, 4);
    assert.equal(finalChar.inventory.currency, 200);
    assert.ok(finalChar.inventory.loadout.some(i => i.lootId === 'CON_HEALTH_POTION' && i.amount === 2));
  });
});
