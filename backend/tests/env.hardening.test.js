// tests/env.hardening.test.js
const { test, describe } = require('node:test');
const assert = require('node:assert/strict');
const errorHandler = require('../src/middleware/errorHandler');

function loadEnv(overrides) {
  const env = {
    NODE_ENV: 'test', PORT: '3000', MONGODB_URI: 'mongodb://localhost:27017/test',
    JWT_SECRET: 'secret', ...overrides
  };
  return require('node:child_process').spawnSync(process.execPath,
    ['-e', `console.log(JSON.stringify(require(${JSON.stringify(require('node:path').resolve(__dirname, '../src/config/env.js'))})))`],
    { env, encoding: 'utf8', cwd: __dirname });
}

test('startup rejects missing NODE_ENV even with a weak JWT_SECRET', () => {
  const result = loadEnv({ NODE_ENV: undefined });
  assert.notEqual(result.status, 0, 'Missing NODE_ENV must fail closed');
  assert.match(result.stderr, /NODE_ENV/);
});

test('startup rejects unrestricted TRUST_PROXY=true', () => {
  const result = loadEnv({ TRUST_PROXY: 'true' });
  assert.notEqual(result.status, 0, 'TRUST_PROXY=true must be rejected');
  assert.match(result.stderr, /TRUST_PROXY/);
});

test('TRUST_PROXY=1 is a numeric hop count', () => {
  const result = loadEnv({ TRUST_PROXY: '1' });
  assert.equal(result.status, 0);
  assert.equal(JSON.parse(result.stdout.trim().split('\n').at(-1)).trustProxy, 1);
});

for (const value of ['false', '0', '2', '-1', '1.5', 'loopback', '9007199254740992']) {
  test(`TRUST_PROXY validates ${value}`, () => {
    const result = loadEnv({ TRUST_PROXY: value });
    if (['false', '0', '2'].includes(value)) {
      assert.equal(result.status, 0);
      assert.equal(JSON.parse(result.stdout.trim().split('\n').at(-1)).trustProxy, value === 'false' ? false : Number(value));
    } else {
      assert.notEqual(result.status, 0);
      assert.match(result.stderr, /TRUST_PROXY/);
    }
  });
}

