// src/services/AuthService.js
const crypto = require('crypto');
const bcrypt = require('bcryptjs');
const jwt = require('jsonwebtoken');
const Account = require('../models/Account');
const RevokedToken = require('../models/RevokedToken');
const RefreshToken = require('../models/RefreshToken');
const config = require('../config/env');
const { isDuplicateKeyError } = require('../utils/mongoErrors');

const PASSWORD_HASH_ROUNDS = 10;

// Unknown usernames are compared against this hash so both failure branches cost
// one bcrypt comparison and the response time does not reveal existing accounts.
let dummyPasswordHashPromise = null;
function getDummyPasswordHash() {
  if (!dummyPasswordHashPromise) {
    dummyPasswordHashPromise = bcrypt.hash(crypto.randomBytes(16).toString('hex'), PASSWORD_HASH_ROUNDS);
  }
  return dummyPasswordHashPromise;
}

function invalidCredentialsError() {
  return {
    statusCode: 401,
    errorCode: 'INVALID_CREDENTIALS',
    message: 'Invalid username or password.'
  };
}

function usernameTakenError() {
  return {
    statusCode: 409,
    errorCode: 'USERNAME_TAKEN',
    message: 'Username already exists'
  };
}

function refreshInvalidError() {
  return {
    statusCode: 401,
    errorCode: 'REFRESH_INVALID',
    message: 'Refresh token is invalid, expired or has been revoked.'
  };
}

function hashToken(value) {
  return crypto.createHash('sha256').update(value).digest('hex');
}

// 'sub' contains the account ID, 'jti' uniquely identifies the token and 'sid' its refresh family.
function signAccessToken(accountId, familyId) {
  const jti = crypto.randomUUID();
  return jwt.sign({ sub: accountId, jti, sid: familyId }, config.jwtSecret, {
    expiresIn: config.jwtExpiresIn + 's'
  });
}

function buildRefreshToken(accountId, familyId) {
  const refreshToken = crypto.randomBytes(32).toString('base64url');
  return {
    refreshToken,
    document: {
      accountId,
      familyId,
      tokenHash: hashToken(refreshToken),
      expiresAt: new Date(Date.now() + config.jwtRefreshExpiresIn * 1000)
    }
  };
}

function sessionResponse(token, refreshToken) {
  return {
    token,
    expiresIn: parseInt(config.jwtExpiresIn, 10),
    refreshToken,
    refreshExpiresIn: config.jwtRefreshExpiresIn
  };
}

// Starts a new refresh family and returns the access/refresh pair of a login or register.
async function issueSession(accountId) {
  const familyId = crypto.randomUUID();
  const { refreshToken, document } = buildRefreshToken(accountId, familyId);
  await RefreshToken.create(document);
  return sessionResponse(signAccessToken(accountId, familyId), refreshToken);
}

function revokeRefreshFamily(familyId, accountId) {
  const filter = { familyId, revokedAt: null };
  if (accountId) filter.accountId = accountId;
  return RefreshToken.updateMany(filter, { $set: { revokedAt: new Date() } });
}

class AuthService {
  /**
   * Authenticates a user and returns a JWT token with unique jti.
   * @throws 401 error if credentials are invalid.
   */
  static async register(username, password) {
    const existingAccount = await Account.findOne({ username: username.toLowerCase().trim() });
    if (existingAccount) {
      throw usernameTakenError();
    }

    const passwordHash = await bcrypt.hash(password, PASSWORD_HASH_ROUNDS);
    const account = new Account({ username, passwordHash });
    try {
      await account.save();
    } catch (error) {
      // A concurrent registration can pass the check above; the unique index decides.
      if (isDuplicateKeyError(error)) throw usernameTakenError();
      throw error;
    }

    return issueSession(account._id);
  }

  static async login(username, password) {
    const account = await Account.findOne({ username: username.toLowerCase() });
    if (!account) {
      await bcrypt.compare(password, await getDummyPasswordHash());
      throw invalidCredentialsError();
    }

    const isMatch = await bcrypt.compare(password, account.passwordHash);
    if (!isMatch) {
      throw invalidCredentialsError();
    }

    return issueSession(account._id);
  }

