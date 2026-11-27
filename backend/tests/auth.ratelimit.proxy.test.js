'use strict';

const { test, describe, beforeEach } = require('node:test');
const assert = require('node:assert/strict');
const express = require('express');
const request = require('supertest');
const authRoutes = require('../src/routes/auth.routes');
const AuthService = require('../src/services/AuthService');
const errorHandler = require('../src/middleware/errorHandler');

function loadEnv(overrides) {
  const env = {
    NODE_ENV: 'test',
    PORT: '3000',
    MONGODB_URI: 'mongodb://localhost:27017/test',
    JWT_SECRET: 'a-very-long-and-high-entropy-secret-value-for-fix37-tests-2026',
    WEBHOOK_SECRET: 'a-very-long-and-high-entropy-webhook-value-for-fix37-tests-2026',
    ...overrides
  };
  return require('node:child_process').spawnSync(
    process.execPath,
    ['-e', `console.log(JSON.stringify(require(${JSON.stringify(require('node:path').resolve(__dirname, '../src/config/env.js'))})))`],
    { env, encoding: 'utf8', cwd: __dirname }
  );
}

describe('FIX-37: per-(username, IP) login lockout and proxy-aware IP rate limiting', () => {
  beforeEach(() => {
    authRoutes.ipRateLimiter.reset();
    authRoutes.userLoginLimiter.reset();
    authRoutes.refreshRateLimiter.reset();
  });

  test('5 failed logins from an attacker IP do not block the legitimate owner logging in from another IP', async t => {
    const origLogin = AuthService.login;
    t.after(() => { AuthService.login = origLogin; });

    AuthService.login = async (username, password) => {
      if (username === 'alice' && password === 'RightPass123!') {
        return { token: 'ok-token', refreshToken: 'ok-refresh', accountId: 'acc1' };
      }
      throw { statusCode: 401, errorCode: 'INVALID_CREDENTIALS', message: 'Invalid username or password.' };
    };

    const proxyApp = express();
    proxyApp.set('trust proxy', 1);
    proxyApp.use(express.json());
    proxyApp.use('/auth', authRoutes);
    proxyApp.use(errorHandler);

    // Attacker sends 5 wrong passwords for alice from 198.51.100.10.
    for (let i = 0; i < 5; i++) {
      const failRes = await request(proxyApp)
        .post('/auth/login')
        .set('X-Forwarded-For', '198.51.100.10')
        .send({ username: 'alice', password: 'WrongPassword!' });
      assert.equal(failRes.status, 401);
    }

    // 6th attempt from the attacker IP is locked out with 429.
    const attackerBlocked = await request(proxyApp)
      .post('/auth/login')
      .set('X-Forwarded-For', '198.51.100.10')
      .send({ username: 'alice', password: 'RightPass123!' });
    assert.equal(attackerBlocked.status, 429);
    assert.equal(attackerBlocked.body.error, 'TOO_MANY_REQUESTS');

    // Legitimate owner from another IP (203.0.113.20) is NOT blocked and succeeds!
    const ownerRes = await request(proxyApp)
      .post('/auth/login')
      .set('X-Forwarded-For', '203.0.113.20')
      .send({ username: 'alice', password: 'RightPass123!' });
    assert.equal(ownerRes.status, 200);
    assert.equal(ownerRes.body.token, 'ok-token');
  });

  test('two clients behind a single reverse proxy have independent IP rate-limit counters', async t => {
    const origLogin = AuthService.login;
    t.after(() => { AuthService.login = origLogin; });
    AuthService.login = async () => ({ token: 'ok', refreshToken: 'r', accountId: 'acc1' });

    const origMax = authRoutes.ipRateLimiter.max;
    authRoutes.ipRateLimiter.max = 2;
    t.after(() => { authRoutes.ipRateLimiter.max = origMax; });

    const proxyApp = express();
    proxyApp.set('trust proxy', 1);
    proxyApp.use(express.json());
    proxyApp.use('/auth', authRoutes);
    proxyApp.use(errorHandler);

    // Client A exhausts its 2-request IP budget.
    for (let i = 0; i < 2; i++) {
      const res = await request(proxyApp)
        .post('/auth/login')
        .set('X-Forwarded-For', '198.51.100.50')
        .send({ username: `userA_${i}`, password: 'Password1!' });
      assert.equal(res.status, 200);
    }
    const blockedA = await request(proxyApp)
      .post('/auth/login')
      .set('X-Forwarded-For', '198.51.100.50')
      .send({ username: 'userA_3', password: 'Password1!' });
    assert.equal(blockedA.status, 429);

    // Client B behind the same proxy still has its own independent counter.
    const resB = await request(proxyApp)
      .post('/auth/login')
      .set('X-Forwarded-For', '203.0.113.99')
      .send({ username: 'userB', password: 'Password1!' });
    assert.equal(resB.status, 200);
  });

  test('staging and production default TRUST_PROXY to 1 hop when unset, while allowing explicit 0/false override', () => {
    for (const nodeEnv of ['staging', 'production']) {
      const defaulted = loadEnv({ NODE_ENV: nodeEnv, TRUST_PROXY: undefined });
      assert.equal(defaulted.status, 0, `${nodeEnv} startup failed: ${defaulted.stderr}`);
      assert.equal(JSON.parse(defaulted.stdout.trim().split('\n').at(-1)).trustProxy, 1);

      const disabled = loadEnv({ NODE_ENV: nodeEnv, TRUST_PROXY: 'false' });
      assert.equal(disabled.status, 0);
      assert.equal(JSON.parse(disabled.stdout.trim().split('\n').at(-1)).trustProxy, false);
    }

    const devDefault = loadEnv({ NODE_ENV: 'development', TRUST_PROXY: undefined });
    assert.equal(devDefault.status, 0);
    assert.equal(JSON.parse(devDefault.stdout.trim().split('\n').at(-1)).trustProxy, false);
  });
});
