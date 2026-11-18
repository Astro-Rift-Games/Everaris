'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const mongoose = require('mongoose');
const { MongoMemoryServer } = require('mongodb-memory-server');
const Character = require('../src/models/Character');
const Account = require('../src/models/Account');
const InventoryService = require('../src/services/InventoryService');
const { getWeeklyRotation } = require('../src/services/CommerceRotationService');

let mongod;

test.before(async () => {
  mongod = await MongoMemoryServer.create();
  await mongoose.connect(mongod.getUri());
});

test.after(async () => {
  await mongoose.disconnect();
  await mongod.stop();
});

test.beforeEach(async () => {
  await Character.deleteMany({});
  await Account.deleteMany({});
});

async function createTestCharacter(overrides = {}) {
  const account = await Account.create({
    username: `user_${Date.now()}_${Math.random().toString(36).slice(2, 6)}`,
    passwordHash: 'dummyhash'
  });
  const equipment = Object.fromEntries(
    ['helmet', 'chest', 'gloves', 'boots', 'weaponSlot1', 'weaponSlot2', 'shield']
      .map(slot => [slot, null])
  );
  const character = await Character.create({
    accountId: account._id,
    name: 'TaborHero',
    revision: 1,
    inventory: {
      inventorySchemaVersion: 3,
      currency: 1000,
      preparedEquipment: equipment,
      loadout: [
        {
          slot: 0,
          lootId: 'arming_sword',
          amount: 1,
          instance: {
            instanceId: '0123456789abcdef01234567',
            modifiers: { gemSocketCount: 0, gems: [] }
          }
        },
        { slot: 1, lootId: 'RES_SLIME_GEL', amount: 5 }
      ],
      stash: [],
      ...overrides
    }
  });
  return { account, character };
}

test('shopTrade applies mixed cart: sales fund purchases and buyback in one atomic commit', async () => {
  const currentWeek = getWeeklyRotation();
  const { account, character } = await createTestCharacter({
    currency: 50,
    loadout: [
      {
        slot: 0,
        lootId: 'arming_sword',
        amount: 1,
        instance: {
          instanceId: '111111111111111111111111',
          modifiers: { gemSocketCount: 0, gems: [] }
        }
      },
      { slot: 1, lootId: 'RES_SLIME_GEL', amount: 10 }
    ],
    buyback: {
      entries: [
        { entryId: 'bb_1', lootId: 'CON_THROWING_DAGGER', amount: 2, unitPrice: 18 }
      ]
    }
  });

  // Sold: arming_sword (sellValue 60) + 10 RES_SLIME_GEL (if demand: floor(8*1.2)=9, else 8 -> >= 80)
  // Revenue: 60 + 80 = 140 gold.
  // Initial currency: 50 -> Funded: 190 gold.
  // Buy: 2 CON_HEALTH_POTION (40*2 = 80 gold).
  // Buyback: 1 CON_THROWING_DAGGER (18 gold).
  // Final gold: 190 - 80 - 18 = 92 gold.
  const result = await InventoryService.shopTrade(account._id, {
    transactionId: `tx_${Date.now()}`,
    expectedRevision: 1,
    weekId: currentWeek.weekId,
    sales: [
      { lootId: 'arming_sword', amount: 1, sourceSlot: 0 },
      { lootId: 'RES_SLIME_GEL', amount: 10, sourceSlot: 1 }
    ],
    purchases: [
      { lootId: 'CON_HEALTH_POTION', amount: 2 }
    ],
    buybacks: [
      { entryId: 'bb_1', amount: 1 }
    ]
  });

  assert.ok(result);
  const updated = await Character.findOne({ accountId: account._id });
  assert.equal(updated.revision, 2);
  assert.ok(updated.inventory.currency >= 92);

  // Sold equipment (arming_sword) must enter buyback history
  const swordBb = updated.inventory.buyback.entries.find(e => e.lootId === 'arming_sword');
  assert.ok(swordBb, 'Sold arming_sword must enter buyback entries');
  assert.equal(swordBb.unitPrice, Math.ceil(60 * 1.5)); // 90

  // Bought back CON_THROWING_DAGGER must have 1 unit remaining in buyback
  const daggerBb = updated.inventory.buyback.entries.find(e => e.entryId === 'bb_1');
  assert.ok(daggerBb);
  assert.equal(daggerBb.amount, 1);
});

