'use strict';
// Dev grant script (odd/tasks/dev-grant-tools.md S2-S10): writes test items/gold to a local Mongo character.
// Run: node --test tools/dev-grant/grant.test.js
const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');

const backendDir = path.resolve(__dirname, '../../backend');
// Synthetic env so backend modules load without reading any real env file.
Object.assign(process.env, { NODE_ENV: 'test', PORT: '3999', MONGODB_URI: 'mongodb://127.0.0.1:27017/unused',
  JWT_SECRET: 'dev-grant-test-secret-not-used-anywhere-0123456789' });

const mongoose = require(require.resolve('mongoose', { paths: [backendDir] }));
const { startMemoryMongo } = require(path.join(backendDir, 'tests/support/memoryMongo'));
const Character = require(path.join(backendDir, 'src/models/Character'));
const Account = require(path.join(backendDir, 'src/models/Account'));
const { planUpgrade } = require(path.join(backendDir, 'src/services/ItemUpgrades'));
const { EQUIPMENT_SLOTS } = require(path.join(backendDir, 'src/config/equipmentSlots'));
const { isLocalMongoUri, parseArgs, grant } = require('./grant');

const MATERIALS = ['RES_RUNE_FRAGMENT', 'RES_ETHER_DUST', 'RES_ATTUNEMENT_GEM', 'RES_BLACK_IRON', 'RES_TANNED_LEATHER', 'RES_ANCIENT_ARMOR'];
const stack = (slot, lootId, amount) => ({ slot, lootId, amount });

test('S7: only local MongoDB URIs are accepted', () => {
  for (const uri of ['mongodb://localhost:27017/grimhold', 'mongodb://127.0.0.1/grimhold', 'mongodb://[::1]:27017/x',
    'mongodb://user:pass@localhost:27017/x?authSource=admin']) assert.equal(isLocalMongoUri(uri), true, uri);
  for (const uri of ['mongodb+srv://cluster.mongodb.net/x', 'mongodb://db.example.com:27017/x', 'mongodb://localhost.evil.com/x',
    'mongodb://localhost:27017,remote:27017/x', '', undefined, 'not a uri']) assert.equal(isLocalMongoUri(uri), false, String(uri));
});

test('parseArgs reads user, preset, repeated items and gold', () => {
  assert.deepEqual(parseArgs(['--user', 'tester_a', '--preset', 'upgrade-gear', '--item', 'RES_RUNE_FRAGMENT:5', '--item', 'rapier:2', '--gold', '500']),
    { username: 'tester_a', preset: 'upgrade-gear', items: [{ lootId: 'RES_RUNE_FRAGMENT', amount: 5 }, { lootId: 'rapier', amount: 2 }], gold: 500 });
  assert.throws(() => parseArgs(['--preset', 'upgrade-gear']), /--user/);
  assert.deepEqual(parseArgs(['--character', 'abcdefabcdefabcdefabcdef', '--gold', '1']),
    { characterId: 'abcdefabcdefabcdefabcdef', username: undefined, preset: undefined, items: [], gold: 1 });
  assert.throws(() => parseArgs(['--character', 'not-an-id', '--gold', '1']), /--character/);
  assert.throws(() => parseArgs(['--user', 'a', '--character', 'abcdefabcdefabcdefabcdef', '--gold', '1']), /either/);
  assert.throws(() => parseArgs(['--user', 'a', '--item', 'RES_RUNE_FRAGMENT']), /lootId:amount/);
  assert.throws(() => parseArgs(['--user', 'a', '--colour', 'red']), /Unknown option/);
});

