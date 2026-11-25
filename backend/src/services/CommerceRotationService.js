'use strict';

const {
  BUY_CATALOG,
  SELL_CATALOG,
  EQUIPMENT_TEMPLATES,
  PERMANENT_BUY_CATALOG
} = require('../config/shopCatalog');

let commerceConfig = null;
try {
  commerceConfig = require('../config/commerceConfig.json');
} catch {
  commerceConfig = null;
}

const PHYSICAL_WEAPONS = Object.freeze(
  commerceConfig?.pools?.physicalWeapons || [
    'arming_sword',
    'long_sword',
    'great_hammer',
    'rondel_dagger',
    'compound_bow',
    'light_crossbow',
    'rapier'
  ]
);

const MAGICAL_WEAPONS = Object.freeze(
  commerceConfig?.pools?.magicalWeapons || [
    'magic_wand',
    'spell_book',
    'magic_staff',
    'magic_sword',
    'magic_cinquedea'
  ]
);

const WEAPON_POOL = Object.freeze([...PHYSICAL_WEAPONS, ...MAGICAL_WEAPONS]);

const ARMOR_POOL = Object.freeze(
  commerceConfig?.pools?.armors || [
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
  ]
);

const UTILITY_POOL = Object.freeze(
  commerceConfig?.pools?.utilities?.map(u => u.lootId) || [
    'CON_LOCKPICK',
    'UTL_RUNE_COMPASS',
    'UTL_ARCANE_DETECTOR'
  ]
);

const UTILITY_STOCKS = Object.freeze(
  commerceConfig?.pools?.utilities
    ? Object.fromEntries(commerceConfig.pools.utilities.map(u => [u.lootId, u.stock]))
    : { CON_LOCKPICK: 3, UTL_RUNE_COMPASS: 1, UTL_ARCANE_DETECTOR: 1 }
);

const DEMAND_CANDIDATE_POOL = Object.freeze(
  commerceConfig?.pools?.demandCandidates || [
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
  ]
);

const SPECIAL_MODIFIERS = Object.freeze(
  commerceConfig?.specialModifiers || [
    { type: 'scaling_E', prob: 30, increment: 20, description: 'Escalado E' },
    { type: 'scaling_D', prob: 25, increment: 35, description: 'Escalado D' },
    { type: 'scaling_C', prob: 15, increment: 50, description: 'Escalado C' },
    { type: 'empty_socket', prob: 30, increment: 60, description: '1 ranura vacía' }
  ]
);

const ROTATION_COUNTS = Object.freeze(
  commerceConfig?.rotationCounts || {
    normalWeapons: 3,
    specialWeapons: 1,
    armors: 3,
    utilities: 2,
    demandItems: 3
  }
);

const MULTIPLIERS = Object.freeze(
  commerceConfig?.multipliers || {
    buyPriceMultiplier: 2.0,
    regularSellMultiplier: 1.0,
    demandSellMultiplier: 1.2,
    buybackPriceMultiplier: 1.5,
    maxBuybackEntries: 16
  }
);

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
 * Probabilities: configured in TownCommerceConfiguration / SPECIAL_MODIFIERS.
 */
function rollSpecialModifier(rand) {
  const roll = rand() * 100;
  let cumulative = 0;
  for (const mod of SPECIAL_MODIFIERS) {
    cumulative += mod.prob;
    if (roll < cumulative) return mod;
  }
  return SPECIAL_MODIFIERS[SPECIAL_MODIFIERS.length - 1];
}

const WEEK_MS = 7 * 24 * 60 * 60 * 1000;
const DEMAND_EPOCH_MS = Date.UTC(2025, 0, 6, 0, 0, 0, 0); // Fixed Monday 00:00 UTC anchor
const MAX_DEMAND_CACHE_ENTRIES = 512;
const demandRotationCache = new Map();

/**
 * Selects `count` distinct demand items from `pool` prioritizing candidates NOT selected
 * in `previousSelectedIds`. Repeats from `previousSelectedIds` are allowed only when the
 * non-repeated pool is smaller than `count` (Doc 23 §7).
 */
