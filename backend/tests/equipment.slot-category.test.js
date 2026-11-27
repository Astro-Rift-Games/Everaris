'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const request = require('supertest');
const jwt = require('jsonwebtoken');
const app = require('../src/app');
const env = require('../src/config/env');
const Character = require('../src/models/Character');
const Revoked = require('../src/models/RevokedToken');
const SessionTicketService = require('../src/services/SessionTicketService');

const ACCOUNT_ID = 'aaaaaaaaaaaaaaaaaaaaaaaa';
const WEAPON_REQUIRED_MESSAGE =
  'Equip a weapon in a Weapon Set or ask the Trainer before preparing an expedition.';

const hexId = n => n.toString(16).padStart(24, '0');
const token = () => jwt.sign({ sub: ACCOUNT_ID }, env.jwtSecret);

function makeItem(version, slot, lootId, idNum) {
  if (version === 1) return { lootId, amount: 1 };
  if (version === 2) return { slot, lootId, amount: 1 };
  const isWeapon = [
    'arming_sword', 'spell_book', 'training_short_sword', 'training_dagger', 'training_wand'
  ].includes(lootId);
  const isArmor = [
    'heavy_armor_helmet', 'heavy_armor_breastplate', 'heavy_armor_gauntlets', 'heavy_armor_leg_plate'
  ].includes(lootId);
  const isShield = lootId === 'shield';
  if (!isWeapon && !isArmor && !isShield) return { slot, lootId, amount: 1 };
  return {
    slot,
    lootId,
    amount: 1,
    instance: {
      instanceId: hexId(idNum),
      ...(isWeapon ? { modifiers: { gemSocketCount: 0, gems: [] } } : {})
    }
  };
}

function makeEquipped(version, lootId, idNum) {
  if (version < 3) return lootId;
  const item = makeItem(3, 0, lootId, idNum);
  return {
    lootId: item.lootId,
    instance: item.instance || { instanceId: hexId(idNum) }
  };
}

function makeIntent(version, lootId, idNum) {
  if (version < 3) return lootId;
  return { lootId, instanceId: hexId(idNum) };
}

function makeCharacter(version, overrides = {}) {
  return {
    _id: ACCOUNT_ID,
    accountId: ACCOUNT_ID,
    name: 'SlotGuardHero',
    level: 5,
    experience: 100,
    revision: 8,
    characterAttributes: {
      vitality: 5, resistance: 5, strength: 10, dexterity: 10, intelligence: 10, luck: 5, availablePoints: 0
    },
    unlockedAbilities: [],
    preparedAbilities: { slot1: '', slot2: '' },
    inventory: {
      inventorySchemaVersion: version,
      currency: 200,
      stash: [],
      loadout: [],
      preparedEquipment: {},
      pendingReservation: null,
      ...overrides
    }
  };
}

function applySet(target, $set) {
  for (const [key, value] of Object.entries($set || {})) {
    const parts = key.split('.');
    let cursor = target;
    for (let i = 0; i < parts.length - 1; i++) cursor = cursor[parts[i]];
    cursor[parts[parts.length - 1]] = structuredClone(value);
  }
}

function stubCharacter(stored) {
  const state = { writes: 0 };
  Character.findOne = async () => structuredClone(stored);
  Character.findOneAndUpdate = async (_query, update) => {
    state.writes += 1;
    const next = structuredClone(stored);
    applySet(next, update.$set);
    if (update.$inc?.revision) next.revision += update.$inc.revision;
    Object.assign(stored, next);
    return structuredClone(stored);
  };
  Character.updateOne = async (_query, update) => {
    state.writes += 1;
    const next = structuredClone(stored);
    applySet(next, update.$set);
    Object.assign(stored, next);
    return { matchedCount: 1, modifiedCount: 1 };
  };
  Revoked.exists = async () => false;
  return state;
}