test('grant against a real (in-memory) Mongo', async t => {
  const server = await startMemoryMongo();
  await mongoose.connect(server.getUri());
  t.after(async () => { await mongoose.connection.dropDatabase(); await mongoose.disconnect(); await server.stop(); });
  const account = await Account.create({ username: 'tester_a', passwordHash: 'x' });
  await Character.create({ accountId: account._id, name: 'Alpha' });
  const reset = async (loadout = []) => Character.updateOne({ accountId: account._id }, { $set: {
    revision: 4, 'inventory.inventorySchemaVersion': 3, 'inventory.currency': 100, 'inventory.pendingReservation': null,
    'inventory.loadout': loadout, 'inventory.stash': [],
    'inventory.preparedEquipment': Object.fromEntries(EQUIPMENT_SLOTS.map(slot => [slot, null])) } });
  const stored = async () => Character.findOne({ accountId: account._id }).lean();
  const unchanged = async (request, code) => {
    const before = await stored();
    await assert.rejects(grant(request), error => error.code === code);
    assert.deepEqual(await stored(), before);
  };

  await t.test('S3: upgrade-materials puts 20 of each material in the Inventory and bumps the revision', async () => {
    await reset();
    const result = await grant({ username: 'tester_a', preset: 'upgrade-materials' });
    assert.equal(result.revision, 5);
    const doc = await stored();
    for (const lootId of MATERIALS) assert.equal(doc.inventory.loadout.find(item => item.lootId === lootId)?.amount, 20, lootId);
    assert.equal(doc.inventory.stash.length, 0);
  });

  await t.test('S4: upgrade-gear issues instances in the requested states, usable by the upgrade rules', async () => {
    await reset();
    await grant({ username: 'tester_a', preset: 'upgrade-gear' });
    const loadout = (await stored()).inventory.loadout;
    assert.equal(loadout.length, 6);
    for (const item of loadout) assert.match(item.instance.instanceId, /^[0-9a-f]{24}$/);
    const rapiers = loadout.filter(item => item.lootId === 'rapier').map(item => item.instance.modifiers);
    assert.ok(rapiers.some(m => !m.primary && m.gemSocketCount === 0), 'rapier without scaling');
    assert.ok(rapiers.some(m => m.primary?.grade === 'C'), 'rapier at C');
    assert.ok(rapiers.some(m => m.gemSocketCount === 1 && m.gems[0] === null), 'rapier with one empty socket');
    assert.ok(loadout.some(item => item.lootId === 'magic_wand'));
    const armor = loadout.filter(item => item.lootId === 'heavy_armor_breastplate');
    assert.deepEqual(armor.map(item => item.instance.modifiers?.upgradeLevel ?? 0).sort(), [0, 3]);
    const plusThree = armor.find(item => item.instance.modifiers?.upgradeLevel === 3);
    assert.throws(() => planUpgrade({ merchant: 'blacksmith', operation: 'armor_level', lootId: plusThree.lootId, instance: plusThree.instance }),
      error => error.errorCode === 'UPGRADE_LIMIT_REACHED');
  });

  await t.test('S8: the Editor can target the character of the running session by id', async () => {
    await reset();
    const { _id } = await Character.findOne({ accountId: account._id });
    const result = await grant({ characterId: _id.toString(), gold: 7 });
    assert.equal(result.currency, 107);
    await unchanged({ characterId: 'ffffffffffffffffffffffff', gold: 1 }, 'CHARACTER_NOT_FOUND');
  });

  await t.test('S5/S6: gold and free items (stacking, alias, one instance per equipment unit)', async () => {
    await reset([stack(0, 'RES_RUNE_FRAGMENT', 3)]);
    const result = await grant({ username: 'tester_a', items: [{ lootId: 'RES_RUNE_FRAGMENT', amount: 7 }, { lootId: 'wand', amount: 2 }], gold: 5000 });
    assert.equal(result.currency, 5100);
    const loadout = (await stored()).inventory.loadout;
    assert.equal(loadout.find(item => item.lootId === 'RES_RUNE_FRAGMENT').amount, 10);
    assert.equal(loadout.filter(item => item.lootId === 'magic_wand').length, 2);
  });

  await t.test('S6/S10: invalid requests change nothing', async () => {
    await reset();
    await unchanged({ username: 'tester_a' }, 'VALIDATION_FAILED');
    await unchanged({ username: 'tester_a', preset: '__proto__' }, 'VALIDATION_FAILED');
    await unchanged({ username: 'tester_a', items: [{ lootId: 'NOT_A_REAL_ITEM', amount: 1 }] }, 'VALIDATION_FAILED');
    await unchanged({ username: 'tester_a', items: [{ lootId: 'RES_RUNE_FRAGMENT', amount: 1000 }] }, 'VALIDATION_FAILED');
    await unchanged({ username: 'tester_a', items: [{ lootId: 'RES_RUNE_FRAGMENT', amount: 0 }] }, 'VALIDATION_FAILED');
    await unchanged({ username: 'tester_a', gold: 100001 }, 'VALIDATION_FAILED');
    await unchanged({ username: 'tester_a', items: Array.from({ length: 51 }, () => ({ lootId: 'RES_ETHER_DUST', amount: 1 })) }, 'VALIDATION_FAILED');
    await unchanged({ username: 'nobody', gold: 1 }, 'ACCOUNT_NOT_FOUND');
  });

  await t.test('S9: a grant that does not fit fails whole with INVENTORY_FULL', async () => {
    // 27 of 30 slots used; the 6 gear instances need 6 free slots.
    await reset(Array.from({ length: 27 }, (_, slot) => stack(slot, ['RES_BLACK_IRON', 'RES_TANNED_LEATHER', 'RES_ANCIENT_ARMOR'][slot % 3], 1)));
    await unchanged({ username: 'tester_a', preset: 'upgrade-gear' }, 'INVENTORY_FULL');
  });

  await t.test('pending reservation, v2 inventory and gold overflow are refused without mutation', async () => {
    await reset();
    await Character.updateOne({ accountId: account._id }, { $set: { 'inventory.pendingReservation': { items: [], reservationId: 'r' } } });
    await unchanged({ username: 'tester_a', gold: 1 }, 'RESERVATION_PENDING');
    await reset();
    await Character.updateOne({ accountId: account._id }, { $set: { 'inventory.inventorySchemaVersion': 2 } });
    await unchanged({ username: 'tester_a', gold: 1 }, 'INVENTORY_MIGRATION_REQUIRED');
    await reset();
    await Character.updateOne({ accountId: account._id }, { $set: { 'inventory.currency': Number.MAX_SAFE_INTEGER } });
    await unchanged({ username: 'tester_a', gold: 1 }, 'INTEGER_OVERFLOW');
  });
});
