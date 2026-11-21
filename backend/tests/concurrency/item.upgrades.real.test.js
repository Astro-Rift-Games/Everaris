'use strict';
// TASK-302 (Doc 23 §9): real Mongo races on the NPC upgrade. One revision-guarded write decides the winner;
// a concurrent retry of the same transaction sees its recorded outcome, every other loser changes nothing.
const test = require('node:test');
const assert = require('node:assert/strict');
const mongoose = require('mongoose');
const { startMemoryMongo } = require('../support/memoryMongo');
const Character = require('../../src/models/Character');
const Inventory = require('../../src/services/InventoryService');
const { createInstance } = require('../../src/services/ItemInstances');

const ROUNDS = 12;
const GOLD = 500;
const amountOf = (items, lootId) => items.filter(item => item.lootId === lootId).reduce((sum, item) => sum + item.amount, 0);

async function seedCharacter() {
  const accountId = new mongoose.Types.ObjectId();
  const instance = createInstance('arming_sword', { gemSocketCount: 0, gems: [] });
  await Character.create({ accountId, name: 'Smith', inventory: { inventorySchemaVersion: 3, currency: GOLD,
    loadout: [{ slot: 9, lootId: 'arming_sword', amount: 1, instance },
      { slot: 3, lootId: 'RES_RUNE_FRAGMENT', amount: 4 }, { slot: 4, lootId: 'RES_ATTUNEMENT_GEM', amount: 1 }] } });
  const ticket = (patch = {}) => ({ transactionId: 'upgrade', expectedRevision: 0, merchant: 'blacksmith',
    operation: 'primary_scaling', instanceId: instance.instanceId, location: 'loadout', slot: 9, ...patch });
  return { accountId, instance, ticket };
}

const upgradeReceipts = stored => stored.appliedShopReceipts.filter(receipt => receipt.type === 'upgrade');
const settle = outcomes => ({
  successes: outcomes.filter(outcome => outcome.status === 'fulfilled').map(outcome => outcome.value),
  failures: outcomes.filter(outcome => outcome.status === 'rejected').map(outcome => outcome.reason)
});

