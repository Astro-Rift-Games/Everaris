'use strict';
// TASK-302 (Doc 23 §9): pure NPC upgrade rules and the armor upgradeLevel instance contract.
const test = require('node:test');
const assert = require('node:assert/strict');
const { planUpgrade, objectValue, upgradedDefense, UPGRADE_OPERATIONS } = require('../src/services/ItemUpgrades');
const { validInstance, validInventory, validateResultItems, createInstance } = require('../src/services/ItemInstances');

const id = n => n.toString(16).padStart(24, '0');
const weapon = (modifiers = { gemSocketCount: 0, gems: [] }) => ({ instanceId: id(1), modifiers });
const scaled = (grade, extra = {}) => weapon({ primary: { attribute: 'strength', grade }, gemSocketCount: 0, gems: [], ...extra });
const armorPiece = level => ({ instanceId: id(2), ...(level === undefined ? {} : { modifiers: { upgradeLevel: level } }) });
const rejects = (fn, errorCode) => assert.throws(fn, error => {
  assert.equal(error.errorCode, errorCode); return true;
});

test('S1: primary scaling none → E → D → C with Doc 23 §9.1 costs', () => {
  const steps = [
    [weapon(), 'E', 60, { RES_RUNE_FRAGMENT: 2 }],
    [scaled('E'), 'D', 100, { RES_RUNE_FRAGMENT: 3, RES_ETHER_DUST: 1 }],
    [scaled('D'), 'C', 160, { RES_RUNE_FRAGMENT: 4, RES_ETHER_DUST: 2 }]
  ];
  for (const [instance, grade, gold, materials] of steps) {
    const plan = planUpgrade({ merchant: 'blacksmith', operation: 'primary_scaling', lootId: 'arming_sword', instance });
    assert.equal(plan.gold, gold);
    assert.deepEqual(plan.materials, materials);
    assert.deepEqual(plan.modifiers.primary, { attribute: 'strength', grade });
    assert.equal(plan.modifiers.gemSocketCount, instance.modifiers.gemSocketCount);
  }
});

test('S1/S2: a weapon instance without modifiers is the plain state', () => {
  const bare = { instanceId: id(1) };
  assert.deepEqual(planUpgrade({ merchant: 'blacksmith', operation: 'primary_scaling', lootId: 'arming_sword', instance: bare }).modifiers,
    { primary: { attribute: 'strength', grade: 'E' }, gemSocketCount: 0, gems: [] });
  assert.deepEqual(planUpgrade({ merchant: 'blacksmith', operation: 'first_socket', lootId: 'arming_sword', instance: bare }).modifiers,
    { gemSocketCount: 1, gems: [null] });
});

test('S1: secondary scaling and sockets stay intact; the input instance is not mutated', () => {
  const modifiers = { primary: { attribute: 'strength', grade: 'E' }, secondary: { attribute: 'luck', grade: 'A' },
    gemSocketCount: 1, gems: ['RES_RUBY_GEM'] };
  const instance = weapon(modifiers);
  const before = structuredClone(instance);
  const plan = planUpgrade({ merchant: 'blacksmith', operation: 'primary_scaling', lootId: 'arming_sword', instance });
  assert.deepEqual(plan.modifiers, { ...modifiers, primary: { attribute: 'strength', grade: 'D' } });
  assert.deepEqual(instance, before);
});

test('S1: C is the NPC limit and B/A/S are never degraded or replaced', () => {
  rejects(() => planUpgrade({ merchant: 'blacksmith', operation: 'primary_scaling', lootId: 'arming_sword', instance: scaled('C') }),
    'UPGRADE_LIMIT_REACHED');
  for (const grade of ['B', 'A', 'S']) {
    rejects(() => planUpgrade({ merchant: 'blacksmith', operation: 'primary_scaling', lootId: 'arming_sword', instance: scaled(grade) }),
      'UPGRADE_GRADE_LOCKED');
  }
});

test('S2: first empty socket only when the weapon has none', () => {
  const plan = planUpgrade({ merchant: 'arcanist', operation: 'first_socket', lootId: 'magic_sword', instance: scaled('C') });
  assert.equal(plan.gold, 180);
  assert.deepEqual(plan.materials, { RES_ATTUNEMENT_GEM: 1, RES_RUNE_FRAGMENT: 2 });
  assert.deepEqual(plan.modifiers, { primary: { attribute: 'strength', grade: 'C' }, gemSocketCount: 1, gems: [null] });
  for (const modifiers of [{ gemSocketCount: 1, gems: [null] }, { gemSocketCount: 2, gems: ['RES_RUBY_GEM', null] }]) {
    rejects(() => planUpgrade({ merchant: 'arcanist', operation: 'first_socket', lootId: 'magic_sword', instance: weapon(modifiers) }),
      'UPGRADE_SOCKET_EXISTS');
  }
});

