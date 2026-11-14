// src/config/itemCatalog.js
//
// Single backend registry of every item identifier the game can grant (FIX-18).
// It is the union of the backend catalogs; tests/item.catalog.parity.test.js
// keeps it a superset of Unity's LootDefinitions.

'use strict';

const { EQUIPMENT_TEMPLATES, GEM_CATALOG, BUY_CATALOG, SELL_CATALOG } = require('./shopCatalog');
const { normalizeLootId } = require('../services/InventoryLootIdNormalizer');

const KNOWN_LOOT_IDS = Object.freeze(new Set([
  ...Object.keys(EQUIPMENT_TEMPLATES),
  ...Object.keys(GEM_CATALOG),
  ...Object.keys(BUY_CATALOG),
  ...Object.keys(SELL_CATALOG)
]));

/** True when the identifier, after legacy alias normalization, names a game item. */
function isKnownLootId(lootId) {
  return typeof lootId === 'string' && KNOWN_LOOT_IDS.has(normalizeLootId(lootId.trim()));
}

module.exports = { KNOWN_LOOT_IDS, isKnownLootId };