test('real v3 upgrade races commit once and never leave a partial mutation', async t => {
  const mongo = await startMemoryMongo(); await mongoose.connect(mongo.getUri());
  t.after(async () => { await mongoose.disconnect(); await mongo.stop(); });

  await t.test('same transactionId and body: both callers succeed, one debit, one receipt', async () => {
    for (let round = 0; round < ROUNDS; round++) {
      const { accountId, instance, ticket } = await seedCharacter();
      const outcomes = await Promise.allSettled([Inventory.upgradeItem(accountId, ticket()), Inventory.upgradeItem(accountId, ticket())]);
      const { successes, failures } = settle(outcomes);
      assert.deepEqual(failures, [], `round ${round}`);
      assert.equal(successes.length, 2);
      assert.equal(successes.filter(result => result.alreadySecured === false).length, 1, `round ${round}`);
      assert.equal(successes.filter(result => result.alreadySecured === true).length, 1, `round ${round}`);
      // Winner and replay report the same recorded outcome and the same post-commit snapshot.
      assert.deepEqual(successes[0].upgrade, successes[1].upgrade);
      assert.equal(successes[0].revision, 1); assert.equal(successes[1].revision, 1);
      assert.equal(successes[0].currency, GOLD - 60); assert.equal(successes[1].currency, GOLD - 60);
      const stored = await Character.findOne({ accountId }).lean();
      assert.equal(stored.revision, 1);
      assert.equal(stored.inventory.currency, GOLD - 60);
      assert.equal(amountOf(stored.inventory.loadout, 'RES_RUNE_FRAGMENT'), 2);
      assert.equal(amountOf(stored.inventory.loadout, 'RES_ATTUNEMENT_GEM'), 1);
      assert.equal(upgradeReceipts(stored).length, 1);
      assert.equal(stored.appliedShopReceipts.length, 1);
      const weapon = stored.inventory.loadout.find(item => item.instance);
      assert.equal(weapon.slot, 9);
      assert.equal(weapon.instance.instanceId, instance.instanceId);
      assert.deepEqual(weapon.instance.modifiers.primary.grade, 'E');
    }
  });

  await t.test('different transactionIds at the same revision: one commits, the other is a clean REVISION_CONFLICT', async () => {
    for (let round = 0; round < ROUNDS; round++) {
      const { accountId, ticket } = await seedCharacter();
      const outcomes = await Promise.allSettled([
        Inventory.upgradeItem(accountId, ticket({ transactionId: 'scaling' })),
        Inventory.upgradeItem(accountId, ticket({ transactionId: 'socket', operation: 'first_socket' }))
      ]);
      const { successes, failures } = settle(outcomes);
      assert.equal(successes.length, 1, `round ${round}`); assert.equal(failures.length, 1, `round ${round}`);
      assert.equal(failures[0].statusCode, 409); assert.equal(failures[0].errorCode, 'REVISION_CONFLICT');
      assert.equal(successes[0].alreadySecured, false);
      const stored = await Character.findOne({ accountId }).lean();
      assert.equal(stored.revision, 1);
      const [receipt] = upgradeReceipts(stored);
      assert.equal(upgradeReceipts(stored).length, 1); assert.equal(stored.appliedShopReceipts.length, 1);
      const weapon = stored.inventory.loadout.find(item => item.instance);
      // Only the winner's cost and modifiers are present; the loser contributed nothing.
      if (receipt.transactionId === 'scaling') {
        assert.equal(stored.inventory.currency, GOLD - 60);
        assert.equal(amountOf(stored.inventory.loadout, 'RES_RUNE_FRAGMENT'), 2);
        assert.equal(amountOf(stored.inventory.loadout, 'RES_ATTUNEMENT_GEM'), 1);
        assert.deepEqual(weapon.instance.modifiers, { primary: { attribute: 'strength', grade: 'E' }, gemSocketCount: 0, gems: [] });
      } else {
        assert.equal(receipt.transactionId, 'socket');
        assert.equal(stored.inventory.currency, GOLD - 180);
        assert.equal(amountOf(stored.inventory.loadout, 'RES_RUNE_FRAGMENT'), 2);
        assert.equal(amountOf(stored.inventory.loadout, 'RES_ATTUNEMENT_GEM'), 0);
        assert.deepEqual(weapon.instance.modifiers, { gemSocketCount: 1, gems: [null] });
      }
    }
  });

  await t.test('same transactionId with a different intent: the loser is TRANSACTION_ID_REUSED, nothing else changes', async () => {
    for (let round = 0; round < ROUNDS; round++) {
      const { accountId, ticket } = await seedCharacter();
      const outcomes = await Promise.allSettled([Inventory.upgradeItem(accountId, ticket()),
        Inventory.upgradeItem(accountId, ticket({ operation: 'first_socket' }))]);
      const { successes, failures } = settle(outcomes);
      assert.equal(successes.length, 1, `round ${round}`); assert.equal(failures.length, 1, `round ${round}`);
      assert.equal(failures[0].statusCode, 409); assert.equal(failures[0].errorCode, 'TRANSACTION_ID_REUSED');
      const stored = await Character.findOne({ accountId }).lean();
      assert.equal(stored.revision, 1); assert.equal(upgradeReceipts(stored).length, 1);
      assert.equal(stored.inventory.currency, GOLD - upgradeReceipts(stored)[0].totalAmount);
    }
  });

  for (const contender of ['move-weapon', 'sell-weapon', 'move-materials']) {
    await t.test(`upgrade racing ${contender}: the loser leaves the stored document consistent`, async () => {
      for (let round = 0; round < ROUNDS; round++) {
        const { accountId, instance, ticket } = await seedCharacter();
        const other = () => contender === 'move-weapon' ? Inventory.moveToStash(accountId, 'arming_sword', 1, 0, 9)
          : contender === 'sell-weapon' ? Inventory.shopSell(accountId, 'arming_sword', 1, 0, 0, 'sell', 9)
            : Inventory.moveToStash(accountId, 'RES_RUNE_FRAGMENT', 4, 0, 3);
        const outcomes = await Promise.allSettled([Inventory.upgradeItem(accountId, ticket()), other()]);
        const { successes, failures } = settle(outcomes);
        assert.equal(successes.length, 1, `${contender} round ${round}`); assert.equal(failures.length, 1);
        assert.equal(failures[0].statusCode, 409); assert.equal(failures[0].errorCode, 'REVISION_CONFLICT');
        const stored = await Character.findOne({ accountId }).lean();
        assert.equal(stored.revision, 1);
        const upgraded = upgradeReceipts(stored).length === 1;
        const weapons = [...stored.inventory.loadout, ...stored.inventory.stash, ...(stored.inventory.buyback?.entries ?? [])]
          .filter(item => item.instance);
        assert.equal(weapons.length, 1);
        assert.equal(weapons[0].instance.instanceId, instance.instanceId);
        const fragments = amountOf(stored.inventory.loadout, 'RES_RUNE_FRAGMENT') + amountOf(stored.inventory.stash, 'RES_RUNE_FRAGMENT');
        if (upgraded) {
          // The upgrade won: its single debit is applied and the other write changed nothing.
          assert.equal(stored.inventory.currency, GOLD - 60); assert.equal(fragments, 2);
          assert.equal(weapons[0].instance.modifiers.primary.grade, 'E');
          assert.equal(stored.appliedShopReceipts.length, 1);
        } else {
          // The other write won: no gold, material or modifier from the upgrade leaked into the document.
          assert.equal(fragments, 4); assert.deepEqual(weapons[0].instance.modifiers, { gemSocketCount: 0, gems: [] });
          assert.equal(stored.inventory.currency, contender === 'sell-weapon' ? GOLD + 60 : GOLD);
        }
        assert.equal(stored.inventory.pendingReservation, null);
      }
    });
  }
});
