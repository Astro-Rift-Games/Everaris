'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const request = require('supertest');
const jwt = require('jsonwebtoken');
const app = require('../src/app');
const env = require('../src/config/env');
const Character = require('../src/models/Character');
const RevokedToken = require('../src/models/RevokedToken');
const AuthoritativeExtractionResult = require('../src/models/AuthoritativeExtractionResult');
const { createEquipment } = require('../src/services/ItemInstances');
const { EQUIPMENT_SLOTS } = require('../src/config/equipmentSlots');

const equipment = (values = {}) => Object.fromEntries(EQUIPMENT_SLOTS.map(slot => [slot, values[slot] || '']));
const admittedAt = new Date('2026-11-14T12:00:00.000Z');
const base = '/character/me/inventory';

test('admitted reservation loss clear with defeat / empty extraction result', async t => {
  let stored;
  let results;
  let writes;
  const token = jwt.sign({ sub: 'account' }, env.jwtSecret);
  const originals = {
    findOne: Character.findOne,
    findOneAndUpdate: Character.findOneAndUpdate,
    revokedExists: RevokedToken.exists,
    resultExists: AuthoritativeExtractionResult.exists,
    resultUpdateMany: AuthoritativeExtractionResult.updateMany
  };

  t.after(() => {
    Character.findOne = originals.findOne;
    Character.findOneAndUpdate = originals.findOneAndUpdate;
    RevokedToken.exists = originals.revokedExists;
    AuthoritativeExtractionResult.exists = originals.resultExists;
    AuthoritativeExtractionResult.updateMany = originals.resultUpdateMany;
  });

  const call = (method, path, body) => {
    const req = request(app)[method](path).set('Authorization', `Bearer ${token}`);
    return body === undefined ? req : req.send(body);
  };

  t.beforeEach(() => {
    stored = {
      _id: 'character',
      accountId: 'account',
      revision: 10,
      inventory: {
        inventorySchemaVersion: 3,
        currency: 0,
        stash: [],
        loadout: [],
        preparedEquipment: {
          weaponSlot1: null,
          weaponSlot2: null,
          offHand1: null,
          offHand2: null,
          helmet: null,
          armor: null,
          gloves: null,
          boots: null
        },
        pendingReservation: {
          reservationId: 'raid-res-1',
          admittedAt,
          items: [{ slot: 0, lootId: 'health_potion', amount: 2 }],
          preparedEquipment: {
            weaponSlot1: createEquipment('training_short_sword'),
            weaponSlot2: null,
            offHand1: null,
            offHand2: null,
            helmet: null,
            armor: null,
            gloves: null,
            boots: null
          }
        },
        appliedExtractionReceipts: []
      }
    };
    results = [];
    writes = 0;
    RevokedToken.exists = async () => null;

    Character.findOne = async query => {
      assert.equal(query.accountId, 'account');
      if (!stored) return null;
      return Object.assign(structuredClone(stored), {
        markModified() {},
        async save() { throw new Error('Fixtures must not need migration'); }
      });
    };

    Character.findOneAndUpdate = async (query, update) => {
      assert.equal(query.accountId, 'account');
      if (query.revision !== undefined) {
        assert.equal(query.revision, stored.revision);
      }
      for (const [path, value] of Object.entries(update.$set || {})) {
        const keys = path.split('.');
        const field = keys.pop();
        const owner = keys.reduce((val, key) => val[key], stored);
        owner[field] = path === 'inventory.preparedEquipment' ? equipment(value) : structuredClone(value);
      }
      if (update.$inc?.revision) {
        stored.revision += update.$inc.revision;
      }
      writes++;
      return structuredClone(stored);
    };

    AuthoritativeExtractionResult.exists = async query => {
      assert.equal(query.accountId, 'account');
      assert.deepEqual(query.consumed, { $ne: true });
      if (query.$or || query['items.0']?.$exists) {
        return results.find(r => r.accountId === query.accountId && r.consumed !== true &&
          (r.items?.length > 0 || (query.$or && EQUIPMENT_SLOTS.some(slot => r.preparedEquipment?.[slot])))) || null;
      }
      return results.find(r => r.accountId === query.accountId && r.consumed !== true) || null;
    };

    AuthoritativeExtractionResult.updateMany = async (filter, update) => {
      assert.equal(filter.accountId, 'account');
      assert.deepEqual(filter.consumed, { $ne: true });
      let matched = 0;
      for (const r of results) {
        if (r.accountId === filter.accountId && r.consumed !== true) {
          if (filter['items.0']?.$exists === false && r.items && r.items.length > 0) {
            continue;
          }
          if (update.$set) {
            Object.assign(r, update.$set);
          }
          matched++;
        }
      }
      return { matchedCount: matched, modifiedCount: matched };
    };
  });

  await t.test('loss clear succeeds when pending extraction result has empty items (defeat)', async () => {
    // Defeat published an unconsumed result with zero items
    results.push({
      accountId: 'account',
      raidId: 'raid-1',
      consumed: false,
      items: [],
      experienceGranted: 50
    });

    const res = await call('delete', `${base}/reservation`);
    assert.equal(res.status, 200);
    assert.equal(res.body.pendingReservation, null);
    assert.equal(stored.inventory.pendingReservation, null);

    // Defeat result has zero loot, so loss clear is unblocked even while unconsumed
    // This allows DefeatCommitService to still claim progression XP subsequently without conflict
    assert.equal(results[0].consumed, false);

    // Now requesting training weapon succeeds because reservation is cleared
    const weaponRes = await call('post', `${base}/training-weapon`, { weaponId: 'training_short_sword', expectedRevision: res.body.revision });
    assert.equal(weaponRes.status, 200);
    assert.equal(weaponRes.body.loadout[0].lootId, 'training_short_sword');
  });

  for (const weaponSlot1 of ['arming_sword', { lootId: 'arming_sword' }]) {
    await t.test(`equipment-only extraction blocks loss clear: ${JSON.stringify(weaponSlot1)}`, async () => {
      results.push({ accountId: 'account', raidId: 'raid-1', reservationId: 'raid-res-1', consumed: false,
        items: [], preparedEquipment: { weaponSlot1 }, experienceGranted: 100 });
      const before = structuredClone(stored);
      const res = await call('delete', `${base}/reservation`);
      assert.equal(res.status, 409);
      assert.equal(res.body.error, 'EXTRACTION_RESULT_PENDING');
      assert.deepEqual(stored, before);
      assert.equal(writes, 0);
      assert.equal(results[0].consumed, false);
    });
  }

  await t.test('loss clear is rejected with 409 EXTRACTION_RESULT_PENDING when unconsumed result has items', async () => {
    // Extraction result has items that must be protected
    results.push({
      accountId: 'account',
      raidId: 'raid-2',
      consumed: false,
      items: [{ lootId: 'iron_ore', amount: 2 }],
      experienceGranted: 100
    });

    const res = await call('delete', `${base}/reservation`);
    assert.equal(res.status, 409);
    assert.equal(res.body.error, 'EXTRACTION_RESULT_PENDING');

    // Reservation remains pending to protect the uncommitted loot
    assert.ok(stored.inventory.pendingReservation !== null);
    assert.equal(results[0].consumed, false);
  });
});
