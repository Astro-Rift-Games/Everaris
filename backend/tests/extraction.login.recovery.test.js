'use strict';

const { describe, it, before, after, beforeEach } = require('node:test');
const assert = require('node:assert/strict');
const mongoose = require('mongoose');
const { MongoMemoryServer } = require('mongodb-memory-server');

const Account = require('../src/models/Account');
const Character = require('../src/models/Character');
const AuthoritativeExtractionResult = require('../src/models/AuthoritativeExtractionResult');
const InventoryService = require('../src/services/InventoryService');
const ExtractionCommitService = require('../src/services/ExtractionCommitService');

describe('FIX-09: Pending authoritative extraction recovery on login after pre-ACK disconnect', () => {
  let mongod;

  before(async () => {
    mongod = await MongoMemoryServer.create();
    if (mongoose.connection.readyState !== 0) {
      await mongoose.disconnect();
    }
    await mongoose.connect(mongod.getUri());
  });

  after(async () => {
    await mongoose.disconnect();
    await mongod.stop();
  });

  beforeEach(async () => {
    await Promise.all([
      Account.deleteMany({}),
      Character.deleteMany({}),
      AuthoritativeExtractionResult.deleteMany({})
    ]);
  });

  it('exposes pendingAuthoritativeExtraction in getInventory and credits loot exactly once on recovery commit', async () => {
    const account = await Account.create({ username: 'fix09player', passwordHash: 'hash' });
    const accountId = account._id.toString();

    await Character.create({
      accountId,
      name: 'Fix09Hero',
      level: 1,
      experience: 0,
      revision: 3,
      inventory: {
        inventorySchemaVersion: 2,
        currency: 100,
        stash: [],
        loadout: [],
        preparedEquipment: {},
        pendingReservation: {
          reservationId: 'res-fix09-1',
          raidId: 'RAID-FIX09',
          admittedAt: new Date(),
          items: [],
          preparedEquipment: {}
        },
        appliedExtractionReceipts: []
      }
    });

    // Host published authoritative extraction result, then Client disconnected BEFORE receiving ACK RPC.
    await AuthoritativeExtractionResult.create({
      resultId: 'RAID-FIX09:char:1',
      raidId: 'RAID-FIX09',
      accountId,
      reservationId: 'res-fix09-1',
      resultSequence: 1,
      items: [{ lootId: 'loot_iron_ore', amount: 4 }],
      preparedEquipment: {},
      experienceGranted: 120,
      consumed: false
    });

    // 1. Login hydration sees pendingAuthoritativeExtraction so client does NOT clear as loss.
    const beforeRecovery = await InventoryService.getInventory(accountId);
    assert.deepEqual(beforeRecovery.pendingAuthoritativeExtraction, {
      raidId: 'RAID-FIX09',
      resultSequence: 1
    });

    // 2. Client recovers by committing with only { raidId, resultSequence } (no local PendingExtractionCommit needed).
    const firstCommit = await ExtractionCommitService.commit(accountId, {
      raidId: 'RAID-FIX09',
      resultSequence: 1
    });
    assert.equal(firstCommit.alreadySecured, false);
    assert.equal(firstCommit.loadout.length, 1);
    assert.equal(firstCommit.loadout[0].lootId, 'loot_iron_ore');
    assert.equal(firstCommit.loadout[0].amount, 4);

    // 3. Subsequent login sees NO pendingAuthoritativeExtraction and cleared pendingReservation.
    const afterRecovery = await InventoryService.getInventory(accountId);
    assert.equal(afterRecovery.pendingAuthoritativeExtraction, undefined);
    assert.equal(afterRecovery.pendingReservation, null);
    assert.equal(afterRecovery.loadout.length, 1);
    assert.equal(afterRecovery.loadout[0].amount, 4);

    // 4. Replaying the recovery commit is idempotent (no duplication).
    const replay = await ExtractionCommitService.commit(accountId, {
      raidId: 'RAID-FIX09',
      resultSequence: 1
    });
    assert.equal(replay.alreadySecured, true);
    assert.equal(replay.loadout.length, 1);
    assert.equal(replay.loadout[0].amount, 4);
  });
});
