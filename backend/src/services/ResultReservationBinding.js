// src/services/ResultReservationBinding.js
//
// FIX-18: an unsigned authoritative result is bound to the reservation it was
// published for. Extraction and defeat commits may credit it only while that
// reservation is still pending and bound to the result's raid, so one
// reservation credits at most one raid. Results without a reservationId
// (signed producers, documents stored before FIX-18) are not gated.

'use strict';

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

/** Character update filter that keeps the bound reservation from being replaced concurrently. */
function resultReservationFilter(authResult) {
  return authResult.reservationId
    ? { 'inventory.pendingReservation.reservationId': authResult.reservationId }
    : {};
}

module.exports = { requireResultReservation, resultReservationFilter };
