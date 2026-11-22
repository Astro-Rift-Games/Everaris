'use strict';
// TASK-302 (Doc 23 §7, §9.3, §13 COM-07, ECO-03, ECO-04): sale/buyback price includes upgrade ObjectValue,
// so buy/upgrade/sell and sell/buyback/upgrade/sell never mint gold. Real Mongo, HTTP only.
const test = require('node:test');
const assert = require('node:assert/strict');
const request = require('supertest');
const app = require('../../src/app');
const testDb = require('./testDb');
const Character = require('../../src/models/Character');
const { EQUIPMENT_SLOTS } = require('../../src/config/equipmentSlots');

const id = n => n.toString(16).padStart(24, '0');
const SWORD = id(1); const BREASTPLATE = id(2);
const unit = (slot, lootId, instanceId, modifiers) =>
  ({ slot, amount: 1, lootId, instance: { instanceId, ...(modifiers ? { modifiers } : {}) } });
const stack = (slot, lootId, amount) => ({ slot, lootId, amount });
const plain = () => ({ gemSocketCount: 0, gems: [] });
const equipment = () => Object.fromEntries(EQUIPMENT_SLOTS.map(slot => [slot, null]));
const START_GOLD = 1000;

test('upgraded equipment sale, buyback and ECO-03/ECO-04 cycles', async t => {
  await testDb.connect();
  t.after(async () => testDb.closeDatabase());
  const reg = await request(app).post('/auth/register').send({ username: 'smith', password: 'password123' });
  const token = reg.body.token;
  assert.equal((await request(app).post('/character/me').set('Authorization', `Bearer ${token}`).send({ name: 'Smith' })).status, 201);
  const { accountId } = await Character.findOne({ name: 'Smith' });
  const auth = req => req.set('Authorization', `Bearer ${token}`);
  const trade = body => auth(request(app).post('/character/me/inventory/shop/trade')).send(body);
  const upgrade = body => auth(request(app).post('/character/me/inventory/upgrades')).send(body);
  const reset = async (loadout, currency = START_GOLD) => Character.updateOne({ accountId }, { $set: {
    revision: 0, appliedShopReceipts: [], 'inventory.inventorySchemaVersion': 3, 'inventory.currency': currency,
    'inventory.pendingReservation': null, 'inventory.preparedEquipment': equipment(), 'inventory.stash': [],
    'inventory.buyback': { sessionEpoch: 'town', entries: [] },
    'inventory.loadout': [...loadout, stack(30, 'RES_RUNE_FRAGMENT', 30), stack(31, 'RES_ETHER_DUST', 10),
      stack(32, 'RES_ATTUNEMENT_GEM', 2), stack(33, 'RES_BLACK_IRON', 10), stack(34, 'RES_ANCIENT_ARMOR', 5)]
  } });
  const stored = async () => Character.findOne({ accountId }).lean();
  const sell = (lootId, sourceSlot, revision, merchant = 'blacksmith', transactionId = `sell-${revision}`) =>
    trade({ transactionId, expectedRevision: revision, merchant, sales: [{ lootId, amount: 1, sourceSlot }] });
  const find = (res, lootId) => res.body.loadout.find(item => item.lootId === lootId);

  await t.test('S4/COM-07: upgraded weapon sells for its ObjectValue; buyback charges ceil(received × 1.5) and keeps modifiers', async () => {
    const modifiers = { primary: { attribute: 'strength', grade: 'C' }, gemSocketCount: 1, gems: [null] };
    await reset([unit(0, 'arming_sword', SWORD, modifiers)]);
    const res = await sell('arming_sword', 0, 0);
    assert.equal(res.status, 200, JSON.stringify(res.body));
    assert.equal(res.body.currency, START_GOLD + 170);
    assert.equal(res.body.buyback.entries.length, 1);
    assert.equal(res.body.buyback.entries[0].unitPrice, 255);
    const entry = (await stored()).inventory.buyback.entries[0];
    assert.deepEqual(entry.instance, { instanceId: SWORD, modifiers });
    const back = await trade({ transactionId: 'back', expectedRevision: 1, buybacks: [{ entryId: res.body.buyback.entries[0].entryId, amount: 1 }] });
    assert.equal(back.status, 200, JSON.stringify(back.body));
    assert.equal(back.body.currency, START_GOLD + 170 - 255);
    assert.deepEqual(find(back, 'arming_sword').instance, { instanceId: SWORD, modifiers });
  });

  await t.test('S4: upgraded armor sells for ObjectValue; buyback rounds up', async () => {
    await reset([unit(0, 'heavy_armor_breastplate', BREASTPLATE, { upgradeLevel: 2 })]);
    const res = await sell('heavy_armor_breastplate', 0, 0);
    assert.equal(res.status, 200, JSON.stringify(res.body));
    assert.equal(res.body.currency, START_GOLD + 195);
    assert.equal(res.body.buyback.entries[0].unitPrice, 293);
    assert.deepEqual((await stored()).inventory.buyback.entries[0].instance, { instanceId: BREASTPLATE, modifiers: { upgradeLevel: 2 } });
  });

  await t.test('S4: installed gems still add their own sell value on top of ObjectValue', async () => {
    await reset([unit(0, 'arming_sword', SWORD, { primary: { attribute: 'strength', grade: 'C' }, gemSocketCount: 1, gems: ['RES_RUBY_GEM'] })]);
    const res = await sell('arming_sword', 0, 0);
    assert.equal(res.status, 200, JSON.stringify(res.body));
    assert.equal(res.body.currency, START_GOLD + 170 + 70);
    assert.equal(res.body.buyback.entries[0].unitPrice, Math.ceil(240 * 1.5));
  });

  await t.test('regression: plain and legacy-shaped equipment keep the template sell and buyback price', async () => {
    await reset([unit(0, 'arming_sword', SWORD, plain()), unit(1, 'heavy_armor_breastplate', BREASTPLATE)]);
    const weapon = await sell('arming_sword', 0, 0);
    assert.equal(weapon.status, 200, JSON.stringify(weapon.body));
    assert.equal(weapon.body.currency, START_GOLD + 60);
    assert.equal(weapon.body.buyback.entries[0].unitPrice, 90);
    const armor = await sell('heavy_armor_breastplate', 1, 1);
    assert.equal(armor.status, 200, JSON.stringify(armor.body));
    assert.equal(armor.body.currency, START_GOLD + 60 + 160);
    assert.equal(armor.body.buyback.entries[1].unitPrice, 240);
  });

  await t.test('ECO-03: buy → upgrade → sell ends with less gold than at the start', async () => {
    await reset([]);
    const bought = await trade({ transactionId: 'buy', expectedRevision: 0, merchant: 'blacksmith', purchases: [{ lootId: 'arming_sword', amount: 1 }] });
    assert.equal(bought.status, 200, JSON.stringify(bought.body));
    assert.equal(bought.body.currency, START_GOLD - 120);
    const sword = find(bought, 'arming_sword');
    const ticket = (revision, operation) => ({ transactionId: `up-${revision}`, expectedRevision: revision, merchant: 'blacksmith',
      operation, instanceId: sword.instance.instanceId, location: 'loadout', slot: sword.slot });
    let revision = 1; let gold = START_GOLD - 120;
    for (const [operation, cost] of [['primary_scaling', 60], ['primary_scaling', 100], ['primary_scaling', 160], ['first_socket', 180]]) {
      const res = await upgrade(ticket(revision, operation));
      assert.equal(res.status, 200, JSON.stringify(res.body));
      gold -= cost; revision += 1;
      assert.equal(res.body.currency, gold);
    }
    const sold = await sell('arming_sword', sword.slot, revision);
    assert.equal(sold.status, 200, JSON.stringify(sold.body));
    // 1000 - 120 (buy) - 500 (NPC) + 170 (ObjectValue of C + first socket) = 550.
    assert.equal(sold.body.currency, 550);
    assert.ok(sold.body.currency < START_GOLD);
  });

  await t.test('ECO-04: each sell → buyback → upgrade → sell cycle loses gold', async () => {
    await reset([unit(0, 'arming_sword', SWORD, plain())]);
    const entryOf = res => res.body.buyback.entries.at(-1).entryId;
    const upgradeAt = (revision, slot) => upgrade({ transactionId: `up-${revision}`, expectedRevision: revision, merchant: 'blacksmith',
      operation: 'primary_scaling', instanceId: SWORD, location: 'loadout', slot });
    const first = await sell('arming_sword', 0, 0);
    assert.equal(first.body.currency, START_GOLD + 60);
    // Cycle 1: buyback 90, upgrade none → E (60), sell 80 (60 + 20).
    let res = await trade({ transactionId: 'back-1', expectedRevision: 1, buybacks: [{ entryId: entryOf(first), amount: 1 }] });
    assert.equal(res.body.currency, START_GOLD + 60 - 90);
    let slot = find(res, 'arming_sword').slot;
    res = await upgradeAt(2, slot);
    assert.equal(res.status, 200, JSON.stringify(res.body));
    assert.equal(res.body.currency, START_GOLD + 60 - 90 - 60);
    const second = await sell('arming_sword', slot, 3);
    assert.equal(second.body.currency, 990);
    assert.ok(second.body.currency - first.body.currency < 0);
    // Cycle 2: buyback 120 (ceil(80 × 1.5)), upgrade E → D (100), sell 95 (60 + 35).
    res = await trade({ transactionId: 'back-2', expectedRevision: 4, buybacks: [{ entryId: entryOf(second), amount: 1 }] });
    assert.equal(res.body.currency, 990 - 120);
    slot = find(res, 'arming_sword').slot;
    res = await upgradeAt(5, slot);
    assert.equal(res.status, 200, JSON.stringify(res.body));
    const third = await sell('arming_sword', slot, 6);
    assert.equal(third.body.currency, 990 - 120 - 100 + 95);
    assert.ok(third.body.currency - second.body.currency < 0);
    assert.ok(third.body.currency < START_GOLD);
  });
});
