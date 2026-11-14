'use strict';

// FIX-18: authoritative validation of published raid results against the game
// catalog, the recipient's raid-bound reservation, and the per-raid budget.
const { test, describe, before, after, beforeEach } = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const mongoose = require('mongoose');
const { startMemoryMongo } = require('./support/memoryMongo');
const env = require('../src/config/env');
const Account = require('../src/models/Account');
const Character = require('../src/models/Character');
const AuthoritativeExtractionResult = require('../src/models/AuthoritativeExtractionResult');
const RaidResultService = require('../src/services/RaidResultService');
const { buildCanonicalResultString } = require('../src/utils/raidResultCanonicalizer');
const { createEquipment } = require('../src/services/ItemInstances');

const RAID_CODE = '482913';
const RAID_ID = `raid-${RAID_CODE}`;

async function rejection(promise) {
  try {
    await promise;
  } catch (error) {
    return error;
  }
  assert.fail('Expected the publication to be rejected');
}

function sign(payload, targetAccountId) {
  const canonical = buildCanonicalResultString({
    raidId: payload.raidId,
    resultId: payload.resultId,
    targetAccountId,
    items: payload.items || [],
    preparedEquipment: payload.preparedEquipment || {},
    experienceGranted: payload.experienceGranted || 0,
    issuedAt: payload.issuedAt || ''
  });
  return crypto.createHmac('sha256', env.webhookSecret).update(canonical).digest('hex');
}

describe('FIX-18 authoritative result validation', () => {
  let mongoServer;
  let sequence = 0;

  before(async () => {
    mongoServer = await startMemoryMongo();
    await mongoose.connect(mongoServer.getUri());
  });

  after(async () => {
    await mongoose.disconnect();
    await mongoServer.stop();
  });

  beforeEach(async () => {
    await Promise.all([Account.deleteMany({}), Character.deleteMany({}), AuthoritativeExtractionResult.deleteMany({})]);
  });

  async function createRaider(name, reservation = {}) {
    const account = await Account.create({ username: `${name}_${++sequence}`, passwordHash: 'x' });
    const character = await Character.create({
      accountId: account._id,
      name: `${name}${sequence}`,
      inventory: {
        inventorySchemaVersion: 3,
        pendingReservation: {
          reservationId: `res_${name}_${sequence}`,
          admittedAt: new Date('2026-11-14T12:00:00.000Z'),
          items: [],
          preparedEquipment: { weaponSlot1: createEquipment('arming_sword') },
          ...reservation
        }
      }
    });
    return { accountId: account._id.toString(), characterId: character._id.toString(), character };
  }

  describe('S1 catalog', () => {
    test('rejects an unsigned result with a lootId outside the catalog with 422 and creates no document', async () => {
      const raider = await createRaider('solo');

      const error = await rejection(RaidResultService.publishHostResult(raider.accountId, {
        raidId: RAID_ID,
        resultId: `${RAID_ID}:${raider.accountId}:1`,
        items: [{ lootId: 'arming_sword', amount: 1 }, { lootId: 'forged_dragon_scale', amount: 3 }],
        experienceGranted: 100
      }));

      assert.equal(error.statusCode, 422);
      assert.equal(error.errorCode, 'UNKNOWN_LOOT_ID');
      assert.match(error.message, /forged_dragon_scale/);
      assert.equal(await AuthoritativeExtractionResult.countDocuments(), 0);
    });

    test('rejects an unsigned result whose prepared equipment is outside the catalog with 422 and creates no document', async () => {
      const raider = await createRaider('solo');

      const error = await rejection(RaidResultService.publishHostResult(raider.accountId, {
        raidId: RAID_ID,
        resultId: `${RAID_ID}:${raider.accountId}:1`,
        items: [],
        preparedEquipment: { weaponSlot1: { lootId: 'forged_dragon_blade' } },
        experienceGranted: 0
      }));

      assert.equal(error.statusCode, 422);
      assert.equal(error.errorCode, 'UNKNOWN_LOOT_ID');
      assert.match(error.message, /forged_dragon_blade/);
      assert.equal(await AuthoritativeExtractionResult.countDocuments(), 0);
    });

    test('rejects a signed result with a lootId outside the catalog with 422 and creates no document', async () => {
      const raider = await createRaider('signed');
      const payload = {
        raidId: RAID_ID,
        resultId: `${RAID_ID}:${raider.accountId}:1`,
        targetAccountId: raider.accountId,
        items: [{ lootId: 'forged_dragon_scale', amount: 1 }],
        experienceGranted: 0,
        issuedAt: '2026-11-14T12:30:00.000Z'
      };
      payload.hostSignature = sign(payload, raider.accountId);

      const error = await rejection(RaidResultService.publishHostResult(raider.accountId, payload));

      assert.equal(error.statusCode, 422);
      assert.equal(error.errorCode, 'UNKNOWN_LOOT_ID');
      assert.equal(await AuthoritativeExtractionResult.countDocuments(), 0);
    });

    test('accepts catalog loot, backend-only GD-14 materials and legacy aliases', async () => {
      const raider = await createRaider('solo');

      const published = await RaidResultService.publishHostResult(raider.accountId, {
        raidId: RAID_ID,
        resultId: `${RAID_ID}:${raider.accountId}:1`,
        items: [{ lootId: 'longsword', amount: 1 }, { lootId: 'slime_gel', amount: 4 }, { lootId: 'TRE_CHAOS_COIN', amount: 1 }],
        preparedEquipment: { weaponSlot1: { lootId: 'arming_sword' } },
        experienceGranted: 250
      });

      assert.equal(published.status, 'published');
      const stored = await AuthoritativeExtractionResult.findOne({ resultId: published.resultId }).lean();
      assert.deepEqual(stored.items.map(item => [item.lootId, item.amount]),
        [['arming_sword', 1], ['slime_gel', 4], ['TRE_CHAOS_COIN', 1]]);
    });
  });

});
