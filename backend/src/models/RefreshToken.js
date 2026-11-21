// src/models/RefreshToken.js
// FIX-34: rotating refresh tokens. Only the SHA-256 hash of the opaque token is stored.
const mongoose = require('mongoose');

const RefreshTokenSchema = new mongoose.Schema({
  accountId: {
    type: mongoose.Schema.Types.ObjectId,
    ref: 'Account',
    required: true,
    index: true
  },
  // Shared by every token rotated from one login/register; carried by the access JWT as `sid`.
  familyId: {
    type: String,
    required: true,
    index: true
  },
  tokenHash: {
    type: String,
    required: true,
    unique: true
  },
  expiresAt: {
    type: Date,
    required: true
  },
  // Set (atomically) when the token is exchanged; a second presentation is reuse.
  usedAt: {
    type: Date,
    default: null
  },
  // Set on logout or reuse detection; the whole family is revoked together.
  revokedAt: {
    type: Date,
    default: null
  },
  createdAt: {
    type: Date,
    default: Date.now
  }
});

// TTL index: MongoDB will automatically remove documents when expiresAt is reached.
RefreshTokenSchema.index({ expiresAt: 1 }, { expireAfterSeconds: 0 });

module.exports = mongoose.model('RefreshToken', RefreshTokenSchema);
