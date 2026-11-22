'use strict';

// TASK-304: attribute respec with configurable gold cost (Doc 22 §6, Doc 04 §11).
const test = require('node:test');
const assert = require('node:assert/strict');
const request = require('supertest');
const app = require('../../src/app');
const testDb = require('./testDb');
const Character = require('../../src/models/Character');
const { RESPEC_GOLD_COST } = require('../../src/config/respecConfig');

const ALLOCATED = Object.freeze({
  vitality: 8, resistance: 5, strength: 7, dexterity: 5, intelligence: 5, luck: 5, availablePoints: 5
});
const OWNED_POINTS = 40;
const ZEROED = Object.freeze({
  vitality: 0, resistance: 0, strength: 0, dexterity: 0, intelligence: 0, luck: 0, availablePoints: OWNED_POINTS
});

function attributesOf(doc) {
  const a = doc.characterAttributes;
  return {
    vitality: a.vitality, resistance: a.resistance, strength: a.strength,
    dexterity: a.dexterity, intelligence: a.intelligence, luck: a.luck, availablePoints: a.availablePoints
  };
}

test('POST /character/me/progression/respec (TASK-304)', async t => {
  await testDb.connect();
  t.after(async () => testDb.closeDatabase());

  const reg = await request(app).post('/auth/register').send({ username: 'respecuser', password: 'password123' });
  const token = reg.body.token;
  assert.equal(
    (await request(app).post('/character/me').set('Authorization', `Bearer ${token}`).send({ name: 'Respecer' })).status,
    201
  );
  const accountId = (await Character.findOne({ name: 'Respecer' })).accountId;

  const respec = body => request(app)
    .post('/character/me/progression/respec')
    .set('Authorization', `Bearer ${token}`)
    .send(body);

  const seed = async ({ attributes = ALLOCATED, currency = 250, revision = 1, pendingReservation = null } = {}) => {
    await Character.updateOne({ accountId }, {
      $set: {
        level: 3,
        experience: 12,
        revision,
        characterAttributes: { ...attributes },
        'inventory.currency': currency,
        'inventory.pendingReservation': pendingReservation
      }
    });
  };

  const assertUnchanged = async ({ attributes, currency, revision }) => {
    const doc = await Character.findOne({ accountId });
    assert.deepEqual(attributesOf(doc), { ...attributes });
    assert.equal(doc.inventory.currency, currency);
    assert.equal(doc.revision, revision);
  };

  await t.test('respec cost defaults to 100 gold', () => {
    assert.equal(RESPEC_GOLD_COST, 100);
  });

  await t.test('GET progression exposes the configured respec cost', async () => {
    const res = await request(app).get('/character/me/progression').set('Authorization', `Bearer ${token}`);
    assert.equal(res.status, 200, JSON.stringify(res.body));
    assert.equal(res.body.respecCost, RESPEC_GOLD_COST);
  });

  await t.test('respec debits exactly the configured cost and returns every owned point to the pool', async () => {
    await seed();
    const res = await respec({ transactionId: 'tx-respec-001', expectedRevision: 1 });

    assert.equal(res.status, 200, JSON.stringify(res.body));
    assert.equal(res.body.alreadySecured, false);
    assert.equal(res.body.transactionId, 'tx-respec-001');
    assert.equal(res.body.cost, RESPEC_GOLD_COST);
    assert.equal(res.body.currency, 250 - RESPEC_GOLD_COST);
    assert.deepEqual(res.body.characterAttributes, { ...ZEROED });
    assert.equal(res.body.level, 3);
    assert.equal(res.body.experience, 12);
    assert.equal(res.body.revision, 2);

    const doc = await Character.findOne({ accountId });
    assert.deepEqual(attributesOf(doc), { ...ZEROED });
    assert.equal(doc.inventory.currency, 250 - RESPEC_GOLD_COST);
    assert.equal(doc.level, 3);
    assert.equal(doc.experience, 12);
    assert.equal(doc.revision, 2);
    const receipt = doc.appliedShopReceipts.find(r => r.transactionId === 'tx-respec-001');
    assert.equal(receipt.type, 'respec');
    assert.equal(receipt.totalAmount, RESPEC_GOLD_COST);
  });

  await t.test('replaying the same transactionId does not charge twice nor touch attributes', async () => {
    const res = await respec({ transactionId: 'tx-respec-001', expectedRevision: 1 });

    assert.equal(res.status, 200, JSON.stringify(res.body));
    assert.equal(res.body.alreadySecured, true);
    assert.equal(res.body.cost, RESPEC_GOLD_COST);
    assert.equal(res.body.currency, 250 - RESPEC_GOLD_COST);
    assert.deepEqual(res.body.characterAttributes, { ...ZEROED });
    assert.equal(res.body.revision, 2);
    await assertUnchanged({ attributes: ZEROED, currency: 250 - RESPEC_GOLD_COST, revision: 2 });
  });

  await t.test('rejects with NO_ASSIGNED_POINTS when every attribute is already 0', async () => {
    const res = await respec({ transactionId: 'tx-respec-002', expectedRevision: 2 });

    assert.equal(res.status, 422, JSON.stringify(res.body));
    assert.equal(res.body.error, 'NO_ASSIGNED_POINTS');
    await assertUnchanged({ attributes: ZEROED, currency: 250 - RESPEC_GOLD_COST, revision: 2 });
  });

  await t.test('rejects with INSUFFICIENT_FUNDS when gold is below the cost', async () => {
    await seed({ currency: RESPEC_GOLD_COST - 1, revision: 5 });
    const res = await respec({ transactionId: 'tx-respec-003', expectedRevision: 5 });

    assert.equal(res.status, 422, JSON.stringify(res.body));
    assert.equal(res.body.error, 'INSUFFICIENT_FUNDS');
    await assertUnchanged({ attributes: ALLOCATED, currency: RESPEC_GOLD_COST - 1, revision: 5 });
  });

  await t.test('exact cost balance is enough and leaves 0 gold', async () => {
    await seed({ currency: RESPEC_GOLD_COST, revision: 6 });
    const res = await respec({ transactionId: 'tx-respec-004', expectedRevision: 6 });

    assert.equal(res.status, 200, JSON.stringify(res.body));
    assert.equal(res.body.currency, 0);
  });

  await t.test('rejects with RESERVATION_PENDING while a raid reservation is active', async () => {
    const pendingReservation = { reservationId: 'res-1' };
    await seed({ revision: 7, pendingReservation });
    const res = await respec({ transactionId: 'tx-respec-005', expectedRevision: 7 });

    assert.equal(res.status, 409, JSON.stringify(res.body));
    assert.equal(res.body.error, 'RESERVATION_PENDING');
    await assertUnchanged({ attributes: ALLOCATED, currency: 250, revision: 7 });
  });

  await t.test('rejects with REVISION_CONFLICT on a stale revision', async () => {
    await seed({ revision: 8 });
    const res = await respec({ transactionId: 'tx-respec-006', expectedRevision: 7 });

    assert.equal(res.status, 409, JSON.stringify(res.body));
    assert.equal(res.body.error, 'REVISION_CONFLICT');
    await assertUnchanged({ attributes: ALLOCATED, currency: 250, revision: 8 });
  });

  await t.test('rejects a transactionId already used by another operation', async () => {
    await seed({ revision: 9 });
    await Character.updateOne({ accountId }, {
      $push: { appliedShopReceipts: { transactionId: 'tx-other-op', type: 'buy', lootId: 'CON_HEALTH_POTION', amount: 1, totalAmount: 80 } }
    });
    const res = await respec({ transactionId: 'tx-other-op', expectedRevision: 9 });

    assert.equal(res.status, 409, JSON.stringify(res.body));
    assert.equal(res.body.error, 'TRANSACTION_ID_REUSED');
    await assertUnchanged({ attributes: ALLOCATED, currency: 250, revision: 9 });
  });

  await t.test('rejects missing transactionId or expectedRevision with 400 and no mutation', async () => {
    await seed({ revision: 10 });
    const noTx = await respec({ expectedRevision: 10 });
    const noRev = await respec({ transactionId: 'tx-respec-007' });

    assert.equal(noTx.status, 400, JSON.stringify(noTx.body));
    assert.equal(noRev.status, 400, JSON.stringify(noRev.body));
    await assertUnchanged({ attributes: ALLOCATED, currency: 250, revision: 10 });
  });

  await t.test('concurrent duplicates of one transactionId charge once', async () => {
    await seed({ revision: 11 });
    const results = await Promise.all([
      respec({ transactionId: 'tx-respec-dup', expectedRevision: 11 }),
      respec({ transactionId: 'tx-respec-dup', expectedRevision: 11 })
    ]);

    for (const res of results) assert.equal(res.status, 200, JSON.stringify(res.body));
    assert.deepEqual(results.map(r => r.body.alreadySecured).sort(), [false, true]);
    await assertUnchanged({ attributes: ZEROED, currency: 250 - RESPEC_GOLD_COST, revision: 12 });
  });

  await t.test('concurrent respecs with different transactionIds on one revision apply once', async () => {
    await seed({ revision: 13 });
    const results = await Promise.all([
      respec({ transactionId: 'tx-respec-a', expectedRevision: 13 }),
      respec({ transactionId: 'tx-respec-b', expectedRevision: 13 })
    ]);

    assert.deepEqual(results.map(r => r.status).sort(), [200, 409]);
    await assertUnchanged({ attributes: ZEROED, currency: 250 - RESPEC_GOLD_COST, revision: 14 });
  });
});

