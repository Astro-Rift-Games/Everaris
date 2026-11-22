// src/services/ItemUpgrades.js
//
// Pure Town NPC upgrade rules (Doc 23 §9, TASK-302). No persistence: InventoryService commits plans.
// Step tables drive every limit, so extending NPC services past C (Doc 23 §9.1) means adding rows.
'use strict';

const { getEquipmentTemplate } = require('../config/shopCatalog');
const { normalizeLootId } = require('./InventoryLootIdNormalizer');
const { PHYSICAL_WEAPONS, MAGICAL_WEAPONS } = require('./CommerceRotationService');
const { ARMOR_CATEGORIES, ARMOR_MAX_UPGRADE_LEVEL } = require('./ItemInstances');

const UPGRADE_OPERATIONS = Object.freeze(['primary_scaling', 'first_socket', 'armor_level']);

// Physical weapons the Blacksmith upgrades but never sells in the weekly rotation (Doc 23 §14 pool stays at 12).
const UPGRADE_ONLY_PHYSICAL_WEAPONS = Object.freeze(['zweihander', 'long_bow']);

// Keyed by the current primary grade ('none' = no scaling). Doc 23 §9.1 table.
const PRIMARY_SCALING_STEPS = Object.freeze({
  none: Object.freeze({ grade: 'E', gold: 60, materials: Object.freeze({ RES_RUNE_FRAGMENT: 2 }) }),
  E: Object.freeze({ grade: 'D', gold: 100, materials: Object.freeze({ RES_RUNE_FRAGMENT: 3, RES_ETHER_DUST: 1 }) }),
  D: Object.freeze({ grade: 'C', gold: 160, materials: Object.freeze({ RES_RUNE_FRAGMENT: 4, RES_ETHER_DUST: 2 }) })
});
// Doc 23 §14 WeaponNpcMaxScaling = C; grades above it come only from the Dungeon.
const NPC_LOCKED_GRADES = Object.freeze(['B', 'A', 'S']);
const FIRST_SOCKET = Object.freeze({ gold: 180, materials: Object.freeze({ RES_ATTUNEMENT_GEM: 1, RES_RUNE_FRAGMENT: 2 }) });

// Index = target level. Each row is the transition from the previous level (Doc 23 §9.2).
const ARMOR_LEVEL_STEPS = Object.freeze([
  null,
  Object.freeze({ gold: 40, basic: 2, ancient: 0 }),
  Object.freeze({ gold: 80, basic: 3, ancient: 1 }),
  Object.freeze({ gold: 140, basic: 4, ancient: 2 })
]);
const ARMOR_DEFENSE_BONUS_PERCENT = Object.freeze([0, 5, 10, 15]);
const ARMOR_VALUE_BONUS = Object.freeze([0, 15, 35, 60]);
// Doc 23 §9.3. B/A/S value bonuses belong to GD-09 and are not invented here.
const PRIMARY_SCALING_VALUE_BONUS = Object.freeze({ E: 20, D: 35, C: 50 });
const FIRST_SOCKET_VALUE_BONUS = 60;

// Family basic material (Doc 23 §9.2): Caballero = Black Iron, Ranger = Tanned Leather, Mago = Ether Dust.
const ARMOR_FAMILY_MATERIALS = Object.freeze([
  ['heavy_armor_', 'RES_BLACK_IRON'], ['light_armor_', 'RES_BLACK_IRON'],
  ['army_ranger_', 'RES_TANNED_LEATHER'], ['forest_ranger_', 'RES_TANNED_LEATHER'],
  ['placeholder_', 'RES_ETHER_DUST'], ['fire_mage_', 'RES_ETHER_DUST']
]);

const reject = (statusCode, errorCode, message) => { throw { statusCode, errorCode, message }; };
const limitReached = () => reject(409, 'UPGRADE_LIMIT_REACHED',
  'NPC upgrade limit reached; higher grades are available in the Dungeon.');

function armorBasicMaterial(lootId) {
  return ARMOR_FAMILY_MATERIALS.find(([prefix]) => lootId.startsWith(prefix))?.[1] ?? null;
}

function itemKind(lootId) {
  const template = getEquipmentTemplate(lootId);
  if (PHYSICAL_WEAPONS.includes(lootId) || UPGRADE_ONLY_PHYSICAL_WEAPONS.includes(lootId)) return { kind: 'weapon', merchant: 'blacksmith' };
  if (MAGICAL_WEAPONS.includes(lootId)) return { kind: 'weapon', merchant: 'arcanist' };
  if (ARMOR_CATEGORIES.includes(template?.category) && armorBasicMaterial(lootId)) return { kind: 'armor', merchant: 'blacksmith' };
  return null;
}