test('FIX-95: prepared equipment rejects incompatible item categories without writing', async t => {
  const originals = [Character.findOne, Character.findOneAndUpdate, Character.updateOne, Revoked.exists];
  t.after(() => { [Character.findOne, Character.findOneAndUpdate, Character.updateOne, Revoked.exists] = originals; });

  const invalidCases = [
    { label: 'material in weaponSlot1', slot: 'weaponSlot1', lootId: 'bone' },
    { label: 'consumable in weaponSlot2', slot: 'weaponSlot2', lootId: 'CON_HEALTH_POTION' },
    { label: 'helmet in weaponSlot1', slot: 'weaponSlot1', lootId: 'heavy_armor_helmet' },
    { label: 'unknown item in weaponSlot1', slot: 'weaponSlot1', lootId: 'leather_cap' },
    { label: 'boots in offHand1', slot: 'offHand1', lootId: 'heavy_armor_leg_plate' },
    { label: 'material in offHand2', slot: 'offHand2', lootId: 'bone' },
    { label: 'weapon in helmet', slot: 'helmet', lootId: 'arming_sword' },
    { label: 'shield in helmet', slot: 'helmet', lootId: 'shield' },
    { label: 'helmet in armor', slot: 'armor', lootId: 'heavy_armor_helmet' },
    { label: 'armor in gloves', slot: 'gloves', lootId: 'heavy_armor_breastplate' },
    { label: 'gloves in boots', slot: 'boots', lootId: 'heavy_armor_gauntlets' },
    { label: 'material in armor', slot: 'armor', lootId: 'bone' }
  ];

  for (const version of [1, 2, 3]) {
    for (const { label, slot, lootId } of invalidCases) {
      await t.test(`v${version} rejects ${label}`, async () => {
        const stored = makeCharacter(version, {
          loadout: [makeItem(version, 0, lootId, 1)],
          preparedEquipment: { weaponSlot1: makeEquipped(version, 'arming_sword', 2) }
        });
        const state = stubCharacter(stored);
        const before = structuredClone(stored);

        const body = {
          [slot]: makeIntent(version, lootId, 1),
          expectedRevision: stored.revision
        };
        const res = await request(app)
          .put('/character/me/inventory/prepared-equipment')
          .set('Authorization', `Bearer ${token()}`)
          .send(body);

        assert.equal(res.status, 422);
        assert.equal(res.body.error, 'INVALID_EQUIPMENT_SLOT');
        assert.equal(state.writes, 0);
        assert.deepEqual(stored, before);
      });
    }
  }
});

test('FIX-95: reservation and admission reject non-weapon items in weapon slots with PREPARED_WEAPON_REQUIRED', async t => {
  const originals = [Character.findOne, Character.findOneAndUpdate, Character.updateOne, Revoked.exists];
  t.after(() => { [Character.findOne, Character.findOneAndUpdate, Character.updateOne, Revoked.exists] = originals; });

  // v1/v2 can hold arbitrary legacy strings; v3 validEquipment already rejects non-equipment shapes,
  // while non-weapon equipment instances (armor/shield) could still sit in weaponSlot1.
  const nonWeaponsByVersion = {
    1: ['bone', 'leather_cap', 'heavy_armor_helmet', 'shield'],
    2: ['bone', 'leather_cap', 'heavy_armor_helmet', 'shield'],
    3: ['heavy_armor_helmet', 'heavy_armor_breastplate', 'shield']
  };

  for (const version of [1, 2, 3]) {
    for (const badLootId of nonWeaponsByVersion[version]) {
      await t.test(`v${version} reservation rejects legacy weaponSlot1='${badLootId}' without writes`, async () => {
        const stored = makeCharacter(version, {
          preparedEquipment: { weaponSlot1: makeEquipped(version, badLootId, 10) }
        });
        const state = stubCharacter(stored);
        const before = structuredClone(stored);

        const res = await request(app)
          .post('/character/me/inventory/reservation')
          .set('Authorization', `Bearer ${token()}`)
          .send({ reservationId: 'res-bad-weapon' });

        assert.equal(res.status, 409);
        assert.deepEqual(res.body, {
          error: 'PREPARED_WEAPON_REQUIRED',
          message: WEAPON_REQUIRED_MESSAGE
        });
        assert.equal(state.writes, 0);
        assert.deepEqual(stored, before);
      });

      await t.test(`v${version} same-ID reservation retry rejects legacy weaponSlot1='${badLootId}'`, async () => {
        const stored = makeCharacter(version, {
          pendingReservation: {
            reservationId: 'res-legacy-retry',
            admittedAt: null,
            items: [],
            preparedEquipment: { weaponSlot1: makeEquipped(version, badLootId, 11) }
          }
        });
        const state = stubCharacter(stored);
        const before = structuredClone(stored);

        const res = await request(app)
          .post('/character/me/inventory/reservation')
          .set('Authorization', `Bearer ${token()}`)
          .send({ reservationId: 'res-legacy-retry' });

        assert.equal(res.status, 409);
        assert.deepEqual(res.body, {
          error: 'PREPARED_WEAPON_REQUIRED',
          message: WEAPON_REQUIRED_MESSAGE
        });
        assert.equal(state.writes, 0);
        assert.deepEqual(stored, before);
      });

      await t.test(`v${version} admission ticket rejects legacy weaponSlot1='${badLootId}' without writes`, async () => {
        const stored = makeCharacter(version, {
          pendingReservation: {
            reservationId: 'res-legacy-ticket',
            admittedAt: null,
            items: [],
            preparedEquipment: { weaponSlot1: makeEquipped(version, badLootId, 12) }
          }
        });
        const state = stubCharacter(stored);
        const before = structuredClone(stored);

        await assert.rejects(
          () => SessionTicketService.issueRaidAdmissionTicket(ACCOUNT_ID, '123456', 'res-legacy-ticket'),
          {
            statusCode: 409,
            errorCode: 'PREPARED_WEAPON_REQUIRED',
            message: WEAPON_REQUIRED_MESSAGE
          }
        );
        assert.equal(state.writes, 0);
        assert.deepEqual(stored, before);
      });
    }
  }
});

