// src/services/DefeatCommitService.js
//
// Authoritative, idempotent commit of expedition progression following a Defeat.
//
// Design invariants:
//   - Idempotent: same (raidId, resultSequence) → alreadySecured = true, no mutation.
//   - Atomic: defeat progression applied and pendingReservation cleared in a single findOneAndUpdate call.
//   - Authoritative: the server independently recalculates level/XP/attribute points from the authoritative raid result.

'use strict';

const Character = require('../models/Character');
const AuthoritativeExtractionResult = require('../models/AuthoritativeExtractionResult');
const { requireMigratedInventory } = require('./InventoryEntries');
const EconomyTelemetry = require('./EconomyTelemetry');
const {
  requireUncreditedResultReservation, uncreditedResultReservationFilter, isDefeatResult, publishedResultSequence,
} = require('./ResultReservationBinding');
const {
  computeLevelAndExperience,
  computeAttributePointsGranted,
} = require('../config/progressionBalance');

const MAX_PROGRESSION_RECEIPTS = 256;
const MAX_COMMIT_ATTEMPTS = 3;
const RECEIPT_POLL_ATTEMPTS = 10;
const RECEIPT_POLL_INTERVAL_MS = 50;

// consumed is also the in-flight claim marker. Only an exact durable receipt
// proves success; bound the wait and never acquire or release another request's claim.
async function waitForProgressionReceipt(accountId, raidId, resultSequence) {
  for (let attempt = 0; attempt <= RECEIPT_POLL_ATTEMPTS; attempt++) {
    if (attempt > 0) await new Promise(resolve => setTimeout(resolve, RECEIPT_POLL_INTERVAL_MS));
    const character = await Character.findOne({ accountId });
    if (character?.appliedProgressionReceipts?.some(r => r.raidId === raidId && r.resultSequence === resultSequence)) {
      return {
        alreadySecured: true,
        level: character.level,
        experience: character.experience,
        characterAttributes: serializeAttributes(character.characterAttributes),
        revision: character.revision || 0,
      };
    }
  }
  return null;
}

function serializeAttributes(attrs) {
  if (!attrs) {
    return { vitality: 0, resistance: 0, strength: 0, dexterity: 0, intelligence: 0, luck: 0, availablePoints: 0 };
  }
  return {
    vitality: attrs.vitality || 0,
    resistance: attrs.resistance || 0,
    strength: attrs.strength || 0,
    dexterity: attrs.dexterity || 0,
    intelligence: attrs.intelligence || 0,
    luck: attrs.luck || 0,
    availablePoints: attrs.availablePoints || 0,
  };
}

