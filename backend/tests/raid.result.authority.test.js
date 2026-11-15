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
const DefeatCommitService = require('../src/services/DefeatCommitService');
const InventoryService = require('../src/services/InventoryService');
const { buildCanonicalResultString } = require('../src/utils/raidResultCanonicalizer');
const { createEquipment } = require('../src/services/ItemInstances');
const { computeLevelAndExperience, computeAttributePointsGranted } = require('../src/config/progressionBalance');
const { EQUIPMENT_SLOTS } = require('../src/config/equipmentSlots');
const { isDefeatResult, pendingLootResultFilter } = require('../src/services/ResultReservationBinding');

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

  describe('review result binding', () => {
    test('equipment-only publication blocks real loss clear and remains extractable', async () => {
      const raider = await createRaider('equipment', { raidId: RAID_ID });
      await RaidResultService.publishHostResult(raider.accountId, { raidId: RAID_ID, resultSequence: 1,
        items: [], preparedEquipment: { weaponSlot1: { lootId: 'arming_sword' } }, experienceGranted: 80 });
      const before = await stored(raider.accountId);
      const error = await rejection(InventoryService.clearPendingReservation(raider.accountId));
      assert.equal(error.statusCode, 409);
      assert.equal(error.errorCode, 'EXTRACTION_RESULT_PENDING');
      assert.deepEqual(await stored(raider.accountId), before);
      assert.equal((await AuthoritativeExtractionResult.findOne({ raidId: RAID_ID })).consumed, false);
      const committed = await ExtractionCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 1 });
      assert.equal(committed.alreadySecured, false);
      assert.equal((await stored(raider.accountId)).inventory.preparedEquipment.weaponSlot1.lootId, 'arming_sword');
    });

    test('revision race preserves an attribute spend and defeat retries against the new state', async () => {
      const raider = await createRaider('revision', { raidId: RAID_ID });
      await Character.updateOne({ accountId: raider.accountId }, { $set: { 'characterAttributes.availablePoints': 5 } });
      await RaidResultService.publishHostResult(raider.accountId, { raidId: RAID_ID, resultSequence: 1,
        items: [], experienceGranted: 80 });
      const original = AuthoritativeExtractionResult.findOneAndUpdate;
      let spent;
      AuthoritativeExtractionResult.findOneAndUpdate = async function(query, update, options) {
        const result = await original.call(this, query, update, options);
        if (update.$set?.consumed === true) {
          await Character.updateOne({ accountId: raider.accountId }, {
            $inc: { revision: 1, 'characterAttributes.availablePoints': -1, 'characterAttributes.strength': 1 }
          });
          spent = await stored(raider.accountId);
        }
        return result;
      };
      try {
        const result = await DefeatCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 1 });
        assert.equal(result.alreadySecured, false);
        assert.equal((await AuthoritativeExtractionResult.findOne({ raidId: RAID_ID })).consumed, true);
      } finally {
        AuthoritativeExtractionResult.findOneAndUpdate = original;
      }
      const committed = await stored(raider.accountId);
      assert.equal(committed.characterAttributes.strength, spent.characterAttributes.strength);
      assert.equal(committed.characterAttributes.availablePoints, spent.characterAttributes.availablePoints +
        computeAttributePointsGranted(spent.level, committed.level));
      assert.equal(committed.revision, spent.revision + 1);
      assert.deepEqual(progressionOf(committed), progressed(spent, 80));
      assert.equal(committed.appliedProgressionReceipts.length, 1);
    });

    test('concurrent duplicate defeat commits credit once while retrying a revision race', async () => {
      const raider = await createRaider('duplicates', { raidId: RAID_ID });
      await RaidResultService.publishHostResult(raider.accountId, { raidId: RAID_ID, resultSequence: 1,
        items: [], experienceGranted: 80 });
      const before = await stored(raider.accountId);
      const original = Character.findOneAndUpdate;
      let raced = false;
      Character.findOneAndUpdate = async function(query, update, options) {
        if (query.accountId === raider.accountId && update.$set?.lastProgressionReceipt && !raced) {
          raced = true;
          await Character.updateOne({ accountId: raider.accountId }, { $inc: { revision: 1, 'characterAttributes.strength': 1 } });
        }
        return original.call(this, query, update, options);
      };
      let outcomes;
      try {
        outcomes = await Promise.allSettled([1, 2].map(() =>
          DefeatCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 1 })));
      } finally {
        Character.findOneAndUpdate = original;
      }
      assert.equal(outcomes.filter(o => o.status === 'fulfilled' && !o.value.alreadySecured).length, 1);
      for (const outcome of outcomes) {
        if (outcome.status === 'rejected') {
          assert.equal(outcome.reason.statusCode, 409);
          assert.equal(outcome.reason.errorCode, 'RESULT_ALREADY_CONSUMED');
        }
      }
      const committed = await stored(raider.accountId);
      assert.deepEqual(progressionOf(committed), progressed(before, 80));
      assert.equal(committed.revision, before.revision + 2);
      assert.equal(committed.characterAttributes.strength, before.characterAttributes.strength + 1);
      assert.equal(committed.appliedProgressionReceipts.length, 1);
      assert.equal((await AuthoritativeExtractionResult.findOne({ raidId: RAID_ID })).consumed, true);
      assert.equal((await DefeatCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 1 })).alreadySecured, true);
      assert.deepEqual(await stored(raider.accountId), committed);
    });

    for (const staleSnapshot of [false, true]) {
      test(`duplicate waits for the held defeat claim (stale unconsumed snapshot: ${staleSnapshot})`, async () => {
        const raider = await createRaider('heldClaim', { raidId: RAID_ID });
        await RaidResultService.publishHostResult(raider.accountId, { raidId: RAID_ID, resultSequence: 1,
          items: [], experienceGranted: 80 });
        const snapshot = await AuthoritativeExtractionResult.findOne({ raidId: RAID_ID });
        const before = await stored(raider.accountId);
        const originalWrite = Character.findOneAndUpdate;
        const originalLookup = AuthoritativeExtractionResult.findOne;
        let notifyHeld;
        const held = new Promise(resolve => { notifyHeld = resolve; });
        Character.findOneAndUpdate = async function(query, update, options) {
          if (query.accountId === raider.accountId && update.$set?.lastProgressionReceipt) {
            notifyHeld();
            await new Promise(resolve => setTimeout(resolve, 400));
          }
          return originalWrite.call(this, query, update, options);
        };
        let outcomes;
        try {
          const winner = DefeatCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 1 });
          await held;
          if (staleSnapshot) AuthoritativeExtractionResult.findOne = async () => snapshot;
          outcomes = await Promise.allSettled([winner,
            DefeatCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 1 })]);
        } finally {
          Character.findOneAndUpdate = originalWrite;
          AuthoritativeExtractionResult.findOne = originalLookup;
        }
        assert.deepEqual(outcomes.map(o => o.status), ['fulfilled', 'fulfilled']);
        const [winner, duplicate] = outcomes.map(o => o.value);
        assert.equal(winner.alreadySecured, false);
        assert.deepEqual(duplicate, { ...winner, alreadySecured: true });
        const committed = await stored(raider.accountId);
        assert.deepEqual(progressionOf(committed), progressed(before, 80));
        assert.equal(committed.revision, before.revision + 1);
        assert.equal(committed.appliedProgressionReceipts.length, 1);
        assert.equal(committed.inventory.pendingReservation, undefined);
        assert.equal((await AuthoritativeExtractionResult.findOne({ raidId: RAID_ID })).consumed, true);
        assert.equal((await DefeatCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 1 })).alreadySecured, true);
        assert.deepEqual(await stored(raider.accountId), committed);
      });
    }

    const slotEncodings = [
      [undefined, false], [null, false], ['', false], ['null', false], [{}, false],
      [[], false], [[{}], false], [['null'], false], [[null], false], [[''], false],
      [[{}, 'null', null, ''], false],
      ['arming_sword', true], [{ lootId: 'arming_sword' }, true],
      [['arming_sword'], true], [[{ lootId: 'arming_sword' }], true],
      [[null, 'arming_sword', {}], true], [[{}, { lootId: 'arming_sword' }, ''], true],
      [0, true], [false, true], [[null, 0], true], [['', false], true],
    ];
    for (const slot of EQUIPMENT_SLOTS) {
      test(`JS and real Mongo ownership parity matrix in ${slot}, including array encodings`, async () => {
        for (const [value, owned] of slotEncodings) {
          const result = new AuthoritativeExtractionResult({ raidId: RAID_ID, accountId: 'parity',
            resultId: `parity-${slot}`, items: [], preparedEquipment: value === undefined ? {} : { [slot]: value } });
          await AuthoritativeExtractionResult.collection.insertOne(result.toObject());
          try {
            const label = `${slot}: ${JSON.stringify(value)}`;
            const mongoOwned = Boolean(await AuthoritativeExtractionResult.findOne({ _id: result._id, ...pendingLootResultFilter() }));
            assert.equal(mongoOwned, owned, `Mongo ${label}`);
            assert.equal(!isDefeatResult(result), owned, `JS ${label}`);
          } finally {
            await AuthoritativeExtractionResult.deleteOne({ _id: result._id });
          }
        }
      });
    }

    // Insert legacy persisted results directly: the current producer normalizes equipment.
    for (const empty of [{}, 'null', [], [{}], ['null'], [null], [''], [{}, 'null', null, '']]) {
      test(`real Mongo empty slots ${JSON.stringify(empty)} allow loss clear and defeat credit`, async () => {
        const raider = await createRaider('emptySlots', { raidId: RAID_ID });
        await AuthoritativeExtractionResult.collection.insertOne({ raidId: RAID_ID, accountId: raider.accountId,
          resultId: `${RAID_ID}:${raider.characterId}:1`, items: [], experienceGranted: 80, consumed: false,
          preparedEquipment: Object.fromEntries(EQUIPMENT_SLOTS.map(slot => [slot, empty])) });
        await InventoryService.clearPendingReservation(raider.accountId);
        const cleared = await stored(raider.accountId);
        assert.equal(cleared.inventory.pendingReservation, null);
        assert.equal(cleared.revision, raider.character.revision + 1);
        assert.equal((await AuthoritativeExtractionResult.findOne({ raidId: RAID_ID })).consumed, false);
        const committed = await DefeatCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 1 });
        assert.equal(committed.alreadySecured, false);
        const after = await stored(raider.accountId);
        assert.deepEqual(progressionOf(after), progressed(cleared, 80));
        assert.equal(after.revision, cleared.revision + 1);
        assert.equal(after.appliedProgressionReceipts.length, 1);
        assert.equal((await AuthoritativeExtractionResult.findOne({ raidId: RAID_ID })).consumed, true);
      });
    }

    for (const slot of EQUIPMENT_SLOTS) {
      test(`real Mongo legacy string ownership in ${slot} blocks loss clear and defeat`, async () => {
        const raider = await createRaider('legacySlot', { raidId: RAID_ID });
        await AuthoritativeExtractionResult.collection.insertOne({ raidId: RAID_ID, accountId: raider.accountId,
          resultId: `${RAID_ID}:${raider.characterId}:1`, resultSequence: 1, items: [], experienceGranted: 80,
          consumed: false, preparedEquipment: { [slot]: 'arming_sword' } });
        const before = await stored(raider.accountId);
        const resultBefore = await AuthoritativeExtractionResult.findOne({ raidId: RAID_ID }).lean();
        const lossError = await rejection(InventoryService.clearPendingReservation(raider.accountId));
        assert.equal(lossError.statusCode, 409);
        assert.equal(lossError.errorCode, 'EXTRACTION_RESULT_PENDING');
        const defeatError = await rejection(DefeatCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 1 }));
        assert.equal(defeatError.statusCode, 422);
        assert.equal(defeatError.errorCode, 'NO_AUTHORITATIVE_RESULT');
        assert.deepEqual(await stored(raider.accountId), before);
        assert.deepEqual(await AuthoritativeExtractionResult.findOne({ raidId: RAID_ID }).lean(), resultBefore);
      });
    }

    test('published defeat sequence is stored and cannot be replaced by the commit caller', async () => {
      const raider = await createRaider('sequence', { raidId: RAID_ID });
      await RaidResultService.publishHostResult(raider.accountId, { raidId: RAID_ID, resultId: 'opaque-result',
        resultSequence: 7, items: [], preparedEquipment: {}, experienceGranted: 80 });
      const result = await AuthoritativeExtractionResult.findOne({ raidId: RAID_ID }).lean();
      assert.equal(result.resultSequence, 7);
      const before = await stored(raider.accountId);
      const error = await rejection(DefeatCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 99 }));
      assert.equal(error.statusCode, 422);
      assert.equal(error.errorCode, 'NO_AUTHORITATIVE_RESULT');
      assert.deepEqual(await stored(raider.accountId), before);
      assert.equal((await AuthoritativeExtractionResult.findOne({ raidId: RAID_ID })).consumed, false);
      await DefeatCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 7 });
      const afterCommit = await stored(raider.accountId);
      assert.equal(afterCommit.lastAppliedProgressionResultSequence, 7);
      assert.equal(afterCommit.lastProgressionReceipt.resultSequence, 7);
    });
  });

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

  describe('S1/S3 bypasses found by verification', () => {
    test('a defeat commit cannot credit a result left behind by a re-bound reservation', async () => {
      const raider = await ticketedRaider('rebinder', RAID_CODE, { admittedAt: null });
      await publish(raider.accountId, raider, RAID_ID);
      await SessionTicketService.issueRaidAdmissionTicket(raider.accountId, OTHER_RAID_CODE, raider.reservationId);
      await publish(raider.accountId, raider, OTHER_RAID_ID);
      await ExtractionCommitService.commit(raider.accountId, { raidId: OTHER_RAID_ID, resultSequence: 1 });
      const before = await stored(raider.accountId);

      const error = await rejection(DefeatCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 1 }));

      assert.equal(error.statusCode, 409);
      assert.equal(error.errorCode, 'RESULT_RESERVATION_MISMATCH');
      assert.deepEqual(await stored(raider.accountId), before);
      assert.equal((await AuthoritativeExtractionResult.findOne({ raidId: RAID_ID }).lean()).consumed, false);
    });

    test('a late defeat commit credits XP after its reservation was replaced', async () => {
      const raider = await ticketedRaider('replaced');
      await publish(raider.accountId, raider, RAID_ID, { items: [] });
      await Character.updateOne({ accountId: raider.accountId }, { $set: { 'inventory.pendingReservation.reservationId': 'res_newer' } });

      const committed = await DefeatCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 1 });

      assert.equal(committed.alreadySecured, false);
      const after = await stored(raider.accountId);
      assert.deepEqual(progressionOf(after), progressed(raider.character, 5000));
      assert.equal(after.appliedProgressionReceipts[0].reservationId, raider.reservationId);
    });

    test('a defeat commit retains XP after the real inventory loss clear', async () => {
      const raider = await ticketedRaider('loss');
      await publish(raider.accountId, raider, RAID_ID, { items: [], experienceGranted: 80 });
      await InventoryService.clearPendingReservation(raider.accountId, { expectedReservationId: raider.reservationId });
      assert.equal((await stored(raider.accountId)).inventory.pendingReservation, null);

      const committed = await DefeatCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 1 });

      assert.equal(committed.alreadySecured, false);
      assert.deepEqual(progressionOf(await stored(raider.accountId)), progressed(raider.character, 80));
      assert.equal((await stored(raider.accountId)).appliedProgressionReceipts[0].reservationId, raider.reservationId);
    });

    for (const secondCommit of ['extraction', 'defeat']) {
      test(`after re-binding, defeat A prevents a second ${secondCommit} credit for B`, async () => {
        const raider = await ticketedRaider('rebind', RAID_CODE, { admittedAt: null });
        await publish(raider.accountId, raider, RAID_ID, { items: [], experienceGranted: 80 });
        await SessionTicketService.issueRaidAdmissionTicket(raider.accountId, OTHER_RAID_CODE, raider.reservationId);
        await publish(raider.accountId, raider, OTHER_RAID_ID, { items: [], experienceGranted: 120 });

        await DefeatCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 1 });
        const before = await stored(raider.accountId);
        assert.deepEqual(progressionOf(before), progressed(raider.character, 80));
        assert.equal(before.appliedProgressionReceipts[0].reservationId, raider.reservationId);
        const service = secondCommit === 'extraction' ? ExtractionCommitService : DefeatCommitService;
        const error = await rejection(service.commit(raider.accountId, { raidId: OTHER_RAID_ID, resultSequence: 1 }));

        assert.equal(error.statusCode, 409);
        assert.equal(error.errorCode, 'RESULT_RESERVATION_MISMATCH');
        assert.deepEqual(await stored(raider.accountId), before);
        assert.equal(before.appliedProgressionReceipts.length, 1);
        assert.equal((await AuthoritativeExtractionResult.findOne({ raidId: OTHER_RAID_ID }).lean()).consumed, false);
      });
    }

    test('after extraction B, defeat A cannot credit the same reservation', async () => {
      const raider = await ticketedRaider('reverse', RAID_CODE, { admittedAt: null });
      await publish(raider.accountId, raider, RAID_ID, { items: [], experienceGranted: 80 });
      await SessionTicketService.issueRaidAdmissionTicket(raider.accountId, OTHER_RAID_CODE, raider.reservationId);
      await publish(raider.accountId, raider, OTHER_RAID_ID, { experienceGranted: 120 });
      await ExtractionCommitService.commit(raider.accountId, { raidId: OTHER_RAID_ID, resultSequence: 1 });
      const before = await stored(raider.accountId);
      assert.deepEqual(progressionOf(before), progressed(raider.character, 120));
      assert.equal(before.inventory.appliedExtractionReceipts[0].reservationId, raider.reservationId);
      assert.equal(before.appliedProgressionReceipts[0].reservationId, raider.reservationId);

      const error = await rejection(DefeatCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 1 }));

      assert.equal(error.statusCode, 409);
      assert.equal(error.errorCode, 'RESULT_RESERVATION_MISMATCH');
      assert.deepEqual(await stored(raider.accountId), before);
      assert.equal(before.appliedProgressionReceipts.length, 1);
      assert.equal((await AuthoritativeExtractionResult.findOne({ raidId: RAID_ID }).lean()).consumed, false);
    });

    test('signed object equipment on a non-weapon slot rejects a padded catalog id', async () => {
      const raider = await ticketedRaider('signedHelmet');
      const payload = {
        raidId: RAID_ID,
        resultId: `${RAID_ID}:${raider.accountId}:1`,
        targetAccountId: raider.accountId,
        items: [],
        preparedEquipment: { helmet: { lootId: '\nheavy_armor_helmet' } },
        experienceGranted: 0
      };
      payload.hostSignature = sign(payload, raider.accountId);
      const before = await stored(raider.accountId);

      const error = await rejection(RaidResultService.publishHostResult(raider.accountId, payload));

      assert.equal(error.statusCode, 422);
      assert.equal(error.errorCode, 'UNKNOWN_LOOT_ID');
      assert.deepEqual(await stored(raider.accountId), before);
      assert.equal(await AuthoritativeExtractionResult.countDocuments(), 0);
    });

    for (const hasCharacter of [true, false]) {
      test(`unsigned non-list items reject before recipient lookup (character exists: ${hasCharacter})`, async () => {
        const raider = hasCharacter ? await ticketedRaider('nonlist') : { accountId: new mongoose.Types.ObjectId().toString() };
        const before = await stored(raider.accountId);

        const error = await rejection(RaidResultService.publishHostResult(raider.accountId, {
          raidId: RAID_ID,
          targetAccountId: raider.accountId,
          items: { lootId: 'slime_gel', amount: 1 }
        }));

        assert.equal(error.statusCode, 400);
        assert.equal(error.errorCode, 'INVALID_RESULT_PAYLOAD');
        assert.deepEqual(await stored(raider.accountId), before);
        assert.equal(await AuthoritativeExtractionResult.countDocuments(), 0);
      });
    }

    test('a padded lootId is not a catalog id and creates no document', async () => {
      const raider = await ticketedRaider('padder');

      for (const items of [[{ lootId: ' arming_sword', amount: 50 }], [{ lootId: 'bone ', amount: 1 }]]) {
        const error = await rejection(publish(raider.accountId, raider, RAID_ID, { items }));
        assert.equal(error.statusCode, 422);
        assert.equal(error.errorCode, 'UNKNOWN_LOOT_ID');
      }
      const error = await rejection(publish(raider.accountId, raider, RAID_ID, {
        items: [], preparedEquipment: { weaponSlot1: { lootId: 'arming_sword ' } }
      }));
      assert.equal(error.errorCode, 'UNKNOWN_LOOT_ID');
      assert.equal(await AuthoritativeExtractionResult.countDocuments(), 0);
    });

    test('a signed result whose items are not a list is rejected and creates no document', async () => {
      // Signed producers may publish for an account whose character does not exist yet.
      const targetAccountId = new mongoose.Types.ObjectId().toString();
      const payload = {
        raidId: RAID_ID,
        resultId: `${RAID_ID}:${targetAccountId}:1`,
        targetAccountId,
        items: { lootId: 'forged_dragon_scale', amount: 1 },
        experienceGranted: 0,
        issuedAt: '2026-11-14T12:30:00.000Z'
      };
      payload.hostSignature = sign(payload, targetAccountId);

      const error = await rejection(RaidResultService.publishHostResult(targetAccountId, payload));

      assert.equal(error.statusCode, 400);
      assert.equal(error.errorCode, 'INVALID_RESULT_PAYLOAD');
      assert.equal(await AuthoritativeExtractionResult.countDocuments(), 0);
    });
  });

  describe('S4 legitimate extractions', () => {
    test('a defeated raider commits the result published for its reservation', async () => {
      const raider = await ticketedRaider('fallen');

      await publish(raider.accountId, raider, RAID_ID, { items: [], experienceGranted: 80 });
      await DefeatCommitService.commit(raider.accountId, { raidId: RAID_ID, resultSequence: 1 });

      assert.deepEqual(progressionOf(await stored(raider.accountId)), progressed(raider.character, 80));
    });

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
