'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');

const character = (inventory = {}, extra = {}) => ({
  _id: 'c1', revision: 4, inventory: { inventorySchemaVersion: 3, stash: [], loadout: [], ...inventory }, ...extra
});
const freeze = value => {
  if (value && typeof value === 'object') { Object.freeze(value); Object.values(value).forEach(freeze); }
  return value;
};

// Fake driver collection: applies $set paths and $inc revision, honouring the revision guard.
function fakeCollection(documents) {
  const calls = { updates: [] };
  return {
    calls,
    async *find() { for (const doc of documents) yield structuredClone(doc); },
    async findOne({ _id }) { const doc = documents.find(d => d._id === _id); return doc && structuredClone(doc); },
    async updateOne(filter, update) {
      calls.updates.push({ filter, update });
      const doc = documents.find(d => d._id === filter._id);
      if (!doc || doc.revision !== filter.revision) return { matchedCount: 0 };
      for (const [path, value] of Object.entries(update.$set)) {
        const keys = path.split('.'); let at = doc;
        for (const key of keys.slice(0, -1)) at = at[key];
        at[keys.at(-1)] = structuredClone(value);
      }
      doc.revision += update.$inc.revision;
      return { matchedCount: 1 };
    }
  };
}

test('planner rewrites legacy ids in stash, loadout, equipment and reservation fields without touching the input', () => {
  const { planLegacyItemIds } = require('../scripts/migrate_legacy_item_ids');
  const input = freeze(character({
    stash: [{ slot: 0, lootId: 'healthpotion', amount: 4, note: 'kept' }, { slot: 1, lootId: 'bone', amount: 2 }],
    loadout: [{ slot: 3, lootId: 'healthpotion', amount: 1 }],
    preparedEquipment: { weaponSlot1: 'longsword', helmet: '' },
    pendingReservation: {
      reservationId: 'r1', admittedAt: null,
      items: [{ slot: 0, lootId: 'healthpotion', amount: 3 }],
      preparedEquipment: { weaponSlot1: { lootId: 'longsword', instance: { instanceId: 'a'.repeat(24) } }, armor: null }
    }
  }));
  const plan = planLegacyItemIds(input);
  assert.equal(plan.status, 'planned');
  assert.deepEqual(plan.set, {
    'inventory.stash': [{ slot: 0, lootId: 'CON_HEALTH_POTION', amount: 4, note: 'kept' }, { slot: 1, lootId: 'bone', amount: 2 }],
    'inventory.loadout': [{ slot: 3, lootId: 'CON_HEALTH_POTION', amount: 1 }],
    'inventory.preparedEquipment.weaponSlot1': 'arming_sword',
    'inventory.pendingReservation.items': [{ slot: 0, lootId: 'CON_HEALTH_POTION', amount: 3 }],
    'inventory.pendingReservation.preparedEquipment.weaponSlot1': { lootId: 'arming_sword', instance: { instanceId: 'a'.repeat(24) } }
  });
  assert.deepEqual(plan.changes.map(c => [c.field, c.from, c.to]).sort(), [
    ['loadout', 'healthpotion', 'CON_HEALTH_POTION'],
    ['pendingReservation.items', 'healthpotion', 'CON_HEALTH_POTION'],
    ['pendingReservation.preparedEquipment.weaponSlot1', 'longsword', 'arming_sword'],
    ['preparedEquipment.weaponSlot1', 'longsword', 'arming_sword'],
    ['stash', 'healthpotion', 'CON_HEALTH_POTION']
  ]);
});

test('planner merges a legacy stack into the existing canonical stack of the same container, preserving totals', () => {
  const { planLegacyItemIds } = require('../scripts/migrate_legacy_item_ids');
  const plan = planLegacyItemIds(freeze(character({
    stash: [
      { slot: 0, lootId: 'healthpotion', amount: 4 },
      { slot: 1, lootId: 'CON_HEALTH_POTION', amount: 2, note: 'target' },
      { slot: 2, lootId: 'healthpotion', amount: 1 },
      { slot: 3, lootId: 'bone', amount: 5 }
    ],
    loadout: [{ slot: 0, lootId: 'healthpotion', amount: 1 }, { slot: 1, lootId: 'healthpotion', amount: 2 }]
  })));
  assert.deepEqual(plan.set['inventory.stash'], [
    { slot: 1, lootId: 'CON_HEALTH_POTION', amount: 7, note: 'target' },
    { slot: 3, lootId: 'bone', amount: 5 }
  ]);
  assert.deepEqual(plan.set['inventory.loadout'], [{ slot: 0, lootId: 'CON_HEALTH_POTION', amount: 3 }],
    'with no canonical stack the lowest legacy slot becomes the stack');
  assert.equal(plan.lists.stash.merged, 2);
  assert.deepEqual(plan.lists.stash.totalsBefore, plan.lists.stash.totalsAfter);
});

