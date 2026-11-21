'use strict';

// TASK-303: the Unity upgrade preview mirrors the authoritative backend rules (Doc 23 §9).
// Parses the C# tables of NpcUpgradeRules.cs and checks them against ItemUpgrades.js behavior.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { planUpgrade, objectValue, upgradedDefense } = require('../src/services/ItemUpgrades');

const rulesPath = path.resolve(__dirname,
  '../../Project Grimhold/Assets/Scripts/Commerce/Upgrades/NpcUpgradeRules.cs');
const defensePath = path.resolve(__dirname,
  '../../Project Grimhold/Assets/Scripts/Player/Equipment/ArmorUpgradeDefense.cs');
const backendRulesPath = path.resolve(__dirname, '../src/services/ItemUpgrades.js');
const source = fs.readFileSync(rulesPath, 'utf8');
const defenseSource = fs.readFileSync(defensePath, 'utf8');

function block(name, text = source) {
  const start = text.indexOf(name);
  assert.ok(start >= 0, `Unity rules must declare ${name}`);
  const open = text.indexOf('{', start);
  const close = text.indexOf('};', open);
  return text.slice(open, close);
}

const materialsOf = text => Object.fromEntries(
  [...text.matchAll(/NpcUpgradeMaterialCost\("(\w+)", (\d+)\)/g)].map(([, id, amount]) => [id, Number(amount)]));
const intArray = (name, text = source) => block(name, text).match(/\d+/g).map(Number);
const constant = name => Number(source.match(new RegExp(`${name} = (\\d+);`))[1]);

const weaponInstance = grade => ({ modifiers: grade === 'None'
  ? { gemSocketCount: 0, gems: [] }
  : { primary: { attribute: 'dexterity', grade }, gemSocketCount: 0, gems: [] } });

test('primary scaling steps match the backend plan for every NPC transition', () => {
  const rows = [...block('PrimaryScalingSteps =').matchAll(
    /new\(WeaponScalingGrade\.(\w+), WeaponScalingGrade\.(\w+), (\d+), (.+?)\),?\s*$/gm)];
  assert.equal(rows.length, 3);
  for (const [, from, to, gold, materials] of rows) {
    const plan = planUpgrade({ merchant: 'blacksmith', operation: 'primary_scaling', lootId: 'rapier',
      instance: weaponInstance(from) });
    assert.equal(plan.modifiers.primary.grade, to, `${from} -> ${to}`);
    assert.equal(plan.gold, Number(gold), `${from} gold`);
    assert.deepEqual(plan.materials, materialsOf(materials), `${from} materials`);
  }
  assert.throws(() => planUpgrade({ merchant: 'blacksmith', operation: 'primary_scaling', lootId: 'rapier',
    instance: weaponInstance('C') }), { errorCode: 'UPGRADE_LIMIT_REACHED' });
});

test('first socket cost matches the backend plan', () => {
  const plan = planUpgrade({ merchant: 'arcanist', operation: 'first_socket', lootId: 'magic_wand',
    instance: weaponInstance('None') });
  assert.equal(plan.gold, constant('FirstSocketGold'));
  assert.deepEqual(plan.materials, materialsOf(block('FirstSocketMaterials =')));
});

test('armor steps, family materials, defense and value bonuses match the backend', () => {
  const steps = [...block('ArmorSteps =').matchAll(/new\((\d+), (\d+), (\d+)\)/g)].map(m => m.slice(1).map(Number));
  assert.equal(steps.length, 3);
  steps.forEach(([gold, basic, ancient], index) => {
    const plan = planUpgrade({ merchant: 'blacksmith', operation: 'armor_level', lootId: 'heavy_armor_breastplate',
      instance: index === 0 ? {} : { modifiers: { upgradeLevel: index } } });
    assert.equal(plan.modifiers.upgradeLevel, index + 1);
    assert.equal(plan.gold, gold);
    assert.deepEqual(plan.materials, { RES_BLACK_IRON: basic, ...(ancient ? { RES_ANCIENT_ARMOR: ancient } : {}) });
  });

  const unityFamilies = [...block('ArmorFamilyMaterials =').matchAll(/\("(\w+_)", "(\w+)"\)/g)].map(m => m.slice(1));
  const backendFamilies = [...fs.readFileSync(backendRulesPath, 'utf8').matchAll(/\['(\w+_)', '(\w+)'\]/g)].map(m => m.slice(1));
  assert.deepEqual(unityFamilies, backendFamilies);

  intArray('BonusPercent =', defenseSource).forEach((percent, level) =>
    assert.equal(upgradedDefense(100, level), 100 + percent, `defense +${level}`));
  const plainArmor = objectValue('heavy_armor_breastplate', {});
  intArray('ArmorValueBonus =').forEach((bonus, level) =>
    assert.equal(objectValue('heavy_armor_breastplate', { modifiers: { upgradeLevel: level } }) - plainArmor, bonus));
});

test('weapon value bonuses and upgrade-only weapons match the backend', () => {
  const plainWeapon = objectValue('rapier', weaponInstance('None'));
  const bonuses = [...source.matchAll(/WeaponScalingGrade\.(E|D|C) => (\d+)/g)];
  assert.equal(bonuses.length, 3);
  for (const [, grade, bonus] of bonuses) {
    assert.equal(objectValue('rapier', weaponInstance(grade)) - plainWeapon, Number(bonus), `grade ${grade}`);
  }
  assert.equal(objectValue('rapier', { modifiers: { gemSocketCount: 1, gems: [null] } }) - plainWeapon,
    constant('FirstSocketValueBonus'));

  const upgradeOnly = block('UpgradeOnlyPhysicalWeapons =').match(/"(\w+)"/g).map(id => id.slice(1, -1));
  assert.deepEqual(upgradeOnly, ['zweihander', 'long_bow']);
  for (const lootId of upgradeOnly) {
    assert.ok(planUpgrade({ merchant: 'blacksmith', operation: 'primary_scaling', lootId, instance: weaponInstance('None') }));
  }
});