test('shopTrade rejects with ROTATION_EXPIRED when weekId does not match current weekly cycle', async () => {
  const { account } = await createTestCharacter();

  await assert.rejects(
    InventoryService.shopTrade(account._id, {
      transactionId: `tx_expired_${Date.now()}`,
      expectedRevision: 1,
      weekId: '2020-01-01', // Stale week
      sales: [],
      purchases: [{ lootId: 'CON_HEALTH_POTION', amount: 1 }]
    }),
    err => {
      assert.equal(err.statusCode, 409);
      assert.equal(err.errorCode, 'ROTATION_EXPIRED');
      return true;
    }
  );
});

test('shopTrade enforces individual player stock quota on rotating items', async () => {
  const currentWeek = getWeeklyRotation();
  const utility = currentWeek.utilities[0]; // e.g. CON_LOCKPICK (stock 3) or compass/detector (stock 1)
  const maxStock = utility.stock;

  const { account } = await createTestCharacter({ currency: 10000 });

  // Purchasing more than personal stock must be rejected
  await assert.rejects(
    InventoryService.shopTrade(account._id, {
      transactionId: `tx_stock_exceeded_${Date.now()}`,
      expectedRevision: 1,
      weekId: currentWeek.weekId,
      sales: [],
      purchases: [{ lootId: utility.lootId, amount: maxStock + 1 }]
    }),
    err => {
      assert.equal(err.statusCode, 409);
      assert.equal(err.errorCode, 'STOCK_EXHAUSTED');
      return true;
    }
  );

  // Purchasing within quota succeeds
  await InventoryService.shopTrade(account._id, {
    transactionId: `tx_stock_ok_${Date.now()}`,
    expectedRevision: 1,
    weekId: currentWeek.weekId,
    sales: [],
    purchases: [{ lootId: utility.lootId, amount: maxStock }]
  });

  // Second purchase in same week fails as stock is now exhausted
  await assert.rejects(
    InventoryService.shopTrade(account._id, {
      transactionId: `tx_stock_second_${Date.now()}`,
      expectedRevision: 2,
      weekId: currentWeek.weekId,
      sales: [],
      purchases: [{ lootId: utility.lootId, amount: 1 }]
    }),
    err => {
      assert.equal(err.statusCode, 409);
      assert.equal(err.errorCode, 'STOCK_EXHAUSTED');
      return true;
    }
  );
});

test('shopTrade applies +20% demand bonus to active weekly demand items', async () => {
  const currentWeek = getWeeklyRotation();
  const demandItem = currentWeek.demandItems[0];
  const expectedDemandPrice = Math.floor(demandItem.objectValue * 1.2);

  const { account } = await createTestCharacter({
    currency: 0,
    loadout: [
      { slot: 0, lootId: demandItem.lootId, amount: 3 }
    ]
  });

  await InventoryService.shopTrade(account._id, {
    transactionId: `tx_demand_${Date.now()}`,
    expectedRevision: 1,
    weekId: currentWeek.weekId,
    sales: [
      { lootId: demandItem.lootId, amount: 3, sourceSlot: 0 }
    ],
    purchases: []
  });

  const updated = await Character.findOne({ accountId: account._id });
  assert.equal(updated.inventory.currency, expectedDemandPrice * 3);
});

test('shared buyback caps at 16 entries with FIFO eviction', async () => {
  const currentWeek = getWeeklyRotation();
  const existingEntries = [];
  for (let i = 1; i <= 16; i++) {
    existingEntries.push({
      entryId: `entry_${i}`,
      lootId: 'CON_THROWING_DAGGER',
      amount: 1,
      unitPrice: 18,
      soldAt: new Date(Date.now() - (100 - i) * 1000)
    });
  }

  const { account } = await createTestCharacter({
    currency: 0,
    loadout: [
      {
        slot: 0,
        lootId: 'arming_sword',
        amount: 1,
        instance: {
          instanceId: '222222222222222222222222',
          modifiers: { gemSocketCount: 0, gems: [] }
        }
      }
    ],
    buyback: { entries: existingEntries }
  });

  // Sell 17th item -> oldest entry (entry_1) must be evicted, keeping exactly 16
  await InventoryService.shopTrade(account._id, {
    transactionId: `tx_fifo_${Date.now()}`,
    expectedRevision: 1,
    weekId: currentWeek.weekId,
    sales: [{ lootId: 'arming_sword', amount: 1, sourceSlot: 0 }],
    purchases: []
  });

  const updated = await Character.findOne({ accountId: account._id });
  assert.equal(updated.inventory.buyback.entries.length, 16);
  assert.equal(updated.inventory.buyback.entries.some(e => e.entryId === 'entry_1'), false, 'entry_1 must be evicted');
  assert.equal(updated.inventory.buyback.entries[15].lootId, 'arming_sword', 'Newest item must be at end of list');
});
