'use strict';

// TASK-309: client-reported MaxTransportable blocks (Doc 23 §8.2 and §12). Analytics only.

const test = require('node:test');
const assert = require('node:assert/strict');
const request = require('supertest');
const mongoose = require('mongoose');
const { MongoMemoryServer } = require('mongodb-memory-server');
const app = require('../src/app');
const Character = require('../src/models/Character');
const EconomyTelemetry = require('../src/services/EconomyTelemetry');
const EconomyTelemetryEvent = require('../src/models/EconomyTelemetryEvent');
const telemetryRoutes = require('../src/routes/telemetry.routes');

const ENDPOINT = '/character/me/telemetry/transport-block';

let mongod;
let token;
let characterId;
let accountId;
let characterlessToken;

test.before(async () => {
  mongod = await MongoMemoryServer.create();
  await mongoose.connect(mongod.getUri());

  const reg = await request(app).post('/auth/register').send({ username: 'transportblocker', password: 'password123' });
  assert.equal(reg.status, 201);
  token = reg.body.token;
  const created = await request(app).post('/character/me').set('Authorization', `Bearer ${token}`).send({ name: 'TransportHero' });
  assert.equal(created.status, 201);
  const character = await Character.findOne({ name: 'TransportHero' });
  characterId = String(character._id);
  accountId = character.accountId;

  const other = await request(app).post('/auth/register').send({ username: 'nocharacter', password: 'password123' });
  assert.equal(other.status, 201);
  characterlessToken = other.body.token;
});

test.after(async () => {
  await EconomyTelemetry.flush();
  await mongoose.disconnect();
  await mongod.stop();
});

test.beforeEach(async () => {
  await EconomyTelemetry.flush();
  await EconomyTelemetryEvent.deleteMany({});
  telemetryRoutes.transportBlockLimiter.reset();
});

function report(body, bearer = token) {
  const req = request(app).post(ENDPOINT);
  if (bearer) req.set('Authorization', `Bearer ${bearer}`);
  return req.send(body);
}

async function characterState() {
  const character = await Character.findOne({ accountId }).lean();
  return { revision: character.revision, currency: character.inventory?.currency, inventory: character.inventory };
}

async function events() {
  await EconomyTelemetry.flush();
  return EconomyTelemetryEvent.find({}).lean();
}

test('a reported expedition-start MaxTransportable block records exactly one transport_block with its source', async () => {
  const before = await characterState();

  const res = await report({ source: 'raid_admission' });

  assert.equal(res.status, 202);
  assert.deepEqual(res.body, { accepted: true });
  const stored = await events();
  assert.equal(stored.length, 1);
  const [event] = stored;
  assert.equal(event.operation, 'transport_block');
  assert.equal(event.characterId, characterId);
  assert.deepEqual(event.details, { source: 'raid_admission', limit: 'max_transportable', reported: 'client' });
  assert.deepEqual(event.gold, []);
  assert.equal(event.goldGenerated, 0);
  assert.equal(event.goldSpent, 0);
  assert.equal(event.balanceAfter, null);
  assert.deepEqual(await characterState(), before, 'the report never alters the player state');
});

test('transport blocks and capacity blocks aggregate into separate block rates', async () => {
  const character = await Character.findOne({ accountId }).lean();
  await EconomyTelemetry.recordCapacityBlock(character, 'purchase');
  assert.equal((await report({ source: 'raid_admission' })).status, 202);
  assert.equal((await report({ source: 'raid_admission' })).status, 202);

  await EconomyTelemetry.flush();
  const rates = await EconomyTelemetryEvent.aggregate([
    { $match: { operation: { $in: ['capacity_block', 'transport_block'] } } },
    { $group: { _id: { operation: '$operation', limit: '$details.limit' }, blocks: { $sum: 1 } } },
    { $sort: { '_id.operation': 1 } }
  ]);
  assert.deepEqual(rates, [
    { _id: { operation: 'capacity_block', limit: 'capacity' }, blocks: 1 },
    { _id: { operation: 'transport_block', limit: 'max_transportable' }, blocks: 2 }
  ]);
});

