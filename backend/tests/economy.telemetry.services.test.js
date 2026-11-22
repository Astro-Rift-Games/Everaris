'use strict';

// TASK-305 T3: confirmed NPC upgrades and respec emit one telemetry event; replays and rejections emit none.
const test = require('node:test');
const assert = require('node:assert/strict');
const mongoose = require('mongoose');
const { MongoMemoryServer } = require('mongodb-memory-server');
const Character = require('../src/models/Character');
const Account = require('../src/models/Account');
const EconomyTelemetryEvent = require('../src/models/EconomyTelemetryEvent');
const EconomyTelemetry = require('../src/services/EconomyTelemetry');
const InventoryService = require('../src/services/InventoryService');
const ProgressionService = require('../src/services/ProgressionService');
const { RESPEC_GOLD_COST } = require('../src/config/respecConfig');

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

const SWORD_ID = '111111111111111111111111';
const ARMOR_ID = '222222222222222222222222';
const SWORD = { slot: 0, lootId: 'arming_sword', amount: 1,
  instance: { instanceId: SWORD_ID, modifiers: { gemSocketCount: 0, gems: [] } } };
const BREASTPLATE = { slot: 1, lootId: 'heavy_armor_breastplate', amount: 1, instance: { instanceId: ARMOR_ID } };
const EMPTY_EQUIPMENT = Object.fromEntries(['helmet', 'chest', 'gloves', 'boots', 'weaponSlot1', 'weaponSlot2', 'shield'].map(s => [s, null]));
const DEFAULT_MATERIALS = { RES_RUNE_FRAGMENT: 5, RES_BLACK_IRON: 5, RES_ATTUNEMENT_GEM: 2 };

async function createCharacter({ currency = 1000, materials = DEFAULT_MATERIALS, attributes } = {}) {
  const account = await Account.create({ username: `user_${Math.random().toString(36).slice(2, 10)}`, passwordHash: 'hash' });
  const materialItems = Object.entries(materials).map(([lootId, amount], index) => ({ slot: 2 + index, lootId, amount }));
  const character = await Character.create({
    accountId: account._id, name: 'Tabor', revision: 1,
    ...(attributes ? { characterAttributes: attributes } : {}),
    inventory: {
      inventorySchemaVersion: 3, currency, stash: [], preparedEquipment: EMPTY_EQUIPMENT,
      loadout: [SWORD, BREASTPLATE, ...materialItems]
    }
  });
  return { accountId: account._id, characterId: String(character._id) };
}

async function events() {
  await EconomyTelemetry.flush();
  return EconomyTelemetryEvent.find({}).sort({ occurredAt: 1 }).lean();
}

const goldOf = event => event.gold.map(({ direction, category, amount }) => ({ direction, category, amount }));
const weaponTicket = (overrides = {}) => ({ transactionId: 'tx-up', expectedRevision: 1, merchant: 'blacksmith',
  operation: 'primary_scaling', instanceId: SWORD_ID, location: 'loadout', slot: 0, ...overrides });
const armorTicket = (overrides = {}) => weaponTicket({ transactionId: 'tx-armor', operation: 'armor_level',
  instanceId: ARMOR_ID, slot: 1, ...overrides });

async function assertUnchanged(accountId, revision, currency) {
  const character = await Character.findOne({ accountId }).lean();
  assert.equal(character.revision, revision);
  assert.equal(character.inventory.currency, currency);
  assert.deepEqual(await events(), []);
}

test('a confirmed weapon upgrade emits one weapon_upgrade event with materials and level', async () => {
  const { accountId, characterId } = await createCharacter();
  await InventoryService.upgradeItem(accountId, weaponTicket());

  const [event, ...rest] = await events();
  assert.equal(rest.length, 0);
  assert.equal(event.characterId, characterId);
  assert.equal(event.operation, 'upgrade');
  assert.deepEqual(goldOf(event), [{ direction: 'spent', category: 'weapon_upgrade', amount: 60 }]);
  assert.equal(event.goldSpent, 60);
  assert.equal(event.balanceAfter, 940);
  const { objectValueAfter, ...details } = event.details;
  assert.deepEqual(details, { merchant: 'blacksmith', upgradeOperation: 'primary_scaling', lootId: 'arming_sword',
    levelAfter: 'E', materials: { RES_RUNE_FRAGMENT: 2 } });
  assert.equal(Number.isSafeInteger(objectValueAfter) && objectValueAfter > 0, true);
  assert.equal(JSON.stringify(event).includes(String(accountId)), false, 'account id must never be stored');
});

test('a first_socket upgrade records weapon_upgrade with the socket count as level', async () => {
  const { accountId } = await createCharacter();
  await assert.rejects(InventoryService.upgradeItem(accountId, weaponTicket({ merchant: 'arcanist', operation: 'first_socket' })),
    { errorCode: 'UPGRADE_WRONG_MERCHANT' });
  assert.deepEqual(await events(), []);

  await InventoryService.upgradeItem(accountId, weaponTicket({ transactionId: 'tx-socket', operation: 'first_socket' }));
  const [event, ...rest] = await events();
  assert.equal(rest.length, 0);
  assert.equal(event.gold[0].category, 'weapon_upgrade');
  assert.equal(event.details.upgradeOperation, 'first_socket');
  assert.equal(event.details.levelAfter, 1);
});

