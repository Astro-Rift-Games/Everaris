const test = require('node:test');
const assert = require('node:assert');
const mongoose = require('mongoose');
const { startMemoryMongo } = require('../support/memoryMongo');

const Character = require('../../src/models/Character');
const AuthoritativeExtractionResult = require('../../src/models/AuthoritativeExtractionResult');
const ProgressionService = require('../../src/services/ProgressionService');
const ExtractionCommitService = require('../../src/services/ExtractionCommitService');

let mongoServer;

test('ProgressionService - Concurrency with Real MongoDB', async (t) => {
  t.before(async () => {
    mongoServer = await startMemoryMongo();
    const uri = mongoServer.getUri();
    await mongoose.connect(uri);
  });

  t.after(async () => {
    await mongoose.disconnect();
    await mongoServer.stop();
  });

  t.afterEach(async () => {
    await Character.deleteMany({});
    await AuthoritativeExtractionResult.deleteMany({});
  });

  await t.test('Promise.allSettled: Two simultaneous commitProgression calls, exact one wins', async () => {
    const accountId = new mongoose.Types.ObjectId();
    const char = new Character({
      accountId: accountId,
      name: 'SimulChar',
      revision: 0,
      characterAttributes: { availablePoints: 5, vitality: 5 }
    });
    await char.save();

    const expectedRevision = 0;

    const results = await Promise.allSettled([
      ProgressionService.commitProgression(accountId.toString(), { attribute: 'vitality', expectedRevision }),
      ProgressionService.commitProgression(accountId.toString(), { attribute: 'vitality', expectedRevision })
    ]);

    const fulfilled = results.filter(r => r.status === 'fulfilled');
    const rejected = results.filter(r => r.status === 'rejected');

    assert.strictEqual(fulfilled.length, 1, 'Exactly one should succeed');
    assert.strictEqual(rejected.length, 1, 'Exactly one should fail');
    
    assert.strictEqual(fulfilled[0].value.revision, 1);
    assert.strictEqual(rejected[0].reason.statusCode, 409);
    assert.strictEqual(rejected[0].reason.errorCode, 'REVISION_CONFLICT');

    const dbChar = await Character.findOne({ accountId });
    assert.strictEqual(dbChar.revision, 1);
    assert.strictEqual(dbChar.characterAttributes.vitality, 6);
    assert.strictEqual(dbChar.characterAttributes.availablePoints, 4);
  });

  const seedRespecCharacter = async (name) => {
    const { createEquipment } = require('../../src/services/ItemInstances');
    const { EQUIPMENT_SLOTS } = require('../../src/config/equipmentSlots');
    const longSword = createEquipment('long_sword');
    const accountId = new mongoose.Types.ObjectId();
    await new Character({
      accountId,
      name,
      revision: 1,
      characterAttributes: { vitality: 5, resistance: 5, strength: 12, dexterity: 10, intelligence: 5, luck: 5, availablePoints: 0 },
      inventory: {
        inventorySchemaVersion: 3,
        currency: 300,
        stash: [],
        loadout: [],
        preparedEquipment: { ...Object.fromEntries(EQUIPMENT_SLOTS.map(slot => [slot, null])), weaponSlot1: longSword }
      }
    }).save();
    return accountId.toString();
  };

  await t.test('Concurrent respec with the same transactionId debits gold once and moves the weapon once', async () => {
    const accountId = await seedRespecCharacter('RespecSame');
    const request = { transactionId: 'tx-same', expectedRevision: 1 };

    const results = await Promise.allSettled([
      ProgressionService.respec(accountId, request),
      ProgressionService.respec(accountId, request)
    ]);

    assert.deepStrictEqual(results.map(r => r.status), ['fulfilled', 'fulfilled'], JSON.stringify(results));
    assert.deepStrictEqual(results.map(r => r.value.alreadySecured).sort(), [false, true]);
    for (const { value } of results) {
      assert.strictEqual(value.currency, 200);
      assert.strictEqual(value.preparedEquipment.weaponSlot1, null);
      assert.strictEqual(value.loadout.length, 1);
    }

    const dbChar = await Character.findOne({ accountId }).lean();
    assert.strictEqual(dbChar.inventory.currency, 200, 'gold debited once');
    assert.strictEqual(dbChar.inventory.loadout.length, 1, 'weapon moved once');
    assert.strictEqual(dbChar.inventory.loadout[0].lootId, 'long_sword');
    assert.strictEqual(dbChar.inventory.preparedEquipment.weaponSlot1, null);
    assert.strictEqual(dbChar.revision, 2);
    assert.strictEqual(dbChar.appliedShopReceipts.filter(r => r.transactionId === 'tx-same').length, 1);
  });

  await t.test('Concurrent respec with different transactionIds on one revision applies exactly one', async () => {
    const accountId = await seedRespecCharacter('RespecDifferent');

    const results = await Promise.allSettled([
      ProgressionService.respec(accountId, { transactionId: 'tx-a', expectedRevision: 1 }),
      ProgressionService.respec(accountId, { transactionId: 'tx-b', expectedRevision: 1 })
    ]);

    const fulfilled = results.filter(r => r.status === 'fulfilled');
    const rejected = results.filter(r => r.status === 'rejected');
    assert.strictEqual(fulfilled.length, 1, JSON.stringify(results));
    assert.strictEqual(rejected.length, 1);
    assert.strictEqual(fulfilled[0].value.alreadySecured, false);
    assert.strictEqual(rejected[0].reason.statusCode, 409);
    assert.strictEqual(rejected[0].reason.errorCode, 'REVISION_CONFLICT');

    const dbChar = await Character.findOne({ accountId }).lean();
    assert.strictEqual(dbChar.inventory.currency, 200, 'gold debited once');
    assert.strictEqual(dbChar.inventory.loadout.length, 1, 'weapon moved once');
    assert.strictEqual(dbChar.revision, 2);
    assert.strictEqual(dbChar.appliedShopReceipts.length, 1);
  });

  await t.test('Concurrent commitProgression during extraction fails the extraction and preserves the spent point', async () => {
    const accountId = new mongoose.Types.ObjectId();
    const char = new Character({
      accountId: accountId,
      name: 'ExtChar',
      revision: 0,
      level: 1,
      experience: 0,
      characterAttributes: { availablePoints: 1, vitality: 5 },
      inventory: { stash: [], loadout: [], preparedEquipment: {} }
    });
    await char.save();

    await AuthoritativeExtractionResult.create({
      raidId: 'raid_999',
      accountId: accountId,
      items: [],
      experienceGranted: 1000 // Grants level up
    });

    const originalFindOneAndUpdate = Character.findOneAndUpdate;
    let updateCalled = false;
    
    Character.findOneAndUpdate = async function(filter, update, options) {
      if (!updateCalled && update.$push && update.$push['appliedProgressionReceipts']) {
        updateCalled = true;
        await ProgressionService.commitProgression(accountId.toString(), { attribute: 'vitality', expectedRevision: 0 });
      }
      return originalFindOneAndUpdate.call(this, filter, update, options);
    };

    try {
      await assert.rejects(
        () => ExtractionCommitService.commit(accountId.toString(), { raidId: 'raid_999', resultSequence: 1 }),
        err => {
          assert.strictEqual(err.statusCode, 409);
          assert.strictEqual(err.errorCode, 'REVISION_CONFLICT');
          return true;
        }
      );
    } finally {
      Character.findOneAndUpdate = originalFindOneAndUpdate;
    }

    const dbChar = await Character.findOne({ accountId });
    assert.strictEqual(dbChar.revision, 1);
    assert.strictEqual(dbChar.characterAttributes.availablePoints, 0);
    assert.strictEqual(dbChar.characterAttributes.vitality, 6);
    assert.strictEqual(dbChar.level, 1);
  });
});
