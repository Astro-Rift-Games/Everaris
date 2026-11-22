// src/config/attributeRequirements.js
//
// TASK-304: attribute requirements mirrored from Unity so a respec can re-evaluate equipment
// server-side (Doc 04 §11). Sources: WeaponDefinition._attributeRequirements (reached through each
// LootDefinition._weaponDefinition) and AbilityDefinition._attributeRequirements.
// attribute.requirements.parity.test.js fails when these drift from the Unity assets.
// Items and abilities without an entry (armors, shields, training weapons) have no requirements.
'use strict';

const { normalizeLootId } = require('../services/InventoryLootIdNormalizer');

const WEAPON_ATTRIBUTE_MINIMUMS = Object.freeze({
  arming_sword: Object.freeze({ strength: 5 }),
  compound_bow: Object.freeze({ dexterity: 10 }),
  great_hammer: Object.freeze({ strength: 15 }),
  light_crossbow: Object.freeze({ dexterity: 15 }),
  long_bow: Object.freeze({ dexterity: 10 }),
  long_sword: Object.freeze({ strength: 10 }),
  magic_cinquedea: Object.freeze({ dexterity: 5 }),
  magic_staff: Object.freeze({ intelligence: 15 }),
  magic_sword: Object.freeze({ strength: 5 }),
  magic_wand: Object.freeze({ intelligence: 5 }),
  rapier: Object.freeze({ strength: 5 }),
  rondel_dagger: Object.freeze({ dexterity: 5 }),
  spell_book: Object.freeze({ intelligence: 10 }),
  zweihander: Object.freeze({ strength: 10 })
});

const ABILITY_ATTRIBUTE_REQUIREMENTS = Object.freeze({
  arcane_projectile: Object.freeze({ intelligence: 10 }),
  charge: Object.freeze({ strength: 10 }),
  empower: Object.freeze({ luck: 10 }),
  life_drain: Object.freeze({ vitality: 7, intelligence: 7 }),
  protective_orbs: Object.freeze({ resistance: 10 }),
  purification: Object.freeze({ vitality: 7, resistance: 7 }),
  restoration: Object.freeze({ vitality: 10 }),
  seismic_strike: Object.freeze({ strength: 15 }),
  trap: Object.freeze({ dexterity: 10 })
});

function satisfies(requirements, attributes) {
  if (!requirements) return true;
  return Object.entries(requirements).every(([attribute, minimum]) => (attributes?.[attribute] ?? 0) >= minimum);
}

function meetsWeaponRequirements(lootId, attributes) {
  return satisfies(WEAPON_ATTRIBUTE_MINIMUMS[normalizeLootId(lootId)], attributes);
}

function meetsAbilityRequirements(abilityId, attributes) {
  const id = typeof abilityId === 'string' ? abilityId.trim().toLowerCase() : abilityId;
  return satisfies(ABILITY_ATTRIBUTE_REQUIREMENTS[id], attributes);
}

module.exports = {
  WEAPON_ATTRIBUTE_MINIMUMS,
  ABILITY_ATTRIBUTE_REQUIREMENTS,
  meetsWeaponRequirements,
  meetsAbilityRequirements
};
