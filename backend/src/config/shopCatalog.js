'use strict';

const { normalizeLootId } = require('../services/InventoryLootIdNormalizer');

/**
 * Maximum quantity allowed in a single shop purchase or sale transaction.
 * Prevents numerical overflow and excessive item creation.
 */
const MAX_TRANSACTION_AMOUNT = 999;
// MerchantRequestValidator: MaxSaleLines and TownMerchantNetworkController.MaxStockSlots.
const MAX_TRADE_SALE_LINES = 30;
const MAX_TRADE_PURCHASE_LINES = 16;

let commerceConfig = null;
try {
  commerceConfig = require('./commerceConfig.json');
} catch {
  commerceConfig = null;
}

const BUYBACK_PRICE_MULTIPLIER = commerceConfig?.multipliers?.buybackPriceMultiplier ?? 1.5;
const BUYBACK_MAX_ENTRIES = commerceConfig?.multipliers?.maxBuybackEntries ?? 16;

// Classification mirrors LootCategory 5..10, including currently unsellable equipment.
const BUYBACK_ELIGIBLE_IDS = Object.freeze([
  'placeholder_helmet', 'placeholder_armor', 'placeholder_gloves', 'placeholder_boots',
  'army_ranger_armor', 'army_ranger_gloves', 'army_ranger_hat', 'army_ranger_trousers',
  'fire_mage_garb', 'fire_mage_gloves', 'fire_mage_hood', 'fire_mage_trousers',
  'forest_ranger_gloves', 'forest_ranger_hood', 'forest_ranger_leather_armor', 'forest_ranger_trousers',
  'heavy_armor_breastplate', 'heavy_armor_gauntlets', 'heavy_armor_helmet', 'heavy_armor_leg_plate',
  'light_armor_chain_mail_armor', 'light_armor_chain_mail_trousers', 'light_armor_gloves', 'light_armor_open_sallet',
  'arming_sword', 'great_hammer', 'compound_bow', 'light_crossbow', 'long_bow', 'long_sword',
  'magic_cinquedea', 'magic_staff', 'magic_sword', 'magic_wand', 'rapier', 'rondel_dagger',
  'shield', 'spell_book', 'zweihander', 'training_short_sword', 'training_dagger', 'training_wand'
]);

// Unity LootDefinitions are the source; parity tests check both references and values.
const weapon = (naturalAttribute, sellValue = 0) => Object.freeze({
  category: 'Weapon', naturalAttribute, legacyCoefficient: 0, sellValue
});
const armor = (category, sellValue = 5) => Object.freeze({
  category, sellValue
});
const EQUIPMENT_TEMPLATES = Object.freeze({
  arming_sword: weapon('strength', 60),
  great_hammer: weapon('strength', 110),
  compound_bow: weapon('dexterity', 80),
  light_crossbow: weapon('dexterity', 100),
  long_bow: weapon('dexterity', 0),
  long_sword: weapon('strength', 100),
  magic_cinquedea: weapon('dexterity', 90),
  magic_staff: weapon('intelligence', 110),
  magic_sword: weapon('strength', 125),
  magic_wand: weapon('intelligence', 70),
  rapier: weapon('strength', 85),
  rondel_dagger: weapon('dexterity', 45),
  spell_book: weapon('intelligence', 95),
  zweihander: weapon('strength', 0),
  shield: Object.freeze({ category: 'Shield', sellValue: 0 }),
  training_short_sword: weapon('strength', 0),
  training_dagger: weapon('dexterity', 0),
  training_wand: weapon('intelligence', 0),
  placeholder_helmet: armor('Helmet', 85),
  placeholder_armor: armor('Armor', 145),
  placeholder_gloves: armor('Gloves', 55),
  placeholder_boots: armor('Boots', 70),
  army_ranger_armor: armor('Armor', 110),
  army_ranger_gloves: armor('Gloves', 45),
  army_ranger_hat: armor('Helmet', 65),
  army_ranger_trousers: armor('Boots', 60),
  fire_mage_garb: armor('Armor', 100),
  fire_mage_gloves: armor('Gloves', 40),
  fire_mage_hood: armor('Helmet', 60),
  fire_mage_trousers: armor('Boots', 55),
  forest_ranger_gloves: armor('Gloves', 55),
  forest_ranger_hood: armor('Helmet', 80),
  forest_ranger_leather_armor: armor('Armor', 135),
  forest_ranger_trousers: armor('Boots', 75),
  heavy_armor_breastplate: armor('Armor', 160),
  heavy_armor_gauntlets: armor('Gloves', 60),
  heavy_armor_helmet: armor('Helmet', 90),
  heavy_armor_leg_plate: armor('Boots', 80),
  light_armor_chain_mail_armor: armor('Armor', 130),
  light_armor_chain_mail_trousers: armor('Boots', 65),
  light_armor_gloves: armor('Gloves', 50),
  light_armor_open_sallet: armor('Helmet', 75)
});
const GEM_CATALOG = Object.freeze({
  RES_ATTUNEMENT_GEM: Object.freeze({ sellValue: 70 }),
  RES_RUBY_GEM: Object.freeze({ sellValue: 70 }),
  RES_SAPPHIRE_GEM: Object.freeze({ sellValue: 70 })
});
function getEquipmentTemplate(lootId) {
  return Object.hasOwn(EQUIPMENT_TEMPLATES, normalizeLootId(lootId)) ? EQUIPMENT_TEMPLATES[normalizeLootId(lootId)] : null;
}