describe('Environment Hardening and Error Sanitization (US-60 / SEC-60.3)', () => {
  test('errorHandler sanitizes 500 errors and strips database messages, details and stack traces', () => {
    const rawDbError = new Error('MongoServerError: authentication failed for user "admin" on db "production"');
    rawDbError.statusCode = 500;
    rawDbError.details = { rawQuery: 'db.accounts.find({ secret: "xyz" })' };

    const headers = {};
    const res = {
      statusCode: 200,
      headers,
      status(code) { this.statusCode = code; return this; },
      json(body) { this.body = body; return this; }
    };

    errorHandler(rawDbError, {}, res, () => {});

    assert.equal(res.statusCode, 500);
    assert.equal(res.body.error, 'INTERNAL_SERVER_ERROR');
    assert.equal(res.body.message, 'An unexpected error occurred.');
    assert.equal(res.body.details, undefined, 'Details must not be exposed in 500 errors');
    assert.equal(JSON.stringify(res.body).includes('MongoServerError'), false, 'DB error details must not leak');
  });

  test('errorHandler treats unhandled exceptions without statusCode as 500 and sanitizes them', () => {
    const unhandledException = new TypeError('Cannot read properties of undefined (reading "_id")');

    const res = {
      status(code) { this.statusCode = code; return this; },
      json(body) { this.body = body; return this; }
    };

    errorHandler(unhandledException, {}, res, () => {});

    assert.equal(res.statusCode, 500);
    assert.equal(res.body.error, 'INTERNAL_SERVER_ERROR');
    assert.equal(res.body.message, 'An unexpected error occurred.');
    assert.equal(JSON.stringify(res.body).includes('Cannot read properties'), false);
  });

  test('errorHandler preserves domain errors with 4xx status codes and structured details', () => {
    const domainError = {
      statusCode: 422,
      errorCode: 'UNPROCESSABLE_ENTITY',
      message: 'Insufficient attribute points',
      details: [{ field: 'strength', reason: 'exceeds cap' }]
    };

    const res = {
      status(code) { this.statusCode = code; return this; },
      json(body) { this.body = body; return this; }
    };

    errorHandler(domainError, {}, res, () => {});

    assert.equal(res.statusCode, 422);
    assert.equal(res.body.error, 'UNPROCESSABLE_ENTITY');
    assert.equal(res.body.message, 'Insufficient attribute points');
    assert.deepEqual(res.body.details, [{ field: 'strength', reason: 'exceeds cap' }]);
  });

  test('env.js rejects short or weak WEBHOOK_SECRET in production startup', () => {
    const { spawnSync } = require('node:child_process');
    const path = require('node:path');
    const envPath = path.resolve(__dirname, '../src/config/env.js');

    const result = spawnSync(
      process.execPath,
      ['-e', `require(${JSON.stringify(envPath)})`],
      {
        env: {
          ...process.env,
          NODE_ENV: 'production',
          PORT: '3000',
          MONGODB_URI: 'mongodb://localhost:27017/grimhold',
          JWT_SECRET: 'a-very-long-and-high-entropy-production-jwt-secret-2026',
          WEBHOOK_SECRET: 'short-secret'
        },
        encoding: 'utf8', cwd: __dirname
      }
    );

    assert.notEqual(result.status, 0, 'Process must exit with non-zero code on weak WEBHOOK_SECRET in production');
    assert.match(result.stderr, /WEBHOOK_SECRET must be set to a secure value/);
  });

  test('env.js rejects default dev WEBHOOK_SECRET in production when ALLOW_DEV_WEBHOOK_SECRET is unset or false', () => {
    const { spawnSync } = require('node:child_process');
    const path = require('node:path');
    const envPath = path.resolve(__dirname, '../src/config/env.js');

    const result = spawnSync(
      process.execPath,
      ['-e', `require(${JSON.stringify(envPath)})`],
      {
        env: {
          ...process.env,
          NODE_ENV: 'production',
          PORT: '3000',
          MONGODB_URI: 'mongodb://localhost:27017/grimhold',
          JWT_SECRET: 'a-very-long-and-high-entropy-production-jwt-secret-2026',
          WEBHOOK_SECRET: 'dev-webhook-secret-do-not-use-in-prod'
        },
        encoding: 'utf8', cwd: __dirname
      }
    );

    assert.notEqual(result.status, 0, 'Must exit non-zero on dev WEBHOOK_SECRET in production');
    assert.match(result.stderr, /ALLOW_DEV_WEBHOOK_SECRET=true/);
  });

  test('env.js permits dev WEBHOOK_SECRET with warning when ALLOW_DEV_WEBHOOK_SECRET=true (US-61 / SEC-61.4)', () => {
    const { spawnSync } = require('node:child_process');
    const path = require('node:path');
    const envPath = path.resolve(__dirname, '../src/config/env.js');

    const result = spawnSync(
      process.execPath,
      ['-e', `console.log(JSON.stringify(require(${JSON.stringify(envPath)})))`],
      {
        env: {
          ...process.env,
          NODE_ENV: 'production',
          PORT: '3000',
          MONGODB_URI: 'mongodb://localhost:27017/grimhold',
          JWT_SECRET: 'a-very-long-and-high-entropy-production-jwt-secret-2026',
          WEBHOOK_SECRET: 'dev-webhook-secret-do-not-use-in-prod',
          ALLOW_DEV_WEBHOOK_SECRET: 'true'
        },
        encoding: 'utf8', cwd: __dirname
      }
    );

    assert.equal(result.status, 0, 'Must succeed when ALLOW_DEV_WEBHOOK_SECRET=true');
    assert.match(result.stderr, /explicit risk acceptance \(US-61 \/ SEC-61\.4\)/);
    const parsed = JSON.parse(result.stdout.trim().split('\n').at(-1));
    assert.equal(parsed.allowDevWebhookSecret, true);
  });

  test('env.js still rejects weak non-dev WEBHOOK_SECRET even if ALLOW_DEV_WEBHOOK_SECRET=true', () => {
    const { spawnSync } = require('node:child_process');
    const path = require('node:path');
    const envPath = path.resolve(__dirname, '../src/config/env.js');

    const result = spawnSync(
      process.execPath,
      ['-e', `require(${JSON.stringify(envPath)})`],
      {
        env: {
          ...process.env,
          NODE_ENV: 'production',
          PORT: '3000',
          MONGODB_URI: 'mongodb://localhost:27017/grimhold',
          JWT_SECRET: 'a-very-long-and-high-entropy-production-jwt-secret-2026',
          WEBHOOK_SECRET: 'short-secret',
          ALLOW_DEV_WEBHOOK_SECRET: 'true'
        },
        encoding: 'utf8', cwd: __dirname
      }
    );

    assert.notEqual(result.status, 0, 'Weak arbitrary secrets must still be rejected');
    assert.match(result.stderr, /WEBHOOK_SECRET must be set to a secure value/);
  });

  test('env.js rejects synthetic PLACEHOLDER values from .env.example in production startup (US-61 / SEC-61.1)', () => {
    const { spawnSync } = require('node:child_process');
    const path = require('node:path');
    const envPath = path.resolve(__dirname, '../src/config/env.js');

    const jwtPlaceholderResult = spawnSync(
      process.execPath,
      ['-e', `require(${JSON.stringify(envPath)})`],
      {
        env: {
          ...process.env,
          NODE_ENV: 'production',
          PORT: '3000',
          MONGODB_URI: 'mongodb://localhost:27017/grimhold',
          JWT_SECRET: 'PLACEHOLDER_JWT_SECRET_MIN_32_CHARS_LONG',
          WEBHOOK_SECRET: 'a-very-long-and-high-entropy-production-webhook-secret-2026'
        },
        encoding: 'utf8', cwd: __dirname
      }
    );

    assert.notEqual(jwtPlaceholderResult.status, 0, 'Must reject PLACEHOLDER_JWT_SECRET_MIN_32_CHARS_LONG in production');
    assert.match(jwtPlaceholderResult.stderr, /JWT_SECRET must be at least 32 characters/);

    const mongoPlaceholderResult = spawnSync(
      process.execPath,
      ['-e', `require(${JSON.stringify(envPath)})`],
      {
        env: {
          ...process.env,
          NODE_ENV: 'production',
          PORT: '3000',
          MONGODB_URI: 'mongodb+srv://PLACEHOLDER_USER:PLACEHOLDER_PASSWORD@PLACEHOLDER_CLUSTER.mongodb.net/grimhold_dev',
          JWT_SECRET: 'a-very-long-and-high-entropy-production-jwt-secret-2026',
          WEBHOOK_SECRET: 'a-very-long-and-high-entropy-production-webhook-secret-2026'
        },
        encoding: 'utf8', cwd: __dirname
      }
    );

    assert.notEqual(mongoPlaceholderResult.status, 0, 'Must reject MONGODB_URI placeholder in production');
    assert.match(mongoPlaceholderResult.stderr, /MONGODB_URI contains an unresolved placeholder/);
  });
});

