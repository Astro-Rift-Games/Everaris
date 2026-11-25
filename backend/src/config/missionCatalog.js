'use strict';

/**
 * Authoritative mission reward catalog mirroring Unity MissionDefinition assets
 * (Project Grimhold/Assets/Scriptable Objects/Missions/**).
 * Clients never declare gold rewards; the server resolves goldReward exclusively by missionId.
 * Parity is verified automatically by backend/tests/mission.catalog.parity.test.js.
 */
const MISSION_CATALOG = Object.freeze({
  // Canonical Rank E daily contracts (Contratos Diarios — Rango E)
  CON_E_001: Object.freeze({ type: 'Normal', rank: 'E', goldReward: 20 }),
  CON_E_002: Object.freeze({ type: 'Normal', rank: 'E', goldReward: 30 }),
  CON_E_003: Object.freeze({ type: 'Normal', rank: 'E', goldReward: 15 }),
  CON_E_004: Object.freeze({ type: 'Normal', rank: 'E', goldReward: 20 }),
  CON_E_005: Object.freeze({ type: 'Normal', rank: 'E', goldReward: 20 }),
  CON_E_006: Object.freeze({ type: 'Normal', rank: 'E', goldReward: 35 }),

  // Rank D test contracts (Assets/Scriptable Objects/Missions/Test/)
  CON_D_TEST_001: Object.freeze({ type: 'Normal', rank: 'D', goldReward: 40 }),
  CON_D_TEST_002: Object.freeze({ type: 'Normal', rank: 'D', goldReward: 45 }),
  CON_D_TEST_003: Object.freeze({ type: 'Normal', rank: 'D', goldReward: 55 }),

  // Rank C test contracts (Assets/Scriptable Objects/Missions/Test/)
  CON_C_TEST_001: Object.freeze({ type: 'Normal', rank: 'C', goldReward: 75 }),
  CON_C_TEST_002: Object.freeze({ type: 'Normal', rank: 'C', goldReward: 85 }),
  CON_C_TEST_003: Object.freeze({ type: 'Normal', rank: 'C', goldReward: 100 })
});

function getMissionDefinition(missionId) {
  if (typeof missionId !== 'string' || !missionId.trim()) return null;
  const key = missionId.trim();
  return Object.hasOwn(MISSION_CATALOG, key) ? MISSION_CATALOG[key] : null;
}

module.exports = {
  MISSION_CATALOG,
  getMissionDefinition
};
