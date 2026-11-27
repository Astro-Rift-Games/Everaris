// src/config/env.js
require('dotenv').config();

const REQUIRED = ['NODE_ENV', 'MONGODB_URI', 'JWT_SECRET', 'PORT'];

const missing = REQUIRED.filter((key) => !process.env[key]);
if (missing.length > 0) {
  console.error('[Config] Missing required environment variables: ' + missing.join(', '));
  process.exit(1);
}

const ALLOWED_ENVS = ['development', 'test', 'staging', 'production'];
if (process.env.NODE_ENV && !ALLOWED_ENVS.includes(process.env.NODE_ENV)) {
  console.error(`[Config] Invalid NODE_ENV: '${process.env.NODE_ENV}'. Allowed values: ${ALLOWED_ENVS.join(', ')}`);
  process.exit(1);
}

const nodeEnv = process.env.NODE_ENV;
const LOCAL_DEBUG_ENVS = ['development', 'test'];
const DEFAULT_DEV_WEBHOOK_SECRET = 'dev-webhook-secret-do-not-use-in-prod';
const webhookSecret = (process.env.WEBHOOK_SECRET || DEFAULT_DEV_WEBHOOK_SECRET).trim();
const allowDevWebhookSecret = process.env.ALLOW_DEV_WEBHOOK_SECRET === 'true';

const INSECURE_JWT_SECRETS = ['dev-secret', 'secret', 'changeme', 'jwt-secret', 'default', '123456'];
const PLACEHOLDER_VALUE_REGEX = /placeholder|<user>|<password>|<cluster>|changeme|example/i;

// FIX-23: Fail closed on default/weak secrets outside automated tests (covers staging, production, and development).
if (nodeEnv !== 'test') {
  if (webhookSecret === DEFAULT_DEV_WEBHOOK_SECRET) {
    if (!allowDevWebhookSecret) {
      console.error(`[Config] CRITICAL: WEBHOOK_SECRET is set to the default development secret in ${nodeEnv}. To explicitly accept this risk for local/MVP builds, set ALLOW_DEV_WEBHOOK_SECRET=true (US-61 / SEC-61.4).`);
      process.exit(1);
    }
    console.warn(`[Config] WARNING: Running in ${nodeEnv} with ${DEFAULT_DEV_WEBHOOK_SECRET} under explicit risk acceptance (US-61 / SEC-61.4). Deferred to #400.`);
  } else if (
    webhookSecret.length < 32 ||
    INSECURE_JWT_SECRETS.includes(webhookSecret.toLowerCase()) ||
    PLACEHOLDER_VALUE_REGEX.test(webhookSecret)
  ) {
    console.error(`[Config] CRITICAL: WEBHOOK_SECRET must be set to a secure value of at least 32 characters in ${nodeEnv}.`);
    process.exit(1);
  }

  const trimmedSecret = (process.env.JWT_SECRET || '').trim();
  if (
    trimmedSecret.length < 32 ||
    INSECURE_JWT_SECRETS.includes(trimmedSecret.toLowerCase()) ||
    PLACEHOLDER_VALUE_REGEX.test(trimmedSecret)
  ) {
    console.error(`[Config] CRITICAL: JWT_SECRET must be at least 32 characters of high entropy in ${nodeEnv}.`);
    process.exit(1);
  }

  const trimmedMongoUri = (process.env.MONGODB_URI || '').trim();
  if (PLACEHOLDER_VALUE_REGEX.test(trimmedMongoUri)) {
    console.error(`[Config] CRITICAL: MONGODB_URI contains an unresolved placeholder in ${nodeEnv}.`);
    process.exit(1);
  }
}

// Only explicit hop counts are supported; boolean true trusts attacker-supplied chains.
const rawTrustProxy = process.env.TRUST_PROXY;
let trustProxy = false;
if (rawTrustProxy && rawTrustProxy !== 'false') {
  if (!/^\d+$/.test(rawTrustProxy) || !Number.isSafeInteger(Number(rawTrustProxy))) {
    console.error('[Config] TRUST_PROXY must be false or a non-negative integer hop count; true is unsafe.');
    process.exit(1);
  }
  trustProxy = Number(rawTrustProxy);
}

// FIX-34: PLACEHOLDER. Game Design has not defined the refresh token lifetime;
// edit JWT_REFRESH_EXPIRES_IN (or this default, seconds) once it does.
const jwtRefreshExpiresIn = parseInt(process.env.JWT_REFRESH_EXPIRES_IN || '86400', 10);
if (!Number.isSafeInteger(jwtRefreshExpiresIn) || jwtRefreshExpiresIn <= 0) {
  console.error('[Config] JWT_REFRESH_EXPIRES_IN must be a positive integer number of seconds.');
  process.exit(1);
}

// FIX-34 (L9): a rotated refresh token presented again within this many seconds of its first use
// is treated as a retry of a lost response, not as theft. 0 disables the grace (strict reuse detection).
const jwtRefreshReuseGraceSeconds = parseInt(process.env.JWT_REFRESH_REUSE_GRACE_SECONDS || '30', 10);
if (!Number.isSafeInteger(jwtRefreshReuseGraceSeconds) || jwtRefreshReuseGraceSeconds < 0) {
  console.error('[Config] JWT_REFRESH_REUSE_GRACE_SECONDS must be a non-negative integer number of seconds (0 disables the grace window).');
  process.exit(1);
}

module.exports = {
  port: parseInt(process.env.PORT, 10),
  mongodbUri: process.env.MONGODB_URI,
  jwtSecret: process.env.JWT_SECRET,
  jwtExpiresIn: process.env.JWT_EXPIRES_IN || '3600',
  jwtRefreshExpiresIn,
  jwtRefreshReuseGraceSeconds,
  nodeEnv: nodeEnv,
  webhookSecret: webhookSecret,
  allowDevWebhookSecret: allowDevWebhookSecret,
  get inventoryProtocolGate() {
    return process.env.INVENTORY_PROTOCOL_GATE === 'true';
  },
  get inventoryInstanceProtocolGate() {
    return process.env.INVENTORY_INSTANCE_PROTOCOL_GATE === 'true';
  },
  get newInventorySchemaVersion() {
    return this.inventoryInstanceProtocolGate ? 3 : this.inventoryProtocolGate ? 2 : 1;
  },
  get inventorySplitEnabled() {
    return process.env.INVENTORY_SPLIT_ENABLED !== 'false';
  },
  get enableDebugRoutes() {
    return LOCAL_DEBUG_ENVS.includes(this.nodeEnv) && process.env.ENABLE_DEV_DEBUG_ROUTES === 'true';
  },
  get enableLegacyExtraction() {
    return LOCAL_DEBUG_ENVS.includes(this.nodeEnv) && process.env.ENABLE_LEGACY_EXTRACTION === 'true';
  },
  trustProxy,
  rateLimitWindowMs: parseInt(process.env.AUTH_RATE_LIMIT_WINDOW_MS || '900000', 10),
  rateLimitIpMax: parseInt(process.env.AUTH_RATE_LIMIT_IP_MAX || '30', 10),
  // FIX-95: PLACEHOLDER. Game Design has not defined the pre-start refund window N (01 §14);
  // edit RESERVATION_REFUND_WINDOW_MS (or this default) once it does.
  reservationRefundWindowMs: parseInt(process.env.RESERVATION_REFUND_WINDOW_MS || '120000', 10),
  rateLimitUserMax: parseInt(process.env.AUTH_RATE_LIMIT_USER_MAX || '5', 10),
};
