'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const request = require('supertest');
const app = require('../../src/app');
const testDb = require('./testDb');
const Character = require('../../src/models/Character');

test('atomic merchant trade HTTP contract', async t => {
  await testDb.connect();
  t.after(async () => testDb.closeDatabase());
  const reg = await request(app).post('/auth/register').send({ username: 'atomictrader', password: 'password123' });
  const token = reg.body.token;
  assert.equal((await request(app).post('/character/me').set('Authorization', `Bearer ${token}`).send({ name: 'Atomic' })).status, 201);
  const { accountId } = await Character.findOne({ name: 'Atomic' });
  const post = (body, header) => {
    const req = request(app).post('/character/me/inventory/shop/trade').set('Authorization', `Bearer ${token}`);
    if (header) req.set('X-Grimhold-Inventory-Protocol', header);
    return req.send(body);
  };
  const sale = { lootId: 'placeholder_helmet', amount: 1, sourceSlot: 0 };
  const purchase = { lootId: 'bone', amount: 1 };
  const ticket = (patch = {}) => ({ transactionId: 'ticket', expectedRevision: 0, purchases: [purchase], sales: [sale], ...patch });
  const reset = async (patch = {}) => Character.updateOne({ accountId }, { $set: {
    revision: 0, appliedShopReceipts: [], 'inventory.inventorySchemaVersion': 2,
    'inventory.currency': 0, 'inventory.pendingReservation': null,
    'inventory.loadout': [sale, { lootId: 'healthpotion', amount: 2, slot: 1 }].map((i, n) => ({ lootId: i.lootId, amount: i.amount, slot: n })),
    'inventory.stash': [{ slot: 8, lootId: 'bone', amount: 3 }],
    'inventory.buyback': { sessionEpoch: 'town', entries: [] }, ...patch
  } });
  const unchanged = async (body, status, code, header) => {
    const before = await Character.findOne({ accountId }).lean();
    const res = await post(body, header);
    assert.equal(res.status, status, JSON.stringify(res.body));
    assert.equal(res.body.error, code);
    assert.deepEqual(await Character.findOne({ accountId }).lean(), before);
  };
  await t.test('mixed ticket uses sale proceeds, one revision and one receipt; replay binds lines', async () => {
    await reset();
    const res = await post(ticket());
    assert.equal(res.status, 200, JSON.stringify(res.body));
    assert.equal(res.body.currency, 0);
    assert.equal(res.body.revision, 1);
    assert.equal(res.body.alreadySecured, false);
    assert.deepEqual(res.body.loadout, [{ slot: 0, lootId: 'bone', amount: 1 }, { slot: 1, lootId: 'healthpotion', amount: 2 }]);
    assert.deepEqual(res.body.stash, [{ slot: 8, lootId: 'bone', amount: 3 }]);
    assert.equal(res.body.buyback.entries.length, 1);
    assert.equal(res.body.buyback.entries[0].unitPrice, 8);
    const saved = await Character.findOne({ accountId });
    assert.equal(saved.appliedShopReceipts.length, 1);
    assert.equal(saved.appliedShopReceipts[0].type, 'trade');
    await saved.validate();
    const before = await Character.findOne({ accountId }).lean();
    const replay = await post(ticket());
    assert.deepEqual(replay.body, { ...res.body, alreadySecured: true });
    assert.deepEqual(await Character.findOne({ accountId }).lean(), before);
    await unchanged(ticket({ purchases: [{ ...purchase, amount: 2 }] }), 409, 'TRANSACTION_ID_REUSED');
  });
  await t.test('multiple sales only', async () => {
    await reset();
    const res = await post(ticket({ purchases: [], sales: [sale, { lootId: 'healthpotion', amount: 2, sourceSlot: 1 }] }));
    assert.equal(res.status, 200);
    assert.equal(res.body.currency, 45);
    assert.deepEqual(res.body.loadout, []);
    assert.equal(res.body.revision, 1);
  });
  await t.test('multiple purchases only preserve ticket placement order', async () => {
    await reset({ 'inventory.currency': 100, 'inventory.loadout': [] });
    const res = await post(ticket({ sales: [], purchases: [purchase, { lootId: 'healthpotion', amount: 2 }] }));
    assert.equal(res.status, 200);
    assert.equal(res.body.currency, 15);
    assert.deepEqual(res.body.loadout, [{ slot: 0, lootId: 'bone', amount: 1 }, { slot: 1, lootId: 'CON_HEALTH_POTION', amount: 2 }]);
  });
  await t.test('capacity is checked after removing sales', async () => {
    const full = Array.from({ length: 30 }, (_, slot) => ({ slot, lootId: slot === 0 ? 'placeholder_helmet' : `filler-${slot}`, amount: 1 }));
    await reset({ 'inventory.loadout': full, 'inventory.currency': 100 });
    await unchanged(ticket({ sales: [] }), 409, 'INVENTORY_FULL');
    const res = await post(ticket());
    assert.equal(res.status, 200);
    assert.equal(res.body.loadout.length, 30);
    assert.deepEqual(res.body.loadout[0], { slot: 0, lootId: 'bone', amount: 1 });
  });
  for (const [name, patch, body, status, code] of [
    ['final funds', {}, ticket({ purchases: [{ lootId: 'healthpotion', amount: 1 }] }), 409, 'INSUFFICIENT_FUNDS'],
    ['later sale fails', {}, ticket({ sales: [sale, { lootId: 'bone', amount: 1, sourceSlot: 5 }] }), 409, 'INSUFFICIENT_ITEMS'],
    ['later purchase fails', { 'inventory.currency': 100 }, ticket({ purchases: [purchase, { lootId: 'unknown', amount: 1 }] }), 404, 'ITEM_NOT_OFFERED'],
    ['unsellable', {}, ticket({ sales: [{ lootId: 'unknown', amount: 1, sourceSlot: 1 }] }), 404, 'ITEM_NOT_SELLABLE'],
    ['revision', {}, ticket({ expectedRevision: 9 }), 409, 'REVISION_CONFLICT'],
    ['reservation', { 'inventory.pendingReservation': { reservationId: 'raid' } }, ticket(), 409, 'RESERVATION_PENDING'],
    ['overflow', { 'inventory.currency': Number.MAX_SAFE_INTEGER }, ticket({ purchases: [] }), 400, 'INTEGER_OVERFLOW'],
    ['empty', {}, ticket({ sales: [], purchases: [] }), 400, 'VALIDATION_FAILED'],
    ['duplicate source', {}, ticket({ sales: [sale, { lootId: 'healthpotion', amount: 1, sourceSlot: 0 }] }), 400, 'VALIDATION_FAILED'],
    ['duplicate sale id', {}, ticket({ sales: [sale, { ...sale, sourceSlot: 1 }] }), 400, 'VALIDATION_FAILED'],
    ['duplicate purchase id', {}, ticket({ purchases: [purchase, purchase] }), 400, 'VALIDATION_FAILED'],
    ['zero amount', {}, ticket({ purchases: [{ ...purchase, amount: 0 }] }), 400, 'VALIDATION_FAILED'],
    ['excess amount', {}, ticket({ purchases: [{ ...purchase, amount: 1000 }] }), 400, 'VALIDATION_FAILED'],
    ['declared price', {}, ticket({ purchases: [{ ...purchase, declaredPrice: 0 }] }), 400, 'VALIDATION_FAILED'],
    ['v2 missing source', {}, ticket({ sales: [{ lootId: sale.lootId, amount: 1 }] }), 409, 'INVENTORY_SLOT_ADDRESS_REQUIRED'],
    ['v3 missing source', { 'inventory.inventorySchemaVersion': 3, 'inventory.loadout': [], 'inventory.preparedEquipment': Object.fromEntries(require('../../src/config/equipmentSlots').EQUIPMENT_SLOTS.map(slot => [slot, null])) }, ticket({ sales: [{ lootId: 'bone', amount: 1 }] }), 409, 'INVENTORY_SLOT_ADDRESS_REQUIRED'],
    ['purchase limit', {}, ticket({ purchases: Array.from({ length: 17 }, (_, i) => ({ lootId: `id-${i}`, amount: 1 })) }), 400, 'VALIDATION_FAILED'],
    ['sale limit', {}, ticket({ sales: Array.from({ length: 31 }, (_, i) => ({ lootId: `id-${i}`, amount: 1, sourceSlot: i })) }), 400, 'VALIDATION_FAILED']
  ]) await t.test(`${name} rejects without any stored change`, async () => {
    await reset(patch); await unchanged(body, status, code);
  });
  await t.test('v3 gem sale pricing and equipment purchase issuance reuse single-line rules', async () => {
    const equipment = Object.fromEntries(require('../../src/config/equipmentSlots').EQUIPMENT_SLOTS.map(slot => [slot, null]));
    const instance = { instanceId: '0123456789abcdef01234567', modifiers: { gemSocketCount: 1, gems: ['RES_RUBY_GEM'] } };
    await reset({ 'inventory.inventorySchemaVersion': 3, 'inventory.preparedEquipment': equipment,
      'inventory.loadout': [{ slot: 0, lootId: 'arming_sword', amount: 1, instance }] });
    const body = ticket({ sales: [{ lootId: 'arming_sword', amount: 1, sourceSlot: 0 }],
      purchases: [{ lootId: 'placeholder_helmet', amount: 2 }] });
    const res = await post(body);
    assert.equal(res.status, 200, JSON.stringify(res.body));
    assert.equal(res.body.currency, 50);
    assert.deepEqual(res.body.buyback.entries, []);
    assert.equal(res.body.loadout.length, 2);
    assert.deepEqual(res.body.loadout.map(i => [i.slot, i.lootId, i.amount]), [[0, 'placeholder_helmet', 1], [1, 'placeholder_helmet', 1]]);
    const ids = res.body.loadout.map(i => i.instance.instanceId);
    assert.equal(new Set(ids).size, 2);
    assert.ok(ids.every(id => /^[0-9a-f]{24}$/.test(id) && id !== instance.instanceId));
    await (await Character.findOne({ accountId })).validate();
    const before = await Character.findOne({ accountId }).lean();
    await Character.updateOne({ accountId }, { $set: { 'inventory.pendingReservation': { reservationId: 'later', items: [], preparedEquipment: equipment } } });
    const reserved = await Character.findOne({ accountId }).lean();
    const replay = await post(body);
    assert.equal(replay.status, 200);
    assert.equal(replay.body.alreadySecured, true);
    assert.deepEqual(replay.body.loadout, res.body.loadout);
    assert.deepEqual(await Character.findOne({ accountId }).lean(), reserved);
    assert.equal(before.appliedShopReceipts.length, 1);
  });
  await t.test('existing single buy and sell retain their response and declared-price behavior', async () => {
    await reset({ 'inventory.currency': 100 });
    const single = (operation, body) => request(app).post(`/character/me/inventory/shop/${operation}`)
      .set('Authorization', `Bearer ${token}`).send(body);
    const buy = await single('buy', { transactionId: 'single-buy', expectedRevision: 0, lootId: 'bone', amount: 2, declaredPrice: 0 });
    assert.equal(buy.status, 200);
    assert.equal(buy.body.currency, 90);
    assert.equal(buy.body.revision, 1);
    assert.equal(buy.body.alreadySecured, undefined);
    const sell = await single('sell', { transactionId: 'single-sell', expectedRevision: 1, lootId: 'bone', amount: 1, sourceSlot: 2, declaredSellValue: 999 });
    assert.equal(sell.status, 200);
    assert.equal(sell.body.currency, 95);
    assert.equal(sell.body.revision, 2);
    assert.deepEqual(sell.body.loadout.find(i => i.lootId === 'bone'), { slot: 2, lootId: 'bone', amount: 1 });
  });
  await t.test('protocol gate precedes validation', async () => {
    await reset();
    const old = process.env.INVENTORY_PROTOCOL_GATE;
    process.env.INVENTORY_PROTOCOL_GATE = 'true';
    try {
      await unchanged({}, 426, 'CLIENT_UPGRADE_REQUIRED');
      assert.equal((await post(ticket(), '2')).status, 200);
    } finally { process.env.INVENTORY_PROTOCOL_GATE = old; }
  });
  await t.test('v1 ignores source slot', async () => {
    await reset({ 'inventory.inventorySchemaVersion': 1, 'inventory.loadout': [{ lootId: 'placeholder_helmet', amount: 1 }] });
    assert.equal((await post(ticket())).status, 200);
  });
  for (const race of ['reservation', 'epoch']) await t.test(`${race} race guards the single atomic write`, async () => {
    await reset();
    const original = Character.findOneAndUpdate;
    let raced;
    Character.findOneAndUpdate = async function(filter, update, options) {
      await Character.updateOne({ accountId }, { $set: race === 'reservation'
        ? { 'inventory.pendingReservation': { reservationId: 'race' } }
        : { 'inventory.buyback': { sessionEpoch: 'new', entries: [] } } });
      raced = await Character.findOne({ accountId }).lean();
      return original.call(this, filter, update, options);
    };
    try {
      const res = await post(ticket());
      assert.equal(res.status, 409);
      assert.equal(res.body.error, race === 'reservation' ? 'RESERVATION_PENDING' : 'REVISION_CONFLICT');
      assert.deepEqual(await Character.findOne({ accountId }).lean(), raced);
    } finally { Character.findOneAndUpdate = original; }
  });

  await t.test('multiple buybacks in one cart commit and reject atomically (TASK-332)', async () => {
    const equipment = Object.fromEntries(require('../../src/config/equipmentSlots').EQUIPMENT_SLOTS.map(slot => [slot, null]));
    const instA = { instanceId: 'aaaaaaaaaaaaaaaaaaaaaaaa', modifiers: { primary: { grade: 'E' }, gemSocketCount: 0, gems: [] } };
    const instB = { instanceId: 'bbbbbbbbbbbbbbbbbbbbbbbb', modifiers: { upgradeLevel: 1 } };
    const buybackEntries = [
      { entryId: 'bb-1', lootId: 'arming_sword', amount: 1, unitPrice: 30, instance: instA },
      { entryId: 'bb-2', lootId: 'heavy_armor_helmet', amount: 1, unitPrice: 25, instance: instB },
      { entryId: 'bb-3', lootId: 'CON_EXPLOSIVE_BOMB', amount: 2, unitPrice: 10 }
    ];

    // 1. Multiple buybacks alone commit atomically in 1 revision & 1 receipt, preserving instances
    await reset({
      'inventory.inventorySchemaVersion': 3,
      'inventory.preparedEquipment': equipment,
      'inventory.currency': 100,
      'inventory.loadout': [],
      'inventory.buyback': { sessionEpoch: 'town', entries: buybackEntries }
    });
    const res = await post(ticket({
      transactionId: 'multi-buyback-ok',
      purchases: [],
      sales: [],
      buybacks: [
        { entryId: 'bb-1', amount: 1 },
        { entryId: 'bb-2', amount: 1 },
        { entryId: 'bb-3', amount: 1 }
      ]
    }));
    assert.equal(res.status, 200, JSON.stringify(res.body));
    assert.equal(res.body.currency, 35);
    assert.equal(res.body.revision, 1);
    assert.equal(res.body.loadout.length, 3);
    assert.equal(res.body.loadout.find(i => i.lootId === 'arming_sword').instance.instanceId, instA.instanceId);
    assert.equal(res.body.loadout.find(i => i.lootId === 'heavy_armor_helmet').instance.instanceId, instB.instanceId);
    assert.deepEqual(res.body.buyback.entries.map(e => [e.entryId, e.amount]), [['bb-3', 1]]);

    // 2. Mixed cart: sale funds multiple buybacks + purchase in one atomic trade
    await reset({
      'inventory.inventorySchemaVersion': 3,
      'inventory.preparedEquipment': equipment,
      'inventory.currency': 40,
      'inventory.loadout': [{ slot: 0, lootId: 'TRE_CHALICE', amount: 1 }],
      'inventory.buyback': { sessionEpoch: 'town', entries: buybackEntries }
    });
    const mixed = await post(ticket({
      transactionId: 'multi-buyback-mixed',
      sales: [{ lootId: 'TRE_CHALICE', amount: 1, sourceSlot: 0 }],
      purchases: [{ lootId: 'CON_HEALTH_POTION', amount: 1 }],
      buybacks: [
        { entryId: 'bb-1', amount: 1 },
        { entryId: 'bb-2', amount: 1 }
      ]
    }));
    assert.equal(mixed.status, 200, JSON.stringify(mixed.body));
    assert.equal(mixed.body.revision, 1);
    assert.equal(mixed.body.loadout.length, 3);
    assert.deepEqual(mixed.body.buyback.entries.map(e => e.entryId), ['bb-3']);

    // 3. Atomic rejection: 2nd buyback fails -> zero partial writes
    await reset({
      'inventory.inventorySchemaVersion': 3,
      'inventory.preparedEquipment': equipment,
      'inventory.currency': 40, // enough for bb-1 (30), NOT enough for bb-1 + bb-2 (55)
      'inventory.loadout': [],
      'inventory.buyback': { sessionEpoch: 'town', entries: buybackEntries }
    });
    await unchanged(ticket({
      transactionId: 'multi-buyback-funds-fail',
      purchases: [], sales: [],
      buybacks: [{ entryId: 'bb-1', amount: 1 }, { entryId: 'bb-2', amount: 1 }]
    }), 409, 'INSUFFICIENT_FUNDS');

    await reset({
      'inventory.inventorySchemaVersion': 3,
      'inventory.preparedEquipment': equipment,
      'inventory.currency': 200,
      'inventory.loadout': [],
      'inventory.buyback': { sessionEpoch: 'town', entries: buybackEntries }
    });
    await unchanged(ticket({
      transactionId: 'multi-buyback-missing-fail',
      purchases: [], sales: [],
      buybacks: [{ entryId: 'bb-1', amount: 1 }, { entryId: 'bb-missing', amount: 1 }]
    }), 404, 'BUYBACK_ENTRY_NOT_FOUND');

    // 4. Duplicate buyback entryId differing only by whitespace must be rejected
    await unchanged(ticket({
      transactionId: 'multi-buyback-dup-ws',
      purchases: [], sales: [],
      buybacks: [{ entryId: 'bb-1', amount: 1 }, { entryId: ' bb-1 ', amount: 1 }]
    }), 400, 'VALIDATION_FAILED');
  });
});
