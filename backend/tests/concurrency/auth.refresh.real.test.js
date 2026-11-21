// tests/concurrency/auth.refresh.real.test.js
//
// FIX-34: simultaneous refreshes with the same refresh token must yield exactly
// one success; the rest are reuse and the family ends up revoked.

'use strict';

const { describe, test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const mongoose = require('mongoose');
const jwt = require('jsonwebtoken');
const { startMemoryMongo } = require('../support/memoryMongo');
const RefreshToken = require('../../src/models/RefreshToken');
const AuthService = require('../../src/services/AuthService');

describe('Concurrent refresh with real MongoDB (FIX-34)', () => {
  let mongoServer;

  before(async () => {
    mongoServer = await startMemoryMongo();
    await mongoose.connect(mongoServer.getUri());
    await RefreshToken.init();
  });

  after(async () => {
    await mongoose.disconnect();
    await mongoServer.stop();
  });

  test('N simultaneous refreshes with one token: exactly one success, the rest REFRESH_INVALID', async () => {
    const login = await AuthService.register('refresh_race', 'password123');
    const sid = jwt.decode(login.token).sid;

    const results = await Promise.allSettled(
      Array.from({ length: 8 }, () => AuthService.refresh(login.refreshToken))
    );

    const fulfilled = results.filter(r => r.status === 'fulfilled');
    const rejected = results.filter(r => r.status === 'rejected');
    assert.equal(fulfilled.length, 1);
    assert.equal(rejected.length, 7);
    for (const r of rejected) {
      assert.equal(r.reason.statusCode, 401);
      assert.equal(r.reason.errorCode, 'REFRESH_INVALID');
    }

    // Losers are indistinguishable from reuse: the family is revoked and nothing stays live.
    const family = await RefreshToken.find({ familyId: sid }).lean();
    assert.ok(family.every(doc => doc.revokedAt), 'no token of the family stays usable after the race');
    assert.equal(family.filter(doc => doc.usedAt).length, 1, 'the original token was consumed exactly once');
    await assert.rejects(AuthService.refresh(fulfilled[0].value.refreshToken), { errorCode: 'REFRESH_INVALID' });
  });
});
