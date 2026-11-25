'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const request = require('supertest');
const mongoose = require('mongoose');
const { MongoMemoryServer } = require('mongodb-memory-server');
const app = require('../src/app');
const Character = require('../src/models/Character');
const Account = require('../src/models/Account');
const EconomyTelemetry = require('../src/services/EconomyTelemetry');
const EconomyTelemetryEvent = require('../src/models/EconomyTelemetryEvent');

let mongod;
let token;
let accountId;
let characterId;

test.before(async () => {
  mongod = await MongoMemoryServer.create();
  await mongoose.connect(mongod.getUri());

  const reg = await request(app).post('/auth/register').send({
    username: 'missionclaimer',
    password: 'password123'
  });
  assert.equal(reg.status, 201);
  token = reg.body.token;

  const created = await request(app)
    .post('/character/me')
    .set('Authorization', `Bearer ${token}`)
    .send({ name: 'MissionHero' });
  assert.equal(created.status, 201);

  const character = await Character.findOne({ name: 'MissionHero' });
  accountId = character.accountId;
  characterId = String(character._id);
});

test.after(async () => {
  await EconomyTelemetry.flush();
  await mongoose.disconnect();
  await mongod.stop();
});

async function resetCharacter(patch = {}) {
  await EconomyTelemetryEvent.deleteMany({});
  await Character.updateOne(
    { accountId },
    {
      $set: {
        revision: 0,
        appliedShopReceipts: [],
        shopReceiptWatermark: 0,
        appliedMissionClaims: [],
        'inventory.currency': 100,
        'inventory.pendingReservation': null,
        ...patch
      }
    }
  );
}

function postClaim(body) {
  return request(app)
    .post('/character/me/missions/claim')
    .set('Authorization', `Bearer ${token}`)
    .send(body);
}

test('POST /character/me/missions/claim credits authoritative gold once and emits mission_reward telemetry (TASK-310)', async () => {
  await resetCharacter({ 'inventory.currency': 100 });
  const txId = `${Date.now()}_11111111-1111-1111-1111-111111111111`;
  const claimKey = 'CON_E_001:2026-11-24T00:00Z';

  const res = await postClaim({
    transactionId: txId,
    missionId: 'CON_E_001',
    claimKey,
    expectedRevision: 0
  });

  assert.equal(res.status, 200, JSON.stringify(res.body));
  assert.equal(res.body.alreadySecured, false);
  assert.equal(res.body.missionId, 'CON_E_001');
  assert.equal(res.body.claimKey, claimKey);
  assert.equal(res.body.goldGranted, 20);
  assert.equal(res.body.currency, 120);
  assert.equal(res.body.revision, 1);

  await EconomyTelemetry.flush();
  const events = await EconomyTelemetryEvent.find({ characterId }).lean();
  assert.equal(events.length, 1);
  assert.equal(events[0].operation, 'mission_reward');
  assert.equal(events[0].goldGenerated, 20);
  assert.equal(events[0].goldSpent, 0);
  assert.equal(events[0].netGold, 20);
  assert.equal(events[0].balanceAfter, 120);
  assert.deepEqual(
    events[0].gold.map(({ direction, category, amount }) => ({ direction, category, amount })),
    [{ direction: 'generated', category: 'mission_reward', amount: 20 }]
  );
  assert.deepEqual(events[0].details, { missionId: 'CON_E_001', claimKey });

  // Network retry with the exact same transactionId returns alreadySecured: true without extra gold or telemetry
  const beforeReplay = await Character.findOne({ accountId }).lean();
  const replay = await postClaim({
    transactionId: txId,
    missionId: 'CON_E_001',
    claimKey,
    expectedRevision: 0
  });
  assert.equal(replay.status, 200, JSON.stringify(replay.body));
  assert.equal(replay.body.alreadySecured, true);
  assert.equal(replay.body.goldGranted, 20);
  assert.equal(replay.body.currency, 120);
  assert.equal(replay.body.revision, 1);
  assert.deepEqual(await Character.findOne({ accountId }).lean(), beforeReplay);

  await EconomyTelemetry.flush();
  assert.equal(await EconomyTelemetryEvent.countDocuments({ characterId }), 1,
    'Idempotent replay must not emit duplicate telemetry');
});