const SPECIAL_CONSUMABLE_BUYBACK_IDS = Object.freeze([
  'CON_EXPLOSIVE_BOMB',
  'CON_SMOKE_BOMB',
  'CON_THROWING_DAGGER',
  'CON_SELF_REVIVE'
]);

function isBuybackEligible(lootId, version = 3) {
  if (!lootId || typeof lootId !== 'string') return false;
  const normalized = normalizeLootId(lootId);
  if (version < 3) {
    return Boolean(getEquipmentTemplate(normalized));
  }
  if (SPECIAL_CONSUMABLE_BUYBACK_IDS.includes(lootId) || SPECIAL_CONSUMABLE_BUYBACK_IDS.includes(normalized)) {
    return true;
  }
  const template = getEquipmentTemplate(normalized);
  if (!template) return false;
  // Exclude training items and non-tradable equipment (ObjectValue <= 0)
  return (template.objectValue || template.sellValue || 0) > 0;
}

/**
 * Permanent buy offers available unconditionally in unlimited quantity (Doc 23 §5.1).
 */
const PERMANENT_BUY_CATALOG = Object.freeze({
  CON_HEALTH_POTION: { buyPrice: 40, isFree: false },
  CON_MANA_FRAGMENT: { buyPrice: 16, isFree: false },
  CON_MANA_POTION: { buyPrice: 40, isFree: false }
});

/**
 * Authoritative catalog of items offered for purchase by Town Merchants.
 * Includes permanent consumables, rotating utilities, and rotating weapons & armors (Doc 23 §5-6).
 * Special consumables, treasures, and resources are NOT buyable from regular NPC stock.
 * Prices are in gold (currency) units.
 */
const BUY_CATALOG = Object.freeze({
  // Permanent offer (Doc 23 §5.1)
  CON_HEALTH_POTION: { buyPrice: 40, isFree: false },
  CON_MANA_FRAGMENT: { buyPrice: 16, isFree: false },
  CON_MANA_POTION: { buyPrice: 40, isFree: false },

  // Rotating Utilities (Doc 23 §5.5)
  CON_LOCKPICK: { buyPrice: 50, isFree: false },
  UTL_ARCANE_DETECTOR: { buyPrice: 360, isFree: false },
  UTL_RUNE_COMPASS: { buyPrice: 280, isFree: false },

  // 12 Normal Weapons (Doc 23 §6.1, buyPrice = ceil(ObjectValue * 2.0))
  arming_sword: { buyPrice: 120, isFree: false },
  compound_bow: { buyPrice: 160, isFree: false },
  great_hammer: { buyPrice: 220, isFree: false },
  light_crossbow: { buyPrice: 200, isFree: false },
  long_sword: { buyPrice: 200, isFree: false },
  magic_cinquedea: { buyPrice: 180, isFree: false },
  magic_staff: { buyPrice: 220, isFree: false },
  magic_sword: { buyPrice: 250, isFree: false },
  magic_wand: { buyPrice: 140, isFree: false },
  rapier: { buyPrice: 170, isFree: false },
  rondel_dagger: { buyPrice: 90, isFree: false },
  spell_book: { buyPrice: 190, isFree: false },

  // 24 Normal Armors (Doc 23 §6.2, buyPrice = ceil(ObjectValue * 2.0))
  heavy_armor_helmet: { buyPrice: 180, isFree: false },
  heavy_armor_breastplate: { buyPrice: 320, isFree: false },
  heavy_armor_gauntlets: { buyPrice: 120, isFree: false },
  heavy_armor_leg_plate: { buyPrice: 160, isFree: false },
  light_armor_open_sallet: { buyPrice: 150, isFree: false },
  light_armor_chain_mail_armor: { buyPrice: 260, isFree: false },
  light_armor_gloves: { buyPrice: 100, isFree: false },
  light_armor_chain_mail_trousers: { buyPrice: 130, isFree: false },
  army_ranger_hat: { buyPrice: 130, isFree: false },
  army_ranger_armor: { buyPrice: 220, isFree: false },
  army_ranger_gloves: { buyPrice: 90, isFree: false },
  army_ranger_trousers: { buyPrice: 120, isFree: false },
  forest_ranger_hood: { buyPrice: 160, isFree: false },
  forest_ranger_leather_armor: { buyPrice: 270, isFree: false },
  forest_ranger_gloves: { buyPrice: 110, isFree: false },
  forest_ranger_trousers: { buyPrice: 150, isFree: false },
  placeholder_helmet: { buyPrice: 170, isFree: false },
  placeholder_armor: { buyPrice: 290, isFree: false },
  placeholder_gloves: { buyPrice: 110, isFree: false },
  placeholder_boots: { buyPrice: 140, isFree: false },
  fire_mage_hood: { buyPrice: 120, isFree: false },
  fire_mage_garb: { buyPrice: 200, isFree: false },
  fire_mage_gloves: { buyPrice: 80, isFree: false },
  fire_mage_trousers: { buyPrice: 110, isFree: false }
});

