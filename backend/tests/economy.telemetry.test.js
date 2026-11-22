'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const mongoose = require('mongoose');
const { MongoMemoryServer } = require('mongodb-memory-server');
const EconomyTelemetryEvent = require('../src/models/EconomyTelemetryEvent');
const EconomyTelemetry = require('../src/services/EconomyTelemetry');

let mongod;

test.before(async () => {
  mongod = await MongoMemoryServer.create();
  await mongoose.connect(mongod.getUri());
});

test.after(async () => {
  await mongoose.disconnect();
  await mongod.stop();
});

test.beforeEach(async () => {
  await EconomyTelemetryEvent.deleteMany({});
});

test('record stores one aggregable event with net gold totals per category', async () => {
  EconomyTelemetry.record({
    characterId: 'char-1',
    operation: 'trade',
    gold: [
      { direction: 'generated', category: 'npc_sale', amount: 72 },
      { direction: 'spent', category: 'purchase', offerType: 'weapon', amount: 120 },
      { direction: 'spent', category: 'buyback', amount: 0 }
    ],
    balanceAfter: 952,
    details: { demandBonusUnits: 1 }
  });
  await EconomyTelemetry.flush();

  const events = await EconomyTelemetryEvent.find({}).lean();
  assert.equal(events.length, 1);
  const [event] = events;
  assert.equal(event.characterId, 'char-1');
  assert.equal(event.operation, 'trade');
  assert.deepEqual(event.gold.map(({ direction, category, offerType, amount }) => ({ direction, category, offerType, amount })), [
    { direction: 'generated', category: 'npc_sale', offerType: undefined, amount: 72 },
    { direction: 'spent', category: 'purchase', offerType: 'weapon', amount: 120 }
  ], 'zero-amount lines are not recorded');
  assert.equal(event.goldGenerated, 72);
  assert.equal(event.goldSpent, 120);
  assert.equal(event.netGold, -48);
  assert.equal(event.balanceAfter, 952);
  assert.deepEqual(event.details, { demandBonusUnits: 1 });
  assert.ok(event.occurredAt instanceof Date);
});

test('record never persists account identity, credentials or unknown top-level fields', async () => {
  EconomyTelemetry.record({
    characterId: 'char-2', operation: 'respec',
    gold: [{ direction: 'spent', category: 'respec', amount: 100 }],
    accountId: 'acc-secret', username: 'player', token: 'jwt.secret'
  });
  await EconomyTelemetry.flush();
  const [event] = await EconomyTelemetryEvent.find({}).lean();
  for (const key of ['accountId', 'username', 'token']) assert.equal(Object.hasOwn(event, key), false, key);
});

test('a failing sink is swallowed: record and flush resolve and nothing is stored', async t => {
  t.mock.method(EconomyTelemetryEvent, 'create', async () => { throw new Error('sink down'); });
  const warn = t.mock.method(console, 'warn', () => {});
  await assert.doesNotReject(EconomyTelemetry.record({ characterId: 'c', operation: 'buy',
    gold: [{ direction: 'spent', category: 'purchase', offerType: 'consumable', amount: 40 }] }));
  await EconomyTelemetry.flush();
  assert.equal(warn.mock.callCount(), 1);
  assert.match(warn.mock.calls[0].arguments[0], /^\[EconomyTelemetry\] dropped buy event: sink down$/);
  t.mock.restoreAll();
  assert.equal(await EconomyTelemetryEvent.countDocuments({}), 0);
});

test('an unknown gold category is rejected by the schema and dropped', async t => {
  t.mock.method(console, 'warn', () => {});
  await EconomyTelemetry.record({ characterId: 'c', operation: 'buy',
    gold: [{ direction: 'spent', category: 'dynamic_tax', amount: 5 }] });
  assert.equal(await EconomyTelemetryEvent.countDocuments({}), 0);
});

test('purchaseOfferType classifies buy offers by catalog family', () => {
  assert.equal(EconomyTelemetry.purchaseOfferType('CON_HEALTH_POTION'), 'consumable');
  assert.equal(EconomyTelemetry.purchaseOfferType('UTL_RUNE_COMPASS'), 'utility');
  assert.equal(EconomyTelemetry.purchaseOfferType('CON_LOCKPICK'), 'utility');
  assert.equal(EconomyTelemetry.purchaseOfferType('arming_sword'), 'weapon');
  assert.equal(EconomyTelemetry.purchaseOfferType('placeholder_helmet'), 'armor');
});
