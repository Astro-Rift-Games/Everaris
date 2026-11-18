// tests/item.instances.training-scaling.test.js
//
// TASK-295 / S3, Doc 09 §15: "No poseen escalado, su daño es fijo y su Valor de Objeto es 0."
// Training weapons must never carry primary/secondary scaling, even when the raid host submits it.

'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const Character = require('../src/models/Character');
const AuthoritativeExtractionResult = require('../src/models/AuthoritativeExtractionResult');
const ExtractionCommitService = require('../src/services/ExtractionCommitService');
const {
  createInstance, validateResultItems, validateResultEquipment, validModifiers, validInventory
} = require('../src/services/ItemInstances');
const { TRAINING_WEAPONS } = require('../src/services/TrainingWeapons');

const NATURAL = Object.freeze({ training_short_sword: 'strength', training_dagger: 'dexterity', training_wand: 'intelligence' });
const OTHER = Object.freeze({ strength: 'dexterity', dexterity: 'intelligence', intelligence: 'strength' });
const EMPTY_EQUIPMENT = Object.freeze({
  weaponSlot1: null, weaponSlot2: null, offHand1: null, offHand2: null,
  helmet: null, armor: null, gloves: null, boots: null
});
const gems = { gemSocketCount: 0, gems: [] };
const withPrimary = (lootId, grade = 'S') => ({ primary: { attribute: NATURAL[lootId], grade }, ...gems });
const withSecondary = lootId => ({
  primary: { attribute: NATURAL[lootId], grade: 'E' },
  secondary: { attribute: OTHER[NATURAL[lootId]], grade: 'E' },
  ...gems
});
const rejected = err => err.statusCode === 400 && err.errorCode === 'INVALID_INVENTORY';

test('training weapons reject attribute scaling in the shared modifier validator (TASK-295 S3)', async (t) => {
  for (const lootId of TRAINING_WEAPONS) {
    await t.test(`${lootId}: primary scaling rejected`, () => {
      assert.equal(validModifiers(lootId, withPrimary(lootId)), false);
      assert.throws(() => validateResultItems([{ lootId, amount: 1, modifiers: withPrimary(lootId) }]), rejected);
      assert.throws(() => validateResultEquipment({ weaponSlot1: { lootId, modifiers: withPrimary(lootId) } }), rejected);
      assert.throws(() => createInstance(lootId, withPrimary(lootId)), rejected);
    });
    await t.test(`${lootId}: primary + secondary scaling rejected`, () => {
      assert.equal(validModifiers(lootId, withSecondary(lootId)), false);
      assert.throws(() => validateResultItems([{ lootId, amount: 1, modifiers: withSecondary(lootId) }]), rejected);
      assert.throws(() => validateResultEquipment({ weaponSlot1: { lootId, modifiers: withSecondary(lootId) } }), rejected);
    });
    await t.test(`${lootId}: stored instance with scaling makes the inventory invalid`, () => {
      const instanceId = '0123456789abcdef01234567';
      const entry = { slot: 0, lootId, amount: 1, instance: { instanceId, modifiers: withPrimary(lootId) } };
      const inventory = { stash: [entry], loadout: [], preparedEquipment: { ...EMPTY_EQUIPMENT } };
      assert.equal(validInventory(inventory), false);
      entry.instance = { instanceId, modifiers: { ...gems } };
      assert.equal(validInventory(inventory), true);
    });
    await t.test(`${lootId}: fixed-damage payload without scaling is still accepted`, () => {
      assert.deepEqual(validateResultItems([{ lootId, amount: 1, modifiers: { ...gems } }]),
        [{ lootId, amount: 1, modifiers: { ...gems } }]);
      assert.deepEqual(validateResultItems([{ lootId, amount: 1 }]), [{ lootId, amount: 1 }]);
      assert.deepEqual(validateResultEquipment({ weaponSlot1: { lootId, modifiers: { ...gems } } }).weaponSlot1,
        { lootId, modifiers: { ...gems } });
      assert.equal(validModifiers(lootId, undefined), true);
    });
  }

  await t.test('a normal weapon with valid primary and secondary scaling is still accepted', () => {
    const modifiers = {
      primary: { attribute: 'strength', grade: 'S' },
      secondary: { attribute: 'dexterity', grade: 'C' },
      ...gems
    };
    assert.equal(validModifiers('arming_sword', modifiers), true);
    assert.deepEqual(validateResultItems([{ lootId: 'arming_sword', amount: 1, modifiers }]),
      [{ lootId: 'arming_sword', amount: 1, modifiers }]);
    assert.equal(validateResultEquipment({ weaponSlot1: { lootId: 'arming_sword', modifiers } }).weaponSlot1.lootId, 'arming_sword');
    assert.equal(createInstance('arming_sword', modifiers).modifiers.primary.grade, 'S');
  });
});