test('planner does not merge across containers or instance-bearing equipment, and ignores canonical duplicates', () => {
  const { planLegacyItemIds } = require('../scripts/migrate_legacy_item_ids');
  const inst = n => ({ instanceId: String(n).repeat(24) });
  const plan = planLegacyItemIds(freeze(character({
    stash: [{ slot: 0, lootId: 'healthpotion', amount: 1 }],
    loadout: [{ slot: 0, lootId: 'CON_HEALTH_POTION', amount: 1 }],
    pendingReservation: { reservationId: 'r', items: [
      { slot: 0, lootId: 'longsword', amount: 1, instance: inst(1) },
      { slot: 1, lootId: 'arming_sword', amount: 1, instance: inst(2) }
    ] }
  })));
  assert.deepEqual(plan.set['inventory.stash'], [{ slot: 0, lootId: 'CON_HEALTH_POTION', amount: 1 }]);
  assert.equal(plan.set['inventory.loadout'], undefined);
  assert.deepEqual(plan.set['inventory.pendingReservation.items'], [
    { slot: 0, lootId: 'arming_sword', amount: 1, instance: inst(1) },
    { slot: 1, lootId: 'arming_sword', amount: 1, instance: inst(2) }
  ]);
  const duplicates = [{ slot: 0, lootId: 'bone', amount: 1 }, { slot: 1, lootId: 'bone', amount: 1 }];
  assert.equal(planLegacyItemIds(character({ stash: duplicates })).status, 'unchanged');
});

test('unknown ids are untouched and a character without legacy ids is unchanged (idempotent plan)', () => {
  const { planLegacyItemIds } = require('../scripts/migrate_legacy_item_ids');
  const unknown = character({ stash: [{ slot: 0, lootId: 'forged_dragon_scale', amount: 1 }, { slot: 1, lootId: ' healthpotion', amount: 1 }] });
  const plan = planLegacyItemIds(unknown);
  assert.deepEqual([plan.status, plan.set, plan.changes], ['unchanged', undefined, []]);
  const first = planLegacyItemIds(character({ stash: [{ slot: 0, lootId: 'healthpotion', amount: 2 }] }));
  const applied = character({ stash: first.set['inventory.stash'] });
  assert.equal(planLegacyItemIds(applied).status, 'unchanged');
});

test('planner aborts on malformed entries or unsafe totals without producing a write set', () => {
  const { planLegacyItemIds } = require('../scripts/migrate_legacy_item_ids');
  for (const stash of [[{ slot: 0, lootId: 'healthpotion', amount: 0 }], [{ slot: 0, lootId: 'healthpotion', amount: 1.5 }], [{ slot: 0 }], 'x']) {
    const plan = planLegacyItemIds(character({ stash }));
    assert.equal(plan.status, 'aborted'); assert.equal(plan.set, undefined);
  }
  const unsafe = planLegacyItemIds(character({ stash: [
    { slot: 0, lootId: 'healthpotion', amount: Number.MAX_SAFE_INTEGER }, { slot: 1, lootId: 'CON_HEALTH_POTION', amount: 1 }] }));
  assert.deepEqual([unsafe.status, unsafe.reason, unsafe.set], ['aborted', 'UNSAFE_AMOUNT', undefined]);
});

