'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const {
  getWeeklyRotation,
  getWeeklyWindow,
  getMerchantOffers,
  WEAPON_POOL,
  ARMOR_POOL,
  UTILITY_POOL,
  DEMAND_CANDIDATE_POOL,
  SPECIAL_MODIFIERS
} = require('../src/services/CommerceRotationService');

test('getWeeklyWindow calculates Monday 00:00 UTC correctly across various days', () => {
  // Wednesday Nov 18, 2026
  const wed = new Date(Date.UTC(2026, 10, 18, 14, 30, 0));
  const w1 = getWeeklyWindow(wed);
  assert.equal(w1.weekId, '2026-11-16');
  assert.equal(w1.monday.toISOString(), '2026-11-16T00:00:00.000Z');
  assert.equal(w1.nextMonday.toISOString(), '2026-11-23T00:00:00.000Z');

  // Sunday Nov 22, 2026 23:59:59
  const sun = new Date(Date.UTC(2026, 10, 22, 23, 59, 59));
  const w2 = getWeeklyWindow(sun);
  assert.equal(w2.weekId, '2026-11-16');

  // Monday Nov 23, 2026 00:00:00 -> new week
  const mon = new Date(Date.UTC(2026, 10, 23, 0, 0, 0));
  const w3 = getWeeklyWindow(mon);
  assert.equal(w3.weekId, '2026-11-23');
});

test('getWeeklyRotation produces deterministic catalog for a given week', () => {
  const rot1 = getWeeklyRotation(new Date('2026-11-18T10:00:00Z'));
  const rot2 = getWeeklyRotation(new Date('2026-11-20T18:00:00Z'));
  assert.deepEqual(rot1, rot2, 'Same weekly cycle must produce identical rotation');

  // Next week produces different rotation
  const nextRot = getWeeklyRotation(new Date('2026-11-24T10:00:00Z'));
  assert.notEqual(rot1.weekId, nextRot.weekId);
});

test('getWeeklyRotation conforms to Doc 23 §5 structure', () => {
  const rot = getWeeklyRotation(new Date('2026-11-18T10:00:00Z'));

  // 3 distinct normal weapons from pool of 12
  assert.equal(rot.normalWeapons.length, 3);
  const normalIds = new Set(rot.normalWeapons.map(w => w.lootId));
  assert.equal(normalIds.size, 3);
  for (const w of rot.normalWeapons) {
    assert.ok(WEAPON_POOL.includes(w.lootId), `${w.lootId} in WEAPON_POOL`);
    assert.equal(w.stock, 1);
    assert.equal(w.buyPrice, Math.ceil(w.objectValue * 2.0));
  }

  // 1 special weapon from remaining 9 weapons
  assert.ok(rot.specialWeapon);
  assert.ok(WEAPON_POOL.includes(rot.specialWeapon.lootId));
  assert.equal(normalIds.has(rot.specialWeapon.lootId), false, 'Special weapon must not duplicate normal weapons');
  assert.equal(rot.specialWeapon.stock, 1);
  assert.ok(['scaling_E', 'scaling_D', 'scaling_C', 'empty_socket'].includes(rot.specialWeapon.modifier.type));
  assert.equal(rot.specialWeapon.objectValue, rot.specialWeapon.baseObjectValue + rot.specialWeapon.modifier.increment);
  assert.equal(rot.specialWeapon.buyPrice, Math.ceil(rot.specialWeapon.objectValue * 2.0));

  // 3 distinct armors from pool of 24
  assert.equal(rot.armors.length, 3);
  const armorIds = new Set(rot.armors.map(a => a.lootId));
  assert.equal(armorIds.size, 3);
  for (const a of rot.armors) {
    assert.ok(ARMOR_POOL.includes(a.lootId), `${a.lootId} in ARMOR_POOL`);
    assert.equal(a.stock, 1);
    assert.equal(a.buyPrice, Math.ceil(a.objectValue * 2.0));
  }

  // 2 distinct utilities from pool of 3
  assert.equal(rot.utilities.length, 2);
  const utilityIds = new Set(rot.utilities.map(u => u.lootId));
  assert.equal(utilityIds.size, 2);
  for (const u of rot.utilities) {
    assert.ok(UTILITY_POOL.includes(u.lootId), `${u.lootId} in UTILITY_POOL`);
    if (u.lootId === 'CON_LOCKPICK') assert.equal(u.stock, 3);
    else assert.equal(u.stock, 1);
    assert.equal(u.buyPrice, Math.ceil(u.objectValue * 2.0));
  }

  // 3 distinct demand items with +20% floor price
  assert.equal(rot.demandItems.length, 3);
  const demandIds = new Set(rot.demandItems.map(d => d.lootId));
  assert.equal(demandIds.size, 3);
  for (const d of rot.demandItems) {
    assert.ok(DEMAND_CANDIDATE_POOL.includes(d.lootId), `${d.lootId} in DEMAND_CANDIDATE_POOL`);
    assert.equal(d.demandSellPrice, Math.floor(d.objectValue * 1.2), `${d.lootId} demand price`);
  }

  // 3 permanent items
  assert.equal(rot.permanentOffers.length, 3);
});

test('getMerchantOffers filters offers according to Doc 23 §3', () => {
  const date = new Date('2026-11-18T10:00:00Z');
  const general = getMerchantOffers('general', date);
  assert.equal(general.merchant, 'general');
  assert.equal(general.permanent.length, 3);
  assert.equal(general.rotating.length, 2);
  assert.equal(general.demand.length, 3);

  const blacksmith = getMerchantOffers('blacksmith', date);
  assert.equal(blacksmith.merchant, 'blacksmith');
  assert.equal(blacksmith.armors.length, 3);
  for (const w of blacksmith.weapons) {
    assert.ok(['arming_sword', 'long_sword', 'great_hammer', 'rondel_dagger', 'compound_bow', 'light_crossbow', 'rapier'].includes(w.lootId));
  }

  const arcanist = getMerchantOffers('arcanist', date);
  assert.equal(arcanist.merchant, 'arcanist');
  assert.equal(arcanist.armors.length, 0);
  for (const w of arcanist.weapons) {
    assert.ok(['magic_wand', 'spell_book', 'magic_staff', 'magic_sword', 'magic_cinquedea'].includes(w.lootId));
  }
});