// Doc 04 §11: "Al confirmar un Respec, el sistema debe reevaluar inmediatamente los requisitos del
// equipamiento. Todo objeto que deje de cumplirlos se desequipa." (Decision L3: same transaction.)
test('respec unequips weapons and abilities that stop meeting requirements (TASK-304 T2)', async t => {
  await testDb.connect();
  t.after(async () => testDb.closeDatabase());
  const { createEquipment } = require('../../src/services/ItemInstances');
  const { EQUIPMENT_SLOTS } = require('../../src/config/equipmentSlots');

  const reg = await request(app).post('/auth/register').send({ username: 'respecgear', password: 'password123' });
  const token = reg.body.token;
  assert.equal(
    (await request(app).post('/character/me').set('Authorization', `Bearer ${token}`).send({ name: 'RespecGear' })).status,
    201
  );
  const accountId = (await Character.findOne({ name: 'RespecGear' })).accountId;
  const respec = body => request(app)
    .post('/character/me/progression/respec')
    .set('Authorization', `Bearer ${token}`)
    .send(body);

  const longSword = createEquipment('long_sword');
  const trainingSword = createEquipment('training_short_sword');
  const helmet = createEquipment('heavy_armor_helmet');
  const equipment = () => {
    const slots = Object.fromEntries(EQUIPMENT_SLOTS.map(slot => [slot, null]));
    return { ...slots, weaponSlot1: structuredClone(longSword), weaponSlot2: structuredClone(trainingSword), helmet: structuredClone(helmet) };
  };
  const seed = async ({ loadout = [], revision = 1 } = {}) => {
    await Character.updateOne({ accountId }, {
      $set: {
        revision,
        characterAttributes: { vitality: 5, resistance: 5, strength: 12, dexterity: 10, intelligence: 5, luck: 5, availablePoints: 0 },
        unlockedAbilities: ['charge', 'trap'],
        preparedAbilities: { slot1: 'charge', slot2: 'trap' },
        'inventory.inventorySchemaVersion': 3,
        'inventory.currency': 300,
        'inventory.pendingReservation': null,
        'inventory.stash': [],
        'inventory.loadout': loadout,
        'inventory.preparedEquipment': equipment()
      }
    });
  };

  await t.test('moves an unmet weapon to the loadout with its instance and clears unmet ability slots', async () => {
    await seed();
    const res = await respec({ transactionId: 'tx-gear-001', expectedRevision: 1 });

    assert.equal(res.status, 200, JSON.stringify(res.body));
    assert.equal(res.body.preparedEquipment.weaponSlot1, null);
    assert.deepEqual(res.body.preparedEquipment.weaponSlot2, trainingSword, 'training weapons have no minimums');
    assert.deepEqual(res.body.preparedEquipment.helmet, helmet, 'armors have no requirements');
    assert.deepEqual(res.body.preparedAbilities, { slot1: '', slot2: '' });
    assert.equal(res.body.loadout.length, 1);
    assert.equal(res.body.loadout[0].lootId, 'long_sword');
    assert.deepEqual(res.body.loadout[0].instance, longSword.instance);
    assert.deepEqual(res.body.stash, []);
    assert.equal(res.body.currency, 200);

    const doc = await Character.findOne({ accountId }).lean();
    assert.equal(doc.inventory.preparedEquipment.weaponSlot1, null);
    assert.deepEqual(doc.inventory.preparedEquipment.weaponSlot2, trainingSword);
    assert.equal(doc.inventory.loadout.length, 1);
    assert.deepEqual(doc.inventory.loadout[0].instance, longSword.instance);
    assert.deepEqual({ slot1: doc.preparedAbilities.slot1, slot2: doc.preparedAbilities.slot2 }, { slot1: '', slot2: '' });
    assert.deepEqual(doc.unlockedAbilities, ['charge', 'trap'], 'respec never removes unlocked abilities');
    assert.equal(doc.characterAttributes.availablePoints, 42);
    assert.equal(doc.revision, 2);
  });

  await t.test('replay returns the recorded equipment state without moving anything again', async () => {
    const res = await respec({ transactionId: 'tx-gear-001', expectedRevision: 1 });

    assert.equal(res.status, 200, JSON.stringify(res.body));
    assert.equal(res.body.alreadySecured, true);
    assert.equal(res.body.loadout.length, 1);
    assert.equal(res.body.preparedEquipment.weaponSlot1, null);
    const doc = await Character.findOne({ accountId }).lean();
    assert.equal(doc.inventory.loadout.length, 1);
    assert.equal(doc.inventory.currency, 200);
    assert.equal(doc.revision, 2);
  });

  await t.test('rejects with INVENTORY_FULL and no mutation when the loadout cannot take the unequipped weapon', async () => {
    const full = Array.from({ length: 30 }, (_, slot) => ({ slot, lootId: 'RES_SLIME_GEL', amount: 1 }));
    await seed({ loadout: full, revision: 5 });
    const res = await respec({ transactionId: 'tx-gear-002', expectedRevision: 5 });

    assert.equal(res.status, 409, JSON.stringify(res.body));
    assert.equal(res.body.error, 'INVENTORY_FULL');
    const doc = await Character.findOne({ accountId }).lean();
    assert.deepEqual(doc.inventory.preparedEquipment.weaponSlot1, longSword);
    assert.equal(doc.inventory.loadout.length, 30);
    assert.equal(doc.inventory.currency, 300);
    assert.equal(doc.characterAttributes.strength, 12);
    assert.equal(doc.preparedAbilities.slot1, 'charge');
    assert.equal(doc.revision, 5);
  });
});
