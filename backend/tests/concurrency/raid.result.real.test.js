'use strict';

const { describe, test, before, after, mock } = require('node:test');
const assert = require('node:assert/strict');
const mongoose = require('mongoose');
const crypto = require('node:crypto');
const env = require('../../src/config/env');
const { buildCanonicalResultString } = require('../../src/utils/raidResultCanonicalizer');
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
    return await Promise.allSettled(payloads.map((payload, index) => RaidResultService.publishHostResult(Array.isArray(accountId) ? accountId[index] : accountId, payload)));
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

  for (const mismatch of ['account', 'raid']) {
    for (const concurrent of [false, true]) {
      test(`reused resultId rejects a different ${mismatch} (${concurrent ? 'race' : 'sequential'})`, async () => {
        const raidId = `raid-identity-${mismatch}-${concurrent}`;
        const first = await raider(raidId);
        const second = mismatch === 'account' ? await raider(raidId) : first;
        const accounts = [first.accountId, second.accountId];
        const requests = [payload(raidId), payload(mismatch === 'raid' ? `${raidId}-other` : raidId, `${raidId}:1`)]
          .map((request, index) => {
            const signed = { ...request, targetAccountId: accounts[index] };
            signed.hostSignature = crypto.createHmac('sha256', env.webhookSecret)
              .update(buildCanonicalResultString({ ...signed, preparedEquipment: {}, issuedAt: '' })).digest('hex');
            return signed;
          });
        const charactersBefore = await Character.find({ accountId: { $in: accounts } }).lean();
        let results;
        let storedBefore;
        if (concurrent) {
          results = await race(accounts, requests);
        } else {
          const value = await RaidResultService.publishHostResult(accounts[0], requests[0]);
          storedBefore = await Result.find({ resultId: requests[0].resultId }).lean();
          results = [{ status: 'fulfilled', value }, ...(await Promise.allSettled([
            RaidResultService.publishHostResult(accounts[1], requests[1])
          ]))];
        }
        assert.equal(results.filter(r => r.status === 'fulfilled').length, 1);
        const rejected = results.filter(r => r.status === 'rejected');
        assert.equal(rejected.length, 1);
        assert.deepEqual(rejected[0].reason, { statusCode: 409, errorCode: 'RESULT_CONFLICT',
          message: 'An authoritative extraction result already exists with conflicting rewards.' });
        const winnerIndex = results.findIndex(r => r.status === 'fulfilled');
        const winnerRequest = requests[winnerIndex];
        assert.deepEqual(results[winnerIndex].value, { status: 'published', alreadyPublished: false,
          raidId: winnerRequest.raidId, accountId: accounts[winnerIndex], resultId: winnerRequest.resultId });
        const stored = await Result.find({ resultId: winnerRequest.resultId }).lean();
        assert.equal(stored.length, 1);
        assert.equal(stored[0].raidId, winnerRequest.raidId);
        assert.equal(stored[0].accountId, accounts[winnerIndex]);
        assert.equal(stored[0].consumed, false);
        assert.equal(await Result.countDocuments({ raidId: requests[1 - winnerIndex].raidId,
          accountId: accounts[1 - winnerIndex] }), 0);
        if (storedBefore) assert.deepEqual(stored, storedBefore);
        assert.deepEqual(await Character.find({ accountId: { $in: accounts } }).lean(), charactersBefore);
      });
    }
  }

  test('a winner consumed before the loser re-reads returns RESULT_ALREADY_CONSUMED (S4)', async () => {
    const raidId = 'raid-consumed-race';
    const { accountId } = await raider(raidId);
    const before = await Character.findOne({ accountId }).lean();
    const findOne = Result.findOne.bind(Result);
    let arrivals = 0;
    let release;
    const gate = new Promise(resolve => { release = resolve; });
    const stub = mock.method(Result, 'findOne', async (...args) => {
      const arrival = ++arrivals;
      if (arrival <= 2) {
        const result = await findOne(...args);
        if (arrival === 2) release();
        await gate;
        return result;
      }
      await Result.updateMany({ raidId }, { $set: { consumed: true } });
      return findOne(...args);
    });
    let results;
    try {
      results = await Promise.allSettled([0, 1].map(() => RaidResultService.publishHostResult(accountId, payload(raidId))));
    } finally {
      stub.mock.restore();
    }
    assert.equal(await Result.countDocuments({ raidId }), 1);
    assert.equal(results.filter(r => r.status === 'fulfilled').length, 1);
    assert.equal(results.find(r => r.status === 'fulfilled').value.alreadyPublished, false);
    assert.deepEqual(results.find(r => r.status === 'rejected').reason,
      { statusCode: 409, errorCode: 'RESULT_ALREADY_CONSUMED',
        message: 'This authoritative extraction result has already been consumed and cannot be overwritten.' });
    assert.deepEqual(await Character.findOne({ accountId }).lean(), before);
  });

  test('many ungated identical publications leave one document and one first publication (S4)', async () => {
    const raidId = 'raid-burst';
    const { accountId } = await raider(raidId);
    const before = await Character.findOne({ accountId }).lean();
    const results = await Promise.all(Array.from({ length: 8 }, () => RaidResultService.publishHostResult(accountId, payload(raidId))));
    assert.equal(await Result.countDocuments({ raidId, accountId }), 1);
    assert.equal(results.filter(r => r.alreadyPublished === false).length, 1);
    for (const result of results) assert.deepEqual(result, {
      status: 'published', alreadyPublished: result.alreadyPublished, raidId, accountId, resultId: `${raidId}:1`
    });
    assert.deepEqual(await Character.findOne({ accountId }).lean(), before);
  });

  test('unique indexes coexist with the unchanged 24-hour TTL index', async () => {
    const indexes = await Result.collection.indexes();
    assert.ok(indexes.some(i => i.unique && JSON.stringify(i.key) === JSON.stringify({ resultId: 1 })));
    assert.ok(indexes.some(i => i.unique && JSON.stringify(i.key) === JSON.stringify({ raidId: 1, accountId: 1 })));
    assert.ok(indexes.some(i => i.key.createdAt === 1 && i.expireAfterSeconds === 86400));
  });
});
