'use strict';

const {
  BUY_CATALOG,
  SELL_CATALOG,
  EQUIPMENT_TEMPLATES,
  PERMANENT_BUY_CATALOG
} = require('../config/shopCatalog');

const PHYSICAL_WEAPONS = Object.freeze([
  'arming_sword',
  'long_sword',
  'great_hammer',
  'rondel_dagger',
  'compound_bow',
  'light_crossbow',
  'rapier'
]);

const MAGICAL_WEAPONS = Object.freeze([
  'magic_wand',
  'spell_book',
  'magic_staff',
  'magic_sword',
  'magic_cinquedea'
]);

const WEAPON_POOL = Object.freeze([...PHYSICAL_WEAPONS, ...MAGICAL_WEAPONS]);

const ARMOR_POOL = Object.freeze([
  // Heavy Armor
  'heavy_armor_helmet', 'heavy_armor_breastplate', 'heavy_armor_gauntlets', 'heavy_armor_leg_plate',
  // Light Armor
  'light_armor_open_sallet', 'light_armor_chain_mail_armor', 'light_armor_gloves', 'light_armor_chain_mail_trousers',
  // Army Ranger
  'army_ranger_hat', 'army_ranger_armor', 'army_ranger_gloves', 'army_ranger_trousers',
  // Forest Ranger
  'forest_ranger_hood', 'forest_ranger_leather_armor', 'forest_ranger_gloves', 'forest_ranger_trousers',
  // Arcane Mage
  'placeholder_helmet', 'placeholder_armor', 'placeholder_gloves', 'placeholder_boots',
  // Fire Mage
  'fire_mage_hood', 'fire_mage_garb', 'fire_mage_gloves', 'fire_mage_trousers'
]);

const UTILITY_POOL = Object.freeze([
  'CON_LOCKPICK',
  'UTL_RUNE_COMPASS',
  'UTL_ARCANE_DETECTOR'
]);

const DEMAND_CANDIDATE_POOL = Object.freeze([
  // 21 Resources
  'RES_SLIME_GEL', 'RES_SLIME_CORE', 'RES_WITHERED_BONE', 'RES_CORRUPT_MARROW',
  'RES_ANCIENT_ARMOR', 'RES_GOBLIN_SCRAP', 'RES_GOBLIN_INSIGNIA', 'RES_GOBLIN_ARROWHEAD',
  'RES_BAT_WING', 'RES_BAT_FANG', 'RES_SPECTRAL_ESSENCE', 'RES_SOUL_FRAGMENT',
  'RES_GOLEM_CORE', 'RES_ETHER_DUST', 'RES_RUNE_FRAGMENT', 'RES_BLACK_IRON',
  'RES_TANNED_LEATHER', 'RES_ANCIENT_ROOT', 'RES_ATTUNEMENT_GEM', 'RES_LOST_GRIMOIRE_PAGE',
  'RES_CUSTODIAN_SEAL',
  // 8 Treasures
  'TRE_CHALICE', 'TRE_CROWN', 'TRE_FAMINE_IDOL', 'TRE_WANDERER_EYE',
  'TRE_PILGRIM_LIGHT', 'TRE_SANCTUARY_HEART', 'TRE_CHAOS_COIN', 'TRE_VOID_BELL'
]);

const SPECIAL_MODIFIERS = Object.freeze([
  { type: 'scaling_E', prob: 30, increment: 20, description: 'Escalado E' },
  { type: 'scaling_D', prob: 25, increment: 35, description: 'Escalado D' },
  { type: 'scaling_C', prob: 15, increment: 50, description: 'Escalado C' },
  { type: 'empty_socket', prob: 30, increment: 60, description: '1 ranura vacía' }
]);

/**
 * Computes the weekly window (Monday 00:00 UTC to next Monday 00:00 UTC).
 * @param {Date|number|string} date
 */
function getWeeklyWindow(date = new Date()) {
  const d = new Date(date);
  const day = d.getUTCDay();
  const diffToMonday = (day + 6) % 7;
  const monday = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate() - diffToMonday, 0, 0, 0, 0));
  const nextMonday = new Date(monday.getTime() + 7 * 24 * 60 * 60 * 1000);
  const weekId = monday.toISOString().slice(0, 10);
  return {
    monday,
    nextMonday,
    weekId,
    timestamp: monday.getTime(),
    nextTimestamp: nextMonday.getTime()
  };
}

/**
 * Creates a deterministic PRNG using Mulberry32.
 * @param {string} seedStr
 */