const MALFORMED = [
  ['an unknown field', { source: 'raid_admission', gold: 999 }],
  ['a client-declared limit', { source: 'raid_admission', limit: 'capacity' }],
  ['an unknown source', { source: 'purchase' }],
  ['a missing source', {}],
  ['a non-string source', { source: 42 }],
  ['an oversized source', { source: 'raid_admission'.repeat(200) }],
  ['an array body', [{ source: 'raid_admission' }]]
];

for (const [name, body] of MALFORMED) {
  test(`a report with ${name} is rejected without writing anything`, async () => {
    const character = await Character.findOne({ accountId }).lean();
    await EconomyTelemetry.recordCapacityBlock(character, 'trade');
    const otherBefore = await events();
    const before = await characterState();

    const res = await report(body);

    assert.equal(res.status, 400);
    assert.equal(res.body.error, 'VALIDATION_FAILED');
    assert.deepEqual(await events(), otherBefore, 'no transport_block written and other data untouched');
    assert.deepEqual(await characterState(), before);
  });
}

test('a report with a __proto__ key is rejected without writing or polluting', async () => {
  const before = await characterState();

  const res = await request(app).post(ENDPOINT).set('Authorization', `Bearer ${token}`)
    .set('Content-Type', 'application/json').send('{"source":"raid_admission","__proto__":{"polluted":1}}');

  assert.equal(res.status, 400);
  assert.equal(res.body.error, 'VALIDATION_FAILED');
  assert.equal(({}).polluted, undefined);
  assert.deepEqual(await events(), []);
  assert.deepEqual(await characterState(), before);
});

test('an oversized report body is rejected with 413 and records nothing', async () => {
  const before = await characterState();

  const res = await request(app).post(ENDPOINT).set('Authorization', `Bearer ${token}`)
    .set('Content-Type', 'application/json').send(JSON.stringify({ source: 'raid_admission', pad: 'x'.repeat(200000) }));

  assert.equal(res.status, 413);
  assert.deepEqual(await events(), []);
  assert.deepEqual(await characterState(), before);
});

test('a telemetry sink failure never fails the report', async () => {
  const create = EconomyTelemetryEvent.create;
  EconomyTelemetryEvent.create = async () => { throw new Error('sink down'); };
  try {
    const res = await report({ source: 'raid_admission' });
    assert.equal(res.status, 202);
    await EconomyTelemetry.flush();
  } finally {
    EconomyTelemetryEvent.create = create;
  }
  assert.deepEqual(await events(), []);
});

test('an unauthenticated report is rejected and records nothing', async () => {
  const res = await report({ source: 'raid_admission' }, null);

  assert.equal(res.status, 401);
  assert.equal(res.body.error, 'UNAUTHORIZED');
  assert.deepEqual(await events(), []);
});

test('a report from an account without a character is rejected and records nothing', async () => {
  const res = await report({ source: 'raid_admission' }, characterlessToken);

  assert.equal(res.status, 404);
  assert.equal(res.body.error, 'CHARACTER_NOT_FOUND');
  assert.deepEqual(await events(), []);
});

test('reports beyond the per-account volume bound are rejected with 429 and record nothing more', async () => {
  const max = telemetryRoutes.TRANSPORT_BLOCK_REPORT_MAX;
  for (let i = 0; i < max; i++) {
    assert.equal((await report({ source: 'raid_admission' })).status, 202);
  }
  const before = await characterState();

  const res = await report({ source: 'raid_admission' });

  assert.equal(res.status, 429);
  assert.equal(res.body.error, 'TOO_MANY_REQUESTS');
  assert.ok(Number(res.headers['retry-after']) >= 1);
  assert.equal((await events()).length, max);
  assert.deepEqual(await characterState(), before);
});

test('the volume bound is per account: another account is not throttled', async () => {
  const max = telemetryRoutes.TRANSPORT_BLOCK_REPORT_MAX;
  for (let i = 0; i < max; i++) await report({ source: 'raid_admission' });
  assert.equal((await report({ source: 'raid_admission' })).status, 429);

  const res = await report({ source: 'raid_admission' }, characterlessToken);

  assert.equal(res.status, 404, 'the other account reaches validation and lookup instead of the limiter');
});
