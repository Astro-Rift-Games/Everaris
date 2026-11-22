'use strict';

// TASK-304: respec re-evaluates equipment requirements server-side (Doc 04 §11), so the backend
// mirrors Unity's weapon minimums (WeaponDefinition._attributeRequirements) and ability
// requirements (AbilityDefinition._attributeRequirements). This test keeps both in sync.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {
  WEAPON_ATTRIBUTE_MINIMUMS,
  ABILITY_ATTRIBUTE_REQUIREMENTS,
  meetsWeaponRequirements,
  meetsAbilityRequirements
} = require('../src/config/attributeRequirements');

const assets = path.resolve(__dirname, '../../Project Grimhold/Assets/Scriptable Objects');
const lootDefinitions = path.join(assets, 'Loot/Definitions');
const abilityDefinitions = path.join(assets, 'Abilities/Definitions');
// Mirrors CharacterAttribute.cs (Vitality = 0 ... Luck = 5).
const ATTRIBUTE_BY_INDEX = ['vitality', 'resistance', 'strength', 'dexterity', 'intelligence', 'luck'];

function assetFiles(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const file = path.join(directory, entry.name);
    return entry.isDirectory() ? assetFiles(file) : entry.name.endsWith('.asset') ? [file] : [];
  });
}

function guidOf(assetPath) {
  return fs.readFileSync(`${assetPath}.meta`, 'utf8').match(/^guid:\s*([a-f0-9]+)/m)?.[1];
}

test('weapon minimums mirror every Unity loot weapon definition', t => {
  if (!fs.existsSync(lootDefinitions)) {
    t.skip('Unity LootDefinition folder is absent (backend deployed alone); requirement parity cannot be checked.');
    return;
  }
  const files = assetFiles(lootDefinitions);
  const weaponByGuid = new Map();
  for (const file of files) {
    const text = fs.readFileSync(file, 'utf8');
    if (!/_minimumStrength:/.test(text)) continue;
    const minimums = {};
    for (const [field, attribute] of [['_minimumStrength', 'strength'], ['_minimumDexterity', 'dexterity'], ['_minimumIntelligence', 'intelligence']]) {
      const value = Number(text.match(new RegExp(`^\\s*${field}:\\s*(\\d+)\\s*$`, 'm'))?.[1] || 0);
      if (value > 0) minimums[attribute] = value;
    }
    weaponByGuid.set(guidOf(file), minimums);
  }

  const expected = {};
  for (const file of files) {
    const text = fs.readFileSync(file, 'utf8');
    const id = text.match(/^\s*_id:\s*(\S+)\s*$/m)?.[1];
    const guid = text.match(/^\s*_weaponDefinition:\s*\{[^}]*guid:\s*([a-f0-9]+)/m)?.[1];
    if (!id || !guid || !weaponByGuid.has(guid)) continue;
    const minimums = weaponByGuid.get(guid);
    if (Object.keys(minimums).length > 0) expected[id] = minimums;
  }

  assert.ok(Object.keys(expected).length > 0, 'Expected at least one Unity weapon with attribute minimums');
  assert.deepEqual(WEAPON_ATTRIBUTE_MINIMUMS, expected);
});

test('ability requirements mirror every Unity ability definition', t => {
  if (!fs.existsSync(abilityDefinitions)) {
    t.skip('Unity ability definitions are absent; requirement parity cannot be checked.');
    return;
  }
  const expected = {};
  for (const file of assetFiles(abilityDefinitions)) {
    const text = fs.readFileSync(file, 'utf8');
    const id = text.match(/^\s*_id:\s*(\S+)/m)?.[1]?.trim().toLowerCase();
    if (!id) continue;
    const requirements = {};
    for (const match of text.matchAll(/-\s*_attribute:\s*(\d+)\s*\n\s*_minimumValue:\s*(\d+)/g)) {
      if (Number(match[2]) > 0) requirements[ATTRIBUTE_BY_INDEX[Number(match[1])]] = Number(match[2]);
    }
    if (Object.keys(requirements).length > 0) expected[id] = requirements;
  }

  assert.ok(Object.keys(expected).length > 0, 'Expected at least one Unity ability with attribute requirements');
  assert.deepEqual(ABILITY_ATTRIBUTE_REQUIREMENTS, expected);
});

test('requirement checks compare each minimum against the attributes', () => {
  const zero = { vitality: 0, resistance: 0, strength: 0, dexterity: 0, intelligence: 0, luck: 0 };
  assert.equal(meetsWeaponRequirements('long_sword', { ...zero, strength: 10 }), true);
  assert.equal(meetsWeaponRequirements('long_sword', { ...zero, strength: 9 }), false);
  assert.equal(meetsWeaponRequirements('greatsword', { ...zero, strength: 9 }), false, 'legacy ids are normalized');
  assert.equal(meetsWeaponRequirements('training_short_sword', zero), true, 'training weapons have no minimums');
  assert.equal(meetsWeaponRequirements('heavy_armor_helmet', zero), true, 'armors have no requirements');
  assert.equal(meetsAbilityRequirements('life_drain', { ...zero, vitality: 7, intelligence: 7 }), true);
  assert.equal(meetsAbilityRequirements('life_drain', { ...zero, vitality: 7, intelligence: 6 }), false);
  assert.equal(meetsAbilityRequirements('unknown_ability', zero), true);
});
