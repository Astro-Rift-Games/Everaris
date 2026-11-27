'use strict';

// TASK-305: confirmed NPC commerce emits one telemetry event; rejections and replays emit none.
// TASK-308: single sales apply the same weekly demand (+20%, Doc 23 §7) as cart sales.
const test = require('node:test');
const assert = require('node:assert/strict');
const mongoose = require('mongoose');
const { MongoMemoryServer } = require('mongodb-memory-server');
const Character = require('../src/models/Character');
const Account = require('../src/models/Account');
const EconomyTelemetryEvent = require('../src/models/EconomyTelemetryEvent');
const EconomyTelemetry = require('../src/services/EconomyTelemetry');
const InventoryService = require('../src/services/InventoryService');
const { getWeeklyRotation, DEMAND_CANDIDATE_POOL } = require('../src/services/CommerceRotationService');
const { getSellOffer } = require('../src/config/shopCatalog');

// Demand rotates weekly, so fixtures pick this week's demand and a sellable candidate outside it.
const WEEK = getWeeklyRotation();
const DEMAND_IDS = WEEK.demandItems.map(item => item.lootId || item);
const DEMAND_ID = DEMAND_IDS[0];
const NON_DEMAND_ID = DEMAND_CANDIDATE_POOL.find(id => !DEMAND_IDS.includes(id) && getSellOffer(id));

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
  await Promise.all([Character.deleteMany({}), Account.deleteMany({}), EconomyTelemetryEvent.deleteMany({})]);
});

const SWORD = { slot: 0, lootId: 'arming_sword', amount: 1,
  instance: { instanceId: '111111111111111111111111', modifiers: { gemSocketCount: 0, gems: [] } } };

async function createCharacter(inventory = {}) {
  const account = await Account.create({ username: `user_${Math.random().toString(36).slice(2, 10)}`, passwordHash: 'hash' });
  const character = await Character.create({
    accountId: account._id, name: 'Tabor', revision: 1,
    inventory: {
      inventorySchemaVersion: 3, currency: 1000, stash: [],
      preparedEquipment: Object.fromEntries(['helmet', 'chest', 'gloves', 'boots', 'weaponSlot1', 'weaponSlot2', 'shield'].map(s => [s, null])),
      loadout: [SWORD, { slot: 1, lootId: NON_DEMAND_ID, amount: 5 }],
      ...inventory
    }
  });
  return { accountId: account._id, characterId: String(character._id) };
}

async function events() {
  await EconomyTelemetry.flush();
  return EconomyTelemetryEvent.find({}).sort({ occurredAt: 1 }).lean();
}

const goldOf = event => event.gold.map(({ direction, category, offerType, amount }) =>
  ({ direction, category, ...(offerType ? { offerType } : {}), amount }));

test('a confirmed single sale records npc_sale gold, the resulting balance and no account identity', async () => {
  const { accountId, characterId } = await createCharacter();
  await InventoryService.shopSell(accountId, NON_DEMAND_ID, 2, 0, 1, 'tx-sell', 1);

  const [event, ...rest] = await events();
  assert.equal(rest.length, 0);
  const revenue = getSellOffer(NON_DEMAND_ID).sellValue * 2;
  assert.equal(event.characterId, characterId);
  assert.equal(event.operation, 'sell');
  assert.deepEqual(goldOf(event), [{ direction: 'generated', category: 'npc_sale', amount: revenue }]);
  assert.equal(event.balanceAfter, 1000 + revenue);
  assert.deepEqual(event.details.sales, [{ lootId: NON_DEMAND_ID, amount: 2, revenue, demandBonus: false }]);
  assert.equal(JSON.stringify(event).includes(String(accountId)), false, 'account id must never be stored');
});

test('a confirmed single purchase records purchase gold by offer type', async () => {
  const { accountId } = await createCharacter();
  await InventoryService.shopBuy(accountId, 'CON_HEALTH_POTION', 2, 0, 1, 'tx-buy');

  const [event, ...rest] = await events();
  assert.equal(rest.length, 0);
  assert.equal(event.operation, 'buy');
  assert.deepEqual(goldOf(event), [{ direction: 'spent', category: 'purchase', offerType: 'consumable', amount: 80 }]);
  assert.equal(event.balanceAfter, 920);
  assert.deepEqual(event.details.purchases, [{ lootId: 'CON_HEALTH_POTION', amount: 2, cost: 80, offerType: 'consumable' }]);
});

