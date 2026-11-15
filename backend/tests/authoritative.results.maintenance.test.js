'use strict';

const { describe, test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const mongoose = require('mongoose');
const { startMemoryMongo } = require('./support/memoryMongo');
const Result = require('../src/models/AuthoritativeExtractionResult');
const { dedupeAuthoritativeResults, parseArgs } = require('../scripts/dedupe_authoritative_results');
const { checkAuthoritativeResultIndexes } = require('../src/db/authoritativeResultIndexes');

describe('FIX-22 result maintenance', () => {
  let mongo;
  before(async () => {
    mongo = await startMemoryMongo();
    await mongoose.connect(mongo.getUri(), { autoIndex: false });
    await Result.createCollection();
  });
  after(async () => {
    await mongoose.disconnect();
    if (mongo) await mongo.stop();
  });

  test('dedupe defaults to dry-run; apply retains consumed, otherwise oldest, across both keys', async () => {
    const collection = Result.collection;
    const date = new Date();
    const rows = [
      { _id: new mongoose.Types.ObjectId(), resultId: 'same-result', raidId: 'a', accountId: 'a', createdAt: new Date(+date - 1000) },
      { _id: new mongoose.Types.ObjectId(), resultId: 'same-result', raidId: 'b', accountId: 'b', consumed: true, createdAt: date },
      { _id: new mongoose.Types.ObjectId(), resultId: 'oldest', raidId: 'c', accountId: 'c', createdAt: new Date(+date - 1000) },
      { _id: new mongoose.Types.ObjectId(), resultId: 'newer', raidId: 'c', accountId: 'c', createdAt: date },
      // Overlapping duplicate keys must be resolved together, not double-counted.
      { _id: new mongoose.Types.ObjectId(), resultId: 'newer', raidId: 'b', accountId: 'b', createdAt: date },
      { _id: new mongoose.Types.ObjectId(), resultId: 'unique', raidId: 'd', accountId: 'd', createdAt: date }
    ];
    await collection.insertMany(rows);
    const other = mongoose.connection.collection('maintenance_sentinel');
    await other.insertOne({ unchanged: true });
    const before = await collection.find({}).toArray();
    const sentinel = await other.find({}).toArray();
    const output = [];
    const dry = await dedupeAuthoritativeResults(collection, { log: line => output.push(JSON.parse(line)) });
    assert.deepEqual(dry, { dryRun: true, scanned: 6, duplicateResultIds: 2, duplicateRaidAccounts: 2,
      kept: 3, plannedDeletes: 3, deleted: 0 });
    assert.deepEqual(output, [dry]);
    assert.deepEqual(await collection.find({}).toArray(), before);
    const applied = await dedupeAuthoritativeResults(collection, { apply: true, log: () => {} });
    assert.deepEqual(applied, { ...dry, dryRun: false, deleted: 3 });
    assert.deepEqual((await collection.find({}).toArray()).map(row => String(row._id)).sort(),
      [rows[1], rows[2], rows[5]].map(row => String(row._id)).sort());
    assert.deepEqual(await other.find({}).toArray(), sentinel);
    const rerun = await dedupeAuthoritativeResults(collection, { apply: true, log: () => {} });
    assert.equal(rerun.plannedDeletes, 0);
    assert.equal(rerun.deleted, 0);
  });

  test('CLI requires explicit --apply, supports help and rejects unknown arguments', () => {
    assert.deepEqual(parseArgs([]), { apply: false, help: false });
    assert.deepEqual(parseArgs(['--dry-run']), { apply: false, help: false });
    assert.deepEqual(parseArgs(['--apply']), { apply: true, help: false });
    assert.deepEqual(parseArgs(['--help']), { apply: false, help: true });
    assert.throws(() => parseArgs(['--apply', '--dry-run']), /Cannot combine/);
    assert.throws(() => parseArgs(['--typo']), /Unknown argument/);
  });

  test('startup logs missing unique indexes after a failed build, without throwing; healthy indexes include TTL', async () => {
    const errors = [];
    const logger = { error: message => errors.push(message) };
    // Same-name legacy non-unique index blocks the model's new unique index build.
    await Result.collection.createIndex({ resultId: 1 });
    assert.equal(await checkAuthoritativeResultIndexes(Result, logger), false);
    assert.equal(errors.length, 1);
    assert.match(errors[0], /AuthoritativeExtractionResult.*missing unique indexes.*resultId/);
    assert.ok((await Result.collection.indexes()).some(i => i.unique && i.key.raidId === 1 && i.key.accountId === 1));
    assert.match(errors[0], /dedupe_authoritative_results\.js/);
    await Result.collection.dropIndex('resultId_1');
    await Result.createIndexes();
    errors.length = 0;
    assert.equal(await checkAuthoritativeResultIndexes(Result, logger), true);
    assert.deepEqual(errors, []);
    assert.ok((await Result.collection.indexes()).some(i => i.key.createdAt === 1 && i.expireAfterSeconds === 86400));
  });
});
