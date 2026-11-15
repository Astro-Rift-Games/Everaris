'use strict';

const { describe, test, before, after, mock } = require('node:test');
const assert = require('node:assert/strict');
const mongoose = require('mongoose');
const { startMemoryMongo } = require('../support/memoryMongo');
const Character = require('../../src/models/Character');
const Result = require('../../src/models/AuthoritativeExtractionResult');
const RaidResultService = require('../../src/services/RaidResultService');
const ExtractionCommitService = require('../../src/services/ExtractionCommitService');
const { computeLevelAndExperience } = require('../../src/config/progressionBalance');

// Force both reads to complete before either insert, reproducing the check/create race.
async function race(accountId, payloads) {
  const findOne = Result.findOne.bind(Result);
  let arrivals = 0;
  let release;
  const gate = new Promise(resolve => { release = resolve; });
  const stub = mock.method(Result, 'findOne', async (...args) => {
    const result = await findOne(...args);
    if (++arrivals === 2) release();
    await gate;
    return result;
  });
  try {
    return await Promise.allSettled(payloads.map(payload => RaidResultService.publishHostResult(accountId, payload)));
  } finally {
    stub.mock.restore();
  }
}

async function raider(raidId) {
  const accountId = new mongoose.Types.ObjectId().toString();
  const character = await Character.create({
    accountId, name: 'RaceHero',
    inventory: { inventorySchemaVersion: 3, pendingReservation: {
      reservationId: `res-${raidId}`, raidId, admittedAt: new Date(), items: []
    } }
  });
  return { accountId, character };
}

const payload = (raidId, resultId = `${raidId}:1`) => ({
  raidId, resultId, items: [{ lootId: 'bone', amount: 2 }], experienceGranted: 100
});