function createPrng(seedStr) {
  let hash = 0;
  for (let i = 0; i < seedStr.length; i++) {
    hash = Math.imul(31, hash) + seedStr.charCodeAt(i) | 0;
  }
  let state = hash >>> 0;
  return function next() {
    state = (state + 0x6D2B79F5) | 0;
    let t = Math.imul(state ^ (state >>> 15), 1 | state);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/**
 * Deterministically shuffles an array copy using Fisher-Yates and a PRNG.
 */
function shuffle(array, rand) {
  const copy = [...array];
  for (let i = copy.length - 1; i > 0; i--) {
    const j = Math.floor(rand() * (i + 1));
    const temp = copy[i];
    copy[i] = copy[j];
    copy[j] = temp;
  }
  return copy;
}

/**
 * Selects an initial modifier for the special weapon according to Doc 23 §5.6.
 * Probabilities: E 30%, D 25%, C 15%, 1 slot 30%.
 */
function rollSpecialModifier(rand) {
  const roll = rand() * 100;
  if (roll < 30) return SPECIAL_MODIFIERS[0]; // E
  if (roll < 55) return SPECIAL_MODIFIERS[1]; // D
  if (roll < 70) return SPECIAL_MODIFIERS[2]; // C
  return SPECIAL_MODIFIERS[3]; // empty_socket
}

/**
 * Generates the authoritative weekly rotation for the given date.
 * @param {Date|number|string} date
 */
function getWeeklyRotation(date = new Date()) {
  const window = getWeeklyWindow(date);
  const rand = createPrng(`grimhold_week_${window.weekId}`);

  // 1. 3 Normal Weapons from pool of 12
  const shuffledWeapons = shuffle(WEAPON_POOL, rand);
  const selectedNormalWeapons = shuffledWeapons.slice(0, 3).map(lootId => {
    const template = EQUIPMENT_TEMPLATES[lootId];
    const objectValue = template.objectValue || template.sellValue;
    const isPhysical = PHYSICAL_WEAPONS.includes(lootId);
    return {
      lootId,
      merchant: isPhysical ? 'blacksmith' : 'arcanist',
      objectValue,
      buyPrice: Math.ceil(objectValue * 2.0),
      sellPrice: Math.floor(objectValue * 1.0),
      stock: 1
    };
  });

  // 2. 1 Special Weapon from remaining 9 weapons
  const remainingWeapons = shuffledWeapons.slice(3);
  const specialWeaponId = remainingWeapons[0];
  const specialTemplate = EQUIPMENT_TEMPLATES[specialWeaponId];
  const baseObjectValue = specialTemplate.objectValue || specialTemplate.sellValue;
  const modifier = rollSpecialModifier(rand);
  const specialObjectValue = baseObjectValue + modifier.increment;
  const isSpecialPhysical = PHYSICAL_WEAPONS.includes(specialWeaponId);
  const specialWeapon = {
    lootId: specialWeaponId,
    merchant: isSpecialPhysical ? 'blacksmith' : 'arcanist',
    baseObjectValue,
    objectValue: specialObjectValue,
    buyPrice: Math.ceil(specialObjectValue * 2.0),
    sellPrice: Math.floor(specialObjectValue * 1.0),
    stock: 1,
    modifier: {
      type: modifier.type,
      increment: modifier.increment,
      description: modifier.description
    }
  };

  // 3. 3 Normal Armors from pool of 24 (All sold by Herrero per Doc 23 §5.4)
  const shuffledArmors = shuffle(ARMOR_POOL, rand);
  const selectedArmors = shuffledArmors.slice(0, 3).map(lootId => {
    const template = EQUIPMENT_TEMPLATES[lootId];
    const objectValue = template.objectValue || template.sellValue;
    return {
      lootId,
      merchant: 'blacksmith',
      objectValue,
      buyPrice: Math.ceil(objectValue * 2.0),
      sellPrice: Math.floor(objectValue * 1.0),
      stock: 1
    };
  });

  // 4. 2 Utilities from pool of 3 (Sold by Mercader General per Doc 23 §5.5)
  const shuffledUtilities = shuffle(UTILITY_POOL, rand);
  const selectedUtilities = shuffledUtilities.slice(0, 2).map(lootId => {
    const sellOffer = SELL_CATALOG[lootId];
    const objectValue = sellOffer.sellValue;
    const stock = lootId === 'CON_LOCKPICK' ? 3 : 1;
    return {
      lootId,
      merchant: 'general',
      objectValue,
      buyPrice: Math.ceil(objectValue * 2.0),
      sellPrice: Math.floor(objectValue * 1.0),
      stock
    };
  });

  // 5. 3 Demand Items (+20% sell price) (Doc 23 §7)
  const shuffledDemand = shuffle(DEMAND_CANDIDATE_POOL, rand);
  const selectedDemand = shuffledDemand.slice(0, 3).map(lootId => {
    const sellOffer = SELL_CATALOG[lootId];
    const objectValue = sellOffer.sellValue;
    return {
      lootId,
      merchant: 'general',
      objectValue,
      regularSellPrice: Math.floor(objectValue * 1.0),
      demandSellPrice: Math.floor(objectValue * 1.2)
    };
  });

  // 6. Permanent Offers (Doc 23 §5.1)
  const permanentOffers = Object.entries(PERMANENT_BUY_CATALOG).map(([lootId, offer]) => {
    const sellOffer = SELL_CATALOG[lootId];
    const objectValue = sellOffer.sellValue;
    return {
      lootId,
      merchant: 'general',
      objectValue,
      buyPrice: offer.buyPrice,
      sellPrice: Math.floor(objectValue * 1.0),
      stock: 'unlimited'
    };
  });

  return {
    weekId: window.weekId,
    startTime: window.monday.toISOString(),
    endTime: window.nextMonday.toISOString(),
    timestamp: window.timestamp,
    nextRotationTimestamp: window.nextTimestamp,
    normalWeapons: selectedNormalWeapons,
    specialWeapon,
    armors: selectedArmors,
    utilities: selectedUtilities,
    demandItems: selectedDemand,
    permanentOffers
  };
}

/**
 * Filters the active weekly offers for a specific merchant.
 * @param {'general'|'blacksmith'|'arcanist'} merchantType
 * @param {Date|number|string} date
 */
function getMerchantOffers(merchantType, date = new Date()) {
  const rotation = getWeeklyRotation(date);
  const type = (merchantType || '').toLowerCase();

  if (type === 'general') {
    return {
      merchant: 'general',
      weekId: rotation.weekId,
      nextRotationTimestamp: rotation.nextRotationTimestamp,
      permanent: rotation.permanentOffers,
      rotating: rotation.utilities,
      demand: rotation.demandItems
    };
  }

  if (type === 'blacksmith') {
    const weapons = rotation.normalWeapons.filter(w => w.merchant === 'blacksmith');
    const special = rotation.specialWeapon.merchant === 'blacksmith' ? rotation.specialWeapon : null;
    return {
      merchant: 'blacksmith',
      weekId: rotation.weekId,
      nextRotationTimestamp: rotation.nextRotationTimestamp,
      weapons,
      specialWeapon: special,
      armors: rotation.armors
    };
  }

  if (type === 'arcanist') {
    const weapons = rotation.normalWeapons.filter(w => w.merchant === 'arcanist');
    const special = rotation.specialWeapon.merchant === 'arcanist' ? rotation.specialWeapon : null;
    return {
      merchant: 'arcanist',
      weekId: rotation.weekId,
      nextRotationTimestamp: rotation.nextRotationTimestamp,
      weapons,
      specialWeapon: special,
      armors: []
    };
  }

  return {
    merchant: type,
    weekId: rotation.weekId,
    nextRotationTimestamp: rotation.nextRotationTimestamp,
    offers: []
  };
}

/**
 * Checks whether an item action is permitted by a specific merchant type according to Doc 23 §3.
 * @param {'general'|'blacksmith'|'arcanist'|''} merchantType
 * @param {'buy'|'sell'|'buyback'} action
 * @param {string} lootId
 * @returns {boolean}
 */
function isAllowedByMerchant(merchantType, action, lootId) {
  const type = (merchantType || '').toLowerCase().trim();
  if (!type) return true; // If merchant unspecified, generic commerce rules apply
  const { normalizeLootId } = require('./InventoryLootIdNormalizer');
  const normalized = normalizeLootId(lootId);
  const isPhysical = PHYSICAL_WEAPONS.includes(normalized);
  const isMagical = MAGICAL_WEAPONS.includes(normalized);
  const isArmor = ARMOR_POOL.includes(normalized);
  const isUtility = UTILITY_POOL.includes(normalized) || UTILITY_POOL.includes(lootId);
  const isPermanent = Object.hasOwn(PERMANENT_BUY_CATALOG, lootId) || Object.hasOwn(PERMANENT_BUY_CATALOG, normalized);

  if (action === 'buyback') {
    // Buyback is shared between the three merchants (Doc 23 §8.3)
    return ['general', 'blacksmith', 'arcanist'].includes(type);
  }

  if (type === 'general') {
    if (action === 'buy') {
      return isPermanent || isUtility;
    }
    if (action === 'sell') {
      return true; // General merchant buys all tradable items
    }
    return false;
  }

  if (type === 'blacksmith') {
    if (action === 'buy' || action === 'sell') {
      return isPhysical || isArmor;
    }
    return false;
  }

  if (type === 'arcanist') {
    if (action === 'buy' || action === 'sell') {
      return isMagical;
    }
    return false;
  }

  return false;
}

module.exports = {
  PHYSICAL_WEAPONS,
  MAGICAL_WEAPONS,
  WEAPON_POOL,
  ARMOR_POOL,
  UTILITY_POOL,
  DEMAND_CANDIDATE_POOL,
  SPECIAL_MODIFIERS,
  getWeeklyWindow,
  getWeeklyRotation,
  getMerchantOffers,
  isAllowedByMerchant
};
