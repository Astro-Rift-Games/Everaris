// tests/auth.refresh.test.js
// FIX-34: rotating refresh token, TOKEN_EXPIRED vs UNAUTHORIZED, family revocation on logout/reuse.
const { test, describe, before, after, afterEach } = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('crypto');
const mongoose = require('mongoose');
const jwt = require('jsonwebtoken');
const { startMemoryMongo } = require('./support/memoryMongo');
const RefreshToken = require('../src/models/RefreshToken');
const RevokedToken = require('../src/models/RevokedToken');
const AuthService = require('../src/services/AuthService');
const authenticate = require('../src/middleware/authenticate');
const config = require('../src/config/env');

const sha256 = (value) => crypto.createHash('sha256').update(value).digest('hex');

async function runAuthenticate(token) {
  const req = { headers: { authorization: `Bearer ${token}` } };
  let error;
  await authenticate(req, {}, (err) => { error = err; });
  return { req, error };
}

async function assertRefreshInvalid(promise) {
  await assert.rejects(promise, (err) => {
    assert.equal(err.statusCode, 401);
    assert.equal(err.errorCode, 'REFRESH_INVALID');
    return true;
  });
}

// Moves the use of a rotated token back in time, as if it had been rotated `secondsAgo` s ago.
async function ageUse(refreshToken, secondsAgo) {
  await RefreshToken.updateOne({ tokenHash: sha256(refreshToken) }, { usedAt: new Date(Date.now() - secondsAgo * 1000) });
}

async function liveUnused(familyId) {
  return RefreshToken.find({ familyId, usedAt: null, revokedAt: null }).lean();
}