test('ExtractionCommitService.commit rejects training weapons carrying scaling (TASK-295 S3)', async (t) => {
  const original = {
    findOne: Character.findOne,
    findOneAndUpdate: Character.findOneAndUpdate,
    authFindOne: AuthoritativeExtractionResult.findOne,
    authFindOneAndUpdate: AuthoritativeExtractionResult.findOneAndUpdate
  };
  let character;
  let authResult;
  let characterWrites;
  let claimWrites;

  t.beforeEach(() => {
    characterWrites = 0;
    claimWrites = 0;
    character = {
      accountId: 'acc-scaling', level: 1, experience: 0, revision: 3, schemaVersion: 11,
      lastAppliedProgressionResultSequence: 0, lastProgressionReceipt: null, appliedProgressionReceipts: [],
      characterAttributes: { vitality: 5, resistance: 5, strength: 5, dexterity: 5, intelligence: 5, luck: 5, availablePoints: 0 },
      inventory: {
        inventorySchemaVersion: 3, stash: [], loadout: [], preparedEquipment: { ...EMPTY_EQUIPMENT },
        pendingReservation: null, appliedExtractionReceipts: []
      }
    };
    Character.findOne = async () => character;
    Character.findOneAndUpdate = async (filter, update) => {
      characterWrites++;
      if (filter.revision !== character.revision) return null;
      const next = structuredClone(character);
      for (const [path, value] of Object.entries(update.$set || {})) {
        const parts = path.split('.');
        let cursor = next;
        for (let i = 0; i < parts.length - 1; i++) cursor = cursor[parts[i]];
        cursor[parts[parts.length - 1]] = structuredClone(value);
      }
      next.revision += 1;
      next.inventory.appliedExtractionReceipts.push(...update.$push['inventory.appliedExtractionReceipts'].$each);
      character = next;
      return character;
    };
    AuthoritativeExtractionResult.findOne = async () => authResult;
    AuthoritativeExtractionResult.findOneAndUpdate = async () => { claimWrites++; return { ...authResult, consumed: true }; };
  });

  t.afterEach(() => {
    Character.findOne = original.findOne;
    Character.findOneAndUpdate = original.findOneAndUpdate;
    AuthoritativeExtractionResult.findOne = original.authFindOne;
    AuthoritativeExtractionResult.findOneAndUpdate = original.authFindOneAndUpdate;
  });

  const commit = () => ExtractionCommitService.commit('acc-scaling', { raidId: 'raid-scaling', resultSequence: 1 });
  const result = (items, preparedEquipment = { ...EMPTY_EQUIPMENT }) =>
    ({ items, preparedEquipment, experienceGranted: 0, consumed: false });

  const rejectedScenarios = {
    'extracted item with primary scaling': () => result([{ lootId: 'training_short_sword', amount: 1, modifiers: withPrimary('training_short_sword') }]),
    'extracted item with primary and secondary scaling': () => result([{ lootId: 'training_dagger', amount: 1, modifiers: withSecondary('training_dagger') }]),
    'equipped weapon with primary scaling': () => result([], { ...EMPTY_EQUIPMENT, weaponSlot1: { lootId: 'training_wand', modifiers: withPrimary('training_wand') } })
  };
  for (const [name, build] of Object.entries(rejectedScenarios)) {
    await t.test(`rejects ${name} without writing`, async () => {
      authResult = build();
      const beforeCharacter = structuredClone(character);
      const beforeResult = structuredClone(authResult);

      await assert.rejects(commit, rejected);

      assert.deepEqual(character, beforeCharacter);
      assert.equal(character.revision, 3);
      assert.deepEqual(authResult, beforeResult);
      assert.equal(characterWrites, 0);
      assert.equal(claimWrites, 0);
    });
  }

  await t.test('the same training weapon without scaling is accepted', async () => {
    authResult = result([{ lootId: 'training_short_sword', amount: 1, modifiers: { ...gems } }]);
    const outcome = await commit();
    assert.equal(outcome.alreadySecured, false);
    assert.deepEqual(character.inventory.loadout.map(entry => entry.lootId), ['training_short_sword']);
    assert.equal(character.inventory.loadout[0].instance.modifiers.primary, undefined);
    assert.equal(character.revision, 4);
    assert.equal(claimWrites, 1);
  });

  await t.test('a normal weapon with valid primary scaling is still accepted', async () => {
    const modifiers = { primary: { attribute: 'strength', grade: 'S' }, ...gems };
    authResult = result([{ lootId: 'arming_sword', amount: 1, modifiers }]);
    const outcome = await commit();
    assert.equal(outcome.alreadySecured, false);
    assert.deepEqual(character.inventory.loadout.map(entry => entry.lootId), ['arming_sword']);
    assert.deepEqual(character.inventory.loadout[0].instance.modifiers, modifiers);
    assert.equal(character.revision, 4);
  });
});