test('dry-run (default) reports per character and writes nothing; apply writes once with a revision guard', async () => {
  const { migrateLegacyItemIds } = require('../scripts/migrate_legacy_item_ids');
  const docs = [
    character({ stash: [{ slot: 0, lootId: 'healthpotion', amount: 2 }] }, { _id: 'a' }),
    character({ stash: [{ slot: 0, lootId: 'bone', amount: 2 }] }, { _id: 'b' })
  ];
  const before = structuredClone(docs);
  const reports = [];
  const dry = fakeCollection(docs);
  const summary = await migrateLegacyItemIds(dry, { report: r => reports.push(r) });
  assert.deepEqual(docs, before); assert.equal(dry.calls.updates.length, 0);
  assert.deepEqual([summary.dryRun, summary.scanned, summary.planned, summary.unchanged, summary.migrated, summary.exitCode],
    [true, 2, 1, 1, 0, 0]);
  assert.deepEqual(reports.slice(0, 2).map(r => [r.characterId, r.status]), [['a', 'planned'], ['b', 'unchanged']]);
  assert.equal(reports[0].changes[0].from, 'healthpotion');
  assert.equal(reports[2].type, 'summary');

  const real = fakeCollection(docs);
  const applied = await migrateLegacyItemIds(real, { apply: true, report: () => {} });
  assert.deepEqual([applied.dryRun, applied.migrated, applied.unchanged], [false, 1, 1]);
  assert.equal(real.calls.updates.length, 1);
  assert.deepEqual(real.calls.updates[0].filter, { _id: 'a', revision: 4 });
  assert.deepEqual(real.calls.updates[0].update.$inc, { revision: 1 });
  assert.equal(docs[0].inventory.stash[0].lootId, 'CON_HEALTH_POTION'); assert.equal(docs[0].revision, 5);

  const again = fakeCollection(docs);
  const second = await migrateLegacyItemIds(again, { apply: true, report: () => {} });
  assert.equal(again.calls.updates.length, 0, 'second run writes nothing');
  assert.deepEqual([second.migrated, second.planned, second.unchanged], [0, 0, 2]);
  assert.equal(docs[0].revision, 5);
});

test('revision conflicts re-plan up to the retry bound; invalid revisions abort', async () => {
  const { migrateLegacyItemIds } = require('../scripts/migrate_legacy_item_ids');
  const doc = character({ stash: [{ slot: 0, lootId: 'healthpotion', amount: 2 }] }, { _id: 'a' });
  const collection = fakeCollection([doc]);
  collection.find = async function* () { const stale = structuredClone(doc); doc.revision++; yield stale; };
  const summary = await migrateLegacyItemIds(collection, { apply: true, report: () => {} });
  assert.deepEqual([summary.migrated, summary.conflicts, summary.aborted], [1, 1, 0]);
  assert.equal(doc.inventory.stash[0].lootId, 'CON_HEALTH_POTION');
  const bad = fakeCollection([{ ...character({ stash: [{ slot: 0, lootId: 'healthpotion', amount: 1 }] }), revision: -1 }]);
  const aborted = await migrateLegacyItemIds(bad, { apply: true, report: () => {} });
  assert.deepEqual([aborted.aborted, aborted.exitCode, bad.calls.updates.length], [1, 1, 0]);
});

test('CLI rejects unknown or contradictory flags without connecting', async () => {
  const { main } = require('../scripts/migrate_legacy_item_ids');
  const errors = []; const original = console.error; console.error = message => errors.push(message);
  try {
    assert.equal(await main(['--bogus']), 2);
    assert.equal(await main(['--apply', '--dry-run']), 2);
  } finally { console.error = original; }
  assert.equal(errors.length, 2);
});

test('several overflow-only legacy stacks are each renamed, none merged', () => {
  const { planLegacyItemIds } = require('../scripts/migrate_legacy_item_ids');
  const plan = planLegacyItemIds({ inventory: { inventorySchemaVersion: 2, loadout: [], stash: [
    { slot: 61, lootId: 'healthpotion', amount: 5 },
    { slot: 62, lootId: 'healthpotion', amount: 2 }
  ] } });
  assert.deepEqual(plan.set['inventory.stash'], [
    { slot: 61, lootId: 'CON_HEALTH_POTION', amount: 5 },
    { slot: 62, lootId: 'CON_HEALTH_POTION', amount: 2 }
  ]);
  assert.equal(plan.lists.stash.merged, 0);
});

test('canonical overflow stack is not a merge target; lowest in-capacity legacy stack absorbs the others', () => {
  const { planLegacyItemIds } = require('../scripts/migrate_legacy_item_ids');
  const plan = planLegacyItemIds({ inventory: { inventorySchemaVersion: 2, loadout: [], stash: [
    { slot: 5, lootId: 'healthpotion', amount: 1 },
    { slot: 2, lootId: 'healthpotion', amount: 3 },
    { slot: 70, lootId: 'CON_HEALTH_POTION', amount: 9 }
  ] } });
  assert.deepEqual(plan.set['inventory.stash'], [
    { slot: 2, lootId: 'CON_HEALTH_POTION', amount: 4 },
    { slot: 70, lootId: 'CON_HEALTH_POTION', amount: 9 }
  ]);
});

test('case variants of a legacy id are not aliases', () => {
  const { planLegacyItemIds } = require('../scripts/migrate_legacy_item_ids');
  const plan = planLegacyItemIds({ inventory: { stash: [{ slot: 0, lootId: 'HealthPotion', amount: 1 }], loadout: [] } });
  assert.equal(plan.status, 'unchanged');
});
