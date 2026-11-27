'use strict';

const { describe, test, before, after, beforeEach } = require('node:test');
const assert = require('node:assert/strict');
const mongoose = require('mongoose');
const { startMemoryMongo } = require('./support/memoryMongo');
const Result = require('../src/models/AuthoritativeExtractionResult');
const Character = require('../src/models/Character');
const ExtractionCommitService = require('../src/services/ExtractionCommitService');
const { checkAuthoritativeResultIndexes } = require('../src/db/authoritativeResultIndexes');
const {
  CONSUMED_RESULT_TTL_SECONDS,
  migrateAuthoritativeResultTtl,
  parseArgs
} = require('../scripts/migrate_authoritative_result_ttl');

describe('FIX-21 authoritative extraction result retention', () => {
  let mongo;

  before(async () => {
    mongo = await startMemoryMongo();
    await mongoose.connect(mongo.getUri(), { autoIndex: false });
    await Result.createCollection();
    await Character.createCollection();
    await Result.createIndexes();
  });

  after(async () => {
    await mongoose.disconnect();
    if (mongo) await mongo.stop();
  });

  beforeEach(async () => {
    await Result.deleteMany({});
    await Character.deleteMany({});
  });

  test('schema defines consumed-only partial TTL on consumedAt and NO unconditional TTL on createdAt', async () => {
    const indexes = await Result.collection.indexes();

    const unconditionalCreatedAtTtl = indexes.find(
      i => i.key && i.key.createdAt === 1 && typeof i.expireAfterSeconds === 'number'
    );
    assert.equal(unconditionalCreatedAtTtl, undefined, 'createdAt must NOT have an unconditional TTL index');

    const consumedTtl = indexes.find(
      i => i.key && i.key.consumedAt === 1 && i.expireAfterSeconds === CONSUMED_RESULT_TTL_SECONDS
    );
    assert.ok(consumedTtl, 'Expected TTL index on consumedAt');
    assert.deepEqual(consumedTtl.partialFilterExpression, { consumed: true });
  });

  test('unconsumed extraction result older than 24 hours remains available and commits cleanly', async () => {
    const accountId = new mongoose.Types.ObjectId().toString();
    const raidId = 'raid-old-unconsumed-72h';
    const reservationId = 'res-old-72h';
    const seventyTwoHoursAgo = new Date(Date.now() - 72 * 60 * 60 * 1000);

    await Character.create({
      accountId,
      name: 'DelayedRaider',
      revision: 1,
      inventory: {
        inventorySchemaVersion: 2,
        stash: [],
        loadout: [],
        preparedEquipment: { weaponSlot1: '', weaponSlot2: '', helmet: '', armor: '', gloves: '', boots: '' },
        pendingReservation: {
          reservationId,
          raidId,
          items: [],
          preparedEquipment: { weaponSlot1: 'arming_sword', weaponSlot2: '', helmet: '', armor: '', gloves: '', boots: '' },
          admittedAt: seventyTwoHoursAgo
        },
        appliedExtractionReceipts: []
      }
    });

    await Result.create({
      raidId,
      accountId,
      reservationId,
      resultSequence: 1,
      items: [{ lootId: 'CON_HEALTH_POTION', amount: 2 }],
      preparedEquipment: { weaponSlot1: 'arming_sword', weaponSlot2: '', helmet: '', armor: '', gloves: '', boots: '' },
      experienceGranted: 150,
      consumed: false,
      consumedAt: null,
      createdAt: seventyTwoHoursAgo
    });

    const committed = await ExtractionCommitService.commit(accountId, { raidId, resultSequence: 1 });
    assert.equal(committed.alreadySecured, false);
    assert.ok(committed.loadout.some(entry => entry.lootId === 'CON_HEALTH_POTION' && entry.amount === 2));

    const stored = await Result.findOne({ raidId, accountId }).lean();
    assert.equal(stored.consumed, true);
    assert.ok(stored.consumedAt instanceof Date, 'Consumed result must record consumedAt for TTL expiration');
  });

  test('migration script dry-run inspects without mutating, and --apply replaces legacy createdAt TTL and backfills consumedAt', async () => {
    const col = Result.collection;
    // Simulate legacy production state: drop consumedAt partial TTL, restore old unconditional createdAt TTL.
    const existing = await col.indexes();
    const partial = existing.find(i => i.key && i.key.consumedAt === 1);
    if (partial) await col.dropIndex(partial.name);
    await col.createIndex({ createdAt: 1 }, { expireAfterSeconds: 86400 });

    const createdLegacy = new Date(Date.now() - 10 * 60 * 60 * 1000);
    await col.insertMany([
      {
        resultId: 'legacy-consumed-missing-date',
        raidId: 'r-consumed',
        accountId: 'acc-1',
        consumed: true,
        consumedAt: null,
        createdAt: createdLegacy
      },
      {
        resultId: 'legacy-unconsumed',
        raidId: 'r-unconsumed',
        accountId: 'acc-2',
        consumed: false,
        consumedAt: null,
        createdAt: createdLegacy
      }
    ]);

    // Startup check warns when legacy unconditional createdAt TTL is still present.
    const errors = [];
    const healthyBefore = await checkAuthoritativeResultIndexes(Result, { error: msg => errors.push(msg) });
    assert.equal(healthyBefore, false);
    assert.ok(errors.some(msg => /migrate_authoritative_result_ttl\.js/.test(msg)));

    // Drop consumedAt index again if checkAuthoritativeResultIndexes created it, so migration is tested from a true legacy DB.
    const afterCheckIndexes = await col.indexes();
    const recreatedPartial = afterCheckIndexes.find(i => i.key && i.key.consumedAt === 1);
    if (recreatedPartial) await col.dropIndex(recreatedPartial.name);

    // Dry-run reports planned changes without mutating DB or indexes.
    const drySummary = await migrateAuthoritativeResultTtl(col, { apply: false, log: () => {} });
    assert.equal(drySummary.dryRun, true);
    assert.equal(drySummary.legacyCreatedAtTtlFound, true);
    assert.equal(drySummary.legacyCreatedAtTtlDropped, false);
    assert.equal(drySummary.consumedAtTtlCreated, false);
    assert.equal(drySummary.consumedMissingConsumedAt, 1);
    assert.equal(drySummary.backfilledConsumedAt, 0);

    // Apply drops legacy TTL, creates consumed-only partial TTL, backfills only consumed rows.
    const applySummary = await migrateAuthoritativeResultTtl(col, { apply: true, log: () => {} });
    assert.equal(applySummary.dryRun, false);
    assert.equal(applySummary.legacyCreatedAtTtlDropped, true);
    assert.equal(applySummary.consumedAtTtlCreated, true);
    assert.equal(applySummary.backfilledConsumedAt, 1);

    const consumedDoc = await col.findOne({ resultId: 'legacy-consumed-missing-date' });
    const unconsumedDoc = await col.findOne({ resultId: 'legacy-unconsumed' });
    assert.ok(consumedDoc.consumedAt instanceof Date);
    assert.equal(unconsumedDoc.consumedAt, null);

    // Startup check is now healthy and re-running --apply is an idempotent no-op.
    const afterErrors = [];
    assert.equal(await checkAuthoritativeResultIndexes(Result, { error: msg => afterErrors.push(msg) }), true);
    assert.deepEqual(afterErrors, []);

    const rerun = await migrateAuthoritativeResultTtl(col, { apply: true, log: () => {} });
    assert.equal(rerun.legacyCreatedAtTtlDropped, false);
    assert.equal(rerun.consumedAtTtlCreated, false);
    assert.equal(rerun.backfilledConsumedAt, 0);
  });

  test('migration CLI parser requires explicit --apply and rejects invalid flags', () => {
    assert.deepEqual(parseArgs([]), { apply: false, help: false });
    assert.deepEqual(parseArgs(['--dry-run']), { apply: false, help: false });
    assert.deepEqual(parseArgs(['--apply']), { apply: true, help: false });
    assert.deepEqual(parseArgs(['--help']), { apply: false, help: true });
    assert.throws(() => parseArgs(['--apply', '--dry-run']), /Cannot combine/);
    assert.throws(() => parseArgs(['--unknown']), /Unknown argument/);
  });
});
