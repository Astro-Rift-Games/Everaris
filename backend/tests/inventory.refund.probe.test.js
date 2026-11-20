'use strict';
// FIX-95 verifier probes, kept as regression tests: rejections never write (byte-identical document),
// the raid-started marker authority, and the revision bump of marking. Same setup as inventory.refund.test.js.
const { test, describe, before, after, beforeEach } = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('crypto');
const mongoose = require('mongoose');
const request = require('supertest');
const jwt = require('jsonwebtoken');
const { startMemoryMongo } = require('./support/memoryMongo');
const app = require('../src/app');
const env = require('../src/config/env');
const { EQUIPMENT_SLOTS } = require('../src/config/equipmentSlots');
const Character = require('../src/models/Character');
const AuthoritativeExtractionResult = require('../src/models/AuthoritativeExtractionResult');

const BASE = '/character/me/inventory';
const characters = () => mongoose.connection.collection('characters');
const rawOf = accountId => characters().findOne({ accountId });
const hashOf = async accountId =>
  crypto.createHash('sha256').update(JSON.stringify(await rawOf(accountId))).digest('hex');
const api = (method, path, accountId) => request(app)[method](`${BASE}${path}`)
  .set('Authorization', `Bearer ${jwt.sign({ sub: accountId.toString() }, env.jwtSecret)}`);

async function seed() {
  const accountId = new mongoose.Types.ObjectId();
  await characters().insertOne({
    accountId, name: `Probe${accountId}`, revision: 1, unlockedAbilities: [],
    inventory: {
      inventorySchemaVersion: 2, currency: 0,
      stash: [{ slot: 0, lootId: 'bone', amount: 2 }],
      loadout: [{ slot: 3, lootId: 'stick', amount: 2 }],
      preparedEquipment: { ...Object.fromEntries(EQUIPMENT_SLOTS.map(slot => [slot, ''])), weaponSlot1: 'arming_sword' },
      pendingReservation: null, appliedExtractionReceipts: []
    }
  });
  return accountId;
}

async function launch(accountId, reservationId, raidId, admit = true) {
  assert.equal((await api('post', '/reservation', accountId).send({ reservationId })).status, 201);
  if (raidId) {
    await characters().updateOne({ accountId }, { $set: { 'inventory.pendingReservation.raidId': raidId } });
  }
  if (admit) {
    assert.equal((await api('post', '/reservation/admit', accountId).send({ reservationId })).status, 200);
  }
}