test('a confirmed armor upgrade emits armor_upgrade with the new upgrade level', async () => {
  const { accountId } = await createCharacter();
  await InventoryService.upgradeItem(accountId, armorTicket());

  const [event, ...rest] = await events();
  assert.equal(rest.length, 0);
  assert.equal(event.operation, 'upgrade');
  assert.deepEqual(goldOf(event), [{ direction: 'spent', category: 'armor_upgrade', amount: 40 }]);
  assert.equal(event.balanceAfter, 960);
  assert.equal(event.details.upgradeOperation, 'armor_level');
  assert.equal(event.details.lootId, 'heavy_armor_breastplate');
  assert.equal(event.details.levelAfter, 1);
  assert.deepEqual(event.details.materials, { RES_BLACK_IRON: 2 });
});

test('replaying the same upgrade transactionId does not emit again', async () => {
  const { accountId } = await createCharacter();
  await InventoryService.upgradeItem(accountId, weaponTicket());
  const replay = await InventoryService.upgradeItem(accountId, weaponTicket());

  assert.equal(replay.alreadySecured, true);
  assert.equal((await events()).length, 1);
});

test('a concurrent duplicate upgrade request emits exactly one event', async () => {
  const { accountId } = await createCharacter();
  const results = await Promise.allSettled([
    InventoryService.upgradeItem(accountId, weaponTicket()),
    InventoryService.upgradeItem(accountId, weaponTicket())
  ]);

  assert.equal(results.every(result => result.status === 'fulfilled'), true);
  assert.equal((await events()).length, 1);
});

test('rejected upgrades emit nothing and leave currency and revision unchanged', async (t) => {
  await t.test('INSUFFICIENT_FUNDS', async () => {
    const { accountId } = await createCharacter({ currency: 10 });
    await assert.rejects(InventoryService.upgradeItem(accountId, weaponTicket()), { errorCode: 'INSUFFICIENT_FUNDS' });
    await assertUnchanged(accountId, 1, 10);
  });
  await t.test('INSUFFICIENT_MATERIALS', async () => {
    const { accountId } = await createCharacter({ materials: { RES_RUNE_FRAGMENT: 1 } });
    await assert.rejects(InventoryService.upgradeItem(accountId, weaponTicket()), { errorCode: 'INSUFFICIENT_MATERIALS' });
    await assertUnchanged(accountId, 1, 1000);
  });
  await t.test('REVISION_CONFLICT', async () => {
    const { accountId } = await createCharacter();
    await assert.rejects(InventoryService.upgradeItem(accountId, weaponTicket({ expectedRevision: 7 })), { errorCode: 'REVISION_CONFLICT' });
    await assertUnchanged(accountId, 1, 1000);
  });
});

test('a confirmed respec emits one respec event with the configured cost', async () => {
  const { accountId, characterId } = await createCharacter();
  await ProgressionService.respec(accountId, { transactionId: 'tx-respec', expectedRevision: 1 });

  const [event, ...rest] = await events();
  assert.equal(rest.length, 0);
  assert.equal(event.characterId, characterId);
  assert.equal(event.operation, 'respec');
  assert.deepEqual(goldOf(event), [{ direction: 'spent', category: 'respec', amount: RESPEC_GOLD_COST }]);
  assert.equal(event.balanceAfter, 1000 - RESPEC_GOLD_COST);
  assert.deepEqual(event.details, { pointsReturned: 30 });
});

test('replaying a respec transactionId does not emit again', async () => {
  const { accountId } = await createCharacter();
  await ProgressionService.respec(accountId, { transactionId: 'tx-respec', expectedRevision: 1 });
  const replay = await ProgressionService.respec(accountId, { transactionId: 'tx-respec', expectedRevision: 1 });

  assert.equal(replay.alreadySecured, true);
  assert.equal((await events()).length, 1);
});

test('rejected respecs emit nothing and leave currency and revision unchanged', async (t) => {
  await t.test('NO_ASSIGNED_POINTS', async () => {
    const zero = { vitality: 0, resistance: 0, strength: 0, dexterity: 0, intelligence: 0, luck: 0, availablePoints: 3 };
    const { accountId } = await createCharacter({ attributes: zero });
    await assert.rejects(ProgressionService.respec(accountId, { transactionId: 'tx-r', expectedRevision: 1 }), { errorCode: 'NO_ASSIGNED_POINTS' });
    await assertUnchanged(accountId, 1, 1000);
  });
  await t.test('INSUFFICIENT_FUNDS', async () => {
    const { accountId } = await createCharacter({ currency: RESPEC_GOLD_COST - 1 });
    await assert.rejects(ProgressionService.respec(accountId, { transactionId: 'tx-r', expectedRevision: 1 }), { errorCode: 'INSUFFICIENT_FUNDS' });
    await assertUnchanged(accountId, 1, RESPEC_GOLD_COST - 1);
  });
});
