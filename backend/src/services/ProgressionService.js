// src/services/ProgressionService.js
const Character = require('../models/Character');
const { RESPEC_GOLD_COST } = require('../config/respecConfig');
const { receiptPush, watermarkUpdate, assertWithinRetention } = require('./ShopReceiptRetention');
const { inventoryVersion, inventoryItems, serializePreparedEquipment } = require('./InventoryEntries');
const { planRespecUnequip } = require('./RespecEquipment');
const EconomyTelemetry = require('./EconomyTelemetry');

const ATTRIBUTE_KEYS = Object.freeze(['vitality', 'resistance', 'strength', 'dexterity', 'intelligence', 'luck']);
const DEFAULT_ATTRIBUTES = Object.freeze({
  vitality: 5, resistance: 5, strength: 5, dexterity: 5, intelligence: 5, luck: 5, availablePoints: 10
});

function attributeSnapshot(attributes) {
  const source = attributes || DEFAULT_ATTRIBUTES;
  const snapshot = {};
  for (const key of ATTRIBUTE_KEYS) snapshot[key] = source[key];
  snapshot.availablePoints = source.availablePoints;
  return snapshot;
}

function respecResult(character, { transactionId, cost, alreadySecured }) {
  const version = inventoryVersion(character);
  return {
    alreadySecured,
    transactionId,
    cost,
    currency: character.inventory?.currency || 0,
    characterAttributes: attributeSnapshot(character.characterAttributes),
    level: character.level,
    experience: character.experience,
    revision: character.revision || 0,
    preparedEquipment: serializePreparedEquipment(character.inventory?.preparedEquipment, version),
    preparedAbilities: {
      slot1: character.preparedAbilities?.slot1 || '',
      slot2: character.preparedAbilities?.slot2 || ''
    },
    stash: inventoryItems(character, 'stash'),
    loadout: inventoryItems(character, 'loadout')
  };
}

function findReceipt(character, transactionId) {
  return (character.appliedShopReceipts || []).find(r => r.transactionId === transactionId);
}

class ProgressionService {
  /**
   * Retrieves the progression state for the given account's character.
   * @throws 404 if no character exists for this account.
   */
  static async getProgression(accountId) {
    const character = await Character.findOne({ accountId });
    if (!character) {
      throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    }

    return {
      level: character.level,
      experience: character.experience,
      lastAppliedProgressionResultSequence: character.lastAppliedProgressionResultSequence,
      characterAttributes: character.characterAttributes || {
        vitality: 5, resistance: 5, strength: 5,
        dexterity: 5, intelligence: 5, luck: 5, availablePoints: 10
      },
      unlockedAbilities: character.unlockedAbilities || [],
      respecCost: RESPEC_GOLD_COST,
      revision: character.revision || 0
    };
  }
  static async commitProgression(accountId, payload) {
    const { MAX_ATTRIBUTE_VALUE } = require('../config/progressionBalance');
    
    const character = await Character.findOne({ accountId });
    if (!character) {
      throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found.' };
    }

    if (!payload || typeof payload.attribute !== 'string' || payload.attribute.trim() === '') {
      throw { statusCode: 400, errorCode: 'INVALID_ATTRIBUTE', message: 'Attribute must be a non-empty string.' };
    }

    if (typeof payload.expectedRevision !== 'number' || payload.expectedRevision < 0 || !Number.isInteger(payload.expectedRevision)) {
      throw { statusCode: 400, errorCode: 'INVALID_REVISION', message: 'expectedRevision must be a non-negative integer.' };
    }

    const attributeName = payload.attribute.toLowerCase();
    const validAttributes = ['vitality', 'resistance', 'strength', 'dexterity', 'intelligence', 'luck'];

    if (!validAttributes.includes(attributeName)) {
      throw { statusCode: 400, errorCode: 'INVALID_ATTRIBUTE', message: `Invalid attribute: ${payload.attribute}` };
    }

    const attrs = character.characterAttributes || {
      vitality: 5, resistance: 5, strength: 5,
      dexterity: 5, intelligence: 5, luck: 5, availablePoints: 10
    };

    if (attrs.availablePoints <= 0) {
      throw { statusCode: 422, errorCode: 'NO_AVAILABLE_POINTS', message: 'No available attribute points.' };
    }

    if (attrs[attributeName] >= MAX_ATTRIBUTE_VALUE) {
      throw { statusCode: 422, errorCode: 'ATTRIBUTE_MAXED', message: `Attribute ${payload.attribute} is already at the maximum value.` };
    }

    const updateQuery = {
      $inc: {
        [`characterAttributes.${attributeName}`]: 1,
        'characterAttributes.availablePoints': -1,
        revision: 1
      }
    };

    const updated = await Character.findOneAndUpdate(
      { 
        accountId, 
        revision: payload.expectedRevision,
        'characterAttributes.availablePoints': { $gt: 0 },
        [`characterAttributes.${attributeName}`]: { $lt: MAX_ATTRIBUTE_VALUE }
      },
      updateQuery,
      { new: true }
    );

    if (!updated) {
      // It might have failed because the revision was wrong, or character deleted,
      // or availablePoints became 0 due to concurrent requests.
      const exists = await Character.findOne({ accountId }, null, { lean: true });
      if (!exists) {
        throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found.' };
      }
      
      const currentAttrs = exists.characterAttributes;
      if (!currentAttrs || typeof currentAttrs.availablePoints !== 'number') {
        console.error(`[ProgressionService] Legacy document detected without characterAttributes.availablePoints. AccountId: ${accountId}`);
        throw { statusCode: 500, errorCode: 'LEGACY_DOCUMENT_UNNORMALIZED', message: 'Legacy document is missing required attributes.' };
      }
      
      if (currentAttrs.availablePoints <= 0) {
        throw { statusCode: 422, errorCode: 'NO_AVAILABLE_POINTS', message: 'No available attribute points.' };
      }

      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict.' };
    }

    return {
      characterAttributes: updated.characterAttributes,
      level: updated.level,
      experience: updated.experience,
      revision: updated.revision
    };
  }