test('FIX-95: valid weapons, training weapons, shields and armors continue working in v1, v2 and v3', async t => {
  const originals = [Character.findOne, Character.findOneAndUpdate, Character.updateOne, Revoked.exists];
  t.after(() => { [Character.findOne, Character.findOneAndUpdate, Character.updateOne, Revoked.exists] = originals; });

  for (const version of [1, 2, 3]) {
    await t.test(`v${version} equips full valid loadout, reserves, and issues admission ticket`, async () => {
      const stored = makeCharacter(version, {
        loadout: [
          makeItem(version, 0, 'arming_sword', 1),
          makeItem(version, 1, 'training_wand', 2),
          makeItem(version, 2, 'shield', 3),
          makeItem(version, 3, 'spell_book', 4),
          makeItem(version, 4, 'heavy_armor_helmet', 5),
          makeItem(version, 5, 'heavy_armor_breastplate', 6),
          makeItem(version, 6, 'heavy_armor_gauntlets', 7),
          makeItem(version, 7, 'heavy_armor_leg_plate', 8)
        ]
      });
      stubCharacter(stored);

      const equipRes = await request(app)
        .put('/character/me/inventory/prepared-equipment')
        .set('Authorization', `Bearer ${token()}`)
        .send({
          weaponSlot1: makeIntent(version, 'arming_sword', 1),
          weaponSlot2: makeIntent(version, 'training_wand', 2),
          offHand1: makeIntent(version, 'shield', 3),
          offHand2: makeIntent(version, 'spell_book', 4),
          helmet: makeIntent(version, 'heavy_armor_helmet', 5),
          armor: makeIntent(version, 'heavy_armor_breastplate', 6),
          gloves: makeIntent(version, 'heavy_armor_gauntlets', 7),
          boots: makeIntent(version, 'heavy_armor_leg_plate', 8),
          expectedRevision: 8
        });

      assert.equal(equipRes.status, 200);
      assert.equal(equipRes.body.revision, 9);

      const reserveRes = await request(app)
        .post('/character/me/inventory/reservation')
        .set('Authorization', `Bearer ${token()}`)
        .send({ reservationId: `res-valid-v${version}` });

      assert.equal(reserveRes.status, 201);

      const ticket = await SessionTicketService.issueRaidAdmissionTicket(
        ACCOUNT_ID, '654321', `res-valid-v${version}`
      );
      assert.ok(ticket.ticket);
      assert.equal(ticket.reservationId, `res-valid-v${version}`);
    });

    await t.test(`v${version} accepts off-hand-only weapon and training weapons for reservation`, async () => {
      for (const [slot, weaponId] of [
        ['weaponSlot1', 'training_short_sword'],
        ['weaponSlot2', 'training_dagger'],
        ['offHand1', 'arming_sword']
      ]) {
        const stored = makeCharacter(version, {
          preparedEquipment: { [slot]: makeEquipped(version, weaponId, 20) }
        });
        stubCharacter(stored);

        const reserveRes = await request(app)
          .post('/character/me/inventory/reservation')
          .set('Authorization', `Bearer ${token()}`)
          .send({ reservationId: `res-${slot}-${weaponId}` });

        assert.equal(reserveRes.status, 201, `v${version} ${slot}=${weaponId} should reserve`);

        const ticket = await SessionTicketService.issueRaidAdmissionTicket(
          ACCOUNT_ID, '112233', `res-${slot}-${weaponId}`
        );
        assert.ok(ticket.ticket, `v${version} ${slot}=${weaponId} should issue ticket`);
      }
    });

    await t.test(`v${version} allows clearing a previously persisted invalid slot`, async () => {
      const legacyBad = version === 3 ? 'heavy_armor_helmet' : 'bone';
      const stored = makeCharacter(version, {
        loadout: [makeItem(version, 0, 'arming_sword', 30)],
        preparedEquipment: { weaponSlot1: makeEquipped(version, legacyBad, 31) }
      });
      stubCharacter(stored);

      const res = await request(app)
        .put('/character/me/inventory/prepared-equipment')
        .set('Authorization', `Bearer ${token()}`)
        .send({
          weaponSlot1: makeIntent(version, 'arming_sword', 30),
          expectedRevision: 8
        });

      assert.equal(res.status, 200);
      assert.equal(res.body.revision, 9);
    });
  }
});
