'use strict';

const Character = require('../models/Character');
const { getMissionDefinition } = require('../config/missionCatalog');
const { receiptPush, watermarkUpdate, assertWithinRetention } = require('./ShopReceiptRetention');
const EconomyTelemetry = require('./EconomyTelemetry');

function findReceipt(character, transactionId) {
  return (character.appliedShopReceipts || []).find(r => r.transactionId === transactionId);
}

function claimResponse(character, { transactionId, missionId, claimKey, goldGranted, alreadySecured }) {
  return {
    alreadySecured,
    transactionId,
    missionId,
    claimKey,
    goldGranted,
    currency: character.inventory?.currency || 0,
    revision: character.revision || 0
  };
}

function isValidClaimKeyForMission(missionId, missionType, claimKey) {
  if (typeof claimKey !== 'string' || !claimKey.trim() || claimKey.length > 160) return false;
  if (missionType === 'Unica') {
    return claimKey === missionId;
  }
  return claimKey === missionId || (claimKey.startsWith(`${missionId}:`) && claimKey.length > missionId.length + 1);
}

/**
 * Authoritative backend service for mission monetary reward claims (TASK-310).
 *
 * Invariants:
 * 1. Authoritative pricing: goldReward is resolved exclusively from backend missionCatalog.js.
 * 2. Atomic single-claim: gold credit, appliedMissionClaims barrier, shop receipt, and revision
 *    commit together in one guarded Character.findOneAndUpdate.
 * 3. Idempotent retries: replaying the same transactionId + intent returns alreadySecured: true
 *    without granting extra gold or emitting telemetry.
 * 4. Unique instance barrier: appliedMissionClaims is never evicted, so reusing a claimKey with
 *    a different transactionId is rejected with 409 MISSION_ALREADY_CLAIMED.
 * 5. Post-commit telemetry: EconomyTelemetry.recordMissionReward runs only after a new commit.
 */
class MissionClaimService {
  static async claim(accountId, { transactionId, missionId, claimKey, expectedRevision } = {}) {
    if (typeof transactionId !== 'string' || !transactionId.trim() || transactionId.length > 128 ||
        typeof missionId !== 'string' || !missionId.trim() ||
        typeof claimKey !== 'string' || !claimKey.trim() || claimKey.length > 160 ||
        !Number.isSafeInteger(expectedRevision) || expectedRevision < 0) {
      throw { statusCode: 400, errorCode: 'VALIDATION_FAILED', message: 'Invalid mission claim parameters.' };
    }

    const canonicalTxId = transactionId.trim();
    const canonicalMissionId = missionId.trim();
    const canonicalClaimKey = claimKey.trim();

    const mission = getMissionDefinition(canonicalMissionId);
    if (!mission) {
      throw { statusCode: 404, errorCode: 'MISSION_NOT_FOUND', message: `Mission '${canonicalMissionId}' does not exist.` };
    }

    if (!isValidClaimKeyForMission(canonicalMissionId, mission.type, canonicalClaimKey)) {
      throw { statusCode: 400, errorCode: 'VALIDATION_FAILED', message: 'Claim key does not match the mission identity.' };
    }

    const character = await Character.findOne({ accountId });
    if (!character) {
      throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    }

    const intent = { missionId: canonicalMissionId, claimKey: canonicalClaimKey };
    const existingReceipt = findReceipt(character, canonicalTxId);
    if (existingReceipt) {
      return MissionClaimService.#replayClaim(character, existingReceipt, canonicalTxId, intent);
    }
    assertWithinRetention(character, canonicalTxId);

    if ((character.appliedMissionClaims || []).includes(canonicalClaimKey)) {
      throw {
        statusCode: 409,
        errorCode: 'MISSION_ALREADY_CLAIMED',
        message: `Mission instance '${canonicalClaimKey}' has already been claimed.`
      };
    }

    if (character.inventory?.pendingReservation) {
      throw {
        statusCode: 409,
        errorCode: 'RESERVATION_PENDING',
        message: 'Cannot claim mission rewards while a raid reservation is pending.'
      };
    }

    if (character.revision !== expectedRevision) {
      throw {
        statusCode: 409,
        errorCode: 'REVISION_CONFLICT',
        message: `Revision conflict. Expected revision ${expectedRevision}, but current revision is ${character.revision}.`
      };
    }

    const goldReward = Number.isSafeInteger(mission.goldReward) && mission.goldReward > 0 ? mission.goldReward : 0;
    const currentCurrency = character.inventory?.currency || 0;
    if (!Number.isSafeInteger(currentCurrency + goldReward)) {
      throw { statusCode: 400, errorCode: 'INTEGER_OVERFLOW', message: 'Calculated currency causes integer overflow.' };
    }

    const updated = await Character.findOneAndUpdate(
      {
        accountId,
        revision: expectedRevision,
        'inventory.pendingReservation': null,
        appliedMissionClaims: { $ne: canonicalClaimKey },
        'appliedShopReceipts.transactionId': { $ne: canonicalTxId }
      },
      {
        $inc: { 'inventory.currency': goldReward, revision: 1 },
        $addToSet: { appliedMissionClaims: canonicalClaimKey },
        ...watermarkUpdate(character),
        $push: {
          appliedShopReceipts: receiptPush({
            transactionId: canonicalTxId,
            type: 'mission_claim',
            intent,
            amount: 1,
            totalAmount: goldReward,
            timestamp: new Date()
          })
        }
      },
      { returnDocument: 'after' }
    );

    if (!updated) {
      const latest = await Character.findOne({ accountId });
      if (!latest) {
        throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
      }
      const recorded = findReceipt(latest, canonicalTxId);
      if (recorded) {
        return MissionClaimService.#replayClaim(latest, recorded, canonicalTxId, intent);
      }
      if ((latest.appliedMissionClaims || []).includes(canonicalClaimKey)) {
        throw {
          statusCode: 409,
          errorCode: 'MISSION_ALREADY_CLAIMED',
          message: `Mission instance '${canonicalClaimKey}' has already been claimed.`
        };
      }
      if (latest.inventory?.pendingReservation) {
        throw {
          statusCode: 409,
          errorCode: 'RESERVATION_PENDING',
          message: 'Cannot claim mission rewards while a raid reservation is pending.'
        };
      }
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict during mission claim.' };
    }

    EconomyTelemetry.recordMissionReward(updated, {
      missionId: canonicalMissionId,
      claimKey: canonicalClaimKey,
      gold: goldReward
    });

    return claimResponse(updated, {
      transactionId: canonicalTxId,
      missionId: canonicalMissionId,
      claimKey: canonicalClaimKey,
      goldGranted: goldReward,
      alreadySecured: false
    });
  }

  static #replayClaim(character, receipt, transactionId, expectedIntent) {
    if (receipt.type !== 'mission_claim' || JSON.stringify(receipt.intent) !== JSON.stringify(expectedIntent)) {
      throw {
        statusCode: 409,
        errorCode: 'TRANSACTION_ID_REUSED',
        message: 'Transaction ID was previously applied with a different operation or parameters.'
      };
    }
    return claimResponse(character, {
      transactionId,
      missionId: expectedIntent.missionId,
      claimKey: expectedIntent.claimKey,
      goldGranted: receipt.totalAmount || 0,
      alreadySecured: true
    });
  }
}

module.exports = MissionClaimService;
