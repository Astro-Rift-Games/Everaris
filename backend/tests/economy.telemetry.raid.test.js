'use strict';

// TASK-305 T4: raid close summaries (extraction / defeat / abandon) and INVENTORY_FULL block events.
// Real in-memory MongoDB and the public service calls; replays, refunds and rejections emit nothing.
const test = require('node:test');
const assert = require('node:assert/strict');
const mongoose = require('mongoose');
const { MongoMemoryServer } = require('mongodb-memory-server');
const Character = require('../src/models/Character');
const Account = require('../src/models/Account');
const AuthoritativeExtractionResult = require('../src/models/AuthoritativeExtractionResult');
const EconomyTelemetryEvent = require('../src/models/EconomyTelemetryEvent');
const EconomyTelemetry = require('../src/services/EconomyTelemetry');
const InventoryService = require('../src/services/InventoryService');
const ExtractionCommitService = require('../src/services/ExtractionCommitService');
const DefeatCommitService = require('../src/services/DefeatCommitService');
const { createEquipment } = require('../src/services/ItemInstances');
const { EQUIPMENT_SLOTS } = require('../src/config/equipmentSlots');

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
  await Promise.all([Character.deleteMany({}), Account.deleteMany({}),
    AuthoritativeExtractionResult.deleteMany({}), EconomyTelemetryEvent.deleteMany({})]);
});

const characters = () => mongoose.connection.collection('characters');
const emptyEquipment = (values = {}) => Object.fromEntries(EQUIPMENT_SLOTS.map(slot => [slot, values[slot] ?? null]));

// Reservation values (Doc 23 catalog): replacement gold = buy price, carried value = sell value.
const GEARED_EQUIPMENT = () => emptyEquipment({
  weaponSlot1: createEquipment('magic_sword'), armor: createEquipment('heavy_armor_breastplate'),
  helmet: createEquipment('heavy_armor_helmet'), gloves: createEquipment('heavy_armor_gauntlets')
});
const GEARED_REPLACEMENT_GOLD = 250 + 320 + 180 + 120;
const GEARED_CARRIED_VALUE = 125 + 160 + 90 + 60 + 20 * 2 + 5 * 3;
const RESERVED_ITEMS = [{ slot: 0, lootId: 'CON_HEALTH_POTION', amount: 2 }, { slot: 1, lootId: 'bone', amount: 3 }];

async function createRaidCharacter({ reservation = {}, inventory = {}, level = 1 } = {}) {
  const accountId = new mongoose.Types.ObjectId();
  const result = await characters().insertOne({
    accountId, name: `Raid${accountId}`, revision: 5, level, experience: 0, unlockedAbilities: [],
    appliedProgressionReceipts: [],
    characterAttributes: { vitality: 5, resistance: 5, strength: 5, dexterity: 5, intelligence: 5, luck: 5, availablePoints: 0 },
    inventory: {
      inventorySchemaVersion: 3, currency: 250, stash: [], loadout: [], preparedEquipment: emptyEquipment(),
      appliedExtractionReceipts: [],
      pendingReservation: {
        reservationId: 'res-1', raidId: 'raid-1', admittedAt: new Date(), startedAt: null,
        items: RESERVED_ITEMS, preparedEquipment: GEARED_EQUIPMENT(), ...reservation
      },
      ...inventory
    }
  });
  return { accountId, characterId: String(result.insertedId) };
}

const publishResult = (accountId, overrides = {}) => AuthoritativeExtractionResult.create({
  raidId: 'raid-1', accountId: accountId.toString(), reservationId: 'res-1', resultSequence: 1,
  items: [], experienceGranted: 0, ...overrides
});

async function events() {
  await EconomyTelemetry.flush();
  return EconomyTelemetryEvent.find({}).sort({ occurredAt: 1 }).lean();
}

const rawOf = accountId => characters().findOne({ accountId });
const byLootId = lines => [...lines].sort((a, b) => a.lootId.localeCompare(b.lootId));

// ---------------------------------------------------------------------------
// Entry profile classification (S5, S8)
// ---------------------------------------------------------------------------

test('classifyEntryProfile maps replacement gold to naked / budget / geared at the boundaries', () => {
  assert.equal(EconomyTelemetry.ENTRY_PROFILE_GEARED_MIN_GOLD, 800);
  assert.equal(EconomyTelemetry.classifyEntryProfile(0), 'naked');
  assert.equal(EconomyTelemetry.classifyEntryProfile(1), 'budget');
  assert.equal(EconomyTelemetry.classifyEntryProfile(799), 'budget');
  assert.equal(EconomyTelemetry.classifyEntryProfile(800), 'geared');
  assert.equal(EconomyTelemetry.classifyEntryProfile(5000), 'geared');
});

