// tests/concurrency/auth.refresh.real.test.js
//
// FIX-34: simultaneous refreshes with the same refresh token. Inside the reuse grace window
// (L9) exactly one live unused successor must remain and the family must not be revoked;
// outside it every request is reuse and the family is revoked.

'use strict';

const { describe, test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('crypto');
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

  test('N simultaneous refreshes with one token inside the grace window leave exactly one live successor and the family unrevoked', async () => {
    const login = await AuthService.register('refresh_race', 'password123');
    const sid = jwt.decode(login.token).sid;

    const results = await Promise.allSettled(
      Array.from({ length: 8 }, () => AuthService.refresh(login.refreshToken))
    );

    // Inside the grace window a simultaneous request is indistinguishable from a retry of a
    // lost response, so each may answer 200; only one of the issued tokens stays live.
    for (const r of results) {
      assert.equal(r.status, 'fulfilled', r.status === 'rejected' ? String(r.reason && r.reason.errorCode) : '');
    }
    const issued = results.map((r) => r.value.refreshToken);
    assert.equal(new Set(issued).size, issued.length, 'every response carries its own token');

    const family = await RefreshToken.find({ familyId: sid }).lean();
    assert.equal(family.length, 9);
    assert.equal(family.filter((doc) => doc.usedAt).length, 1, 'the original token was consumed exactly once');
    assert.equal(family.filter((doc) => doc.usedAt && doc.revokedAt).length, 0, 'the family is not revoked');
    assert.equal(family.filter((doc) => !doc.usedAt && !doc.revokedAt).length, 1, 'exactly one live unused successor');
    assert.equal(family.filter((doc) => !doc.usedAt && doc.revokedAt).length, 7, 'all other successors are revoked');

    let exchangeable = 0;
    for (const refreshToken of issued) {
      const [outcome] = await Promise.allSettled([AuthService.refresh(refreshToken)]);
      if (outcome.status === 'fulfilled') exchangeable++;
    }
    assert.equal(exchangeable, 1, 'exactly one issued token can still be exchanged');
  });

  test('N simultaneous refreshes after the grace window: none succeeds and the family is revoked', async () => {
    const login = await AuthService.register('refresh_race_strict', 'password123');
    const sid = jwt.decode(login.token).sid;
    const rotated = await AuthService.refresh(login.refreshToken);
    await RefreshToken.updateOne(
      { tokenHash: crypto.createHash('sha256').update(login.refreshToken).digest('hex') },
      { usedAt: new Date(Date.now() - 3600 * 1000) }
    );

    const results = await Promise.allSettled(
      Array.from({ length: 8 }, () => AuthService.refresh(login.refreshToken))
    );
    for (const r of results) {
      assert.equal(r.status, 'rejected');
      assert.equal(r.reason.errorCode, 'REFRESH_INVALID');
    }
    const family = await RefreshToken.find({ familyId: sid }).lean();
    assert.ok(family.every((doc) => doc.revokedAt));
    await assert.rejects(AuthService.refresh(rotated.refreshToken), { errorCode: 'REFRESH_INVALID' });
  });
});
