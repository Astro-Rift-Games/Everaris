'use strict';
// FIX-95: pre-start refund of an admitted reservation and the server-side raid start marker.
// Public HTTP contract against a real in-memory database; fixtures are inserted as raw documents.
const { test, describe, before, after, beforeEach } = require('node:test');
const assert = require('node:assert/strict');
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
const instance = n => ({ instanceId: n.toString(16).padStart(24, '0'), modifiers: { gemSocketCount: 0, gems: [] } });
const characters = () => mongoose.connection.collection('characters');
const rawOf = accountId => characters().findOne({ accountId });
const tokenOf = accountId => jwt.sign({ sub: accountId.toString() }, env.jwtSecret);
const api = (method, path, accountId) => request(app)[method](`${BASE}${path}`)
  .set('Authorization', `Bearer ${tokenOf(accountId)}`);

async function seed(version, overrides = {}) {
  const accountId = new mongoose.Types.ObjectId();
  const weapon = n => (version === 3 ? { lootId: 'arming_sword', instance: instance(n) } : 'arming_sword');
  const at = (slot, lootId, amount) => (version === 1 ? { lootId, amount } : { slot, lootId, amount });
  await characters().insertOne({
    accountId, name: `Refund${accountId}`, revision: 7, unlockedAbilities: [],
    inventory: {
      inventorySchemaVersion: version, currency: 0,
      stash: [at(0, 'bone', 2)],
      loadout: [at(2, 'stick', 1), at(5, 'bone', 1)],
      // Every slot is stored explicitly (empty = '' or null) so equality with the restored form is exact.
      preparedEquipment: { ...Object.fromEntries(EQUIPMENT_SLOTS.map(slot => [slot, version === 3 ? null : ''])),
        weaponSlot1: weapon(1), weaponSlot2: weapon(2) },
      pendingReservation: null,
      appliedExtractionReceipts: [],
      ...overrides
    }
  });
  return accountId;
}

const snapshotOf = async accountId => {
  const { revision, inventory } = await rawOf(accountId);
  return { revision, stash: inventory.stash, loadout: inventory.loadout, preparedEquipment: inventory.preparedEquipment };
};

// Launch + admit through the public API so the reservation is the one production would hold.
async function reserveAndAdmit(accountId, reservationId = 'res-1', raidId = 'raid-ABC') {
  assert.equal((await api('post', '/reservation', accountId).send({ reservationId })).status, 201);
  if (raidId) {
    await characters().updateOne({ accountId }, { $set: { 'inventory.pendingReservation.raidId': raidId } });
  }
  assert.equal((await api('post', '/reservation/admit', accountId).send({ reservationId })).status, 200);
}

const setReservationField = (accountId, field, value) =>
  characters().updateOne({ accountId }, { $set: { [`inventory.pendingReservation.${field}`]: value } });

