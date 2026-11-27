'use strict';

const { EQUIPMENT_SLOTS } = require('../config/equipmentSlots');
const { getEquipmentTemplate } = require('../config/shopCatalog');
const { normalizeLootId } = require('./InventoryLootIdNormalizer');

// Mirrors Unity's EquipmentSlotRules.IsCompatible(LootCategory, EquipmentSlot).
const HAND_SLOTS = Object.freeze(['weaponSlot1', 'weaponSlot2', 'offHand1', 'offHand2']);
const SLOT_ALLOWED_CATEGORIES = Object.freeze({
  weaponSlot1: Object.freeze(['Weapon', 'Shield']),
  weaponSlot2: Object.freeze(['Weapon', 'Shield']),
  offHand1: Object.freeze(['Weapon', 'Shield']),
  offHand2: Object.freeze(['Weapon', 'Shield']),
  helmet: Object.freeze(['Helmet']),
  armor: Object.freeze(['Armor']),
  gloves: Object.freeze(['Gloves']),
  boots: Object.freeze(['Boots'])
});

const PREPARED_WEAPON_REQUIRED_MESSAGE =
  'Equip a weapon in a Weapon Set or ask the Trainer before preparing an expedition.';

function slotLootId(value) {
  if (typeof value === 'string') return value;
  if (value && typeof value === 'object' && typeof value.lootId === 'string') return value.lootId;
  return '';
}

function isItemCompatibleWithSlot(slot, lootId) {
  const allowed = Object.hasOwn(SLOT_ALLOWED_CATEGORIES, slot) ? SLOT_ALLOWED_CATEGORIES[slot] : null;
  if (!allowed || typeof lootId !== 'string' || lootId === '') return false;
  const template = getEquipmentTemplate(normalizeLootId(lootId));
  return Boolean(template && allowed.includes(template.category));
}

function assertSlotCompatible(slot, lootId) {
  if (!isItemCompatibleWithSlot(slot, lootId)) {
    throw {
      statusCode: 422,
      errorCode: 'INVALID_EQUIPMENT_SLOT',
      message: `Item '${lootId}' cannot be equipped in slot '${slot}'.`
    };
  }
}

function assertPreparedEquipmentCategories(slots) {
  for (const slot of EQUIPMENT_SLOTS) {
    const lootId = slotLootId(slots?.[slot]);
    if (!lootId) continue;
    assertSlotCompatible(slot, lootId);
  }
}

// Mirrors Unity's PreparedEquipmentLoadout.HasUsableWeapon across all hand slots.
function hasPreparedWeapon(equipment) {
  return HAND_SLOTS.some(slot => {
    const lootId = slotLootId(equipment?.[slot]);
    if (!lootId) return false;
    const template = getEquipmentTemplate(normalizeLootId(lootId));
    return template?.category === 'Weapon';
  });
}

function requirePreparedWeapon(equipment) {
  if (!hasPreparedWeapon(equipment)) {
    throw {
      statusCode: 409,
      errorCode: 'PREPARED_WEAPON_REQUIRED',
      message: PREPARED_WEAPON_REQUIRED_MESSAGE
    };
  }
}

module.exports = {
  HAND_SLOTS,
  SLOT_ALLOWED_CATEGORIES,
  PREPARED_WEAPON_REQUIRED_MESSAGE,
  isItemCompatibleWithSlot,
  assertSlotCompatible,
  assertPreparedEquipmentCategories,
  hasPreparedWeapon,
  requirePreparedWeapon
};