function selectDemandItems(pool, count, weekId, previousSelectedIds = []) {
  const uniquePool = [...new Set(pool || [])];
  if (uniquePool.length === 0 || count <= 0) return [];
  const targetCount = Math.min(count, uniquePool.length);
  const previousSet = new Set(previousSelectedIds || []);
  const rand = createPrng(`grimhold_demand_${weekId}`);

  const fresh = shuffle(uniquePool.filter(id => !previousSet.has(id)), rand);
  if (fresh.length >= targetCount) {
    return fresh.slice(0, targetCount);
  }

  const repeated = shuffle(uniquePool.filter(id => previousSet.has(id)), rand);
  return [...fresh, ...repeated.slice(0, targetCount - fresh.length)];
}

/**
 * Deterministically resolves the weekly demand loot IDs for `window`, chaining from the
 * fixed Monday anchor so every week W prioritizes candidates absent from week W-1.
 */
function getWeeklyDemandIds(window, pool = DEMAND_CANDIDATE_POOL, count = ROTATION_COUNTS.demandItems) {
  const useDefaultCache = pool === DEMAND_CANDIDATE_POOL && count === ROTATION_COUNTS.demandItems;
  if (useDefaultCache && demandRotationCache.has(window.weekId)) {
    return demandRotationCache.get(window.weekId);
  }

  if (window.timestamp <= DEMAND_EPOCH_MS) {
    const selected = selectDemandItems(pool, count, window.weekId, []);
    if (useDefaultCache) cacheDemandWeek(window.weekId, selected);
    return selected;
  }

  const weeksFromEpoch = Math.floor((window.timestamp - DEMAND_EPOCH_MS) / WEEK_MS);
  // Start from the nearest cached predecessor if available
  let startStep = 0;
  let previous = [];
  if (useDefaultCache) {
    for (let step = weeksFromEpoch - 1; step >= 0; step--) {
      const candidateWeekId = new Date(DEMAND_EPOCH_MS + step * WEEK_MS).toISOString().slice(0, 10);
      if (demandRotationCache.has(candidateWeekId)) {
        previous = demandRotationCache.get(candidateWeekId);
        startStep = step + 1;
        break;
      }
    }
  }

  let current = previous;
  for (let step = startStep; step <= weeksFromEpoch; step++) {
    const stepWeekId = new Date(DEMAND_EPOCH_MS + step * WEEK_MS).toISOString().slice(0, 10);
    current = selectDemandItems(pool, count, stepWeekId, current);
    if (useDefaultCache) cacheDemandWeek(stepWeekId, current);
  }

  return current;
}

function cacheDemandWeek(weekId, ids) {
  if (demandRotationCache.size >= MAX_DEMAND_CACHE_ENTRIES) {
    const oldestKey = demandRotationCache.keys().next().value;
    demandRotationCache.delete(oldestKey);
  }
  demandRotationCache.set(weekId, ids);
}

/**
 * Generates the authoritative weekly rotation for the given date.
 * @param {Date|number|string} date
 */
