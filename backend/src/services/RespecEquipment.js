'use strict';

const { EQUIPMENT_SLOTS } = require('../config/equipmentSlots');
const { meetsWeaponRequirements, meetsAbilityRequirements } = require('../config/attributeRequirements');
const { inventoryVersion, copyEntry, inventoryItems, serializePreparedEquipment, receiveItem,
  requireMigratedInventory } = require('./InventoryEntries');

const ABILITY_SLOTS = Object.freeze(['slot1', 'slot2']);

function equippedLootId(value) {
  return typeof value === 'object' ? value.lootId : value;
}

// Plan on an in-memory candidate: prepared weapons that stop meeting their minimums return to
// the loadout (v3 keeps the instance), preserving the unequip rule of InventoryService.updateInstanceEquipment.
function planEquipment(character, attributes) {
  const version = inventoryVersion(character);
  const equipment = serializePreparedEquipment(character.inventory.preparedEquipment, version);
  const unmet = EQUIPMENT_SLOTS.filter(slot => equipment[slot] && !meetsWeaponRequirements(equippedLootId(equipment[slot]), attributes));
  if (unmet.length === 0) return { changed: false, preparedEquipment: equipment, loadout: inventoryItems(character, 'loadout') };

  requireMigratedInventory(character);
  const loadout = inventoryItems(character, 'loadout');
  for (const slot of unmet) {
    const item = equipment[slot];
    // INVENTORY_FULL is thrown here, before any write.
    receiveItem(loadout, 'loadout', version === 3 ? { ...copyEntry(item), amount: 1 } : { lootId: item, amount: 1 }, version);
    equipment[slot] = version === 3 ? null : '';
  }
  return { changed: true, preparedEquipment: equipment, loadout };
}

function planAbilities(character, attributes) {
  const abilities = {};
  let changed = false;
  for (const slot of ABILITY_SLOTS) {
    const abilityId = character.preparedAbilities?.[slot] || '';
    const unmet = abilityId !== '' && !meetsAbilityRequirements(abilityId, attributes);
    abilities[slot] = unmet ? '' : abilityId;
    changed ||= unmet;
  }
  return { changed, preparedAbilities: abilities };
}

/**
 * Plans the equipment side of a respec (Doc 04 §11): every prepared weapon and ability whose
 * requirements the reset attributes no longer meet is unequipped. unlockedAbilities is never touched.
 * Pure: returns the $set fields to merge into the respec update (only what changed); the caller
 * writes them in the same guarded update as the attribute reset.
 * @throws 409 INVENTORY_FULL when the loadout cannot take an unequipped weapon.
 */
function planRespecUnequip(character, attributes) {
  const equipment = planEquipment(character, attributes);
  const abilities = planAbilities(character, attributes);
  const set = {};
  if (equipment.changed) {
    set['inventory.preparedEquipment'] = equipment.preparedEquipment;
    set['inventory.loadout'] = equipment.loadout;
  }
  if (abilities.changed) set.preparedAbilities = abilities.preparedAbilities;
  return set;
}

module.exports = { planRespecUnequip };