describe('FIX-95 pre-start refund', () => {
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

  async function assertRejectedUnchanged(accountId, send, status, errorCode) {
    const before = await rawOf(accountId);
    const response = await send();
    assert.equal(response.status, status, JSON.stringify(response.body));
    assert.equal(response.body.error, errorCode);
    assert.deepEqual(await rawOf(accountId), before, 'a rejected refund must not write');
    return response;
  }

  test('S6: the refund window is an env placeholder defaulting to 120000 ms', () => {
    assert.equal(typeof env.reservationRefundWindowMs, 'number');
    assert.equal(originalWindow, 120000);
  });

  describe('POST /reservation/refund', () => {
    for (const version of [1, 2, 3]) {
      test(`S1 v${version}: refund within the window restores Stash/Loadout/Equipment exactly once`, async () => {
        const accountId = await seed(version);
        const original = await snapshotOf(accountId);
        await reserveAndAdmit(accountId);
        const admitted = await rawOf(accountId);
        assert.deepEqual(admitted.inventory.loadout, []);

        const first = await api('post', '/reservation/refund', accountId).send({ reservationId: 'res-1' });
        assert.equal(first.status, 200, JSON.stringify(first.body));
        assert.deepEqual(first.body, { pendingReservation: null, revision: admitted.revision + 1 });

        const refunded = await snapshotOf(accountId);
        assert.deepEqual(refunded.stash, original.stash);
        assert.deepEqual(refunded.loadout, original.loadout);
        assert.deepEqual(refunded.preparedEquipment, original.preparedEquipment);
        assert.equal((await rawOf(accountId)).inventory.pendingReservation, null);

        // S3: retry after a lost response is a read-only no-op.
        const second = await api('post', '/reservation/refund', accountId).send({ reservationId: 'res-1' });
        assert.equal(second.status, 200);
        assert.deepEqual(second.body, first.body);
        assert.deepEqual(await snapshotOf(accountId), refunded, 'no duplication and no revision change on retry');
      });
    }

    test('S1: a non-admitted reservation is restored like a rollback, with no window check', async () => {
      const accountId = await seed(2);
      const original = await snapshotOf(accountId);
      assert.equal((await api('post', '/reservation', accountId).send({ reservationId: 'res-1' })).status, 201);
      env.reservationRefundWindowMs = 0;

      const response = await api('post', '/reservation/refund', accountId).send({ reservationId: 'res-1' });
      assert.equal(response.status, 200, JSON.stringify(response.body));
      assert.equal(response.body.pendingReservation, null);
      const refunded = await snapshotOf(accountId);
      assert.deepEqual(refunded.loadout, original.loadout);
      assert.deepEqual(refunded.preparedEquipment, original.preparedEquipment);
    });

    test('S1: refund is allowed just before the admitted window ends', async () => {
      const accountId = await seed(2);
      await reserveAndAdmit(accountId);
      await setReservationField(accountId, 'admittedAt', new Date(Date.now() - env.reservationRefundWindowMs + 5000));
      const response = await api('post', '/reservation/refund', accountId).send({ reservationId: 'res-1' });
      assert.equal(response.status, 200, JSON.stringify(response.body));
    });

    test('S3: refund without an active reservation is a 200 no-op with the current revision', async () => {
      const accountId = await seed(2);
      const before = await rawOf(accountId);
      const response = await api('post', '/reservation/refund', accountId).send({ reservationId: 'res-1' });
      assert.equal(response.status, 200);
      assert.deepEqual(response.body, { pendingReservation: null, revision: before.revision });
      assert.deepEqual(await rawOf(accountId), before);
    });

    test('a different active reservation is rejected with 409 RESERVATION_MISMATCH', async () => {
      const accountId = await seed(2);
      await reserveAndAdmit(accountId, 'res-1');
      await assertRejectedUnchanged(accountId,
        () => api('post', '/reservation/refund', accountId).send({ reservationId: 'other' }), 409, 'RESERVATION_MISMATCH');
    });

    test('S5: a started raid rejects with 409 RAID_ALREADY_STARTED', async () => {
      const accountId = await seed(2);
      await reserveAndAdmit(accountId);
      await setReservationField(accountId, 'startedAt', new Date());
      await assertRejectedUnchanged(accountId,
        () => api('post', '/reservation/refund', accountId).send({ reservationId: 'res-1' }), 409, 'RAID_ALREADY_STARTED');
    });

    test('S2: an admission older than the window rejects with 409 REFUND_WINDOW_EXPIRED', async () => {
      const accountId = await seed(2);
      await reserveAndAdmit(accountId);
      await setReservationField(accountId, 'admittedAt', new Date(Date.now() - env.reservationRefundWindowMs - 1000));
      await assertRejectedUnchanged(accountId,
        () => api('post', '/reservation/refund', accountId).send({ reservationId: 'res-1' }), 409, 'REFUND_WINDOW_EXPIRED');
    });

    test('S2/S6: the window follows env.reservationRefundWindowMs', async () => {
      const accountId = await seed(2);
      await reserveAndAdmit(accountId);
      await setReservationField(accountId, 'admittedAt', new Date(Date.now() - 30000));
      env.reservationRefundWindowMs = 10000;
      await assertRejectedUnchanged(accountId,
        () => api('post', '/reservation/refund', accountId).send({ reservationId: 'res-1' }), 409, 'REFUND_WINDOW_EXPIRED');
      env.reservationRefundWindowMs = 60000;
      assert.equal((await api('post', '/reservation/refund', accountId).send({ reservationId: 'res-1' })).status, 200);
    });

    test('S2: a pending authoritative result rejects with 409 EXTRACTION_RESULT_PENDING', async () => {
      const accountId = await seed(2);
      await reserveAndAdmit(accountId);
      await AuthoritativeExtractionResult.create({
        raidId: 'raid-ABC', accountId: accountId.toString(), reservationId: 'res-1',
        items: [{ lootId: 'bone', amount: 1 }], experienceGranted: 0
      });
      await assertRejectedUnchanged(accountId,
        () => api('post', '/reservation/refund', accountId).send({ reservationId: 'res-1' }), 409, 'EXTRACTION_RESULT_PENDING');
    });

    test('S2: an applied extraction receipt for the raid rejects with 409 RESULT_ALREADY_CONSUMED', async () => {
      const accountId = await seed(2);
      await reserveAndAdmit(accountId);
      await characters().updateOne({ accountId }, { $push: { 'inventory.appliedExtractionReceipts':
        { raidId: 'raid-ABC', resultSequence: 1, reservationId: 'res-1', timestamp: new Date() } } });
      await assertRejectedUnchanged(accountId,
        () => api('post', '/reservation/refund', accountId).send({ reservationId: 'res-1' }), 409, 'RESULT_ALREADY_CONSUMED');
    });

    test('S2: a receipt for another raid does not block the refund', async () => {
      const accountId = await seed(2);
      await reserveAndAdmit(accountId);
      await characters().updateOne({ accountId }, { $push: { 'inventory.appliedExtractionReceipts':
        { raidId: 'raid-OLD', resultSequence: 1, reservationId: 'res-old', timestamp: new Date() } } });
      assert.equal((await api('post', '/reservation/refund', accountId).send({ reservationId: 'res-1' })).status, 200);
    });

    test('a reservation without raidId is not matched against receipts', async () => {
      const accountId = await seed(2);
      await reserveAndAdmit(accountId, 'res-1', null);
      await characters().updateOne({ accountId }, { $push: { 'inventory.appliedExtractionReceipts':
        { raidId: 'raid-OLD', resultSequence: 1, timestamp: new Date() } } });
      assert.equal((await api('post', '/reservation/refund', accountId).send({ reservationId: 'res-1' })).status, 200);
    });

    test('rejects a missing or non-string reservationId with 400 VALIDATION_FAILED', async () => {
      const accountId = await seed(2);
      await reserveAndAdmit(accountId);
      for (const body of [{}, { reservationId: 7 }, { reservationId: '  ' }]) {
        await assertRejectedUnchanged(accountId,
          () => api('post', '/reservation/refund', accountId).send(body), 400, 'VALIDATION_FAILED');
      }
    });

    test('requires authentication', async () => {
      const response = await request(app).post(`${BASE}/reservation/refund`).send({ reservationId: 'res-1' });
      assert.equal(response.status, 401);
    });
  });

  describe('POST /reservation/raid-started', () => {
    async function bound(raidId, { admitted = true, version = 2 } = {}) {
      const accountId = await seed(version);
      await reserveAndAdmit(accountId, `res-${accountId}`, raidId);
      if (!admitted) await setReservationField(accountId, 'admittedAt', null);
      return accountId;
    }

    test('S5: marks every admitted reservation of the raid and bumps their revision', async () => {
      const host = await bound('raid-A');
      const guest = await bound('raid-A');
      const before = { host: await rawOf(host), guest: await rawOf(guest) };

      const response = await api('post', '/reservation/raid-started', host).send({ raidId: 'raid-A' });
      assert.equal(response.status, 200, JSON.stringify(response.body));
      assert.deepEqual(response.body, { raidId: 'raid-A', marked: 2 });
      for (const [name, id] of [['host', host], ['guest', guest]]) {
        const after = await rawOf(id);
        assert.ok(after.inventory.pendingReservation.startedAt instanceof Date);
        assert.equal(after.revision, before[name].revision + 1);
        assert.deepEqual(after.inventory.loadout, before[name].inventory.loadout, 'marking does not touch items');
      }
    });

    test('S5: does not mark other raids or non-admitted reservations', async () => {
      const host = await bound('raid-A');
      const other = await bound('raid-B');
      const unadmitted = await bound('raid-A', { admitted: false });
      const beforeOther = await rawOf(other);
      const beforeUnadmitted = await rawOf(unadmitted);

      const response = await api('post', '/reservation/raid-started', host).send({ raidId: 'raid-A' });
      assert.deepEqual(response.body, { raidId: 'raid-A', marked: 1 });
      assert.deepEqual(await rawOf(other), beforeOther);
      assert.deepEqual(await rawOf(unadmitted), beforeUnadmitted);
    });

    test('S3/S5: is idempotent; a retry marks nothing and changes nothing', async () => {
      const host = await bound('raid-A');
      await api('post', '/reservation/raid-started', host).send({ raidId: 'raid-A' });
      const marked = await rawOf(host);
      const retry = await api('post', '/reservation/raid-started', host).send({ raidId: 'raid-A' });
      assert.equal(retry.status, 200);
      assert.deepEqual(retry.body, { raidId: 'raid-A', marked: 0 });
      assert.deepEqual(await rawOf(host), marked);
    });

    test('S5: rejects a caller whose reservation is not admitted and bound to the raid with 403 HOST_NOT_IN_RAID', async () => {
      const member = await bound('raid-A');
      const wrongRaid = await bound('raid-B');
      const notAdmitted = await bound('raid-A', { admitted: false });
      const noReservation = await seed(2);
      const unbound = await seed(2);
      await reserveAndAdmit(unbound, 'res-unbound', null);
      const memberBefore = await rawOf(member);

      for (const caller of [wrongRaid, notAdmitted, noReservation, unbound]) {
        const before = await rawOf(caller);
        const response = await api('post', '/reservation/raid-started', caller).send({ raidId: 'raid-A' });
        assert.equal(response.status, 403, JSON.stringify(response.body));
        assert.equal(response.body.error, 'HOST_NOT_IN_RAID');
        assert.deepEqual(await rawOf(caller), before);
      }
      assert.deepEqual(await rawOf(member), memberBefore, 'a rejected caller marks nobody');
    });

    test('rejects a missing or empty raidId with 400 VALIDATION_FAILED', async () => {
      const host = await bound('raid-A');
      const before = await rawOf(host);
      for (const body of [{}, { raidId: '' }, { raidId: 5 }]) {
        const response = await api('post', '/reservation/raid-started', host).send(body);
        assert.equal(response.status, 400);
        assert.equal(response.body.error, 'VALIDATION_FAILED');
      }
      assert.deepEqual(await rawOf(host), before);
    });

    test('S5: after the start marker the refund is rejected and the items stay with the raid', async () => {
      const host = await bound('raid-A');
      await api('post', '/reservation/raid-started', host).send({ raidId: 'raid-A' });
      await assertRejectedUnchanged(host,
        () => api('post', '/reservation/refund', host).send({ reservationId: `res-${host}` }), 409, 'RAID_ALREADY_STARTED');
    });

    test('exposes startedAt on the serialized reservation', async () => {
      const host = await bound('raid-A');
      const before = await api('get', '', host);
      assert.equal(before.body.pendingReservation.startedAt, null);
      await api('post', '/reservation/raid-started', host).send({ raidId: 'raid-A' });
      const after = await api('get', '', host);
      assert.match(after.body.pendingReservation.startedAt, /^\d{4}-\d{2}-\d{2}T/);
    });
  });

  describe('regressions', () => {
    test('rollback of an admitted reservation still returns 409 RESERVATION_ADMITTED', async () => {
      const accountId = await seed(2);
      await reserveAndAdmit(accountId);
      await assertRejectedUnchanged(accountId,
        () => api('delete', '/reservation?rollback=true', accountId), 409, 'RESERVATION_ADMITTED');
    });

    test('a non-admitted rollback still restores and a loss clear of an admitted reservation still consumes it', async () => {
      const accountId = await seed(2);
      const original = await snapshotOf(accountId);
      await api('post', '/reservation', accountId).send({ reservationId: 'res-1' });
      assert.equal((await api('delete', '/reservation?rollback=true', accountId)).status, 200);
      assert.deepEqual((await snapshotOf(accountId)).loadout, original.loadout);

      await reserveAndAdmit(accountId, 'res-2');
      const cleared = await api('delete', '/reservation', accountId);
      assert.equal(cleared.status, 200);
      assert.equal((await rawOf(accountId)).inventory.pendingReservation, null);
    });
  });
});