test('JWT_REFRESH_EXPIRES_IN defaults to 86400 and accepts a positive integer', () => {
  assert.equal(JSON.parse(loadEnv({}).stdout.trim().split(String.fromCharCode(10)).at(-1)).jwtRefreshExpiresIn, 86400);
  assert.equal(JSON.parse(loadEnv({ JWT_REFRESH_EXPIRES_IN: '120' }).stdout.trim().split(String.fromCharCode(10)).at(-1)).jwtRefreshExpiresIn, 120);
});

test('startup rejects a non-positive or non-numeric JWT_REFRESH_EXPIRES_IN', () => {
  for (const value of ['0', '-5', 'abc']) {
    const result = loadEnv({ JWT_REFRESH_EXPIRES_IN: value });
    assert.notEqual(result.status, 0, value + ' must fail closed');
    assert.match(result.stderr, /JWT_REFRESH_EXPIRES_IN/);
  }
});

test('JWT_REFRESH_REUSE_GRACE_SECONDS defaults to 30 and accepts 0 or a positive integer', () => {
  const last = (r) => JSON.parse(r.stdout.trim().split(String.fromCharCode(10)).at(-1));
  assert.equal(last(loadEnv({})).jwtRefreshReuseGraceSeconds, 30);
  assert.equal(last(loadEnv({ JWT_REFRESH_REUSE_GRACE_SECONDS: '0' })).jwtRefreshReuseGraceSeconds, 0);
  assert.equal(last(loadEnv({ JWT_REFRESH_REUSE_GRACE_SECONDS: '120' })).jwtRefreshReuseGraceSeconds, 120);
});

