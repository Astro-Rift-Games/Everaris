'use strict';

const express = require('express');
const router = express.Router();
const authenticate = require('../middleware/authenticate');
const { MemoryRateLimiter } = require('../middleware/rateLimiter');
const Character = require('../models/Character');
const EconomyTelemetry = require('../services/EconomyTelemetry');
const { transportBlockReportValidator } = require('../validators/telemetry.validators');

// Volume bound for client-reported analytics, per account (TASK-309). Each accepted report is one
// refused expedition start, so this comfortably exceeds real play while capping abuse.
const TRANSPORT_BLOCK_REPORT_MAX = 20;
const TRANSPORT_BLOCK_REPORT_WINDOW_MS = 10 * 60 * 1000;

const transportBlockLimiter = new MemoryRateLimiter({
  windowMs: TRANSPORT_BLOCK_REPORT_WINDOW_MS,
  max: TRANSPORT_BLOCK_REPORT_MAX,
  keyGenerator: req => req.accountId,
  message: 'Too many transport block reports. Please try again later.'
});

router.transportBlockLimiter = transportBlockLimiter;
router.TRANSPORT_BLOCK_REPORT_MAX = TRANSPORT_BLOCK_REPORT_MAX;

// POST /character/me/telemetry/transport-block (TASK-309)
// Records a client-reported MaxTransportable block as internal analytics. It never reads or writes
// the character beyond resolving its pseudonymous _id, and recording is fire-and-forget.
router.post('/me/telemetry/transport-block', authenticate, transportBlockLimiter.middleware(),
  transportBlockReportValidator, async (req, res, next) => {
    try {
      const character = await Character.findOne({ accountId: req.accountId }, { _id: 1 }).lean();
      if (!character) {
        throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
      }
      EconomyTelemetry.recordTransportBlock(character, req.body.source);
      res.status(202).json({ accepted: true });
    } catch (err) {
      next(err);
    }
  });

module.exports = router;