test('POST /character/me/missions/claim rejects duplicate claimKey, reused transactionId, unknown mission, declared gold, stale revision, and pending reservation without gold or telemetry', async () => {
  await resetCharacter({ 'inventory.currency': 50 });
  const tx1 = `${Date.now()}_22222222-2222-2222-2222-222222222222`;
  const claimKey1 = 'CON_E_002:2026-11-24T00:00Z';

  // First valid claim succeeds (+30 gold -> 80)
  const ok = await postClaim({ transactionId: tx1, missionId: 'CON_E_002', claimKey: claimKey1, expectedRevision: 0 });
  assert.equal(ok.status, 200, JSON.stringify(ok.body));
  assert.equal(ok.body.currency, 80);
  await EconomyTelemetry.flush();
  assert.equal(await EconomyTelemetryEvent.countDocuments({ characterId }), 1);

  const expectRejectedUnchanged = async (body, expectedStatus, expectedError) => {
    const before = await Character.findOne({ accountId }).lean();
    const res = await postClaim(body);
    assert.equal(res.status, expectedStatus, `${expectedError}: ${JSON.stringify(res.body)}`);
    assert.equal(res.body.error, expectedError);
    assert.deepEqual(await Character.findOne({ accountId }).lean(), before);
    await EconomyTelemetry.flush();
    assert.equal(await EconomyTelemetryEvent.countDocuments({ characterId }), 1,
      `Rejected claim (${expectedError}) must not emit telemetry`);
  };

  // Same claimKey with a new transactionId -> 409 MISSION_ALREADY_CLAIMED
  await expectRejectedUnchanged({
    transactionId: `${Date.now() + 1}_33333333-3333-3333-3333-333333333333`,
    missionId: 'CON_E_002',
    claimKey: claimKey1,
    expectedRevision: 1
  }, 409, 'MISSION_ALREADY_CLAIMED');

  // Reused transactionId with different intent -> 409 TRANSACTION_ID_REUSED
  await expectRejectedUnchanged({
    transactionId: tx1,
    missionId: 'CON_E_003',
    claimKey: 'CON_E_003:2026-11-24T00:00Z',
    expectedRevision: 1
  }, 409, 'TRANSACTION_ID_REUSED');

  // Unknown missionId -> 404 MISSION_NOT_FOUND
  await expectRejectedUnchanged({
    transactionId: `${Date.now() + 2}_44444444-4444-4444-4444-444444444444`,
    missionId: 'CON_FAKE_999',
    claimKey: 'CON_FAKE_999:2026-11-24T00:00Z',
    expectedRevision: 1
  }, 404, 'MISSION_NOT_FOUND');

  // Client-declared gold field rejected -> 400 VALIDATION_FAILED
  await expectRejectedUnchanged({
    transactionId: `${Date.now() + 3}_55555555-5555-5555-5555-555555555555`,
    missionId: 'CON_E_003',
    claimKey: 'CON_E_003:2026-11-24T00:00Z',
    expectedRevision: 1,
    gold: 99999
  }, 400, 'VALIDATION_FAILED');

  // Stale expectedRevision -> 409 REVISION_CONFLICT
  await expectRejectedUnchanged({
    transactionId: `${Date.now() + 4}_66666666-6666-6666-6666-666666666666`,
    missionId: 'CON_E_003',
    claimKey: 'CON_E_003:2026-11-24T00:00Z',
    expectedRevision: 0
  }, 409, 'REVISION_CONFLICT');

  // Pending raid reservation -> 409 RESERVATION_PENDING
  await Character.updateOne({ accountId }, { $set: { 'inventory.pendingReservation': { reservationId: 'res-1' } } });
  await expectRejectedUnchanged({
    transactionId: `${Date.now() + 5}_77777777-7777-7777-7777-777777777777`,
    missionId: 'CON_E_003',
    claimKey: 'CON_E_003:2026-11-24T00:00Z',
    expectedRevision: 1
  }, 409, 'RESERVATION_PENDING');

  // Repeatable Normal mission in next day's rotation (new claimKey) succeeds once reservation is cleared
  await Character.updateOne({ accountId }, { $set: { 'inventory.pendingReservation': null } });
  const nextDay = await postClaim({
    transactionId: `${Date.now() + 6}_88888888-8888-8888-8888-888888888888`,
    missionId: 'CON_E_002',
    claimKey: 'CON_E_002:2026-11-25T00:00Z',
    expectedRevision: 1
  });
  assert.equal(nextDay.status, 200, JSON.stringify(nextDay.body));
  assert.equal(nextDay.body.currency, 110);
  assert.equal(nextDay.body.revision, 2);
  await EconomyTelemetry.flush();
  assert.equal(await EconomyTelemetryEvent.countDocuments({ characterId }), 2);
});