test('a confirmed buyback records buyback gold', async () => {
  const { accountId } = await createCharacter({
    buyback: { entries: [{ entryId: 'bb_1', lootId: 'CON_THROWING_DAGGER', amount: 2, unitPrice: 18 }] }
  });
  await InventoryService.shopBuyback(accountId, 'bb_1', 2, 1, 'tx-bb');

  const [event, ...rest] = await events();
  assert.equal(rest.length, 0);
  assert.equal(event.operation, 'buyback');
  assert.deepEqual(goldOf(event), [{ direction: 'spent', category: 'buyback', amount: 36 }]);
  assert.equal(event.balanceAfter, 964);
  assert.deepEqual(event.details.buybacks, [{ lootId: 'CON_THROWING_DAGGER', amount: 2, cost: 36 }]);
});

test('a confirmed trade records each category net amount and the bonus demand used', async () => {
  const week = getWeeklyRotation();
  const demandId = week.demandItems[0].lootId || week.demandItems[0];
  const listed = getSellOffer(demandId).sellValue;
  const { accountId } = await createCharacter({
    currency: 50,
    loadout: [SWORD, { slot: 1, lootId: demandId, amount: 3 }],
    buyback: { entries: [{ entryId: 'bb_1', lootId: 'CON_THROWING_DAGGER', amount: 2, unitPrice: 18 }] }
  });
  await InventoryService.shopTrade(accountId, {
    transactionId: 'tx-trade', expectedRevision: 1, weekId: week.weekId,
    sales: [{ lootId: 'arming_sword', amount: 1, sourceSlot: 0 }, { lootId: demandId, amount: 3, sourceSlot: 1 }],
    purchases: [{ lootId: 'CON_HEALTH_POTION', amount: 1 }],
    buybacks: [{ entryId: 'bb_1', amount: 1 }]
  });

  const [event, ...rest] = await events();
  assert.equal(rest.length, 0);
  const demandRevenue = Math.floor(listed * 1.2) * 3;
  const revenue = 60 + demandRevenue;
  assert.equal(event.operation, 'trade');
  assert.deepEqual(goldOf(event), [
    { direction: 'generated', category: 'npc_sale', amount: revenue },
    { direction: 'spent', category: 'purchase', offerType: 'consumable', amount: 40 },
    { direction: 'spent', category: 'buyback', amount: 18 }
  ]);
  assert.equal(event.netGold, revenue - 58);
  assert.equal(event.balanceAfter, 50 + revenue - 58);
  assert.equal(event.details.demandBonusUnits, 3);
  assert.equal(event.details.demandBonusGold, demandRevenue - listed * 3);
  assert.deepEqual(event.details.sales[1], { lootId: demandId, amount: 3, revenue: demandRevenue, demandBonus: true });
});

test('rejected operations record nothing and leave gold unchanged', async () => {
  const week = getWeeklyRotation();
  const { accountId } = await createCharacter({ currency: 10 });
  await assert.rejects(InventoryService.shopBuy(accountId, 'CON_HEALTH_POTION', 1, 0, 1, 'tx-a'), e => e.errorCode === 'INSUFFICIENT_FUNDS');
  await assert.rejects(InventoryService.shopSell(accountId, NON_DEMAND_ID, 99, 0, 1, 'tx-b', 1), e => e.errorCode === 'INSUFFICIENT_ITEMS');
  await assert.rejects(InventoryService.shopSell(accountId, NON_DEMAND_ID, 1, 0, 7, 'tx-c', 1), e => e.errorCode === 'REVISION_CONFLICT');
  await assert.rejects(InventoryService.shopTrade(accountId, { transactionId: 'tx-d', expectedRevision: 1, weekId: week.weekId,
    purchases: [{ lootId: 'CON_HEALTH_POTION', amount: 1 }] }), e => e.errorCode === 'INSUFFICIENT_FUNDS');
  await assert.rejects(InventoryService.shopBuyback(accountId, 'missing', 1, 1, 'tx-e'), e => e.errorCode === 'BUYBACK_ENTRY_NOT_FOUND');

  assert.equal((await events()).length, 0);
  const character = await Character.findOne({ accountId });
  assert.equal(character.inventory.currency, 10);
  assert.equal(character.revision, 1);
});

test('replaying a confirmed transaction does not record it twice', async () => {
  const { accountId } = await createCharacter();
  await InventoryService.shopBuy(accountId, 'CON_HEALTH_POTION', 1, 0, 1, 'tx-replay');
  const replay = await InventoryService.shopBuy(accountId, 'CON_HEALTH_POTION', 1, 0, 1, 'tx-replay');
  assert.equal(replay.alreadySecured, true);
  assert.equal((await events()).length, 1);
});

