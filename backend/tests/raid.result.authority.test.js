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
const SessionTicketService = require('../src/services/SessionTicketService');
const ExtractionCommitService = require('../src/services/ExtractionCommitService');
const { buildCanonicalResultString } = require('../src/utils/raidResultCanonicalizer');
const { createEquipment } = require('../src/services/ItemInstances');
const { computeLevelAndExperience } = require('../src/config/progressionBalance');

const RAID_CODE = '482913';
const RAID_ID = `raid-${RAID_CODE}`;

const OTHER_RAID_CODE = '730154';
const OTHER_RAID_ID = `raid-${OTHER_RAID_CODE}`;

async function rejection(promise) {
  try {
    await promise;
  } catch (error) {
    return error;
  }
  assert.fail('Expected the operation to be rejected');
}

const stored = accountId => Character.findOne({ accountId }).lean();

// Progression after crediting `granted` XP on top of the character's starting state.
function progressed(character, granted) {
  const { resultingLevel, resultingExperience } = computeLevelAndExperience(character.level, character.experience, granted);
  return { level: resultingLevel, experience: resultingExperience };
}

const progressionOf = character => ({ level: character.level, experience: character.experience });

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
      const raider = await ticketedRaider('solo');

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

  // Every raider below has entered a raid: its reservation was bound by the admission ticket.
  async function ticketedRaider(name, raidCode = RAID_CODE, reservation = {}) {
    const raider = await createRaider(name, reservation);
    const reservationId = raider.character.inventory.pendingReservation.reservationId;
    await SessionTicketService.issueRaidAdmissionTicket(raider.accountId, raidCode, reservationId);
    return { ...raider, reservationId };
  }

  const publish = (callerAccountId, recipient, raidId, extra = {}) => RaidResultService.publishHostResult(callerAccountId, {
    raidId,
    resultId: `${raidId}:${recipient.accountId}:1`,
    resultSequence: 1,
    targetCharacterId: recipient.characterId,
    items: [{ lootId: 'slime_gel', amount: 2 }],
    experienceGranted: 5000,
    ...extra
  });

  describe('S2 raid-reservation binding', () => {
    test('the admission ticket binds the reservation to its raid without touching the revision', async () => {
      const raider = await createRaider('solo', { admittedAt: null });
      const before = await stored(raider.accountId);

      await SessionTicketService.issueRaidAdmissionTicket(raider.accountId, RAID_CODE, before.inventory.pendingReservation.reservationId);

      const after = await stored(raider.accountId);
      assert.equal(after.inventory.pendingReservation.raidId, RAID_ID);
      assert.equal(after.revision, before.revision);
    });

    test('a reservation not yet admitted can be re-bound to another raid', async () => {
      const raider = await ticketedRaider('relaunch', RAID_CODE, { admittedAt: null });

      await SessionTicketService.issueRaidAdmissionTicket(raider.accountId, OTHER_RAID_CODE, raider.reservationId);

      assert.equal((await stored(raider.accountId)).inventory.pendingReservation.raidId, OTHER_RAID_ID);
    });

    test('an admitted reservation rejects a ticket for another raid and keeps its binding', async () => {
      const raider = await ticketedRaider('admitted');
      const before = await stored(raider.accountId);

      const error = await rejection(SessionTicketService.issueRaidAdmissionTicket(raider.accountId, OTHER_RAID_CODE, raider.reservationId));

      assert.equal(error.statusCode, 409);
      assert.equal(error.errorCode, 'RESERVATION_RAID_MISMATCH');
      assert.deepEqual(await stored(raider.accountId), before);
    });

    test('repeating the ticket for the same raid keeps the binding', async () => {
      const raider = await ticketedRaider('retry');

      await SessionTicketService.issueRaidAdmissionTicket(raider.accountId, RAID_CODE, raider.reservationId);

      assert.equal((await stored(raider.accountId)).inventory.pendingReservation.raidId, RAID_ID);
    });

    test('rejects a self-published result for a raid other than the admitted reservation and creates no document', async () => {
      const raider = await ticketedRaider('solo');

      const error = await rejection(publish(raider.accountId, raider, OTHER_RAID_ID));

      assert.equal(error.statusCode, 422);
      assert.equal(error.errorCode, 'RESERVATION_RAID_MISMATCH');
      assert.equal(await AuthoritativeExtractionResult.countDocuments(), 0);
    });

    test('rejects a result for a reservation never bound to a raid and creates no document', async () => {
      const raider = await createRaider('unbound');

      const error = await rejection(publish(raider.accountId, raider, RAID_ID));

      assert.equal(error.statusCode, 422);
      assert.equal(error.errorCode, 'RESERVATION_RAID_MISMATCH');
      assert.equal(await AuthoritativeExtractionResult.countDocuments(), 0);
    });

    test('rejects a host bound to another raid publishing for a member and creates no document', async () => {
      const host = await ticketedRaider('host', OTHER_RAID_CODE);
      const member = await ticketedRaider('member');

      const error = await rejection(publish(host.accountId, member, RAID_ID));

      assert.equal(error.statusCode, 403);
      assert.equal(error.errorCode, 'HOST_NOT_IN_RAID');
      assert.equal(await AuthoritativeExtractionResult.countDocuments(), 0);
    });
  });

  describe('S3 per-raid budget', () => {
    test('one reservation cannot credit the experience of two self-published raids', async () => {
      const raider = await ticketedRaider('farmer');
      await publish(raider.accountId, raider, RAID_ID);

      const error = await rejection(publish(raider.accountId, raider, OTHER_RAID_ID));
      await ExtractionCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 1 });

      assert.equal(error.errorCode, 'RESERVATION_RAID_MISMATCH');
      assert.equal(await AuthoritativeExtractionResult.countDocuments(), 1);
      assert.equal((await stored(raider.accountId)).inventory.appliedExtractionReceipts.length, 1);
    });

    test('a result left behind by a re-bound reservation can no longer be committed', async () => {
      const raider = await ticketedRaider('rebinder', RAID_CODE, { admittedAt: null });
      await publish(raider.accountId, raider, RAID_ID);
      await SessionTicketService.issueRaidAdmissionTicket(raider.accountId, OTHER_RAID_CODE, raider.reservationId);
      await publish(raider.accountId, raider, OTHER_RAID_ID);
      const before = await stored(raider.accountId);

      const error = await rejection(ExtractionCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 1 }));

      assert.equal(error.statusCode, 409);
      assert.equal(error.errorCode, 'RESULT_RESERVATION_MISMATCH');
      assert.deepEqual(await stored(raider.accountId), before);
      assert.equal((await AuthoritativeExtractionResult.findOne({ raidId: RAID_ID }).lean()).consumed, false);
    });

    test('a result cannot be committed once its reservation was replaced', async () => {
      const raider = await ticketedRaider('replaced');
      await publish(raider.accountId, raider, RAID_ID);
      await Character.updateOne({ accountId: raider.accountId }, { $set: { 'inventory.pendingReservation.reservationId': 'res_newer' } });

      const error = await rejection(ExtractionCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 1 }));

      assert.equal(error.errorCode, 'RESULT_RESERVATION_MISMATCH');
      assert.deepEqual(progressionOf(await stored(raider.accountId)), progressionOf(raider.character));
    });

    test('a repeated publication for the same raid with a larger reward is rejected and the first stays', async () => {
      const raider = await ticketedRaider('repeat');
      await publish(raider.accountId, raider, RAID_ID, { experienceGranted: 3000 });

      const error = await rejection(publish(raider.accountId, raider, RAID_ID, {
        resultId: `${RAID_ID}:${raider.accountId}:2`, experienceGranted: 5000
      }));

      assert.equal(error.errorCode, 'RESULT_CONFLICT');
      const results = await AuthoritativeExtractionResult.find({}).lean();
      assert.deepEqual(results.map(result => result.experienceGranted), [3000]);
    });

    test('a single publication above the raid budget is rejected', async () => {
      const raider = await ticketedRaider('greedy');

      const error = await rejection(publish(raider.accountId, raider, RAID_ID, { experienceGranted: 5001 }));

      assert.equal(error.errorCode, 'EXCEEDS_EXPERIENCE_BUDGET');
      assert.equal(await AuthoritativeExtractionResult.countDocuments(), 0);
    });
  });

  describe('S4 legitimate extractions', () => {
    test('a solo raider publishes and commits its own extraction', async () => {
      const raider = await ticketedRaider('solo');

      await publish(raider.accountId, raider, RAID_ID, { experienceGranted: 120 });
      const committed = await ExtractionCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 1 });

      assert.equal(committed.alreadySecured, false);
      assert.deepEqual(committed.loadout.map(item => [item.lootId, item.amount]), [['slime_gel', 2]]);
      const after = await stored(raider.accountId);
      assert.deepEqual(progressionOf(after), progressed(raider.character, 120));
      assert.equal(after.inventory.pendingReservation, undefined);
    });

    test('a party host publishes for itself and a member, and both commit', async () => {
      const host = await ticketedRaider('host');
      const member = await ticketedRaider('member');

      await publish(host.accountId, member, RAID_ID, { experienceGranted: 200 });
      await publish(host.accountId, host, RAID_ID, { experienceGranted: 300 });
      await ExtractionCommitService.commit(member.accountId, { raidId: RAID_ID, resultSequence: 1 });
      await ExtractionCommitService.commit(host.accountId, { raidId: RAID_ID, resultSequence: 1 });

      assert.deepEqual(progressionOf(await stored(member.accountId)), progressed(member.character, 200));
      assert.deepEqual(progressionOf(await stored(host.accountId)), progressed(host.character, 300));
      assert.equal(await AuthoritativeExtractionResult.countDocuments({ consumed: true }), 2);
    });

    test('a host that already extracted can still publish for a member of that raid', async () => {
      const host = await ticketedRaider('host');
      const member = await ticketedRaider('member');
      await publish(host.accountId, host, RAID_ID, { experienceGranted: 300 });
      await ExtractionCommitService.commit(host.accountId, { raidId: RAID_ID, resultSequence: 1 });

      const published = await publish(host.accountId, member, RAID_ID, { experienceGranted: 200 });

      assert.equal(published.alreadyPublished, false);
    });
  });
});
