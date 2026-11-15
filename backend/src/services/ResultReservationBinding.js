// src/services/ResultReservationBinding.js
//
// FIX-18: one reservation may credit at most one result across both commits.
// Extraction additionally requires the reservation to remain pending and bound
// to the result's raid. Defeat can credit XP after a loss clear.
// Signed/legacy results without reservationId skip these gates.

'use strict';

const { EQUIPMENT_SLOTS } = require('../config/equipmentSlots');

const EMPTY_EQUIPMENT_ENCODINGS = [null, '', 'null', {}];

function ownsEquipment(value) {
  return value !== undefined && value !== null && value !== '' && value !== 'null' &&
    !(typeof value === 'object' && !Array.isArray(value) && Object.keys(value).length === 0);
}

// The publish contract has no outcome discriminator. Empty ownership is defeat-compatible.
function isDefeatResult(result) {
  return !(result.items?.length) && !EQUIPMENT_SLOTS.some(slot => {
    const value = result.preparedEquipment?.[slot];
    return Array.isArray(value) ? value.some(ownsEquipment) : ownsEquipment(value);
  });
}

function publishedResultSequence(result) {
  if (Number.isSafeInteger(result.resultSequence) && result.resultSequence > 0) return result.resultSequence;
  // Pre-existing results encode the published sequence in the generated result ID.
  const prefix = `${result.raidId}:`;
  const remainder = result.resultId?.startsWith(prefix) ? result.resultId.slice(prefix.length) : '';
  const match = /^[^:]+:([1-9]\d*)$/.exec(remainder);
  const sequence = match ? Number(match[1]) : NaN;
  return Number.isSafeInteger(sequence) ? sequence : null;
}

function pendingLootResultFilter() {
  return { $or: [
    { 'items.0': { $exists: true } },
    // $nin alone excludes a mixed array if even one element is empty. Check
    // array elements separately so any owned element protects the result.
    ...EQUIPMENT_SLOTS.flatMap(slot => [
      { [`preparedEquipment.${slot}`]: { $exists: true, $not: { $type: 'array' }, $nin: EMPTY_EQUIPMENT_ENCODINGS } },
      { [`preparedEquipment.${slot}`]: { $elemMatch: { $nin: EMPTY_EQUIPMENT_ENCODINGS } } },
    ]),
  ] };
}

function requireUncreditedResultReservation(character, authResult) {
  if (!authResult.reservationId) return;
  const receipts = [
    ...(character.inventory?.appliedExtractionReceipts || []),
    ...(character.appliedProgressionReceipts || []),
  ];
  if (receipts.some(receipt => receipt.reservationId === authResult.reservationId)) {
    throw {
      statusCode: 409,
      errorCode: 'RESULT_RESERVATION_MISMATCH',
      message: 'The reservation has already been credited.',
    };
  }
}

function requireResultReservation(character, authResult) {
  if (!authResult.reservationId) return;
  const reservation = character.inventory?.pendingReservation;
  if (reservation?.reservationId !== authResult.reservationId || reservation?.raidId !== authResult.raidId) {
    throw {
      statusCode: 409,
      errorCode: 'RESULT_RESERVATION_MISMATCH',
      message: 'The authoritative result does not match the active raid reservation.',
    };
  }
}

/** Separate conjunctions avoid overwriting the commits' existing receipt filters. */
function uncreditedResultReservationFilter(authResult) {
  if (!authResult.reservationId) return {};
  const reservationId = authResult.reservationId;
  return {
    $and: [
      { 'inventory.appliedExtractionReceipts': { $not: { $elemMatch: { reservationId } } } },
      { appliedProgressionReceipts: { $not: { $elemMatch: { reservationId } } } },
    ],
  };
}

/** Extraction only: preserve the pending reservation binding during the write. */
function resultReservationFilter(authResult) {
  return authResult.reservationId
    ? { 'inventory.pendingReservation.reservationId': authResult.reservationId,
        'inventory.pendingReservation.raidId': authResult.raidId }
    : {};
}

module.exports = {
  isDefeatResult,
  publishedResultSequence,
  pendingLootResultFilter,
  requireUncreditedResultReservation,
  requireResultReservation,
  uncreditedResultReservationFilter,
  resultReservationFilter,
};