test('S3: armor +0 → +3 transition costs with family basic material', () => {
  const families = [['heavy_armor_breastplate', 'RES_BLACK_IRON'], ['light_armor_gloves', 'RES_BLACK_IRON'],
    ['army_ranger_hat', 'RES_TANNED_LEATHER'], ['forest_ranger_trousers', 'RES_TANNED_LEATHER'],
    ['placeholder_armor', 'RES_ETHER_DUST'], ['fire_mage_hood', 'RES_ETHER_DUST']];
  const rows = [[undefined, 1, 40, 2, 0], [1, 2, 80, 3, 1], [2, 3, 140, 4, 2]];
  for (const [lootId, basic] of families) {
    for (const [from, to, gold, basicCount, ancient] of rows) {
      const plan = planUpgrade({ merchant: 'blacksmith', operation: 'armor_level', lootId, instance: armorPiece(from) });
      assert.equal(plan.gold, gold);
      assert.deepEqual(plan.materials, { [basic]: basicCount, ...(ancient ? { RES_ANCIENT_ARMOR: ancient } : {}) });
      assert.deepEqual(plan.modifiers, { upgradeLevel: to });
    }
  }
  rejects(() => planUpgrade({ merchant: 'blacksmith', operation: 'armor_level', lootId: 'fire_mage_hood', instance: armorPiece(3) }),
    'UPGRADE_LIMIT_REACHED');
});

test('S3: DefensaFinal = floor(DefensaBase × (1 + BonoMejora)), accumulated not compounded', () => {
  assert.deepEqual([0, 1, 2, 3].map(level => upgradedDefense(20, level)), [20, 21, 22, 23]);
  assert.deepEqual([0, 1, 2, 3].map(level => upgradedDefense(7, level)), [7, 7, 7, 8]);
  assert.equal(upgradedDefense(100, 3), 115);
  assert.throws(() => upgradedDefense(10, 4));
});

test('S4: ObjectValue bonuses replace the previous grade bonus; socket adds +60; armor +15/+35/+60', () => {
  assert.equal(objectValue('magic_sword', weapon()), 125);
  assert.deepEqual(['E', 'D', 'C'].map(grade => objectValue('magic_sword', scaled(grade))), [145, 160, 175]);
  // Doc 23 §9.3 example: MagicSword base 125, C +50 and one socket +60 → 235.
  assert.equal(objectValue('magic_sword', scaled('C', { gemSocketCount: 1, gems: [null] })), 235);
  assert.deepEqual([undefined, 1, 2, 3].map(level => objectValue('heavy_armor_breastplate', armorPiece(level))), [160, 175, 195, 220]);
});

test('S5: Blacksmith upgrades physical weapons and all armor; Arcanist only magic weapons', () => {
  rejects(() => planUpgrade({ merchant: 'arcanist', operation: 'primary_scaling', lootId: 'arming_sword', instance: weapon() }),
    'UPGRADE_WRONG_MERCHANT');
  rejects(() => planUpgrade({ merchant: 'blacksmith', operation: 'primary_scaling', lootId: 'magic_wand', instance: weapon() }),
    'UPGRADE_WRONG_MERCHANT');
  rejects(() => planUpgrade({ merchant: 'arcanist', operation: 'armor_level', lootId: 'fire_mage_hood', instance: armorPiece() }),
    'UPGRADE_WRONG_MERCHANT');
  rejects(() => planUpgrade({ merchant: 'general', operation: 'primary_scaling', lootId: 'arming_sword', instance: weapon() }),
    'UPGRADE_WRONG_MERCHANT');
  rejects(() => planUpgrade({ merchant: 'blacksmith', operation: 'primary_scaling', lootId: 'training_short_sword', instance: weapon() }),
    'UPGRADE_NOT_ELIGIBLE');
  rejects(() => planUpgrade({ merchant: 'blacksmith', operation: 'armor_level', lootId: 'shield', instance: { instanceId: id(3) } }),
    'UPGRADE_NOT_ELIGIBLE');
  rejects(() => planUpgrade({ merchant: 'blacksmith', operation: 'armor_level', lootId: 'arming_sword', instance: weapon() }),
    'UPGRADE_NOT_ELIGIBLE');
  rejects(() => planUpgrade({ merchant: 'blacksmith', operation: 'first_socket', lootId: 'heavy_armor_helmet', instance: armorPiece() }),
    'UPGRADE_NOT_ELIGIBLE');
  assert.deepEqual([...UPGRADE_OPERATIONS].sort(), ['armor_level', 'first_socket', 'primary_scaling']);
});

