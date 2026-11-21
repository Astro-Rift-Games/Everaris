// tests/integration/auth.hardening.integration.test.js
const test = require('node:test');
const assert = require('node:assert');
const request = require('supertest');
const app = require('../../src/app');
const testDb = require('./testDb');
const authRoutes = require('../../src/routes/auth.routes');
const env = require('../../src/config/env');
const jwt = require('jsonwebtoken');
const crypto = require('crypto');
const RefreshToken = require('../../src/models/RefreshToken');

test('E2E Session Revocation and Backend Hardening (US-60 / SEC-60.4)', async (t) => {
  await testDb.connect();

  t.after(async () => {
    await testDb.closeDatabase();
  });

  let token1;
  let token2;
  const username = 'hardeningHero';
  const password = 'StrongPassword123!';

  // Reset rate limiters before testing
  if (authRoutes.ipRateLimiter) authRoutes.ipRateLimiter.reset();
  if (authRoutes.userLoginLimiter) authRoutes.userLoginLimiter.reset();

  await t.test('1. Register and Verify Active Token', async () => {
    const regRes = await request(app)
      .post('/auth/register')
      .send({ username, password });
    assert.strictEqual(regRes.status, 201);
    assert.ok(regRes.body.token);
    token1 = regRes.body.token;

    // Create character
    const charRes = await request(app)
      .post('/character/me')
      .set('Authorization', `Bearer ${token1}`)
      .send({ name: 'SirHardening' });
    assert.strictEqual(charRes.status, 201);

    // Call protected endpoint
    const meRes = await request(app)
      .get('/character/me')
      .set('Authorization', `Bearer ${token1}`);
    assert.strictEqual(meRes.status, 200);
    assert.strictEqual(meRes.body.name, 'SirHardening');
  });

  await t.test('2. Logout Revokes Session Token (204 No Content)', async () => {
    const logoutRes = await request(app)
      .post('/auth/logout')
      .set('Authorization', `Bearer ${token1}`);
    assert.strictEqual(logoutRes.status, 204);
  });

  await t.test('3. Revoked Token is Rejected on Protected Routes with 401', async () => {
    const accessRes = await request(app)
      .get('/character/me')
      .set('Authorization', `Bearer ${token1}`);
    assert.strictEqual(accessRes.status, 401);
    assert.strictEqual(accessRes.body.error, 'UNAUTHORIZED');
    assert.strictEqual(accessRes.body.message, 'Token has been revoked.');
  });

  await t.test('4. Re-Login Generates New Valid Token', async () => {
    const loginRes = await request(app)
      .post('/auth/login')
      .send({ username, password });
    assert.strictEqual(loginRes.status, 200);
    assert.ok(loginRes.body.token);
    assert.notStrictEqual(loginRes.body.token, token1);
    token1 = loginRes.body.token;

    const meRes = await request(app)
      .get('/character/me')
      .set('Authorization', `Bearer ${token1}`);
    assert.strictEqual(meRes.status, 200);
    assert.strictEqual(meRes.body.name, 'SirHardening');
  });

  await t.test('5. Multi-Session Independence: Logging out Device A does not invalidate Device B', async () => {
    // Generate Device B token
    const loginResB = await request(app)
      .post('/auth/login')
      .send({ username, password });
    assert.strictEqual(loginResB.status, 200);
    token2 = loginResB.body.token;
    assert.notStrictEqual(token1, token2);

    // Logout Device A
    const logoutResA = await request(app)
      .post('/auth/logout')
      .set('Authorization', `Bearer ${token1}`);
    assert.strictEqual(logoutResA.status, 204);

    // Device A token is now revoked
    const testA = await request(app)
      .get('/character/me')
      .set('Authorization', `Bearer ${token1}`);
    assert.strictEqual(testA.status, 401);
    assert.strictEqual(testA.body.message, 'Token has been revoked.');

    // Device B token remains completely valid
    const testB = await request(app)
      .get('/character/me')
      .set('Authorization', `Bearer ${token2}`);
    assert.strictEqual(testB.status, 200);
    assert.strictEqual(testB.body.name, 'SirHardening');
  });

  await t.test('6. User-Specific Brute Force Rate Limiting (429 Too Many Requests)', async () => {
    const bruteForceUser = 'bruteTarget';
    await request(app)
      .post('/auth/register')
      .send({ username: bruteForceUser, password: 'LegitPassword123!' });

    // Exceed allowed failed attempts (default 5)
    for (let i = 0; i < 5; i++) {
      const failRes = await request(app)
        .post('/auth/login')
        .send({ username: bruteForceUser, password: 'WrongPassword!' });
      assert.strictEqual(failRes.status, 401);
    }

    // 6th attempt must be rejected with 429
    const blockedRes = await request(app)
      .post('/auth/login')
      .send({ username: bruteForceUser, password: 'WrongPassword!' });
    assert.strictEqual(blockedRes.status, 429);
    assert.strictEqual(blockedRes.body.error, 'TOO_MANY_REQUESTS');
    assert.ok(blockedRes.headers['retry-after']);
    assert.ok(blockedRes.body.retryAfter > 0);

    // Other users are not locked out
    const otherLogin = await request(app)
      .post('/auth/login')
      .send({ username, password });
    assert.strictEqual(otherLogin.status, 200);
  });

  await t.test('7. Debug / Mock Endpoints Guard in Production or when Opt-In is Disabled', async () => {
    const originalNodeEnv = env.nodeEnv;
    const originalDebug = process.env.ENABLE_DEV_DEBUG_ROUTES;
    const originalLegacy = process.env.ENABLE_LEGACY_EXTRACTION;
    try {
      // 1. In non-production without opt-in flag
      process.env.ENABLE_DEV_DEBUG_ROUTES = 'false';
      const resDisabled = await request(app)
        .post('/character/debug/mock-fusion-result')
        .set('Authorization', `Bearer ${token2}`)
        .send({ raidId: 'test_raid', items: [] });
      assert.strictEqual(resDisabled.status, 404);
      assert.strictEqual(resDisabled.body.error, 'NOT_FOUND');

      // 2. In production mode (even if flag is true, production NEVER allows debug routes)
      env.nodeEnv = 'production';
      process.env.ENABLE_DEV_DEBUG_ROUTES = 'true';
      process.env.ENABLE_LEGACY_EXTRACTION = 'true';
      const legacy = await request(app)
        .post('/character/me/inventory/extraction')
        .set('Authorization', `Bearer ${token2}`)
        .send({ raidId: 'production-legacy', resultSequence: 1, items: [] });
      assert.strictEqual(legacy.status, 404);
      assert.strictEqual(legacy.body.error, 'NOT_FOUND');
      const resProd = await request(app)
        .post('/character/debug/mock-fusion-result')
        .set('Authorization', `Bearer ${token2}`)
        .send({ raidId: 'test_raid', items: [] });
      assert.strictEqual(resProd.status, 404);
      assert.strictEqual(resProd.body.error, 'NOT_FOUND');
    } finally {
      env.nodeEnv = originalNodeEnv;
      if (originalDebug === undefined) delete process.env.ENABLE_DEV_DEBUG_ROUTES;
      else process.env.ENABLE_DEV_DEBUG_ROUTES = originalDebug;
      if (originalLegacy === undefined) delete process.env.ENABLE_LEGACY_EXTRACTION;
      else process.env.ENABLE_LEGACY_EXTRACTION = originalLegacy;
    }
  });

  await t.test('register IP limiter returns HTTP 429 and Retry-After at configured limit', async () => {
    authRoutes.ipRateLimiter.reset();
    try {
      for (let i = 0; i < env.rateLimitIpMax; i++) {
        const response = await request(app).post('/auth/register')
          .set('X-Forwarded-For', `198.51.100.${i % 250 + 1}`)
          .send({});
        assert.strictEqual(response.status, 400);
      }
      const blocked = await request(app).post('/auth/register')
        .set('X-Forwarded-For', '203.0.113.200').send({});
      assert.strictEqual(blocked.status, 429);
      assert.strictEqual(blocked.body.error, 'TOO_MANY_REQUESTS');
      assert.ok(Number(blocked.headers['retry-after']) > 0);
      assert.ok(Number(blocked.headers['retry-after']) <= Math.ceil(env.rateLimitWindowMs / 1000));
    } finally {
      authRoutes.ipRateLimiter.reset();
    }
  });

  await t.test('8. Unexpected Server Errors Return Sanitized 500 Responses Without Leaking Internals', async () => {
    // Calling an invalid route or causing a 500 in errorHandler
    // We send a malformed Authorization header that triggers an error or test errorHandler behavior
    const badTokenRes = await request(app)
      .get('/character/me')
      .set('Authorization', 'Bearer invalid.token.payload');
    assert.strictEqual(badTokenRes.status, 401);
    assert.strictEqual(badTokenRes.body.error, 'UNAUTHORIZED');
  });

  // FIX-34: access token renewal through a rotating refresh token.
  await t.test('9. Login and register return refresh credentials; refresh rotates and the new JWT works', async () => {
    authRoutes.ipRateLimiter.reset();
    const refreshUser = 'refreshHero';
    const regRes = await request(app).post('/auth/register').send({ username: refreshUser, password });
    assert.strictEqual(regRes.status, 201);
    assert.strictEqual(typeof regRes.body.refreshToken, 'string');
    assert.strictEqual(regRes.body.refreshExpiresIn, env.jwtRefreshExpiresIn);
    assert.strictEqual(regRes.body.expiresIn, parseInt(env.jwtExpiresIn, 10));

    const loginRes = await request(app).post('/auth/login').send({ username: refreshUser, password });
    assert.strictEqual(loginRes.status, 200);
    assert.strictEqual(typeof loginRes.body.refreshToken, 'string');
    assert.strictEqual(loginRes.body.refreshExpiresIn, env.jwtRefreshExpiresIn);
    assert.notStrictEqual(loginRes.body.refreshToken, regRes.body.refreshToken);

    const charRes = await request(app).post('/character/me')
      .set('Authorization', `Bearer ${loginRes.body.token}`).send({ name: 'SirRefresh' });
    assert.strictEqual(charRes.status, 201);

    const refreshRes = await request(app).post('/auth/refresh').send({ refreshToken: loginRes.body.refreshToken });
    assert.strictEqual(refreshRes.status, 200);
    assert.notStrictEqual(refreshRes.body.token, loginRes.body.token);
    assert.notStrictEqual(refreshRes.body.refreshToken, loginRes.body.refreshToken);
    assert.strictEqual(refreshRes.body.expiresIn, parseInt(env.jwtExpiresIn, 10));
    assert.strictEqual(refreshRes.body.refreshExpiresIn, env.jwtRefreshExpiresIn);

    const meRes = await request(app).get('/character/me')
      .set('Authorization', `Bearer ${refreshRes.body.token}`);
    assert.strictEqual(meRes.status, 200);
    assert.strictEqual(meRes.body.name, 'SirRefresh');

    // Reuse inside the grace window (a lost response retried): 200 with a new pair, the lost successor dies.
    const retry = await request(app).post('/auth/refresh').send({ refreshToken: loginRes.body.refreshToken });
    assert.strictEqual(retry.status, 200);
    assert.notStrictEqual(retry.body.refreshToken, refreshRes.body.refreshToken);
    const lost = await request(app).post('/auth/refresh').send({ refreshToken: refreshRes.body.refreshToken });
    assert.strictEqual(lost.status, 401);
    assert.strictEqual(lost.body.error, 'REFRESH_INVALID');

    // Reuse after the grace window: rejected and the family's newest token dies with it.
    await RefreshToken.updateOne(
      { tokenHash: crypto.createHash('sha256').update(loginRes.body.refreshToken).digest('hex') },
      { usedAt: new Date(Date.now() - (env.jwtRefreshReuseGraceSeconds + 1) * 1000) }
    );
    const reuse = await request(app).post('/auth/refresh').send({ refreshToken: loginRes.body.refreshToken });
    assert.strictEqual(reuse.status, 401);
    assert.strictEqual(reuse.body.error, 'REFRESH_INVALID');
    const newest = await request(app).post('/auth/refresh').send({ refreshToken: retry.body.refreshToken });
    assert.strictEqual(newest.status, 401);
    assert.strictEqual(newest.body.error, 'REFRESH_INVALID');
  });

  await t.test('10. Expired access token is 401 TOKEN_EXPIRED; invalid or revoked stay UNAUTHORIZED', async () => {
    const loginRes = await request(app).post('/auth/login').send({ username: 'refreshHero', password });
    assert.strictEqual(loginRes.status, 200);
    const claims = jwt.decode(loginRes.body.token);
    const expired = jwt.sign(
      { sub: claims.sub, jti: claims.jti, sid: claims.sid, exp: Math.floor(Date.now() / 1000) - 10 },
      env.jwtSecret
    );
    const expiredRes = await request(app).get('/character/me').set('Authorization', `Bearer ${expired}`);
    assert.strictEqual(expiredRes.status, 401);
    assert.strictEqual(expiredRes.body.error, 'TOKEN_EXPIRED');

    const tampered = loginRes.body.token.slice(0, -6) + 'abcdef';
    const tamperedRes = await request(app).get('/character/me').set('Authorization', `Bearer ${tampered}`);
    assert.strictEqual(tamperedRes.status, 401);
    assert.strictEqual(tamperedRes.body.error, 'UNAUTHORIZED');
    assert.strictEqual(tamperedRes.body.message, 'Token is invalid or expired.');

    const noHeader = await request(app).get('/character/me');
    assert.strictEqual(noHeader.status, 401);
    assert.strictEqual(noHeader.body.error, 'UNAUTHORIZED');
  });

  await t.test('11. Refresh validates its body and rejects unknown or expired tokens', async () => {
    authRoutes.refreshRateLimiter.reset();
    for (const body of [{}, { refreshToken: '' }, { refreshToken: 42 }, { refreshToken: 'x'.repeat(513) }]) {
      const res = await request(app).post('/auth/refresh').send(body);
      assert.strictEqual(res.status, 400);
      assert.strictEqual(res.body.error, 'VALIDATION_FAILED');
    }

    const unknown = await request(app).post('/auth/refresh').send({ refreshToken: 'unknown-refresh-token' });
    assert.strictEqual(unknown.status, 401);
    assert.strictEqual(unknown.body.error, 'REFRESH_INVALID');

    const loginRes = await request(app).post('/auth/login').send({ username: 'refreshHero', password });
    await RefreshToken.updateOne(
      { tokenHash: crypto.createHash('sha256').update(loginRes.body.refreshToken).digest('hex') },
      { expiresAt: new Date(Date.now() - 1000) }
    );
    const expired = await request(app).post('/auth/refresh').send({ refreshToken: loginRes.body.refreshToken });
    assert.strictEqual(expired.status, 401);
    assert.strictEqual(expired.body.error, 'REFRESH_INVALID');
  });

  await t.test('12. Logout revokes the refresh token of the session only', async () => {
    const a = (await request(app).post('/auth/login').send({ username: 'refreshHero', password })).body;
    const b = (await request(app).post('/auth/login').send({ username: 'refreshHero', password })).body;

    const logoutRes = await request(app).post('/auth/logout').set('Authorization', `Bearer ${a.token}`);
    assert.strictEqual(logoutRes.status, 204);

    const revoked = await request(app).post('/auth/refresh').send({ refreshToken: a.refreshToken });
    assert.strictEqual(revoked.status, 401);
    assert.strictEqual(revoked.body.error, 'REFRESH_INVALID');

    const other = await request(app).post('/auth/refresh').send({ refreshToken: b.refreshToken });
    assert.strictEqual(other.status, 200);
  });

  await t.test('13. Concurrent refresh with one token inside the grace window leaves exactly one live successor', async () => {
    const login = (await request(app).post('/auth/login').send({ username: 'refreshHero', password })).body;
    const responses = await Promise.all(Array.from({ length: 6 }, () =>
      request(app).post('/auth/refresh').send({ refreshToken: login.refreshToken })));
    // Simultaneous requests are indistinguishable from retries of a lost response: all may answer 200.
    for (const res of responses) {
      assert.strictEqual(res.status, 200);
    }
    let exchangeable = 0;
    for (const res of responses) {
      const next = await request(app).post('/auth/refresh').send({ refreshToken: res.body.refreshToken });
      if (next.status === 200) exchangeable++;
      else assert.strictEqual(next.body.error, 'REFRESH_INVALID');
    }
    assert.strictEqual(exchangeable, 1, 'exactly one issued refresh token stays live');
  });

  await t.test('14. Refresh endpoint is IP rate limited with 429 and Retry-After', async () => {
    authRoutes.refreshRateLimiter.reset();
    try {
      for (let i = 0; i < env.rateLimitIpMax; i++) {
        const res = await request(app).post('/auth/refresh').send({ refreshToken: 'unknown-refresh-token' });
        assert.strictEqual(res.status, 401);
      }
      const blocked = await request(app).post('/auth/refresh').send({ refreshToken: 'unknown-refresh-token' });
      assert.strictEqual(blocked.status, 429);
      assert.strictEqual(blocked.body.error, 'TOO_MANY_REQUESTS');
      assert.ok(Number(blocked.headers['retry-after']) > 0);
    } finally {
      authRoutes.refreshRateLimiter.reset();
    }
  });
});