describe('FIX-95 verifier probes', () => {
  let mongoServer;
  const originalWindow = env.reservationRefundWindowMs;

  before(async () => {
    mongoServer = await startMemoryMongo();
    await mongoose.connect(mongoServer.getUri());
  });

  after(async () => {
    env.reservationRefundWindowMs = originalWindow;
    await mongoose.disconnect();
    await mongoServer.stop();
  });

  beforeEach(async () => {
    env.reservationRefundWindowMs = originalWindow;
    await Character.deleteMany({});
    await AuthoritativeExtractionResult.deleteMany({});
  });

  test('P1: refund restores the exact pre-reservation state once; a retry is a byte-identical no-op', async () => {
    const accountId = await seed();
    const before = await rawOf(accountId);
    await launch(accountId, 'r1', 'raid-P1');

    const refund = await api('post', '/reservation/refund', accountId).send({ reservationId: 'r1' });
    assert.equal(refund.status, 200, JSON.stringify(refund.body));
    assert.equal(refund.body.refunded, true);
    const after = await rawOf(accountId);
    assert.deepEqual(after.inventory.loadout, before.inventory.loadout);
    assert.deepEqual(after.inventory.stash, before.inventory.stash);
    assert.deepEqual(after.inventory.preparedEquipment, before.inventory.preparedEquipment);

    const hash = await hashOf(accountId);
    const retry = await api('post', '/reservation/refund', accountId).send({ reservationId: 'r1' });
    assert.equal(retry.status, 200);
    assert.equal(retry.body.refunded, false);
    assert.equal(await hashOf(accountId), hash);
  });

  test('P2: every rejection leaves the document byte-identical', async () => {
    const cases = [
      ['MISMATCH', async () => {}, { reservationId: 'other' }, 409, 'RESERVATION_MISMATCH'],
      ['STARTED', accountId => characters().updateOne({ accountId },
        { $set: { 'inventory.pendingReservation.startedAt': new Date() } }), { reservationId: 'r1' }, 409, 'RAID_ALREADY_STARTED'],
      ['EXPIRED', accountId => characters().updateOne({ accountId },
        { $set: { 'inventory.pendingReservation.admittedAt': new Date(Date.now() - env.reservationRefundWindowMs - 1000) } }),
        { reservationId: 'r1' }, 409, 'REFUND_WINDOW_EXPIRED'],
      ['CONSUMED', accountId => characters().updateOne({ accountId },
        { $push: { 'inventory.appliedExtractionReceipts': { raidId: 'raid-P2', resultSequence: 1 } } }),
        { reservationId: 'r1' }, 409, 'RESULT_ALREADY_CONSUMED'],
      ['PENDING', accountId => AuthoritativeExtractionResult.create({
        raidId: 'raid-P2', accountId: accountId.toString(), reservationId: 'r1',
        items: [{ lootId: 'bone', amount: 1 }], experienceGranted: 0 }),
        { reservationId: 'r1' }, 409, 'EXTRACTION_RESULT_PENDING'],
      ['EMPTY_ID', async () => {}, { reservationId: '' }, 400, 'VALIDATION_FAILED']
    ];
    for (const [name, prepare, body, status, errorCode] of cases) {
      const accountId = await seed();
      // Fresh database per case: a started marker left by STARTED would be inherited by later admissions (A1).
      await launch(accountId, 'r1', 'raid-P2');
      await prepare(accountId);
      const hash = await hashOf(accountId);
      const response = await api('post', '/reservation/refund', accountId).send(body);
      assert.equal(response.status, status, `${name}: ${JSON.stringify(response.body)}`);
      assert.equal(response.body.error, errorCode, name);
      assert.equal(await hashOf(accountId), hash, `${name}: a rejection must not write`);
      await Character.deleteMany({});
    }
  });

  test('P3: raid-started rejects a caller outside the raid and writes nothing on anyone', async () => {
    const host = await seed(); await launch(host, 'h1', 'raid-P3');
    const mate = await seed(); await launch(mate, 'm1', 'raid-P3');
    const outsider = await seed(); await launch(outsider, 'o1', 'raid-OTHER');
    const unadmitted = await seed(); await launch(unadmitted, 'p1', 'raid-P3', false);
    const everyone = [host, mate, outsider, unadmitted];
    const before = await Promise.all(everyone.map(hashOf));

    for (const caller of [outsider, unadmitted]) {
      const response = await api('post', '/reservation/raid-started', caller).send({ raidId: 'raid-P3' });
      assert.equal(response.status, 403, JSON.stringify(response.body));
      assert.equal(response.body.error, 'HOST_NOT_IN_RAID');
    }
    assert.deepEqual(await Promise.all(everyone.map(hashOf)), before);

    const started = await api('post', '/reservation/raid-started', host).send({ raidId: ' raid-P3 ' });
    assert.equal(started.status, 200, JSON.stringify(started.body));
    assert.deepEqual(started.body, { raidId: 'raid-P3', marked: 2 });
    assert.equal((await rawOf(unadmitted)).inventory.pendingReservation.startedAt, null);
    assert.equal((await rawOf(outsider)).inventory.pendingReservation.startedAt, null);

    const again = await api('post', '/reservation/raid-started', mate).send({ raidId: 'raid-P3' });
    assert.deepEqual(again.body, { raidId: 'raid-P3', marked: 0 });
    const refund = await api('post', '/reservation/refund', mate).send({ reservationId: 'm1' });
    assert.equal(refund.status, 409);
    assert.equal(refund.body.error, 'RAID_ALREADY_STARTED');
  });

  test('P4/A1: a teammate admitted after the start marker cannot refund', async () => {
    const host = await seed(); await launch(host, 'h1', 'raid-P4');
    const late = await seed(); await launch(late, 'l1', 'raid-P4', false);
    assert.equal((await api('post', '/reservation/raid-started', host).send({ raidId: 'raid-P4' })).status, 200);
    assert.equal((await api('post', '/reservation/admit', late).send({ reservationId: 'l1' })).status, 200);

    const hash = await hashOf(late);
    const refund = await api('post', '/reservation/refund', late).send({ reservationId: 'l1' });
    assert.equal(refund.status, 409, JSON.stringify(refund.body));
    assert.equal(refund.body.error, 'RAID_ALREADY_STARTED');
    assert.equal(await hashOf(late), hash);
  });

  test('P6: raid-started increments the revision of the caller and of every marked participant', async () => {
    const host = await seed(); await launch(host, 'h1', 'raid-P6');
    const mate = await seed(); await launch(mate, 'm1', 'raid-P6');
    const before = [(await rawOf(host)).revision, (await rawOf(mate)).revision];
    await api('post', '/reservation/raid-started', host).send({ raidId: 'raid-P6' });
    assert.deepEqual([(await rawOf(host)).revision, (await rawOf(mate)).revision], before.map(r => r + 1));
  });
});
