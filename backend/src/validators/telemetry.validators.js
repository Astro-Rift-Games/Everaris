'use strict';

const { body } = require('express-validator');
const validate = require('../middleware/validate');
const { TRANSPORT_BLOCK_SOURCES } = require('../services/EconomyTelemetry');

const ALLOWED_TRANSPORT_BLOCK_FIELDS = ['source'];

// Client reports are non-authoritative (TASK-309): only a known source is accepted; any other
// field (client-declared limits, gold, items, identifiers) rejects the whole report.
const transportBlockReportValidator = [
  body().custom(value => {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
    if (Object.keys(value).some(key => !ALLOWED_TRANSPORT_BLOCK_FIELDS.includes(key))) return false;
    return TRANSPORT_BLOCK_SOURCES.includes(value.source);
  }).withMessage('Invalid transport block report.'),
  validate
];

module.exports = { transportBlockReportValidator };