  /**
   * Exchanges a refresh token for a new access/refresh pair of the same family (FIX-34).
   * A refresh token is single use. Presenting a used one again within the reuse grace window
   * (`jwtRefreshReuseGraceSeconds`, measured from its first use) is a retry of a lost response:
   * the unused successor issued earlier is revoked and a new one is issued. Outside the window
   * (or with a 0 grace) it is reuse and revokes the whole family.
   * @throws 401 REFRESH_INVALID for unknown, expired, revoked or reused tokens.
   */
  static async refresh(refreshToken) {
    const graceMs = config.jwtRefreshReuseGraceSeconds * 1000;
    const current = await RefreshToken.findOne({ tokenHash: hashToken(refreshToken) });
    if (!current) {
      throw refreshInvalidError();
    }

    const now = Date.now();
    if (current.usedAt) {
      const withinGrace = graceMs > 0
        && !current.revokedAt
        && current.expiresAt.getTime() > now
        && now - current.usedAt.getTime() <= graceMs;
      if (!withinGrace) {
        await revokeRefreshFamily(current.familyId);
        throw refreshInvalidError();
      }
    } else if (current.revokedAt || current.expiresAt.getTime() <= now) {
      throw refreshInvalidError();
    }

    // The successor exists before the claim: a concurrent reuse that revokes the family right
    // after the claim also covers it, so no live token survives a revoked family.
    const successor = buildRefreshToken(current.accountId, current.familyId);
    await RefreshToken.create(successor.document);

    // One atomic step either consumes the token (first use) or, inside the grace window, keeps its
    // original usedAt; in both cases the new successor replaces the previous one as the parent's
    // successor. Concurrent requests serialize on this document, so each one learns exactly which
    // successor it displaced and no more than one stays live.
    const reusableUse = graceMs > 0 ? [{ usedAt: { $gte: new Date(now - graceMs) } }] : [];
    const previous = await RefreshToken.findOneAndUpdate(
      {
        _id: current._id,
        revokedAt: null,
        expiresAt: { $gt: new Date() },
        $or: [{ usedAt: null }, ...reusableUse]
      },
      [{ $set: { usedAt: { $ifNull: ['$usedAt', new Date()] }, successorHash: successor.document.tokenHash } }],
      { returnDocument: 'before', updatePipeline: true }
    );
    if (!previous) {
      await RefreshToken.deleteOne({ tokenHash: successor.document.tokenHash });
      const latest = await RefreshToken.findById(current._id);
      if (latest && latest.usedAt) {
        await revokeRefreshFamily(current.familyId);
      }
      throw refreshInvalidError();
    }

    if (previous.successorHash) {
      // The lost successor, unless the client did receive and use it.
      await RefreshToken.updateOne(
        { tokenHash: previous.successorHash, usedAt: null, revokedAt: null },
        { $set: { revokedAt: new Date() } }
      );
    }

    return sessionResponse(signAccessToken(current.accountId, current.familyId), successor.refreshToken);
  }

  /**
   * Revokes the specified session token persistently.
   * Other sessions of the same account remain unaffected.
   */
  static async logout(accountId, token) {
    if (!token) {
      return false;
    }

    const tokenHash = crypto.createHash('sha256').update(token).digest('hex');
    let expiresAt;
    let jti = null;
    let sid = null;

    try {
      const decoded = jwt.decode(token);
      if (decoded && decoded.exp) {
        expiresAt = new Date(decoded.exp * 1000);
        jti = decoded.jti || null;
        sid = typeof decoded.sid === 'string' ? decoded.sid : null;
      }
    } catch (_err) {
      // ignore decode error, will use fallback expiry
    }

    if (!expiresAt || isNaN(expiresAt.getTime())) {
      expiresAt = new Date(Date.now() + (parseInt(config.jwtExpiresIn, 10) || 3600) * 1000);
    }

    await RevokedToken.findOneAndUpdate(
      { tokenHash },
      {
        tokenHash,
        jti,
        accountId,
        expiresAt,
        revokedAt: new Date(),
        reason: 'logout'
      },
      { upsert: true, returnDocument: 'after' }
    );

    // FIX-34: the session's refresh tokens die with it. Tokens issued before FIX-34 have no sid.
    if (sid) {
      await revokeRefreshFamily(sid, accountId);
    }

    return true;
  }
}

module.exports = AuthService;