  /**
   * TASK-304 respec (Doc 04 §11, Doc 22 §6): redistributes every owned attribute point.
   *
   * Invariants:
   * 1. Full pool: all six attributes become 0 and availablePoints becomes the owned total;
   *    level and experience are untouched and no point is created or destroyed.
   * 2. Atomic: gold debit, attribute reset, receipt and revision are one guarded update.
   * 3. Idempotent: a replayed transactionId, including a concurrent duplicate, returns the
   *    recorded outcome with alreadySecured: true and never charges twice.
   * 4. Rejections (no assigned points, insufficient gold, pending reservation, stale revision)
   *    leave gold and attributes unchanged.
   * 5. Requirements are re-evaluated against the reset attributes in the same update: prepared
   *    weapons that no longer qualify return to the loadout (v3 keeps the instance) and unmet
   *    prepared ability slots become ''; unlockedAbilities is never touched. A loadout without room
   *    rejects with 409 INVENTORY_FULL before any write.
   */
  static async respec(accountId, { transactionId, expectedRevision } = {}) {
    if (typeof transactionId !== 'string' || transactionId.trim() === '') {
      throw { statusCode: 400, errorCode: 'INVALID_TRANSACTION_ID', message: 'transactionId is required.' };
    }
    if (typeof expectedRevision !== 'number' || expectedRevision < 0 || !Number.isInteger(expectedRevision)) {
      throw { statusCode: 400, errorCode: 'INVALID_REVISION', message: 'expectedRevision must be a non-negative integer.' };
    }

    const character = await Character.findOne({ accountId });
    if (!character) {
      throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    }

    const existingReceipt = findReceipt(character, transactionId);
    if (existingReceipt) {
      return ProgressionService.#replayRespec(character, existingReceipt, transactionId);
    }
    assertWithinRetention(character, transactionId);

    if (character.inventory?.pendingReservation) {
      throw { statusCode: 409, errorCode: 'RESERVATION_PENDING', message: 'Cannot respec while a raid reservation is pending.' };
    }
    if (character.revision !== expectedRevision) {
      throw {
        statusCode: 409,
        errorCode: 'REVISION_CONFLICT',
        message: `Revision conflict. Expected revision ${expectedRevision}, but current revision is ${character.revision}.`
      };
    }

    const attributes = attributeSnapshot(character.characterAttributes);
    const assignedPoints = ATTRIBUTE_KEYS.reduce((sum, key) => sum + attributes[key], 0);
    if (assignedPoints <= 0) {
      throw { statusCode: 422, errorCode: 'NO_ASSIGNED_POINTS', message: 'There are no assigned attribute points to redistribute.' };
    }

    const cost = RESPEC_GOLD_COST;
    const currency = character.inventory?.currency || 0;
    if (currency < cost) {
      throw {
        statusCode: 422,
        errorCode: 'INSUFFICIENT_FUNDS',
        message: `Insufficient currency to respec. Required: ${cost}, available: ${currency}.`
      };
    }

    const resetAttributes = { availablePoints: assignedPoints + attributes.availablePoints };
    for (const key of ATTRIBUTE_KEYS) resetAttributes[key] = 0;
    const unequipped = planRespecUnequip(character, resetAttributes);

    const updated = await Character.findOneAndUpdate(
      {
        accountId,
        revision: expectedRevision,
        'inventory.pendingReservation': null,
        'inventory.currency': { $gte: cost },
        'appliedShopReceipts.transactionId': { $ne: transactionId }
      },
      {
        $set: { characterAttributes: resetAttributes, ...unequipped },
        $inc: { 'inventory.currency': -cost, revision: 1 },
        ...watermarkUpdate(character),
        $push: {
          appliedShopReceipts: receiptPush({
            transactionId,
            type: 'respec',
            amount: 1,
            totalAmount: cost,
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
      const recorded = findReceipt(latest, transactionId);
      if (recorded) return ProgressionService.#replayRespec(latest, recorded, transactionId);
      if (latest.inventory?.pendingReservation) {
        throw { statusCode: 409, errorCode: 'RESERVATION_PENDING', message: 'Cannot respec while a raid reservation is pending.' };
      }
      if ((latest.inventory?.currency || 0) < cost) {
        throw { statusCode: 422, errorCode: 'INSUFFICIENT_FUNDS', message: 'Insufficient currency to respec.' };
      }
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict during respec.' };
    }

    EconomyTelemetry.recordRespec(updated, { cost, pointsReturned: assignedPoints });
    return respecResult(updated, { transactionId, cost, alreadySecured: false });
  }

  static #replayRespec(character, receipt, transactionId) {
    if (receipt.type !== 'respec') {
      throw {
        statusCode: 409,
        errorCode: 'TRANSACTION_ID_REUSED',
        message: 'Transaction ID has already been used for a different operation.'
      };
    }
    return respecResult(character, { transactionId, cost: receipt.totalAmount, alreadySecured: true });
  }
}

module.exports = ProgressionService;
