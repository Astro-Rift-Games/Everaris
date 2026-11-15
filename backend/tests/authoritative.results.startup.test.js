'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const mongoose = require('mongoose');
const { startMemoryMongo } = require('./support/memoryMongo');

// Separate process/model initialization: maintenance tests deliberately disable autoIndex.
test('production autoIndex startup tolerates a legacy non-unique resultId index', async () => {
  let mongo;
  try {
    mongo = await startMemoryMongo();
    await mongoose.connect(mongo.getUri(), { autoIndex: true });
    const collection = mongoose.connection.collection('authoritativeextractionresults');
    await collection.createIndex({ resultId: 1 });
    // Load only after the legacy index exists, so automatic initialization sees it.
    const Result = require('../src/models/AuthoritativeExtractionResult');
    const { checkAuthoritativeResultIndexes } = require('../src/db/authoritativeResultIndexes');
    await Result.init().catch(() => {});
    const errors = [];
    assert.equal(await checkAuthoritativeResultIndexes(Result, { error: message => errors.push(message) }), false);
    assert.equal(errors.length, 1);
    assert.match(errors[0], /missing unique indexes.*resultId/);
    const indexes = await Result.collection.indexes();
    assert.notEqual(indexes.find(index => index.name === 'resultId_1').unique, true);
    assert.equal(indexes.find(index => index.name === 'raidId_1_accountId_1').unique, true);
  } finally {
    await mongoose.disconnect();
    if (mongo) await mongo.stop();
  }
});