/**
 * Authoritative catalog of items the Town Merchant is willing to purchase from players.
 * Values are in gold (currency) units per single unit.
 * Unity LootDefinition assets are the source of truth: mirror positive sell values only.
 * Backend-only ids use GD-14 values and have no Unity asset yet.
 */
const SELL_CATALOG = Object.freeze({
  CON_EXPLOSIVE_BOMB: { sellValue: 30 },
  CON_HEALTH_POTION: { sellValue: 20 },
  CON_LOCKPICK: { sellValue: 25 },
  CON_MANA_FRAGMENT: { sellValue: 8 },
  CON_MANA_POTION: { sellValue: 20 },
  CON_SELF_REVIVE: { sellValue: 300 },
  CON_SMOKE_BOMB: { sellValue: 45 },
  CON_THROWING_DAGGER: { sellValue: 12 },
  RES_ANCIENT_ARMOR: { sellValue: 30 },
  RES_ANCIENT_ROOT: { sellValue: 12 },
  ...GEM_CATALOG,
  RES_BAT_FANG: { sellValue: 22 },
  RES_BAT_WING: { sellValue: 12 },
  RES_BLACK_IRON: { sellValue: 15 },
  RES_CORRUPT_MARROW: { sellValue: 28 },
  RES_CUSTODIAN_SEAL: { sellValue: 45 },
  RES_ETHER_DUST: { sellValue: 18 },
  RES_GOBLIN_ARROWHEAD: { sellValue: 14 },
  RES_GOBLIN_INSIGNIA: { sellValue: 32 },
  RES_GOBLIN_SCRAP: { sellValue: 10 },
  RES_GOLEM_CORE: { sellValue: 70 },
  RES_LOST_GRIMOIRE_PAGE: { sellValue: 35 },
  RES_RUNE_FRAGMENT: { sellValue: 20 },
  RES_SLIME_CORE: { sellValue: 24 },
  RES_SLIME_GEL: { sellValue: 8 },
  RES_SOUL_FRAGMENT: { sellValue: 65 },
  RES_SPECTRAL_ESSENCE: { sellValue: 38 },
  RES_TANNED_LEATHER: { sellValue: 10 },
  RES_WITHERED_BONE: { sellValue: 10 },
  TRE_CHALICE: { sellValue: 200 },
  TRE_CHAOS_COIN: { sellValue: 400 },
  TRE_CROWN: { sellValue: 350 },
  TRE_FAMINE_IDOL: { sellValue: 400 },
  TRE_PILGRIM_LIGHT: { sellValue: 300 },
  TRE_SANCTUARY_HEART: { sellValue: 350 },
  TRE_VOID_BELL: { sellValue: 450 },
  TRE_WANDERER_EYE: { sellValue: 450 },
  UTL_ARCANE_DETECTOR: { sellValue: 180 },
  UTL_RUNE_COMPASS: { sellValue: 140 },
  bone: { sellValue: 5 },

  // 12 Normal Weapons (Doc 23 §6.1, sellValue = floor(ObjectValue * 1.0))
  arming_sword: { sellValue: 60 },
  compound_bow: { sellValue: 80 },
  great_hammer: { sellValue: 110 },
  light_crossbow: { sellValue: 100 },
  long_sword: { sellValue: 100 },
  magic_cinquedea: { sellValue: 90 },
  magic_staff: { sellValue: 110 },
  magic_sword: { sellValue: 125 },
  magic_wand: { sellValue: 70 },
  rapier: { sellValue: 85 },
  rondel_dagger: { sellValue: 45 },
  spell_book: { sellValue: 95 },

  // 24 Normal Armors (Doc 23 §6.2, sellValue = floor(ObjectValue * 1.0))
  placeholder_helmet: { sellValue: 85 },
  placeholder_armor: { sellValue: 145 },
  placeholder_gloves: { sellValue: 55 },
  placeholder_boots: { sellValue: 70 },
  army_ranger_armor: { sellValue: 110 },
  army_ranger_gloves: { sellValue: 45 },
  army_ranger_hat: { sellValue: 65 },
  army_ranger_trousers: { sellValue: 60 },
  fire_mage_garb: { sellValue: 100 },
  fire_mage_gloves: { sellValue: 40 },
  fire_mage_hood: { sellValue: 60 },
  fire_mage_trousers: { sellValue: 55 },
  forest_ranger_gloves: { sellValue: 55 },
  forest_ranger_hood: { sellValue: 80 },
  forest_ranger_leather_armor: { sellValue: 135 },
  forest_ranger_trousers: { sellValue: 75 },
  heavy_armor_breastplate: { sellValue: 160 },
  heavy_armor_gauntlets: { sellValue: 60 },
  heavy_armor_helmet: { sellValue: 90 },
  heavy_armor_leg_plate: { sellValue: 80 },
  light_armor_chain_mail_armor: { sellValue: 130 },
  light_armor_chain_mail_trousers: { sellValue: 65 },
  light_armor_gloves: { sellValue: 50 },
  light_armor_open_sallet: { sellValue: 75 },
  coins: { sellValue: 1 },
  gold_coin: { sellValue: 1 },
  // GD-14 master list resources
  slime_gel: { sellValue: 8 },
  slime_core: { sellValue: 24 },
  withered_bone: { sellValue: 10 },
  corrupt_marrow: { sellValue: 28 },
  ancient_armor_fragment: { sellValue: 30 },
  goblin_scrap: { sellValue: 10 },
  goblin_insignia: { sellValue: 32 },
  goblin_arrowhead: { sellValue: 14 },
  bat_wing: { sellValue: 12 },
  bat_fang: { sellValue: 22 },
  spectral_essence: { sellValue: 38 },
  soul_fragment: { sellValue: 65 },
  golem_core: { sellValue: 70 },
  black_iron_ore: { sellValue: 15 },
  tanned_leather: { sellValue: 10 },
  ancient_root_wood: { sellValue: 12 },
  ether_dust: { sellValue: 18 },
  runic_fragment: { sellValue: 20 },
  tuning_gem: { sellValue: 70 },
  lost_grimoire_page: { sellValue: 35 },
  warden_seal: { sellValue: 45 }
});