describe('Refresh token rotation (FIX-34)', () => {
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

  test('jwtRefreshExpiresIn is a numeric placeholder defaulting to 24 h', () => {
    assert.equal(config.jwtRefreshExpiresIn, 86400);
  });

  test('jwtRefreshReuseGraceSeconds defaults to 30 s', () => {
    assert.equal(config.jwtRefreshReuseGraceSeconds, 30);
  });

  describe('reuse grace window (FIX-34 L9)', () => {
    let originalGrace;
    before(() => { originalGrace = config.jwtRefreshReuseGraceSeconds; });
    afterEach(() => { config.jwtRefreshReuseGraceSeconds = originalGrace; });

    test('reusing a token inside the grace window returns a working new pair and revokes the lost successor', async () => {
      const login = await AuthService.register('grace_retry', 'password123');
      const sid = jwt.decode(login.token).sid;
      const lost = await AuthService.refresh(login.refreshToken); // response never reached the client

      const retried = await AuthService.refresh(login.refreshToken);
      assert.notEqual(retried.refreshToken, lost.refreshToken);
      assert.notEqual(retried.token, lost.token);
      assert.equal(retried.expiresIn, parseInt(config.jwtExpiresIn, 10));
      assert.equal(retried.refreshExpiresIn, config.jwtRefreshExpiresIn);
      assert.equal(jwt.decode(retried.token).sid, sid);
      const { req, error } = await runAuthenticate(retried.token);
      assert.equal(error, undefined);
      assert.equal(String(req.accountId), jwt.decode(login.token).sub);

      const live = await liveUnused(sid);
      assert.equal(live.length, 1, 'exactly one live unused successor');
      assert.equal(live[0].tokenHash, sha256(retried.refreshToken));
      const lostDoc = await RefreshToken.findOne({ tokenHash: sha256(lost.refreshToken) }).lean();
      assert.ok(lostDoc.revokedAt, 'the lost successor is revoked');
      assert.equal(lostDoc.usedAt ?? null, null);
      const parent = await RefreshToken.findOne({ tokenHash: sha256(login.refreshToken) }).lean();
      assert.equal(parent.revokedAt ?? null, null, 'the family is not revoked');

      // The lost successor is dead, without taking the family with it.
      await assertRefreshInvalid(AuthService.refresh(lost.refreshToken));
      const chained = await AuthService.refresh(retried.refreshToken);
      assert.equal(jwt.decode(chained.token).sid, sid);
    });

    test('repeated retries inside the window keep exactly one live successor', async () => {
      const login = await AuthService.register('grace_repeat', 'password123');
      const sid = jwt.decode(login.token).sid;
      await AuthService.refresh(login.refreshToken);
      await AuthService.refresh(login.refreshToken);
      const last = await AuthService.refresh(login.refreshToken);

      const live = await liveUnused(sid);
      assert.equal(live.length, 1);
      assert.equal(live[0].tokenHash, sha256(last.refreshToken));
      assert.equal(await RefreshToken.countDocuments({ familyId: sid, revokedAt: null }), 2, 'parent (used) and the live successor');
    });

    test('a successor already used is not revoked by a later retry of its parent', async () => {
      const login = await AuthService.register('grace_used_child', 'password123');
      const first = await AuthService.refresh(login.refreshToken);
      await AuthService.refresh(first.refreshToken); // the client did receive `first`

      const retried = await AuthService.refresh(login.refreshToken);
      assert.ok(retried.refreshToken);
      const firstDoc = await RefreshToken.findOne({ tokenHash: sha256(first.refreshToken) }).lean();
      assert.ok(firstDoc.usedAt);
      assert.equal(firstDoc.revokedAt ?? null, null);
    });

    test('reuse just inside the window succeeds, just outside revokes the family and the newest token dies too', async () => {
      const grace = config.jwtRefreshReuseGraceSeconds;
      const login = await AuthService.register('grace_boundary', 'password123');
      const sid = jwt.decode(login.token).sid;
      const newest = await AuthService.refresh(login.refreshToken);

      await ageUse(login.refreshToken, grace - 5);
      const inside = await AuthService.refresh(login.refreshToken);
      assert.ok(inside.refreshToken);

      await ageUse(login.refreshToken, grace + 1);
      const before = await RefreshToken.countDocuments({ familyId: sid });
      await assertRefreshInvalid(AuthService.refresh(login.refreshToken));
      assert.equal(await RefreshToken.countDocuments({ familyId: sid }), before, 'a rejected reuse issues nothing');
      const family = await RefreshToken.find({ familyId: sid }).lean();
      assert.ok(family.every((doc) => doc.revokedAt), 'the whole family is revoked');
      await assertRefreshInvalid(AuthService.refresh(inside.refreshToken));
      await assertRefreshInvalid(AuthService.refresh(newest.refreshToken));
    });

    test('a retry does not extend the window: it is measured from the first use', async () => {
      const grace = config.jwtRefreshReuseGraceSeconds;
      const login = await AuthService.register('grace_fixed_window', 'password123');
      await AuthService.refresh(login.refreshToken);
      await ageUse(login.refreshToken, grace - 5);
      await AuthService.refresh(login.refreshToken);
      const parent = await RefreshToken.findOne({ tokenHash: sha256(login.refreshToken) }).lean();
      const ageMs = Date.now() - parent.usedAt.getTime();
      assert.ok(ageMs >= (grace - 6) * 1000, 'usedAt keeps the original time');
    });

    test('grace 0 keeps the strict behavior: immediate reuse revokes the family', async () => {
      config.jwtRefreshReuseGraceSeconds = 0;
      const login = await AuthService.register('grace_zero', 'password123');
      const sid = jwt.decode(login.token).sid;
      const rotated = await AuthService.refresh(login.refreshToken);

      await assertRefreshInvalid(AuthService.refresh(login.refreshToken));
      const family = await RefreshToken.find({ familyId: sid }).lean();
      assert.equal(family.length, 2);
      assert.ok(family.every((doc) => doc.revokedAt));
      await assertRefreshInvalid(AuthService.refresh(rotated.refreshToken));
    });

    test('a used token of a revoked family or an expired token is REFRESH_INVALID even inside the window', async () => {
      const revoked = await AuthService.register('grace_revoked', 'password123');
      await AuthService.refresh(revoked.refreshToken);
      await AuthService.logout(jwt.decode(revoked.token).sub, revoked.token);
      await assertRefreshInvalid(AuthService.refresh(revoked.refreshToken));

      const expired = await AuthService.register('grace_expired', 'password123');
      const sid = jwt.decode(expired.token).sid;
      await AuthService.refresh(expired.refreshToken);
      await RefreshToken.updateOne({ tokenHash: sha256(expired.refreshToken) }, { expiresAt: new Date(Date.now() - 1000) });
      const before = await RefreshToken.countDocuments({ familyId: sid });
      await assertRefreshInvalid(AuthService.refresh(expired.refreshToken));
      assert.equal(await RefreshToken.countDocuments({ familyId: sid }), before);
    });

    test('logout after a grace retry revokes every token of the family', async () => {
      const login = await AuthService.register('grace_logout', 'password123');
      const sid = jwt.decode(login.token).sid;
      await AuthService.refresh(login.refreshToken);
      const retried = await AuthService.refresh(login.refreshToken);

      assert.equal(await AuthService.logout(jwt.decode(retried.token).sub, retried.token), true);
      const family = await RefreshToken.find({ familyId: sid }).lean();
      assert.ok(family.every((doc) => doc.revokedAt));
      await assertRefreshInvalid(AuthService.refresh(retried.refreshToken));
      await assertRefreshInvalid(AuthService.refresh(login.refreshToken));
    });
  });


  test('register and login return refresh fields and a sid claim; only the hash is stored', async () => {
    const reg = await AuthService.register('refresh_issue', 'password123');
    const login = await AuthService.login('refresh_issue', 'password123');

    for (const result of [reg, login]) {
      assert.equal(typeof result.token, 'string');
      assert.equal(result.expiresIn, parseInt(config.jwtExpiresIn, 10));
      assert.equal(typeof result.refreshToken, 'string');
      assert.ok(result.refreshToken.length >= 32);
      assert.equal(result.refreshExpiresIn, config.jwtRefreshExpiresIn);
      assert.ok(jwt.decode(result.token).sid, 'access JWT must carry sid');
    }
    assert.notEqual(reg.refreshToken, login.refreshToken);
    assert.notEqual(jwt.decode(reg.token).sid, jwt.decode(login.token).sid, 'each login starts its own family');

    const stored = await RefreshToken.findOne({ tokenHash: sha256(login.refreshToken) }).lean();
    assert.ok(stored, 'refresh token is stored by its SHA-256 hash');
    assert.equal(stored.familyId, jwt.decode(login.token).sid);
    assert.equal(String(stored.accountId), jwt.decode(login.token).sub);
    assert.equal(stored.usedAt ?? null, null);
    assert.equal(stored.revokedAt ?? null, null);
    assert.equal(await RefreshToken.countDocuments({ tokenHash: login.refreshToken }), 0, 'raw token is never stored');
    const ttlMs = stored.expiresAt.getTime() - Date.now();
    assert.ok(ttlMs > 0 && ttlMs <= config.jwtRefreshExpiresIn * 1000);
  });

  test('refresh rotates within the family and the new access token authenticates', async () => {
    const login = await AuthService.register('refresh_rotate', 'password123');
    const sid = jwt.decode(login.token).sid;

    const rotated = await AuthService.refresh(login.refreshToken);
    assert.notEqual(rotated.token, login.token);
    assert.notEqual(rotated.refreshToken, login.refreshToken);
    assert.equal(rotated.expiresIn, parseInt(config.jwtExpiresIn, 10));
    assert.equal(rotated.refreshExpiresIn, config.jwtRefreshExpiresIn);
    const decoded = jwt.decode(rotated.token);
    assert.equal(decoded.sid, sid);
    assert.equal(decoded.sub, jwt.decode(login.token).sub);
    assert.notEqual(decoded.jti, jwt.decode(login.token).jti);

    const { req, error } = await runAuthenticate(rotated.token);
    assert.equal(error, undefined);
    assert.equal(String(req.accountId), decoded.sub);

    const old = await RefreshToken.findOne({ tokenHash: sha256(login.refreshToken) }).lean();
    assert.ok(old.usedAt, 'rotated token is marked used');
    const fresh = await RefreshToken.findOne({ tokenHash: sha256(rotated.refreshToken) }).lean();
    assert.equal(fresh.familyId, sid);
    assert.equal(fresh.usedAt ?? null, null);

    // The chain keeps working.
    const second = await AuthService.refresh(rotated.refreshToken);
    assert.equal(jwt.decode(second.token).sid, sid);
  });

  test('reusing a rotated refresh token after the grace window is REFRESH_INVALID and revokes the whole family', async () => {
    const login = await AuthService.register('refresh_reuse', 'password123');
    const sid = jwt.decode(login.token).sid;
    const rotated = await AuthService.refresh(login.refreshToken);
    await ageUse(login.refreshToken, config.jwtRefreshReuseGraceSeconds + 1);

    await assertRefreshInvalid(AuthService.refresh(login.refreshToken));

    const family = await RefreshToken.find({ familyId: sid }).lean();
    assert.equal(family.length, 2);
    assert.ok(family.every((doc) => doc.revokedAt), 'every token of the family is revoked');
    await assertRefreshInvalid(AuthService.refresh(rotated.refreshToken));
  });

  test('reuse in one family leaves other families of the same account usable', async () => {
    await AuthService.register('refresh_families', 'password123');
    const a = await AuthService.login('refresh_families', 'password123');
    const b = await AuthService.login('refresh_families', 'password123');
    await AuthService.refresh(a.refreshToken);
    await ageUse(a.refreshToken, config.jwtRefreshReuseGraceSeconds + 1);
    await assertRefreshInvalid(AuthService.refresh(a.refreshToken));

    const rotatedB = await AuthService.refresh(b.refreshToken);
    assert.ok(rotatedB.refreshToken);
  });

  test('unknown and expired refresh tokens are REFRESH_INVALID', async () => {
    await assertRefreshInvalid(AuthService.refresh(crypto.randomBytes(32).toString('base64url')));

    const login = await AuthService.register('refresh_expired', 'password123');
    await RefreshToken.updateOne({ tokenHash: sha256(login.refreshToken) }, { expiresAt: new Date(Date.now() - 1000) });
    await assertRefreshInvalid(AuthService.refresh(login.refreshToken));
  });

  test('failed refresh does not create new tokens', async () => {
    const before = await RefreshToken.countDocuments({});
    await assertRefreshInvalid(AuthService.refresh('not-a-known-token'));
    assert.equal(await RefreshToken.countDocuments({}), before);
  });

  test('logout revokes the refresh family of the access token sid', async () => {
    const login = await AuthService.register('refresh_logout', 'password123');
    const rotated = await AuthService.refresh(login.refreshToken);
    const decoded = jwt.decode(rotated.token);

    assert.equal(await AuthService.logout(decoded.sub, rotated.token), true);

    await assertRefreshInvalid(AuthService.refresh(rotated.refreshToken));
    const family = await RefreshToken.find({ familyId: decoded.sid }).lean();
    assert.ok(family.every((doc) => doc.revokedAt));
  });

  test('logout of one session keeps another family of the account refreshable', async () => {
    await AuthService.register('refresh_logout_multi', 'password123');
    const a = await AuthService.login('refresh_logout_multi', 'password123');
    const b = await AuthService.login('refresh_logout_multi', 'password123');
    await AuthService.logout(jwt.decode(a.token).sub, a.token);

    await assertRefreshInvalid(AuthService.refresh(a.refreshToken));
    assert.ok((await AuthService.refresh(b.refreshToken)).refreshToken);
  });

  test('logout with a pre-FIX-34 access token (no sid) still revokes the token cleanly', async () => {
    const reg = await AuthService.register('refresh_legacy', 'password123');
    const accountId = jwt.decode(reg.token).sub;
    const legacy = jwt.sign({ sub: accountId, jti: crypto.randomUUID() }, config.jwtSecret, { expiresIn: '60s' });

    assert.equal(await AuthService.logout(accountId, legacy), true);
    assert.ok(await RevokedToken.exists({ tokenHash: sha256(legacy) }));
    // The unrelated family of the same account is untouched.
    assert.ok((await AuthService.refresh(reg.refreshToken)).token);
  });

  test('authenticate: expired access JWT is 401 TOKEN_EXPIRED', async () => {
    const expired = jwt.sign(
      { sub: new mongoose.Types.ObjectId().toString(), jti: crypto.randomUUID(), exp: Math.floor(Date.now() / 1000) - 10 },
      config.jwtSecret
    );
    const { error } = await runAuthenticate(expired);
    assert.equal(error.statusCode, 401);
    assert.equal(error.errorCode, 'TOKEN_EXPIRED');
  });

  test('authenticate: bad signature, malformed and revoked tokens stay 401 UNAUTHORIZED', async () => {
    const wrongSecret = jwt.sign({ sub: 'x', jti: 'y' }, 'another-secret-entirely-different');
    let result = await runAuthenticate(wrongSecret);
    assert.equal(result.error.statusCode, 401);
    assert.equal(result.error.errorCode, 'UNAUTHORIZED');
    assert.equal(result.error.message, 'Token is invalid or expired.');

    // An expired token with a wrong signature is not trusted enough to be called expired.
    const expiredWrongSecret = jwt.sign(
      { sub: 'x', jti: 'y', exp: Math.floor(Date.now() / 1000) - 10 },
      'another-secret-entirely-different'
    );
    result = await runAuthenticate(expiredWrongSecret);
    assert.equal(result.error.errorCode, 'UNAUTHORIZED');

    result = await runAuthenticate('not.a.jwt');
    assert.equal(result.error.errorCode, 'UNAUTHORIZED');
    assert.equal(result.error.message, 'Token is invalid or expired.');

    const user = await AuthService.register('refresh_revoked', 'password123');
    await AuthService.logout(jwt.decode(user.token).sub, user.token);
    result = await runAuthenticate(user.token);
    assert.equal(result.error.statusCode, 401);
    assert.equal(result.error.errorCode, 'UNAUTHORIZED');
    assert.equal(result.error.message, 'Token has been revoked.');
  });
});