test('startup rejects a negative or non-numeric JWT_REFRESH_REUSE_GRACE_SECONDS', () => {
  for (const value of ['-1', 'abc']) {
    const result = loadEnv({ JWT_REFRESH_REUSE_GRACE_SECONDS: value });
    assert.notEqual(result.status, 0, value + ' must fail closed');
    assert.match(result.stderr, /JWT_REFRESH_REUSE_GRACE_SECONDS/);
  }
});

describe('FIX-23 non-production hardening: staging/dev secrets and debug/legacy endpoints', () => {
  const STRONG_JWT = 'a-very-long-and-high-entropy-staging-jwt-secret-2026-fix23';
  const STRONG_WEBHOOK = 'a-very-long-and-high-entropy-staging-webhook-secret-2026-fix23';

  test('NODE_ENV=staging disables debug and legacy extraction flags even when env flags are true', () => {
    const result = loadEnv({
      NODE_ENV: 'staging',
      JWT_SECRET: STRONG_JWT,
      WEBHOOK_SECRET: STRONG_WEBHOOK,
      ENABLE_DEV_DEBUG_ROUTES: 'true',
      ENABLE_LEGACY_EXTRACTION: 'true'
    });
    assert.equal(result.status, 0, `Expected staging startup to succeed with strong secrets, stderr=${result.stderr}`);
    const parsed = JSON.parse(result.stdout.trim().split('\n').at(-1));
    assert.equal(parsed.enableDebugRoutes, false, 'staging must never enable debug routes');
    assert.equal(parsed.enableLegacyExtraction, false, 'staging must never enable legacy extraction');
  });

  test('staging HTTP requests to /debug/mock-fusion-result and /me/inventory/extraction return 404 even with flags enabled', async () => {
    const envConfig = require('../src/config/env');
    const RevokedToken = require('../src/models/RevokedToken');
    const app = require('../src/app');
    const request = require('supertest');
    const jwt = require('jsonwebtoken');

    const prevNodeEnv = envConfig.nodeEnv;
    const prevDebug = process.env.ENABLE_DEV_DEBUG_ROUTES;
    const prevLegacy = process.env.ENABLE_LEGACY_EXTRACTION;
    const origExists = RevokedToken.exists;
    const token = jwt.sign({ accountId: 'acc-fix23' }, envConfig.jwtSecret, { expiresIn: '1h' });

    envConfig.nodeEnv = 'staging';
    process.env.ENABLE_DEV_DEBUG_ROUTES = 'true';
    process.env.ENABLE_LEGACY_EXTRACTION = 'true';
    RevokedToken.exists = async () => null;

    try {
      const debugRes = await request(app)
        .post('/character/debug/mock-fusion-result')
        .set('Authorization', `Bearer ${token}`)
        .send({ raidId: 'raid-1', items: [] });
      assert.equal(debugRes.status, 404);
      assert.equal(debugRes.body.error, 'NOT_FOUND');

      const legacyRes = await request(app)
        .post('/character/me/inventory/extraction')
        .set('Authorization', `Bearer ${token}`)
        .send({ raidId: 'raid-1', resultSequence: 1, items: [] });
      assert.equal(legacyRes.status, 404);
      assert.equal(legacyRes.body.error, 'NOT_FOUND');
    } finally {
      RevokedToken.exists = origExists;
      envConfig.nodeEnv = prevNodeEnv;
      if (prevDebug === undefined) delete process.env.ENABLE_DEV_DEBUG_ROUTES;
      else process.env.ENABLE_DEV_DEBUG_ROUTES = prevDebug;
      if (prevLegacy === undefined) delete process.env.ENABLE_LEGACY_EXTRACTION;
      else process.env.ENABLE_LEGACY_EXTRACTION = prevLegacy;
    }
  });

  test('startup rejects default/unset WEBHOOK_SECRET in staging and development unless ALLOW_DEV_WEBHOOK_SECRET=true', () => {
    for (const nodeEnv of ['staging', 'development']) {
      const unsetRes = loadEnv({ NODE_ENV: nodeEnv, JWT_SECRET: STRONG_JWT, WEBHOOK_SECRET: undefined });
      assert.notEqual(unsetRes.status, 0, `${nodeEnv} must fail closed when WEBHOOK_SECRET falls back to default`);
      assert.match(unsetRes.stderr, /WEBHOOK_SECRET/);

      const explicitDefault = loadEnv({
        NODE_ENV: nodeEnv,
        JWT_SECRET: STRONG_JWT,
        WEBHOOK_SECRET: 'dev-webhook-secret-do-not-use-in-prod'
      });
      assert.notEqual(explicitDefault.status, 0, `${nodeEnv} must fail closed on default WEBHOOK_SECRET`);
      assert.match(explicitDefault.stderr, /WEBHOOK_SECRET/);
    }
  });

  test('staging rejects weak JWT_SECRET and placeholder MONGODB_URI (fail-closed)', () => {
    const weakJwt = loadEnv({
      NODE_ENV: 'staging',
      JWT_SECRET: 'short',
      WEBHOOK_SECRET: STRONG_WEBHOOK
    });
    assert.notEqual(weakJwt.status, 0, 'staging must reject weak JWT_SECRET');
    assert.match(weakJwt.stderr, /JWT_SECRET/);

    const placeholderMongo = loadEnv({
      NODE_ENV: 'staging',
      JWT_SECRET: STRONG_JWT,
      WEBHOOK_SECRET: STRONG_WEBHOOK,
      MONGODB_URI: 'mongodb+srv://PLACEHOLDER_USER:PLACEHOLDER_PASSWORD@PLACEHOLDER_CLUSTER.mongodb.net/grimhold'
    });
    assert.notEqual(placeholderMongo.status, 0, 'staging must reject placeholder MONGODB_URI');
    assert.match(placeholderMongo.stderr, /MONGODB_URI/);
  });

  test('development with explicit strong secrets still permits opt-in debug/legacy flags for local dev', () => {
    const res = loadEnv({
      NODE_ENV: 'development',
      JWT_SECRET: STRONG_JWT,
      WEBHOOK_SECRET: STRONG_WEBHOOK,
      ENABLE_DEV_DEBUG_ROUTES: 'true',
      ENABLE_LEGACY_EXTRACTION: 'true'
    });
    assert.equal(res.status, 0, `Expected local development startup to succeed, stderr=${res.stderr}`);
    const parsed = JSON.parse(res.stdout.trim().split('\n').at(-1));
    assert.equal(parsed.enableDebugRoutes, true);
    assert.equal(parsed.enableLegacyExtraction, true);
  });

  test('FIX-35: NODE_ENV=staging enforces production secret rules (rejects JWT_SECRET=secret and sets isProductionLike)', () => {
    const weakSecret = loadEnv({
      NODE_ENV: 'staging',
      JWT_SECRET: 'secret',
      WEBHOOK_SECRET: STRONG_WEBHOOK
    });
    assert.notEqual(weakSecret.status, 0, 'staging must reject JWT_SECRET=secret');
    assert.match(weakSecret.stderr, /JWT_SECRET/);

    const okStaging = loadEnv({
      NODE_ENV: 'staging',
      JWT_SECRET: STRONG_JWT,
      WEBHOOK_SECRET: STRONG_WEBHOOK
    });
    assert.equal(okStaging.status, 0);
    const parsed = JSON.parse(okStaging.stdout.trim().split('\n').at(-1));
    assert.equal(parsed.isProductionLike, true);
  });
});