/**
 * Resolves a buy offer for the given lootId.
 * Checks direct key match first, then normalized alias.
 * @param {string} lootId
 * @returns {{ buyPrice: number, isFree: boolean } | null}
 */
function getBuyOffer(lootId) {
  if (!lootId || typeof lootId !== 'string') return null;
  if (BUY_CATALOG[lootId]) return BUY_CATALOG[lootId];
  const normalized = normalizeLootId(lootId);
  return BUY_CATALOG[normalized] || null;
}

/**
 * Resolves a sell offer for the given lootId.
 * Checks direct key match first, then normalized alias.
 * @param {string} lootId
 * @returns {{ sellValue: number } | null}
 */
function getSellOffer(lootId) {
  if (!lootId || typeof lootId !== 'string') return null;
  if (SELL_CATALOG[lootId]) return SELL_CATALOG[lootId];
  const normalized = normalizeLootId(lootId);
  return SELL_CATALOG[normalized] || null;
}

/**
 * Checks whether an item is eligible for purchase from the shop.
 * @param {string} lootId
 * @returns {boolean}
 */
function isItemBuyable(lootId) {
  return getBuyOffer(lootId) !== null;
}

/**
 * Checks whether an item can be sold to the shop.
 * @param {string} lootId
 * @returns {boolean}
 */
function isItemSellable(lootId) {
  const offer = getSellOffer(lootId);
  return offer !== null && offer.sellValue > 0;
}

module.exports = {
  EQUIPMENT_TEMPLATES,
  GEM_CATALOG,
  getEquipmentTemplate,
  BUYBACK_PRICE_MULTIPLIER,
  BUYBACK_MAX_ENTRIES,
  BUYBACK_ELIGIBLE_IDS,
  SPECIAL_CONSUMABLE_BUYBACK_IDS,
  isBuybackEligible,
  MAX_TRANSACTION_AMOUNT,
  MAX_TRADE_SALE_LINES,
  MAX_TRADE_PURCHASE_LINES,
  PERMANENT_BUY_CATALOG,
  BUY_CATALOG,
  SELL_CATALOG,
  getBuyOffer,
  getSellOffer,
  isItemBuyable,
  isItemSellable
};