// ---------------------------------------------------------------------------
// Extraction
// ---------------------------------------------------------------------------

test('a committed extraction emits one raid_close event with the entry profile and extracted value', async () => {
  const { accountId, characterId } = await createRaidCharacter();
  await publishResult(accountId, {
    items: [{ lootId: 'CON_HEALTH_POTION', amount: 1 }],
    preparedEquipment: { weaponSlot1: { lootId: 'magic_sword' }, armor: { lootId: 'heavy_armor_breastplate' } },
    experienceGranted: 50
  });
  await ExtractionCommitService.commit(accountId, { raidId: 'raid-1', resultSequence: 1 });

  const [event, ...rest] = await events();
  assert.equal(rest.length, 0);
  assert.equal(event.characterId, characterId);
  assert.equal(event.operation, 'raid_close');
  assert.deepEqual(event.gold, []);
  assert.equal(event.goldGenerated, 0);
  assert.equal(event.goldSpent, 0);
  assert.deepEqual(event.details, {
    outcome: 'extraction', raidId: 'raid-1', entryProfile: 'geared',
    carriedEquipmentReplacementGold: GEARED_REPLACEMENT_GOLD, carriedItemsValue: GEARED_CARRIED_VALUE,
    extractedItemsValue: 20 + 125 + 160, lostValue: 0, experienceGranted: 50
  });
  assert.equal(JSON.stringify(event).includes(String(accountId)), false, 'account id must never be stored');
});

test('an extraction that carried only a training weapon is classified naked with zero replacement gold', async () => {
  const { accountId } = await createRaidCharacter({
    reservation: { items: [], preparedEquipment: emptyEquipment({ weaponSlot1: createEquipment('training_short_sword') }) }
  });
  await publishResult(accountId, { preparedEquipment: { weaponSlot1: { lootId: 'training_short_sword' } }, experienceGranted: 10 });
  await ExtractionCommitService.commit(accountId, { raidId: 'raid-1', resultSequence: 1 });

  const [event] = await events();
  assert.equal(event.details.entryProfile, 'naked');
  assert.equal(event.details.carriedEquipmentReplacementGold, 0);
  assert.equal(event.details.carriedItemsValue, 0);
  assert.equal(event.details.extractedItemsValue, 0);
});

test('an extraction replay emits nothing more and leaves state unchanged', async () => {
  const { accountId } = await createRaidCharacter();
  await publishResult(accountId, { items: [{ lootId: 'bone', amount: 1 }] });
  await ExtractionCommitService.commit(accountId, { raidId: 'raid-1', resultSequence: 1 });
  const afterFirst = await rawOf(accountId);

  const replay = await ExtractionCommitService.commit(accountId, { raidId: 'raid-1', resultSequence: 1 });
  assert.equal(replay.alreadySecured, true);
  assert.deepEqual(await rawOf(accountId), afterFirst);
  assert.equal((await events()).length, 1);
});

test('rejected extraction commits emit nothing', async () => {
  const { accountId } = await createRaidCharacter();
  await assert.rejects(ExtractionCommitService.commit(accountId, { raidId: 'raid-none', resultSequence: 1 }),
    e => e.errorCode === 'NO_AUTHORITATIVE_RESULT');
  await publishResult(accountId, { consumed: true });
  await assert.rejects(ExtractionCommitService.commit(accountId, { raidId: 'raid-1', resultSequence: 1 }),
    e => e.errorCode === 'RESULT_ALREADY_CONSUMED');
  assert.deepEqual(await events(), []);
});

// ---------------------------------------------------------------------------
// Defeat
// ---------------------------------------------------------------------------

test('a committed defeat emits one raid_close event with the lost value and lost items', async () => {
  const { accountId, characterId } = await createRaidCharacter();
  await publishResult(accountId, { experienceGranted: 30 });
  await DefeatCommitService.commit(accountId, { raidId: 'raid-1', resultSequence: 1 });

  const [event, ...rest] = await events();
  assert.equal(rest.length, 0);
  assert.equal(event.characterId, characterId);
  assert.equal(event.operation, 'raid_close');
  assert.deepEqual(event.gold, []);
  const { lostItems, ...details } = event.details;
  assert.deepEqual(details, {
    outcome: 'defeat', raidId: 'raid-1', entryProfile: 'geared',
    carriedEquipmentReplacementGold: GEARED_REPLACEMENT_GOLD, carriedItemsValue: GEARED_CARRIED_VALUE,
    lostValue: GEARED_CARRIED_VALUE, experienceGranted: 30
  });
  assert.equal('extractedItemsValue' in details, false);
  assert.deepEqual(byLootId(lostItems), byLootId([
    { lootId: 'CON_HEALTH_POTION', amount: 2 }, { lootId: 'bone', amount: 3 }, { lootId: 'magic_sword', amount: 1 },
    { lootId: 'heavy_armor_breastplate', amount: 1 }, { lootId: 'heavy_armor_helmet', amount: 1 },
    { lootId: 'heavy_armor_gauntlets', amount: 1 }
  ]));
});

