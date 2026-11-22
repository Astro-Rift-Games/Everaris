'use strict';
// TASK-302 (Doc 23 §9, §13 UPG-01..08): authoritative NPC upgrade HTTP contract against real Mongo.
const test = require('node:test');
const assert = require('node:assert/strict');
const request = require('supertest');
const app = require('../../src/app');
const testDb = require('./testDb');
const Character = require('../../src/models/Character');
const { EQUIPMENT_SLOTS } = require('../../src/config/equipmentSlots');

const id = n => n.toString(16).padStart(24, '0');
const SWORD = id(1); const HOOD = id(2); const WAND = id(3); const STASHED = id(4);
const unit = (slot, lootId, instanceId, modifiers) =>
  ({ slot, amount: 1, lootId, instance: { instanceId, ...(modifiers ? { modifiers } : {}) } });
const stack = (slot, lootId, amount) => ({ slot, lootId, amount });
const noScaling = () => ({ gemSocketCount: 0, gems: [] });
const equipment = (patch = {}) => ({ ...Object.fromEntries(EQUIPMENT_SLOTS.map(slot => [slot, null])), ...patch });

test('authoritative NPC upgrade HTTP contract', async t => {
  await testDb.connect();
  t.after(async () => testDb.closeDatabase());
  const reg = await request(app).post('/auth/register').send({ username: 'upgrader', password: 'password123' });
  const token = reg.body.token;
  assert.equal((await request(app).post('/character/me').set('Authorization', `Bearer ${token}`).send({ name: 'Upgrader' })).status, 201);
  const { accountId } = await Character.findOne({ name: 'Upgrader' });
  const post = body => request(app).post('/character/me/inventory/upgrades').set('Authorization', `Bearer ${token}`).send(body);
  // `slots` replaces entries of the default Inventory by array index.
  const reset = async (patch = {}, slots = {}) => Character.updateOne({ accountId }, { $set: {
    revision: 0, appliedShopReceipts: [], 'inventory.inventorySchemaVersion': 3,
    'inventory.currency': 1000, 'inventory.pendingReservation': null,
    'inventory.loadout': Object.assign([
      unit(0, 'arming_sword', SWORD, noScaling()),
      unit(1, 'fire_mage_hood', HOOD),
      unit(2, 'magic_wand', WAND, noScaling()),
      stack(3, 'RES_RUNE_FRAGMENT', 3), stack(4, 'RES_RUNE_FRAGMENT', 20),
      stack(5, 'RES_ETHER_DUST', 20), stack(6, 'RES_ATTUNEMENT_GEM', 2), stack(7, 'RES_ANCIENT_ARMOR', 5)
    ], slots),
    'inventory.stash': [unit(0, 'arming_sword', STASHED, noScaling()), stack(1, 'RES_BLACK_IRON', 10)],
    'inventory.preparedEquipment': equipment(),
    ...patch
  } });
  const ticket = (patch = {}) => ({ transactionId: 'upg-1', expectedRevision: 0, merchant: 'blacksmith',
    operation: 'primary_scaling', instanceId: SWORD, location: 'loadout', slot: 0, ...patch });
  const unchanged = async (body, status, code) => {
    const before = await Character.findOne({ accountId }).lean();
    const res = await post(body);
    assert.equal(res.status, status, JSON.stringify(res.body));
    assert.equal(res.body.error, code);
    assert.deepEqual(await Character.findOne({ accountId }).lean(), before);
  };
  const amountOf = (items, lootId) => items.filter(item => item.lootId === lootId).reduce((sum, item) => sum + item.amount, 0);

  await t.test('UPG-03: E → D → C costs, secondary intact, InstanceID and slot preserved, one debit each', async () => {
    const secondary = { attribute: 'luck', grade: 'A' };
    await reset({}, { 0: unit(0, 'arming_sword', SWORD,
      { primary: { attribute: 'strength', grade: 'E' }, secondary, gemSocketCount: 0, gems: [] }) });
    let res = await post(ticket());
    assert.equal(res.status, 200, JSON.stringify(res.body));
    assert.equal(res.body.alreadySecured, false);
    assert.equal(res.body.currency, 900);
    assert.equal(res.body.revision, 1);
    assert.equal(amountOf(res.body.loadout, 'RES_RUNE_FRAGMENT'), 20);
    assert.equal(amountOf(res.body.loadout, 'RES_ETHER_DUST'), 19);
    assert.deepEqual(res.body.upgrade, { operation: 'primary_scaling', location: 'loadout', slot: 0, lootId: 'arming_sword',
      instance: { instanceId: SWORD, modifiers: { primary: { attribute: 'strength', grade: 'D' }, secondary, gemSocketCount: 0, gems: [] } },
      gold: 100, materials: { RES_RUNE_FRAGMENT: 3, RES_ETHER_DUST: 1 }, objectValue: 95 });
    res = await post(ticket({ transactionId: 'upg-2', expectedRevision: 1 }));
    assert.equal(res.status, 200, JSON.stringify(res.body));
    assert.equal(res.body.currency, 740);
    assert.equal(amountOf(res.body.loadout, 'RES_RUNE_FRAGMENT'), 16);
    const stored = (await Character.findOne({ accountId }).lean()).inventory.loadout.find(item => item.slot === 0);
    assert.deepEqual(stored.instance, { instanceId: SWORD,
      modifiers: { primary: { attribute: 'strength', grade: 'C' }, secondary, gemSocketCount: 0, gems: [] } });
    await unchanged(ticket({ transactionId: 'upg-3', expectedRevision: 2 }), 409, 'UPGRADE_LIMIT_REACHED');
  });

  await t.test('UPG-01: materials only in Stash fail; gold shortage fails; nothing is consumed', async () => {
    // Knight basic material (Black Iron) exists only in the Stash.
    await reset({}, { 1: unit(1, 'heavy_armor_helmet', HOOD) });
    await unchanged(ticket({ operation: 'armor_level', instanceId: HOOD, slot: 1 }), 409, 'INSUFFICIENT_MATERIALS');
    await reset({ 'inventory.currency': 59 });
    await unchanged(ticket(), 409, 'INSUFFICIENT_FUNDS');
  });

  await t.test('S6: materials split between Inventory and Stash are insufficient; exact gold leaves 0; replay ignores expectedRevision', async () => {
    await reset({ 'inventory.loadout': [unit(0, 'heavy_armor_helmet', HOOD), stack(1, 'RES_BLACK_IRON', 1)],
      'inventory.stash': [stack(0, 'RES_BLACK_IRON', 5)] });
    await unchanged(ticket({ operation: 'armor_level', instanceId: HOOD }), 409, 'INSUFFICIENT_MATERIALS');
    await reset({ 'inventory.currency': 40, 'inventory.loadout': [unit(0, 'heavy_armor_helmet', HOOD), stack(1, 'RES_BLACK_IRON', 2)] });
    const first = await post(ticket({ operation: 'armor_level', instanceId: HOOD }));
    assert.equal(first.status, 200, JSON.stringify(first.body));
    assert.equal(first.body.currency, 0);
    assert.equal(amountOf(first.body.loadout, 'RES_BLACK_IRON'), 0);
    const replay = await post(ticket({ operation: 'armor_level', instanceId: HOOD, expectedRevision: 7 }));
    assert.equal(replay.status, 200, JSON.stringify(replay.body));
    assert.equal(replay.body.alreadySecured, true);
    assert.equal(replay.body.revision, 1);
    assert.equal(replay.body.currency, 0);
  });

  await t.test('UPG-02 / S6: equipped item stays equipped; stashed item is rejected', async () => {
    await reset({ 'inventory.loadout': [stack(0, 'RES_ETHER_DUST', 9), stack(1, 'RES_ANCIENT_ARMOR', 3)],
      'inventory.preparedEquipment': equipment({ helmet: { lootId: 'fire_mage_hood', instance: { instanceId: HOOD } } }) });
    const res = await post(ticket({ operation: 'armor_level', instanceId: HOOD, location: 'equipment', slot: 'helmet' }));
    assert.equal(res.status, 200, JSON.stringify(res.body));
    assert.deepEqual(res.body.preparedEquipment.helmet, { lootId: 'fire_mage_hood', instance: { instanceId: HOOD, modifiers: { upgradeLevel: 1 } } });
    assert.equal(res.body.currency, 960);
    assert.equal(amountOf(res.body.loadout, 'RES_ETHER_DUST'), 7);
    assert.equal(res.body.upgrade.objectValue, 75);
    await reset();
    await unchanged(ticket({ instanceId: STASHED, location: 'stash', slot: 0 }), 400, 'VALIDATION_FAILED');
    await unchanged(ticket({ instanceId: STASHED, location: 'loadout', slot: 0 }), 409, 'INSTANCE_MISMATCH');
    await unchanged(ticket({ instanceId: SWORD, location: 'equipment', slot: 'weaponSlot1' }), 409, 'INSTANCE_MISMATCH');
  });

  await t.test('UPG-04: B/A/S primary is never degraded or replaced', async () => {
    for (const grade of ['B', 'A', 'S']) {
      await reset({}, { 0: unit(0, 'arming_sword', SWORD, { primary: { attribute: 'strength', grade }, gemSocketCount: 0, gems: [] }) });
      await unchanged(ticket(), 409, 'UPGRADE_GRADE_LOCKED');
    }
  });

  await t.test('UPG-05: first socket only when the weapon has none', async () => {
    await reset();
    const res = await post(ticket({ merchant: 'arcanist', operation: 'first_socket', instanceId: WAND, slot: 2 }));
    assert.equal(res.status, 200, JSON.stringify(res.body));
    assert.deepEqual(res.body.upgrade.instance, { instanceId: WAND, modifiers: { gemSocketCount: 1, gems: [null] } });
    assert.equal(res.body.currency, 820);
    assert.equal(amountOf(res.body.loadout, 'RES_ATTUNEMENT_GEM'), 1);
    assert.equal(amountOf(res.body.loadout, 'RES_RUNE_FRAGMENT'), 21);
    assert.equal(res.body.upgrade.objectValue, 130);
    await unchanged(ticket({ transactionId: 'upg-9', expectedRevision: 1, merchant: 'arcanist', operation: 'first_socket',
      instanceId: WAND, slot: 2 }), 409, 'UPGRADE_SOCKET_EXISTS');
  });

  await t.test('UPG-06: armor +0 → +3 consumes the transition row only and caps at +3', async () => {
    await reset({ 'inventory.loadout': [unit(0, 'fire_mage_hood', HOOD), stack(1, 'RES_ETHER_DUST', 9), stack(2, 'RES_ANCIENT_ARMOR', 3)] });
    const golds = [];
    for (let revision = 0; revision < 3; revision++) {
      const res = await post(ticket({ transactionId: `armor-${revision}`, expectedRevision: revision, operation: 'armor_level', instanceId: HOOD }));
      assert.equal(res.status, 200, JSON.stringify(res.body));
      assert.deepEqual(res.body.upgrade.instance.modifiers, { upgradeLevel: revision + 1 });
      golds.push(res.body.currency);
    }
    assert.deepEqual(golds, [960, 880, 740]);
    const stored = await Character.findOne({ accountId }).lean();
    assert.equal(amountOf(stored.inventory.loadout, 'RES_ETHER_DUST'), 0);
    assert.equal(amountOf(stored.inventory.loadout, 'RES_ANCIENT_ARMOR'), 0);
    assert.equal(stored.inventory.loadout.length, 1);
    await unchanged(ticket({ transactionId: 'armor-4', expectedRevision: 3, operation: 'armor_level', instanceId: HOOD }), 409, 'UPGRADE_LIMIT_REACHED');
  });

  await t.test('S5: wrong NPC is rejected without mutation', async () => {
    await reset();
    await unchanged(ticket({ merchant: 'arcanist' }), 422, 'UPGRADE_WRONG_MERCHANT');
    await unchanged(ticket({ merchant: 'blacksmith', instanceId: WAND, slot: 2 }), 422, 'UPGRADE_WRONG_MERCHANT');
    await unchanged(ticket({ merchant: 'general' }), 422, 'UPGRADE_WRONG_MERCHANT');
  });

  await t.test('UPG-08 / COM-04: replay returns the recorded outcome once; reuse and stale revision change nothing', async () => {
    await reset();
    const first = await post(ticket());
    assert.equal(first.status, 200, JSON.stringify(first.body));
    const before = await Character.findOne({ accountId }).lean();
    const replay = await post(ticket());
    assert.equal(replay.status, 200, JSON.stringify(replay.body));
    assert.equal(replay.body.alreadySecured, true);
    assert.deepEqual(replay.body.upgrade, first.body.upgrade);
    assert.equal(replay.body.currency, 940);
    assert.deepEqual(await Character.findOne({ accountId }).lean(), before);
    await unchanged(ticket({ slot: 2, instanceId: WAND, merchant: 'arcanist' }), 409, 'TRANSACTION_ID_REUSED');
    await unchanged(ticket({ transactionId: 'upg-stale', expectedRevision: 0 }), 409, 'REVISION_CONFLICT');
    await reset({ 'inventory.pendingReservation': { reservationId: 'r1', items: [], preparedEquipment: equipment() } });
    await unchanged(ticket(), 409, 'RESERVATION_PENDING');
  });

  await t.test('validation: malformed tickets are rejected without mutation', async () => {
    await reset();
    for (const patch of [{ transactionId: '' }, { operation: 'second_socket' }, { merchant: 'trainer' }, { instanceId: 'x' },
      { expectedRevision: -1 }, { location: 'equipment', slot: 'cape' }, { location: 'loadout', slot: -1 }]) {
      await unchanged(ticket(patch), 400, 'VALIDATION_FAILED');
    }
    await reset({ 'inventory.inventorySchemaVersion': 2, 'inventory.loadout': [], 'inventory.stash': [] });
    await unchanged(ticket(), 409, 'INVENTORY_MIGRATION_REQUIRED');
  });
});