test('S5: zweihander and long_bow are Blacksmith-upgradable physical weapons outside the rotation pool', () => {
  const { PHYSICAL_WEAPONS } = require('../src/services/CommerceRotationService');
  for (const [lootId, attribute] of [['zweihander', 'strength'], ['long_bow', 'dexterity']]) {
    assert.equal(PHYSICAL_WEAPONS.includes(lootId), false);
    const plan = planUpgrade({ merchant: 'blacksmith', operation: 'primary_scaling', lootId, instance: weapon() });
    assert.deepEqual(plan.modifiers.primary, { attribute, grade: 'E' });
    assert.equal(planUpgrade({ merchant: 'blacksmith', operation: 'first_socket', lootId, instance: weapon() }).gold, 180);
    rejects(() => planUpgrade({ merchant: 'arcanist', operation: 'primary_scaling', lootId, instance: weapon() }),
      'UPGRADE_WRONG_MERCHANT');
  }
});

test('S3 contract: armor accepts only modifiers.upgradeLevel 0..3; other equipment rules unchanged', () => {
  for (const level of [undefined, 0, 1, 2, 3]) assert.equal(validInstance('heavy_armor_helmet', armorPiece(level)), true);
  for (const modifiers of [{ upgradeLevel: 4 }, { upgradeLevel: -1 }, { upgradeLevel: 1.5 }, { upgradeLevel: '1' },
    { upgradeLevel: 1, gemSocketCount: 0 }, { gemSocketCount: 0, gems: [] }, {}]) {
    assert.equal(validInstance('heavy_armor_helmet', { instanceId: id(2), modifiers }), false, JSON.stringify(modifiers));
  }
  assert.equal(validInstance('shield', { instanceId: id(3), modifiers: { upgradeLevel: 1 } }), false);
  assert.equal(validInstance('arming_sword', { instanceId: id(1), modifiers: { upgradeLevel: 1 } }), false);
  assert.equal(validInstance('arming_sword', weapon()), true);
  assert.equal(createInstance('heavy_armor_helmet').modifiers, undefined);
  assert.equal(validInventory({ stash: [], loadout: [{ slot: 0, amount: 1, lootId: 'fire_mage_hood', instance: armorPiece(2) }],
    preparedEquipment: {} }), true);
  assert.deepEqual(validateResultItems([{ lootId: 'fire_mage_hood', amount: 1, modifiers: { upgradeLevel: 3 } }]),
    [{ lootId: 'fire_mage_hood', amount: 1, modifiers: { upgradeLevel: 3 } }]);
});

test('S7: every NPC step raises ObjectValue by strictly less than its gold cost (no artificial gold)', () => {
  const { PHYSICAL_WEAPONS, MAGICAL_WEAPONS, ARMOR_POOL } = require('../src/services/CommerceRotationService');
  const walk = (merchant, lootId, operations) => {
    let instance = { instanceId: id(9) }; let steps = 0;
    for (const operation of operations) {
      for (;;) {
        let plan;
        try { plan = planUpgrade({ merchant, operation, lootId, instance }); } catch (error) {
          assert.match(error.errorCode, /^UPGRADE_(LIMIT_REACHED|SOCKET_EXISTS)$/); break;
        }
        const gain = objectValue(lootId, { ...instance, modifiers: plan.modifiers }) - objectValue(lootId, instance);
        assert.ok(gain > 0 && gain < plan.gold, `${lootId} ${operation}: +${gain} ObjectValue for ${plan.gold} gold`);
        instance = { ...instance, modifiers: plan.modifiers }; steps++;
      }
    }
    return steps;
  };
  assert.ok(PHYSICAL_WEAPONS.length > 0 && MAGICAL_WEAPONS.length > 0 && ARMOR_POOL.length > 0);
  for (const lootId of PHYSICAL_WEAPONS) assert.equal(walk('blacksmith', lootId, ['primary_scaling', 'first_socket']), 4, lootId);
  for (const lootId of MAGICAL_WEAPONS) assert.equal(walk('arcanist', lootId, ['primary_scaling', 'first_socket']), 4, lootId);
  for (const lootId of ARMOR_POOL) assert.equal(walk('blacksmith', lootId, ['armor_level']), 3, lootId);
});
