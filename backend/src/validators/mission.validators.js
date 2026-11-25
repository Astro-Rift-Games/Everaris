'use strict';

const { body } = require('express-validator');
const validate = require('../middleware/validate');

const ALLOWED_CLAIM_FIELDS = ['transactionId', 'missionId', 'claimKey', 'expectedRevision'];

// Reject unknown fields (such as client-declared gold or currency amounts).
const missionClaimValidator = [
  body().custom(value => {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
    if (Object.keys(value).some(key => !ALLOWED_CLAIM_FIELDS.includes(key))) return false;
    if (typeof value.transactionId !== 'string' || !value.transactionId.trim() || value.transactionId.length > 128) return false;
    if (typeof value.missionId !== 'string' || !value.missionId.trim() || value.missionId.length > 128) return false;
    if (typeof value.claimKey !== 'string' || !value.claimKey.trim() || value.claimKey.length > 160) return false;
    if (!Number.isSafeInteger(value.expectedRevision) || value.expectedRevision < 0) return false;
    return true;
  }).withMessage('Invalid mission claim request.'),
  validate
];

module.exports = { missionClaimValidator };