function getWeeklyRotation(date = new Date()) {
  const window = getWeeklyWindow(date);
  const rand = createPrng(`grimhold_week_${window.weekId}`);

  // 1. Normal Weapons from configured pool
  const shuffledWeapons = shuffle(WEAPON_POOL, rand);
  const selectedNormalWeapons = shuffledWeapons.slice(0, ROTATION_COUNTS.normalWeapons).map(lootId => {
    const template = EQUIPMENT_TEMPLATES[lootId];
    const objectValue = template.objectValue || template.sellValue;
    const isPhysical = PHYSICAL_WEAPONS.includes(lootId);
    return {
      lootId,
      merchant: isPhysical ? 'blacksmith' : 'arcanist',
      objectValue,
      buyPrice: Math.ceil(objectValue * MULTIPLIERS.buyPriceMultiplier),
      sellPrice: Math.floor(objectValue * MULTIPLIERS.regularSellMultiplier),
      stock: 1
    };
  });

  // 2. Special Weapon from remaining weapons
  const remainingWeapons = shuffledWeapons.slice(ROTATION_COUNTS.normalWeapons);
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
    buyPrice: Math.ceil(specialObjectValue * MULTIPLIERS.buyPriceMultiplier),
    sellPrice: Math.floor(specialObjectValue * MULTIPLIERS.regularSellMultiplier),
    stock: 1,
    modifier: {
      type: modifier.type,
      increment: modifier.increment,
      description: modifier.description
    }
  };

  // 3. Normal Armors from configured pool (All sold by Herrero per Doc 23 §5.4)
  const shuffledArmors = shuffle(ARMOR_POOL, rand);
  const selectedArmors = shuffledArmors.slice(0, ROTATION_COUNTS.armors).map(lootId => {
    const template = EQUIPMENT_TEMPLATES[lootId];
    const objectValue = template.objectValue || template.sellValue;
    return {
      lootId,
      merchant: 'blacksmith',
      objectValue,
      buyPrice: Math.ceil(objectValue * MULTIPLIERS.buyPriceMultiplier),
      sellPrice: Math.floor(objectValue * MULTIPLIERS.regularSellMultiplier),
      stock: 1
    };
  });

  // 4. Utilities from configured pool (Sold by Mercader General per Doc 23 §5.5)
  const shuffledUtilities = shuffle(UTILITY_POOL, rand);
  const selectedUtilities = shuffledUtilities.slice(0, ROTATION_COUNTS.utilities).map(lootId => {
    const sellOffer = SELL_CATALOG[lootId];
    const objectValue = sellOffer.sellValue;
    const stock = UTILITY_STOCKS[lootId] !== undefined ? UTILITY_STOCKS[lootId] : (lootId === 'CON_LOCKPICK' ? 3 : 1);
    return {
      lootId,
      merchant: 'general',
      objectValue,
      buyPrice: Math.ceil(objectValue * MULTIPLIERS.buyPriceMultiplier),
      sellPrice: Math.floor(objectValue * MULTIPLIERS.regularSellMultiplier),
      stock
    };
  });

  // 5. Demand Items (Doc 23 §7): prioritize candidates absent from the previous weekly rotation
  const selectedDemandIds = getWeeklyDemandIds(window);
  const selectedDemand = selectedDemandIds.map(lootId => {
    const sellOffer = SELL_CATALOG[lootId];
    const objectValue = sellOffer.sellValue;
    return {
      lootId,
      merchant: 'general',
      objectValue,
      regularSellPrice: Math.floor(objectValue * MULTIPLIERS.regularSellMultiplier),
      demandSellPrice: Math.floor(objectValue * MULTIPLIERS.demandSellMultiplier)
    };
  });

  // 6. Permanent Offers (Doc 23 §5.1)
  const permanentOffers = commerceConfig?.permanentOffers
    ? commerceConfig.permanentOffers.map(p => {
        const sellOffer = SELL_CATALOG[p.lootId] || { sellValue: Math.floor(p.buyPrice / MULTIPLIERS.buyPriceMultiplier) };
        const objectValue = sellOffer.sellValue;
        return {
          lootId: p.lootId,
          merchant: 'general',
          objectValue,
          buyPrice: p.buyPrice,
          sellPrice: Math.floor(objectValue * MULTIPLIERS.regularSellMultiplier),
          stock: p.unlimited ? 'unlimited' : p.stock
        };
      })
    : Object.entries(PERMANENT_BUY_CATALOG).map(([lootId, offer]) => {
        const sellOffer = SELL_CATALOG[lootId];
        const objectValue = sellOffer.sellValue;
        return {
          lootId,
          merchant: 'general',
          objectValue,
          buyPrice: offer.buyPrice,
          sellPrice: Math.floor(objectValue * MULTIPLIERS.regularSellMultiplier),
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
  selectDemandItems,
  getMerchantOffers,
  isAllowedByMerchant
};
