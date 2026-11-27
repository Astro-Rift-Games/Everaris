'use strict';

const { test, describe, before, after, beforeEach } = require('node:test');
const assert = require('node:assert/strict');
const mongoose = require('mongoose');
const { startMemoryMongo } = require('./support/memoryMongo');
const Account = require('../src/models/Account');
const Character = require('../src/models/Character');
const ProgressionService = require('../src/services/ProgressionService');

describe('FIX-36: idempotent attribute point spend reconciliation', () => {
  let mongoServer;
  let accountId;

  before(async () => {
    mongoServer = await startMemoryMongo();
    await mongoose.connect(mongoServer.getUri());
  });

  after(async () => {
    await mongoose.disconnect();
    await mongoServer.stop();
  });

  beforeEach(async () => {
    await Account.deleteMany({});
    await Character.deleteMany({});
    const account = await Account.create({ username: 'fix36_hero', passwordHash: 'hash' });
    accountId = account._id.toString();
    await Character.create({
      accountId: account._id,
      name: 'Fix36Hero',
      level: 2,
      experience: 100,
      revision: 3,
      characterAttributes: {
        vitality: 5, resistance: 5, strength: 5, dexterity: 5, intelligence: 5, luck: 5,
        availablePoints: 4
      }
    });
  });

  test('replaying the same transactionId after a lost response returns alreadySecured without double spend', async () => {
    const txId = `attr-${Date.now()}-001`;
    const first = await ProgressionService.commitProgression(accountId, {
      attribute: 'strength',
      expectedRevision: 3,
      transactionId: txId
    });

    assert.equal(first.alreadySecured, false);
    assert.equal(first.transactionId, txId);
    assert.equal(first.revision, 4);
    assert.equal(first.characterAttributes.strength, 6);
    assert.equal(first.characterAttributes.availablePoints, 3);

    // Client never received the HTTP 200 and retries with the SAME transactionId and old expectedRevision=3.
    const replay = await ProgressionService.commitProgression(accountId, {
      attribute: 'strength',
      expectedRevision: 3,
      transactionId: txId
    });

    assert.equal(replay.alreadySecured, true);
    assert.equal(replay.transactionId, txId);
    assert.equal(replay.revision, 4);
    assert.equal(replay.characterAttributes.strength, 6);
    assert.equal(replay.characterAttributes.availablePoints, 3);

    const stored = await Character.findOne({ accountId }).lean();
    assert.equal(stored.revision, 4);
    assert.equal(stored.characterAttributes.strength, 6);
    assert.equal(stored.characterAttributes.availablePoints, 3);
    assert.equal(stored.appliedShopReceipts.filter(r => r.transactionId === txId).length, 1);
  });

  test('reusing a transactionId for a different attribute rejects with 409 TRANSACTION_ID_REUSED', async () => {
    const txId = `attr-${Date.now()}-002`;
    await ProgressionService.commitProgression(accountId, {
      attribute: 'vitality',
      expectedRevision: 3,
      transactionId: txId
    });

    await assert.rejects(
      () => ProgressionService.commitProgression(accountId, {
        attribute: 'strength',
        expectedRevision: 3,
        transactionId: txId
      }),
      err => err.statusCode === 409 && err.errorCode === 'TRANSACTION_ID_REUSED'
    );

    const stored = await Character.findOne({ accountId }).lean();
    assert.equal(stored.revision, 4);
    assert.equal(stored.characterAttributes.vitality, 6);
    assert.equal(stored.characterAttributes.strength, 5);
    assert.equal(stored.characterAttributes.availablePoints, 3);
  });

  test('concurrent duplicate retries with the same transactionId spend the point exactly once', async () => {
    const txId = `attr-${Date.now()}-003`;
    const [a, b] = await Promise.all([
      ProgressionService.commitProgression(accountId, { attribute: 'dexterity', expectedRevision: 3, transactionId: txId }),
      ProgressionService.commitProgression(accountId, { attribute: 'dexterity', expectedRevision: 3, transactionId: txId })
    ]);

    assert.equal(a.revision, 4);
    assert.equal(b.revision, 4);
    assert.equal(a.characterAttributes.dexterity, 6);
    assert.equal(b.characterAttributes.dexterity, 6);
    assert.equal([a.alreadySecured, b.alreadySecured].filter(Boolean).length, 1);
  });
});
