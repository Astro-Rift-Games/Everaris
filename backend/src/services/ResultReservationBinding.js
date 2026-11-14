// src/services/ResultReservationBinding.js
//
// FIX-18: one reservation may credit at most one result across both commits.
// Extraction additionally requires the reservation to remain pending and bound
// to the result's raid. Defeat can credit XP after a loss clear.
// Signed/legacy results without reservationId skip these gates.

'use strict';

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
  requireUncreditedResultReservation,
  requireResultReservation,
  uncreditedResultReservationFilter,
  resultReservationFilter,
};
