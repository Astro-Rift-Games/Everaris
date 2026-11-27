// src/middleware/rateLimiter.js

class MemoryRateLimiter {
  constructor(options = {}) {
    this.windowMs = options.windowMs || 15 * 60 * 1000; // default 15 minutes
    this.max = options.max || 30;
    this.message = options.message || 'Too many requests. Please try again later.';
    this.nowProvider = options.nowProvider || (() => Date.now());
    this.keyGenerator = options.keyGenerator || ((req) => req.ip || 'unknown');
    this.hits = new Map(); // key -> [timestamp1, timestamp2, ...]
  }

  _cleanOldHits(key, now) {
    const cutoff = now - this.windowMs;
    const timestamps = this.hits.get(key);
    if (!timestamps) return [];
    const valid = timestamps.filter((t) => t > cutoff);
    if (valid.length === 0) {
      this.hits.delete(key);
      return [];
    }
    this.hits.set(key, valid);
    return valid;
  }

  isBlocked(key) {
    if (!key) return { blocked: false };
    const now = this.nowProvider();
    const valid = this._cleanOldHits(key, now);
    if (valid.length >= this.max) {
      const oldestHit = valid[0];
      const resetTime = oldestHit + this.windowMs;
      const retryAfterSeconds = Math.max(1, Math.ceil((resetTime - now) / 1000));
      return { blocked: true, retryAfterSeconds };
    }
    return { blocked: false };
  }

  recordHit(key) {
    if (!key) return;
    const now = this.nowProvider();
    const valid = this._cleanOldHits(key, now);
    valid.push(now);
    this.hits.set(key, valid);
  }

  clearKey(key) {
    if (key) {
      this.hits.delete(key);
    }
  }

  reset() {
    this.hits.clear();
  }

  // Express middleware to limit incoming requests
  middleware() {
    return (req, res, next) => {
      const key = this.keyGenerator(req);
      const status = this.isBlocked(key);
      if (status.blocked) {
        res.setHeader('Retry-After', status.retryAfterSeconds);
        return res.status(429).json({
          error: 'TOO_MANY_REQUESTS',
          message: this.message,
          retryAfter: status.retryAfterSeconds
        });
      }
      this.recordHit(key);
      next();
    };
  }
}

// User+IP failed attempt limiter (FIX-37: prevents third-party lockout of a known username from another IP)
class UserFailedLoginLimiter {
  constructor(options = {}) {
    this.windowMs = options.windowMs || 15 * 60 * 1000;
    this.max = options.max || 5;
    this.message = options.message || 'Too many failed login attempts for this account. Please try again later.';
    this.nowProvider = options.nowProvider || (() => Date.now());
    this.failures = new Map(); // "username::ip" -> [timestamp1, ...]
  }

  _buildKey(username, ip = '') {
    if (!username || typeof username !== 'string') return '';
    const normUser = username.toLowerCase().trim();
    if (!normUser) return '';
    const normIp = typeof ip === 'string' ? ip.trim() : '';
    return normIp ? `${normUser}::${normIp}` : normUser;
  }

  _cleanOldFailures(key, now) {
    const cutoff = now - this.windowMs;
    const timestamps = this.failures.get(key);
    if (!timestamps) return [];
    const valid = timestamps.filter((t) => t > cutoff);
    if (valid.length === 0) {
      this.failures.delete(key);
      return [];
    }
    this.failures.set(key, valid);
    return valid;
  }

  isBlocked(username, ip = '') {
    const key = this._buildKey(username, ip);
    if (!key) return { blocked: false };
    const now = this.nowProvider();
    const valid = this._cleanOldFailures(key, now);
    if (valid.length >= this.max) {
      const oldest = valid[0];
      const resetTime = oldest + this.windowMs;
      const retryAfterSeconds = Math.max(1, Math.ceil((resetTime - now) / 1000));
      return { blocked: true, retryAfterSeconds };
    }
    return { blocked: false };
  }

  recordFailure(username, ip = '') {
    const key = this._buildKey(username, ip);
    if (!key) return;
    const now = this.nowProvider();
    const valid = this._cleanOldFailures(key, now);
    valid.push(now);
    this.failures.set(key, valid);
  }

  clearFailures(username, ip = '') {
    const key = this._buildKey(username, ip);
    if (!key) return;
    this.failures.delete(key);
  }

  reset() {
    this.failures.clear();
  }

  // Pre-check middleware: verifies if (username, ip) is currently locked out
  preCheck() {
    return (req, res, next) => {
      const username = req.body && req.body.username;
      const ip = req.ip || '';
      const status = this.isBlocked(username, ip);
      if (status.blocked) {
        res.setHeader('Retry-After', status.retryAfterSeconds);
        return res.status(429).json({
          error: 'TOO_MANY_REQUESTS',
          message: this.message,
          retryAfter: status.retryAfterSeconds
        });
      }
      next();
    };
  }
}

module.exports = {
  MemoryRateLimiter,
  UserFailedLoginLimiter
};