function planPrimaryScaling(modifiers) {
  const current = modifiers.primary?.grade ?? 'none';
  if (NPC_LOCKED_GRADES.includes(current)) {
    reject(409, 'UPGRADE_GRADE_LOCKED', 'The NPC cannot modify a B, A or S primary scaling.');
  }
  const step = PRIMARY_SCALING_STEPS[current];
  if (!step) limitReached();
  return { gold: step.gold, materials: { ...step.materials },
    modifiers: { ...modifiers, primary: { attribute: modifiers.primary?.attribute, grade: step.grade } } };
}

function planFirstSocket(modifiers) {
  if (modifiers.gemSocketCount > 0) reject(409, 'UPGRADE_SOCKET_EXISTS', 'The weapon already has a gem socket.');
  return { gold: FIRST_SOCKET.gold, materials: { ...FIRST_SOCKET.materials },
    modifiers: { ...modifiers, gemSocketCount: 1, gems: [null] } };
}

function planArmorLevel(lootId, modifiers) {
  const target = (modifiers?.upgradeLevel ?? 0) + 1;
  if (target > ARMOR_MAX_UPGRADE_LEVEL) limitReached();
  const step = ARMOR_LEVEL_STEPS[target];
  return { gold: step.gold,
    materials: { [armorBasicMaterial(lootId)]: step.basic, ...(step.ancient ? { RES_ANCIENT_ARMOR: step.ancient } : {}) },
    modifiers: { upgradeLevel: target } };
}

/**
 * Plan one upgrade for a validated instance. Returns { gold, materials: { lootId: amount }, modifiers }
 * with new modifiers; never mutates the input. Throws a service error when the upgrade is not allowed.
 */
function planUpgrade({ merchant, operation, lootId, instance }) {
  if (!UPGRADE_OPERATIONS.includes(operation)) reject(400, 'VALIDATION_FAILED', 'Unknown upgrade operation.');
  lootId = normalizeLootId(lootId);
  const kind = itemKind(lootId);
  const wantsArmor = operation === 'armor_level';
  if (!kind || (kind.kind === 'armor') !== wantsArmor) {
    reject(422, 'UPGRADE_NOT_ELIGIBLE', `Item '${lootId}' does not support the '${operation}' upgrade.`);
  }
  if (merchant !== kind.merchant) {
    reject(422, 'UPGRADE_WRONG_MERCHANT', `Merchant '${merchant}' does not upgrade '${lootId}'.`);
  }
  if (wantsArmor) return planArmorLevel(lootId, instance.modifiers);
  // A weapon instance may omit modifiers; that is the plain state (no scaling, no sockets).
  const modifiers = structuredClone(instance.modifiers ?? { gemSocketCount: 0, gems: [] });
  if (operation === 'first_socket') return planFirstSocket(modifiers);
  if (!modifiers.primary) modifiers.primary = { attribute: getEquipmentTemplate(lootId).naturalAttribute };
  return planPrimaryScaling(modifiers);
}

/** ObjectValue of one instance excluding installed gems (Doc 23 §9.3). */
function objectValue(lootId, instance) {
  const template = getEquipmentTemplate(lootId);
  const modifiers = instance?.modifiers;
  if (template.category === 'Weapon') {
    return template.sellValue + (PRIMARY_SCALING_VALUE_BONUS[modifiers?.primary?.grade] ?? 0) +
      ((modifiers?.gemSocketCount ?? 0) > 0 ? FIRST_SOCKET_VALUE_BONUS : 0);
  }
  return template.sellValue + ARMOR_VALUE_BONUS[modifiers?.upgradeLevel ?? 0];
}

/** DefensaFinal = floor(DefensaBase × (1 + BonoMejora)); applied separately to physical and magical defense. */
function upgradedDefense(baseDefense, upgradeLevel = 0) {
  const percent = ARMOR_DEFENSE_BONUS_PERCENT[upgradeLevel];
  if (!Number.isSafeInteger(baseDefense) || baseDefense < 0 || percent === undefined) {
    throw new RangeError('Invalid base defense or armor upgrade level.');
  }
  // Integer arithmetic avoids float error such as 20 × 1.15 = 22.999….
  return Math.floor(baseDefense * (100 + percent) / 100);
}

module.exports = { UPGRADE_OPERATIONS, planUpgrade, objectValue, upgradedDefense };