describe('FIX-22 concurrent authoritative publication', () => {
  let mongo;
  before(async () => {
    mongo = await startMemoryMongo();
    await mongoose.connect(mongo.getUri());
    await Promise.all([Result.init(), Character.init()]);
  });
  after(async () => {
    await mongoose.disconnect();
    if (mongo) await mongo.stop();
  });

  test('identical concurrent publications create one result and commit credits rewards only once', async () => {
    const raidId = 'raid-identical';
    const { accountId, character } = await raider(raidId);
    const results = await race(accountId, [payload(raidId), payload(raidId)]);
    assert.equal(await Result.countDocuments({ raidId, accountId }), 1);
    assert.equal(results.filter(r => r.status === 'fulfilled').length, 2);
    assert.deepEqual(results.map(r => r.value.alreadyPublished).sort(), [false, true]);
    for (const result of results) assert.deepEqual(result.value, {
      status: 'published', alreadyPublished: result.value.alreadyPublished,
      raidId, accountId, resultId: `${raidId}:1`
    });
    const first = await ExtractionCommitService.commit(accountId, { raidId, resultSequence: 1 });
    const snapshot = await Character.findOne({ accountId }).lean();
    const replay = await ExtractionCommitService.commit(accountId, { raidId, resultSequence: 1 });
    assert.equal(first.alreadySecured, false);
    assert.equal(replay.alreadySecured, true);
    assert.deepEqual(await Character.findOne({ accountId }).lean(), snapshot);
    assert.deepEqual(first.loadout, [{ slot: 0, lootId: 'bone', amount: 2 }]);
    const expected = computeLevelAndExperience(character.level, character.experience, 100);
    assert.equal(first.level, expected.resultingLevel);
    assert.equal(first.experience, expected.resultingExperience);
    assert.equal(snapshot.revision, character.revision + 1);
    assert.equal(snapshot.inventory.appliedExtractionReceipts.length, 1);
    assert.equal(snapshot.appliedProgressionReceipts.length, 1);
    await assert.rejects(ExtractionCommitService.commit(accountId, { raidId, resultSequence: 2 }),
      { statusCode: 409, errorCode: 'RESULT_ALREADY_CONSUMED' });
    assert.deepEqual(await Character.findOne({ accountId }).lean(), snapshot);
  });

  for (const sameId of [true, false]) {
    test(`different concurrent rewards conflict (${sameId ? 'resultId' : 'raid/account'} collision)`, async () => {
      const raidId = `raid-conflict-${sameId}`;
      const { accountId } = await raider(raidId);
      const before = await Character.findOne({ accountId }).lean();
      const a = payload(raidId);
      const b = { ...payload(raidId, sameId ? a.resultId : `${raidId}:2`), experienceGranted: 200 };
      const results = await race(accountId, [a, b]);
      assert.equal(await Result.countDocuments({ raidId, accountId }), 1);
      assert.equal(results.filter(r => r.status === 'fulfilled').length, 1);
      const rejected = results.filter(r => r.status === 'rejected');
      assert.equal(rejected.length, 1);
      assert.deepEqual(rejected[0].reason, { statusCode: 409, errorCode: 'RESULT_CONFLICT',
        message: 'An authoritative extraction result already exists with conflicting rewards.' });
      assert.deepEqual(await Character.findOne({ accountId }).lean(), before);
      const winner = results.find(r => r.status === 'fulfilled').value;
      assert.equal(winner.alreadyPublished, false);
      const stored = await Result.findOne({ raidId, accountId }).lean();
      const winningPayload = [a, b][results.findIndex(r => r.status === 'fulfilled')];
      assert.equal(stored.resultId, winningPayload.resultId);
      assert.equal(stored.experienceGranted, winningPayload.experienceGranted);
      assert.deepEqual(stored.items.map(i => ({ lootId: i.lootId, amount: i.amount })), winningPayload.items);
      assert.equal(stored.consumed, false);
    });
  }

  test('sequential replay preserves reward equivalence, conflicts and the consumed-result lock', async () => {
    const raidId = 'raid-sequential';
    const { accountId } = await raider(raidId);
    const request = payload(raidId);
    assert.equal((await RaidResultService.publishHostResult(accountId, request)).alreadyPublished, false);
    const before = await Result.findOne({ raidId, accountId }).lean();
    assert.equal((await RaidResultService.publishHostResult(accountId, request)).alreadyPublished, true);
    await assert.rejects(RaidResultService.publishHostResult(accountId, { ...request, experienceGranted: 101 }),
      { statusCode: 409, errorCode: 'RESULT_CONFLICT' });
    assert.deepEqual(await Result.findOne({ raidId, accountId }).lean(), before);
    await ExtractionCommitService.commit(accountId, { raidId, resultSequence: 1 });
    const committed = await Character.findOne({ accountId }).lean();
    // A consumed result with admission still present reaches the existing-result lock;
    // a completed commit would first fail the unsigned admission check.
    const fresh = await raider('raid-consumed');
    const consumedPayload = payload('raid-consumed');
    await RaidResultService.publishHostResult(fresh.accountId, consumedPayload);
    await Result.updateOne({ raidId: 'raid-consumed' }, { $set: { consumed: true } });
    await assert.rejects(RaidResultService.publishHostResult(fresh.accountId, consumedPayload),
      { statusCode: 409, errorCode: 'RESULT_ALREADY_CONSUMED',
        message: 'This authoritative extraction result has already been consumed and cannot be overwritten.' });
    assert.deepEqual(await Character.findOne({ accountId }).lean(), committed);
    assert.equal(await Result.countDocuments({ raidId: 'raid-consumed' }), 1);
  });

  test('unique indexes coexist with the unchanged 24-hour TTL index', async () => {
    const indexes = await Result.collection.indexes();
    assert.ok(indexes.some(i => i.unique && JSON.stringify(i.key) === JSON.stringify({ resultId: 1 })));
    assert.ok(indexes.some(i => i.unique && JSON.stringify(i.key) === JSON.stringify({ raidId: 1, accountId: 1 })));
    assert.ok(indexes.some(i => i.key.createdAt === 1 && i.expireAfterSeconds === 86400));
  });
});
