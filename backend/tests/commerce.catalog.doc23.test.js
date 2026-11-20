'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const {
  BUY_CATALOG,
  SELL_CATALOG,
  EQUIPMENT_TEMPLATES,
  getBuyOffer,
  getSellOffer,
  isItemBuyable,
  isItemSellable
} = require('../src/config/shopCatalog');

// Doc 23 §5.1, §5.5, §6.1, §6.2
const EXPECTED_NORMAL_WEAPONS = {
  arming_sword: 60,
  long_sword: 100,
  great_hammer: 110,
  rondel_dagger: 45,
  compound_bow: 80,
  light_crossbow: 100,
  rapier: 85,
  magic_wand: 70,
  spell_book: 95,
  magic_staff: 110,
  magic_sword: 125,
  magic_cinquedea: 90
};

const EXPECTED_ARMORS = {
  // Caballero Pesado
  heavy_armor_helmet: 90,
  heavy_armor_breastplate: 160,
  heavy_armor_gauntlets: 60,
  heavy_armor_leg_plate: 80,
  // Caballero Templado
  light_armor_open_sallet: 75,
  light_armor_chain_mail_armor: 130,
  light_armor_gloves: 50,
  light_armor_chain_mail_trousers: 65,
  // Ranger Explorador
  army_ranger_hat: 65,
  army_ranger_armor: 110,
  army_ranger_gloves: 45,
  army_ranger_trousers: 60,
  // Ranger Acechador
  forest_ranger_hood: 80,
  forest_ranger_leather_armor: 135,
  forest_ranger_gloves: 55,
  forest_ranger_trousers: 75,
  // Mago Arcano (placeholders)
  placeholder_helmet: 85,
  placeholder_armor: 145,
  placeholder_gloves: 55,
  placeholder_boots: 70,
  // Mago Viajero
  fire_mage_hood: 60,
  fire_mage_garb: 100,
  fire_mage_gloves: 40,
  fire_mage_trousers: 55
};

const PERMANENT_CONSUMABLES = {
  CON_HEALTH_POTION: 20,
  CON_MANA_POTION: 20,
  CON_MANA_FRAGMENT: 8
};

const UTILITIES = {
  CON_LOCKPICK: 25,
  UTL_RUNE_COMPASS: 140,
  UTL_ARCANE_DETECTOR: 180
};

const SPECIAL_CONSUMABLES = [
  'CON_EXPLOSIVE_BOMB',
  'CON_SMOKE_BOMB',
  'CON_THROWING_DAGGER',
  'CON_SELF_REVIVE'
];

const TRAINING_ITEMS = [
  'training_short_sword',
  'training_dagger',
  'training_wand'
];

test('Doc 23 §5.1: Permanent buy offers contain only the 3 basic consumables', () => {
  for (const [id, ov] of Object.entries(PERMANENT_CONSUMABLES)) {
    const offer = getBuyOffer(id);
    assert.ok(offer, `Permanent consumable ${id} must have buy offer`);
    assert.equal(offer.buyPrice, Math.ceil(ov * 2.0), `${id} buy price`);
  }
});

test('Doc 23 §6.1: 12 normal weapons have correct ObjectValue, buyPrice, and sellValue', () => {
  assert.equal(Object.keys(EXPECTED_NORMAL_WEAPONS).length, 12);
  for (const [id, ov] of Object.entries(EXPECTED_NORMAL_WEAPONS)) {
    const buyOffer = getBuyOffer(id);
    assert.ok(buyOffer, `Weapon ${id} must be in BUY_CATALOG`);
    assert.equal(buyOffer.buyPrice, Math.ceil(ov * 2.0), `${id} buyPrice`);

    const template = EQUIPMENT_TEMPLATES[id];
    assert.ok(template, `Weapon ${id} must have equipment template`);
    assert.equal(template.sellValue, Math.floor(ov * 1.0), `${id} template sellValue`);

    const sellOffer = getSellOffer(id);
    assert.ok(sellOffer, `Weapon ${id} must have sell offer`);
    assert.equal(sellOffer.sellValue, Math.floor(ov * 1.0), `${id} sellOffer sellValue`);
  }
});

test('Doc 23 §6.2: 24 armor pieces have correct ObjectValue, buyPrice, and sellValue', () => {
  assert.equal(Object.keys(EXPECTED_ARMORS).length, 24);
  for (const [id, ov] of Object.entries(EXPECTED_ARMORS)) {
    const buyOffer = getBuyOffer(id);
    assert.ok(buyOffer, `Armor ${id} must be in BUY_CATALOG`);
    assert.equal(buyOffer.buyPrice, Math.ceil(ov * 2.0), `${id} buyPrice`);

    const template = EQUIPMENT_TEMPLATES[id];
    assert.ok(template, `Armor ${id} must have equipment template`);
    assert.equal(template.sellValue, Math.floor(ov * 1.0), `${id} template sellValue`);

    const sellOffer = getSellOffer(id);
    assert.ok(sellOffer, `Armor ${id} must have sell offer`);
    assert.equal(sellOffer.sellValue, Math.floor(ov * 1.0), `${id} sellOffer sellValue`);
  }
});

test('Doc 23 §5.5: 3 utilities have correct buyPrice and sellValue', () => {
  for (const [id, ov] of Object.entries(UTILITIES)) {
    const buyOffer = getBuyOffer(id);
    assert.ok(buyOffer, `Utility ${id} must be in BUY_CATALOG`);
    assert.equal(buyOffer.buyPrice, Math.ceil(ov * 2.0), `${id} buyPrice`);

    const sellOffer = getSellOffer(id);
    assert.ok(sellOffer, `Utility ${id} must have sell offer`);
    assert.equal(sellOffer.sellValue, Math.floor(ov * 1.0), `${id} sellValue`);
  }
});

test('Doc 23 §4 & §10: Special consumables, treasures, and resources are NOT in BUY_CATALOG', () => {
  for (const id of SPECIAL_CONSUMABLES) {
    assert.equal(isItemBuyable(id), false, `Special consumable ${id} must not be buyable from NPC regular shop`);
    assert.equal(isItemSellable(id), true, `Special consumable ${id} must still be sellable to NPC`);
  }

  for (const id of ['TRE_CHALICE', 'TRE_CROWN', 'TRE_CHAOS_COIN', 'TRE_VOID_BELL']) {
    assert.equal(isItemBuyable(id), false, `Treasure ${id} must not be buyable from NPC regular shop`);
    assert.equal(isItemSellable(id), true, `Treasure ${id} must still be sellable to NPC`);
  }

  for (const id of ['RES_SLIME_GEL', 'RES_BLACK_IRON', 'RES_GOLEM_CORE', 'bone']) {
    assert.equal(isItemBuyable(id), false, `Resource ${id} must not be buyable from NPC regular shop`);
    assert.equal(isItemSellable(id), true, `Resource ${id} must still be sellable to NPC`);
  }
});

test('Doc 23 §10: Training items are excluded from buy and sell', () => {
  for (const id of TRAINING_ITEMS) {
    assert.equal(isItemBuyable(id), false, `Training item ${id} must not be buyable`);
    assert.equal(isItemSellable(id), false, `Training item ${id} must not be sellable`);
  }
});