class DefeatCommitService {
  static async commit(accountId, payload) {
    const { raidId, resultSequence } = payload || {};

    if (!raidId || typeof raidId !== 'string' || raidId.trim() === '') {
      throw {
        statusCode: 400,
        errorCode: 'INVALID_PAYLOAD',
        message: 'raidId is required.',
      };
    }

    if (!Number.isInteger(resultSequence) || resultSequence < 1) {
      throw {
        statusCode: 400,
        errorCode: 'INVALID_PAYLOAD',
        message: 'resultSequence must be a positive integer.',
      };
    }

    // ------------------------------------------------------------------
    // 1. Authoritative Lookup
    // ------------------------------------------------------------------
    let authResult = await AuthoritativeExtractionResult.findOne({ raidId, accountId });
    if (!authResult) {
      // In case the commit arrived slightly before the host finished publishing,
      // poll briefly (up to 500ms) before rejecting.
      for (let attempt = 0; attempt < 5 && !authResult; attempt++) {
        await new Promise(resolve => setTimeout(resolve, 100));
        authResult = await AuthoritativeExtractionResult.findOne({ raidId, accountId });
      }
    }

    if (!authResult) {
      throw {
        statusCode: 422,
        errorCode: 'NO_AUTHORITATIVE_RESULT',
        message: 'No authoritative defeat result found for this raid. Client payloads are not trusted.',
      };
    }

    // ------------------------------------------------------------------
    // 2. Pre-fetch character and enforce single consumption
    // ------------------------------------------------------------------
    let character = await Character.findOne({ accountId });
    if (!character) {
      throw {
        statusCode: 404,
        errorCode: 'CHARACTER_NOT_FOUND',
        message: 'No character found for this account.',
      };
    }

    requireMigratedInventory(character);

    const progressionReceipts = character.appliedProgressionReceipts || [];
    const exactReceiptMatch = progressionReceipts.some(
      r => r.raidId === raidId && r.resultSequence === resultSequence
    );

    if (exactReceiptMatch) {
      return {
        alreadySecured: true,
        level: character.level,
        experience: character.experience,
        characterAttributes: serializeAttributes(character.characterAttributes),
        revision: character.revision || 0
      };
    }

    if (authResult.consumed) {
      const replay = await waitForProgressionReceipt(accountId, raidId, resultSequence);
      if (replay) return replay;
      throw {
        statusCode: 409,
        errorCode: 'RESULT_ALREADY_CONSUMED',
        message: 'This authoritative result has already been consumed and cannot be claimed again.',
      };
    }

    const raidAlreadyApplied = progressionReceipts.some(r => r.raidId === raidId);
    if (raidAlreadyApplied) {
      throw {
        statusCode: 409,
        errorCode: 'RESULT_ALREADY_CONSUMED',
        message: 'A progression receipt for this raid has already been committed and cannot be claimed with a different sequence.',
      };
    }

    requireUncreditedResultReservation(character, authResult);

    const authoritativeSequence = publishedResultSequence(authResult);
    if (!isDefeatResult(authResult) || authoritativeSequence === null || resultSequence !== authoritativeSequence) {
      throw {
        statusCode: 422,
        errorCode: 'NO_AUTHORITATIVE_RESULT',
        message: 'No authoritative defeat result found for this sequence.',
      };
    }

    // ------------------------------------------------------------------
    // 3. Atomically claim authoritative result (distributed lock)
    // ------------------------------------------------------------------
    const authQuery = authResult._id ? { _id: authResult._id } : { raidId, accountId };
    const claimedResult = await AuthoritativeExtractionResult.findOneAndUpdate(
      { ...authQuery, consumed: { $ne: true } },
      { $set: { consumed: true, consumedAt: new Date() } },
      { new: true }
    );

    if (!claimedResult) {
      const replay = await waitForProgressionReceipt(accountId, raidId, resultSequence);
      if (replay) return replay;

      throw {
        statusCode: 409,
        errorCode: 'RESULT_ALREADY_CONSUMED',
        message: 'This authoritative result has already been consumed.',
      };
    }

    // Keep this request's result claim held across bounded revision retries.
    // Recompute every derived field and reservation decision from the latest character.
    let secured = false;
    try {
      for (let attempt = 0; attempt < MAX_COMMIT_ATTEMPTS; attempt++) {
        requireMigratedInventory(character);
        requireUncreditedResultReservation(character, authResult);
        const experienceToGrant = Math.max(0, Number(authResult.experienceGranted || 0));
        const computed = computeLevelAndExperience(character.level, character.experience, experienceToGrant);
        const pointsGranted = computeAttributePointsGranted(character.level, computed.resultingLevel);
        const newAvailablePoints = (character.characterAttributes?.availablePoints || 0) + pointsGranted;
        const progressionReceipt = {
          ...(authResult.reservationId ? { reservationId: authResult.reservationId } : {}),
          raidId,
          resultSequence: authoritativeSequence,
          consolidatedExperience: experienceToGrant,
          resultingLevel: computed.resultingLevel,
        };

        // Only this result's reservation may be cleared; preserve later launches.
        // Unbound legacy results may clear only an admitted reservation of this raid (or no raid).
        const pendingReservation = character.inventory?.pendingReservation;
        const clearsReservation = Boolean(pendingReservation) && (authResult.reservationId
          ? pendingReservation.reservationId === authResult.reservationId
          : Boolean(pendingReservation.admittedAt) && (!pendingReservation.raidId || pendingReservation.raidId === raidId));
        const updatedCharacter = await Character.findOneAndUpdate({
          accountId,
          revision: character.revision,
          ...(clearsReservation ? { 'inventory.pendingReservation.reservationId': pendingReservation.reservationId } : {}),
          ...uncreditedResultReservationFilter(authResult),
          appliedProgressionReceipts: { $not: { $elemMatch: { raidId, resultSequence } } }
        }, {
          ...(clearsReservation ? { $unset: { 'inventory.pendingReservation': 1 } } : {}),
          $set: {
            level: computed.resultingLevel,
            experience: computed.resultingExperience,
            'characterAttributes.availablePoints': newAvailablePoints,
            lastAppliedProgressionResultSequence: authoritativeSequence,
            lastProgressionReceipt: progressionReceipt
          },
          $inc: { revision: 1 },
          $push: { appliedProgressionReceipts: { $each: [progressionReceipt], $slice: -MAX_PROGRESSION_RECEIPTS } }
        }, { new: true });

        if (updatedCharacter) {
          secured = true;
          // An abandon already closed (and counted) a reservation it cleared earlier.
          if (clearsReservation) {
            EconomyTelemetry.recordRaidClose(updatedCharacter, {
              outcome: 'defeat', raidId, reservation: pendingReservation, experienceGranted: experienceToGrant
            });
          }
          return {
            alreadySecured: false,
            level: updatedCharacter.level,
            experience: updatedCharacter.experience,
            characterAttributes: serializeAttributes(updatedCharacter.characterAttributes),
            revision: updatedCharacter.revision
          };
        }

        const refreshedChar = await Character.findOne({ accountId });
        const alreadyApplied = (refreshedChar?.appliedProgressionReceipts || []).some(
          r => r.raidId === raidId && r.resultSequence === resultSequence
        );
        if (alreadyApplied) {
          secured = true;
          return {
            alreadySecured: true,
            level: refreshedChar.level,
            experience: refreshedChar.experience,
            characterAttributes: serializeAttributes(refreshedChar.characterAttributes),
            revision: refreshedChar.revision || 0
          };
        }
        // Retry only a lost revision race, never an unrelated failed guard.
        if (!refreshedChar || refreshedChar.revision === character.revision) break;
        character = refreshedChar;
      }
      throw {
        statusCode: 409,
        errorCode: 'CONCURRENT_MUTATION',
        message: 'Could not commit defeat progression due to a concurrent update. Please retry.',
      };
    } finally {
      if (!secured) {
        await AuthoritativeExtractionResult.findOneAndUpdate(
          { ...authQuery, consumed: true },
          { $set: { consumed: false, consumedAt: null } }
        );
      }
    }
  }
}

module.exports = DefeatCommitService;
