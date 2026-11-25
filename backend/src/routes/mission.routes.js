'use strict';

const express = require('express');
const router = express.Router();
const authenticate = require('../middleware/authenticate');
const MissionClaimService = require('../services/MissionClaimService');
const { missionClaimValidator } = require('../validators/mission.validators');

// All mission endpoints require a valid JWT token.
router.use(authenticate);

// POST /character/me/missions/claim (TASK-310)
// Authoritatively credits the mission's configured gold reward once per claimKey and records mission_reward telemetry.
router.post('/me/missions/claim', missionClaimValidator, async (req, res, next) => {
  try {
    const result = await MissionClaimService.claim(req.accountId, req.body);
    res.json(result);
  } catch (err) {
    next(err);
  }
});

module.exports = router;