test('a telemetry sink failure never fails or rolls back a confirmed sale', async t => {
  t.mock.method(EconomyTelemetryEvent, 'create', async () => { throw new Error('sink down'); });
  t.mock.method(console, 'warn', () => {});
  const { accountId } = await createCharacter();
  await InventoryService.shopSell(accountId, NON_DEMAND_ID, 1, 0, 1, 'tx-sink', 1);
  await EconomyTelemetry.flush();
  const character = await Character.findOne({ accountId });
  assert.equal(character.inventory.currency, 1000 + getSellOffer(NON_DEMAND_ID).sellValue);
  assert.equal(character.revision, 2);
});

// Sells `amount` units of `lootId` by one route on a fresh character and returns the gold gained and its event.
async function sellBy(route, lootId, amount) {
  const { accountId } = await createCharacter({ currency: 0, loadout: [SWORD, { slot: 1, lootId, amount }] });
  if (route === 'single') await InventoryService.shopSell(accountId, lootId, amount, 0, 1, `tx-${route}-${lootId}`, 1);
  else await InventoryService.shopTrade(accountId, { transactionId: `tx-${route}-${lootId}`, expectedRevision: 1,
    weekId: WEEK.weekId, sales: [{ lootId, amount, sourceSlot: 1 }] });
  const character = await Character.findOne({ accountId });
  const [event, ...rest] = await events();
  assert.equal(rest.length, 0);
  await EconomyTelemetryEvent.deleteMany({});
  return { gold: character.inventory.currency, loadout: character.inventory.loadout.map(i => i.lootId), event };
}

test('selling an in-demand item pays the same weekly +20% gold per unit by the single and cart routes', async () => {
  const listed = getSellOffer(DEMAND_ID).sellValue;
  const unit = Math.floor(listed * 1.2);
  const single = await sellBy('single', DEMAND_ID, 3);
  const cart = await sellBy('cart', DEMAND_ID, 3);

  assert.equal(single.gold, unit * 3);
  assert.equal(single.gold, cart.gold);
  assert.deepEqual(single.loadout, ['arming_sword']);
  for (const { event } of [single, cart]) {
    assert.deepEqual(goldOf(event), [{ direction: 'generated', category: 'npc_sale', amount: unit * 3 }]);
    assert.deepEqual(event.details.sales, [{ lootId: DEMAND_ID, amount: 3, revenue: unit * 3, demandBonus: true }]);
    assert.equal(event.details.demandBonusUnits, 3);
    assert.equal(event.details.demandBonusGold, (unit - listed) * 3);
  }
});

test('selling an item outside the weekly demand keeps its catalog price by the single and cart routes', async () => {
  const listed = getSellOffer(NON_DEMAND_ID).sellValue;
  const single = await sellBy('single', NON_DEMAND_ID, 2);
  const cart = await sellBy('cart', NON_DEMAND_ID, 2);

  assert.equal(single.gold, listed * 2);
  assert.equal(cart.gold, listed * 2);
  for (const { event } of [single, cart]) {
    assert.deepEqual(event.details.sales, [{ lootId: NON_DEMAND_ID, amount: 2, revenue: listed * 2, demandBonus: false }]);
    assert.equal(event.details.demandBonusGold ?? 0, 0);
  }
});

test('a rejected single sale of an in-demand item changes no gold, inventory or revision and records nothing', async () => {
  const loadout = [SWORD, { slot: 1, lootId: DEMAND_ID, amount: 2 }];
  const { accountId } = await createCharacter({ currency: 10, loadout });
  await assert.rejects(InventoryService.shopSell(accountId, DEMAND_ID, 3, 0, 1, 'tx-r1', 1), e => e.errorCode === 'INSUFFICIENT_ITEMS');
  await assert.rejects(InventoryService.shopSell(accountId, DEMAND_ID, 1, 0, 7, 'tx-r2', 1), e => e.errorCode === 'REVISION_CONFLICT');

  assert.equal((await events()).length, 0);
  const character = await Character.findOne({ accountId });
  assert.equal(character.inventory.currency, 10);
  assert.equal(character.revision, 1);
  assert.deepEqual(character.inventory.loadout.map(({ slot, lootId, amount }) => ({ slot, lootId, amount })),
    [{ slot: 0, lootId: 'arming_sword', amount: 1 }, { slot: 1, lootId: DEMAND_ID, amount: 2 }]);
});
