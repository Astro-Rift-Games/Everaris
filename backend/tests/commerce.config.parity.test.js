'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('path');

const configJsonPath = path.resolve(__dirname, '../src/config/commerceConfig.json');
const assetPath = path.resolve(__dirname, '../../Project Grimhold/Assets/Scriptable Objects/Commerce/TownCommerceConfiguration.asset');
const { EQUIPMENT_TEMPLATES, SELL_CATALOG } = require('../src/config/shopCatalog');

test('TownCommerceConfiguration: parity between Unity ScriptableObject and backend JSON', t => {
  if (!fs.existsSync(configJsonPath)) {
    assert.fail('commerceConfig.json does not exist');
  }

  const raw = fs.readFileSync(configJsonPath, 'utf8').replace(/^\uFEFF/, '');
  const jsonContent = JSON.parse(raw);

  // 1. Verify structure of JSON
  assert.ok(jsonContent.multipliers, 'Missing multipliers in JSON');
  assert.equal(jsonContent.multipliers.buyPriceMultiplier, 2.0);
  assert.equal(jsonContent.multipliers.regularSellMultiplier, 1.0);
  assert.equal(jsonContent.multipliers.demandSellMultiplier, 1.2);
  assert.equal(jsonContent.multipliers.buybackPriceMultiplier, 1.5);
  assert.equal(jsonContent.multipliers.maxBuybackEntries, 16);

  assert.ok(jsonContent.rotationCounts, 'Missing rotationCounts in JSON');
  assert.equal(jsonContent.rotationCounts.normalWeapons, 3);
  assert.equal(jsonContent.rotationCounts.specialWeapons, 1);
  assert.equal(jsonContent.rotationCounts.armors, 3);
  assert.equal(jsonContent.rotationCounts.utilities, 2);
  assert.equal(jsonContent.rotationCounts.demandItems, 3);

  // 2. Verify pools
  assert.ok(jsonContent.pools, 'Missing pools in JSON');
  assert.equal(jsonContent.pools.physicalWeapons.length, 7, 'Expected 7 physical weapons');
  assert.equal(jsonContent.pools.magicalWeapons.length, 5, 'Expected 5 magical weapons');
  assert.equal(jsonContent.pools.armors.length, 24, 'Expected 24 armors');
  assert.equal(jsonContent.pools.utilities.length, 3, 'Expected 3 utilities');
  assert.equal(jsonContent.pools.demandCandidates.length, 29, 'Expected 29 demand candidates');

  // Verify all weapons exist in templates
  for (const w of [...jsonContent.pools.physicalWeapons, ...jsonContent.pools.magicalWeapons]) {
    assert.ok(EQUIPMENT_TEMPLATES[w], `Weapon ${w} must exist in EQUIPMENT_TEMPLATES`);
  }

  // Verify all armors exist in templates
  for (const a of jsonContent.pools.armors) {
    assert.ok(EQUIPMENT_TEMPLATES[a], `Armor ${a} must exist in EQUIPMENT_TEMPLATES`);
  }

  // Verify all utilities exist in SELL_CATALOG
  for (const u of jsonContent.pools.utilities) {
    assert.ok(SELL_CATALOG[u.lootId], `Utility ${u.lootId} must exist in SELL_CATALOG`);
    assert.ok(u.stock > 0, `Utility ${u.lootId} must have stock > 0`);
  }

  // Verify all demand candidates exist in SELL_CATALOG
  for (const d of jsonContent.pools.demandCandidates) {
    assert.ok(SELL_CATALOG[d], `Demand candidate ${d} must exist in SELL_CATALOG`);
  }

  // 3. Verify Unity ScriptableObject asset exists
  if (fs.existsSync(assetPath)) {
    const assetContent = fs.readFileSync(assetPath, 'utf8');
    assert.ok(assetContent.includes('TownCommerceConfiguration'), 'Asset must be TownCommerceConfiguration');
    assert.ok(assetContent.includes('_buyPriceMultiplier: 2'), 'Asset must have _buyPriceMultiplier');
    assert.ok(assetContent.includes('_physicalWeaponsPool:'), 'Asset must have _physicalWeaponsPool');
    assert.ok(assetContent.includes('_magicalWeaponsPool:'), 'Asset must have _magicalWeaponsPool');
    assert.ok(assetContent.includes('_armorsPool:'), 'Asset must have _armorsPool');
    assert.ok(assetContent.includes('_utilitiesPool:'), 'Asset must have _utilitiesPool');
    assert.ok(assetContent.includes('_demandCandidatesPool:'), 'Asset must have _demandCandidatesPool');
  }
});