test('a budget defeat classifies by the replacement gold of the carried equipment only', async () => {
  const { accountId } = await createRaidCharacter({
    reservation: { items: [], preparedEquipment: emptyEquipment({ weaponSlot1: createEquipment('arming_sword') }) }
  });
  await publishResult(accountId);
  await DefeatCommitService.commit(accountId, { raidId: 'raid-1', resultSequence: 1 });

  const [event] = await events();
  assert.equal(event.details.entryProfile, 'budget');
  assert.equal(event.details.carriedEquipmentReplacementGold, 120);
  assert.equal(event.details.lostValue, 60);
});

test('a defeat replay and rejected defeat commits emit nothing more', async () => {
  const { accountId } = await createRaidCharacter();
  await assert.rejects(DefeatCommitService.commit(accountId, { raidId: 'raid-1', resultSequence: 0 }),
    e => e.errorCode === 'INVALID_PAYLOAD');
  await publishResult(accountId);
  await DefeatCommitService.commit(accountId, { raidId: 'raid-1', resultSequence: 1 });
  const afterFirst = await rawOf(accountId);

  const replay = await DefeatCommitService.commit(accountId, { raidId: 'raid-1', resultSequence: 1 });
  assert.equal(replay.alreadySecured, true);
  assert.deepEqual(await rawOf(accountId), afterFirst);
  await assert.rejects(DefeatCommitService.commit(accountId, { raidId: 'raid-1', resultSequence: 2 }),
    e => e.errorCode === 'RESULT_ALREADY_CONSUMED');
  assert.equal((await events()).length, 1);
});

// ---------------------------------------------------------------------------
// Abandon
// ---------------------------------------------------------------------------

test('abandoning an admitted reservation emits one raid_close abandon event with the lost items', async () => {
  const { accountId, characterId } = await createRaidCharacter();
  await InventoryService.clearPendingReservation(accountId);

  const [event, ...rest] = await events();
  assert.equal(rest.length, 0);
  assert.equal(event.characterId, characterId);
  assert.equal(event.operation, 'raid_close');
  assert.deepEqual(event.gold, []);
  const { lostItems, ...details } = event.details;
  assert.deepEqual(details, {
    outcome: 'abandon', raidId: 'raid-1', entryProfile: 'geared',
    carriedEquipmentReplacementGold: GEARED_REPLACEMENT_GOLD, carriedItemsValue: GEARED_CARRIED_VALUE,
    lostValue: GEARED_CARRIED_VALUE, experienceGranted: 0
  });
  assert.equal(lostItems.length, 6);
  assert.equal((await rawOf(accountId)).inventory.pendingReservation, null);
});

test('a pre-admission rollback and a pre-start refund restore the items and emit nothing', async () => {
  const rolledBack = await createRaidCharacter({ reservation: { admittedAt: null } });
  await InventoryService.clearPendingReservation(rolledBack.accountId, { rollback: true });

  const refunded = await createRaidCharacter();
  const result = await InventoryService.refundPendingReservation(refunded.accountId, 'res-1');
  assert.equal(result.refunded, true);

  assert.deepEqual(await events(), []);
});

test('clearing when no reservation is held, or with a mismatched guard, emits nothing', async () => {
  const { accountId } = await createRaidCharacter();
  await assert.rejects(InventoryService.clearPendingReservation(accountId, { expectedReservationId: 'other' }),
    e => e.errorCode === 'RESERVATION_MISMATCH');
  await characters().updateOne({ accountId }, { $set: { 'inventory.pendingReservation': null } });
  await InventoryService.clearPendingReservation(accountId);
  assert.deepEqual(await events(), []);
});

test('abandoning a reservation that carried nothing emits nothing', async () => {
  const { accountId } = await createRaidCharacter({ reservation: { items: [], preparedEquipment: emptyEquipment() } });
  await InventoryService.clearPendingReservation(accountId);
  assert.equal((await rawOf(accountId)).inventory.pendingReservation, null);
  assert.deepEqual(await events(), []);
});

test('a defeat commit after an abandon does not count the expedition twice', async () => {
  const { accountId } = await createRaidCharacter();
  await InventoryService.clearPendingReservation(accountId);
  await publishResult(accountId, { experienceGranted: 20 });
  await DefeatCommitService.commit(accountId, { raidId: 'raid-1', resultSequence: 1 });

  const recorded = await events();
  assert.deepEqual(recorded.map(event => event.details.outcome), ['abandon']);
});

// ---------------------------------------------------------------------------
// Capacity blocks (S4)
// ---------------------------------------------------------------------------

const fullLoadout = () => Array.from({ length: 30 }, (_, slot) => ({ slot, lootId: `filler_${slot}`, amount: 1 }));
const capacityBlock = source => ({ source, limit: 'capacity' });

async function createFullCharacter(inventory = {}) {
  return createRaidCharacter({ inventory: { pendingReservation: null, loadout: fullLoadout(), ...inventory } });
}

async function assertBlockedWithoutWrite(accountId, action, source) {
  const before = await rawOf(accountId);
  await assert.rejects(action(), e => e.errorCode === 'INVENTORY_FULL' && e.statusCode === 409);
  assert.deepEqual(await rawOf(accountId), before, 'a blocked operation must not write currency, revision or items');
  const [event, ...rest] = await events();
  assert.equal(rest.length, 0);
  assert.equal(event.operation, 'capacity_block');
  assert.deepEqual(event.gold, []);
  assert.equal(event.goldGenerated, 0);
  assert.equal(event.goldSpent, 0);
  assert.deepEqual(event.details, capacityBlock(source));
}

test('a purchase refused for capacity records a purchase capacity_block and no gold', async () => {
  const { accountId } = await createFullCharacter();
  await assertBlockedWithoutWrite(accountId,
    () => InventoryService.shopBuy(accountId, 'CON_HEALTH_POTION', 1, 0, 5, 'tx-buy'), 'purchase');
});

test('a buyback refused for capacity records a buyback capacity_block and no gold', async () => {
  const { accountId } = await createFullCharacter({
    buyback: { entries: [{ entryId: 'bb_1', lootId: 'CON_THROWING_DAGGER', amount: 1, unitPrice: 18 }] }
  });
  await assertBlockedWithoutWrite(accountId, () => InventoryService.shopBuyback(accountId, 'bb_1', 1, 5, 'tx-bb'), 'buyback');
});

test('a trade refused for capacity records a trade capacity_block and no gold', async () => {
  const { accountId } = await createFullCharacter();
  await assertBlockedWithoutWrite(accountId, () => InventoryService.shopTrade(accountId, {
    transactionId: 'tx-trade', expectedRevision: 5, purchases: [{ lootId: 'CON_HEALTH_POTION', amount: 1 }]
  }), 'trade');
});

test('an extraction refused for capacity records an extraction capacity_block and leaves the result claimable', async () => {
  const { accountId } = await createRaidCharacter();
  await publishResult(accountId, { items: Array.from({ length: 31 }, (_, n) => ({ lootId: `spoil_${n}`, amount: 1 })) });
  const before = await rawOf(accountId);

  await assert.rejects(ExtractionCommitService.commit(accountId, { raidId: 'raid-1', resultSequence: 1 }),
    e => e.errorCode === 'INVENTORY_FULL');
  assert.deepEqual(await rawOf(accountId), before);
  assert.equal((await AuthoritativeExtractionResult.findOne({ raidId: 'raid-1' })).consumed, false);
  const [event, ...rest] = await events();
  assert.equal(rest.length, 0);
  assert.equal(event.operation, 'capacity_block');
  assert.deepEqual(event.gold, []);
  assert.deepEqual(event.details, capacityBlock('extraction'));
});

test('non-capacity rejections and replays record no capacity_block', async () => {
  const { accountId } = await createFullCharacter({ currency: 10 });
  await assert.rejects(InventoryService.shopBuy(accountId, 'CON_HEALTH_POTION', 1, 0, 5, 'tx-poor'),
    e => e.errorCode === 'INSUFFICIENT_FUNDS');
  await assert.rejects(InventoryService.shopBuy(accountId, 'CON_HEALTH_POTION', 1, 0, 9, 'tx-rev'),
    e => e.errorCode === 'REVISION_CONFLICT');
  assert.deepEqual(await events(), []);
});

test('a purchase that fits records its purchase event and no capacity_block', async () => {
  const { accountId } = await createRaidCharacter({ inventory: { pendingReservation: null } });
  await InventoryService.shopBuy(accountId, 'CON_HEALTH_POTION', 1, 0, 5, 'tx-ok');
  assert.deepEqual((await events()).map(event => event.operation), ['buy']);
});
