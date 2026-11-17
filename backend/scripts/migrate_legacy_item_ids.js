'use strict';

/**
 * Legacy item id migration (explicit, manual; never invoked by a request or a read).
 * From backend/: node scripts/migrate_legacy_item_ids.js [--apply] [--help]
 * MONGODB_URI is read from the environment / backend/.env, like the other scripts.
 *
 * Rewrites stored ids that became aliases (e.g. healthpotion -> CON_HEALTH_POTION) to
 * their canonical id, using the single alias table of InventoryLootIdNormalizer.
 * Dry-run is the DEFAULT: nothing is written unless --apply is passed. Back up the
 * characters collection first and run the dry-run against a copy.
 *
 * Covered fields: inventory.stash, inventory.loadout, inventory.pendingReservation.items,
 * inventory.preparedEquipment.<slot> and inventory.pendingReservation.preparedEquipment.<slot>.
 * Unknown ids are never touched. Schema version, slots, instances and every other
 * field are preserved.
 *
 * Stacking: inside one stash/loadout/reservation list, a legacy stack merges into the
 * lowest-slot canonical stack already there (stash/loadout: only within capacity; overflow
 * entries are sources only), otherwise the lowest-slot legacy stack becomes the canonical
 * one and absorbs the other legacy stacks. Merged-away entries free their slot. Equipment
 * entries carrying an instance are never merged (one unit each): only their id is rewritten.
 * Two already-canonical stacks are never merged, and nothing merges across lists.
 * Quantity totals per canonical id are verified before/after; mismatch or unsafe sums abort
 * that character with no write.
 *
 * Idempotent: a character with no legacy ids is `unchanged`; a second --apply writes nothing.
 * Each write is one atomic $set + $inc revision, guarded by the read revision. Conflicts
 * re-read/re-plan, at most three retries per character.
 * Output: JSON lines (one record per character, then a summary).
 * Exit: 0 = no aborted characters, 1 = abort/database failure, 2 = invalid usage.
 *
 * Exports accept plain stored documents / a MongoDB collection; importing this module
 * does not connect, load .env, or run the migration.
 */

const { EQUIPMENT_SLOTS } = require('../src/config/equipmentSlots');
const { normalizeLootId } = require('../src/services/InventoryLootIdNormalizer');
const { CAPACITIES } = require('../src/services/InventoryEntries');

const isStackable = item => item.instance === undefined || item.instance === null;
const inCapacity = (item, capacity) => !Number.isSafeInteger(item.slot) || item.slot < capacity;
const lowestSlot = (items, candidates) => candidates.reduce((best, i) =>
  best === undefined || (items[i].slot ?? -1) < (items[best].slot ?? -1) ? i : best, undefined);

// BigInt keeps the before/after comparison independent of Number rounding.
function totals(items) {
  const result = new Map();
  for (const item of items) {
    const id = normalizeLootId(item.lootId);
    result.set(id, (result.get(id) || 0n) + BigInt(item.amount));
  }
  return result;
}

const reportTotals = values => Object.fromEntries([...values].map(([id, amount]) => [id, amount.toString()]));

function planList(items, capacity) {
  if (!Array.isArray(items)) return { error: 'INVALID_LIST' };
  if (items.some(item => !item || typeof item.lootId !== 'string' || !item.lootId ||
      !Number.isSafeInteger(item.amount) || item.amount < 1)) {
    return { error: 'INVALID_ENTRY' };
  }
  const legacy = items.map((item, i) => normalizeLootId(item.lootId) !== item.lootId ? i : -1).filter(i => i !== -1);
  if (!legacy.length) return { entries: items, changes: [] };

  const entries = structuredClone(items);
  const removed = new Set();
  const changes = [];
  for (const id of new Set(legacy.map(i => normalizeLootId(items[i].lootId)))) {
    const group = legacy.filter(i => normalizeLootId(items[i].lootId) === id);
    const stacks = group.filter(i => isStackable(items[i]));
    const canonical = items.map((item, i) => i).filter(i => items[i].lootId === id && isStackable(items[i]) &&
      inCapacity(items[i], capacity));
    const target = lowestSlot(items, canonical.length ? canonical : stacks.filter(i => inCapacity(items[i], capacity)));
    for (const i of group) {
      const merge = target !== undefined && isStackable(items[i]) && i !== target;
      if (merge) { entries[target].amount += items[i].amount; removed.add(i); }
      entries[i].lootId = id;
      changes.push({ from: items[i].lootId, to: id, amount: items[i].amount, slot: items[i].slot, merged: merge });
    }
  }
  if (entries.some(item => !Number.isSafeInteger(item.amount))) return { error: 'UNSAFE_AMOUNT' };
  const result = entries.filter((_, i) => !removed.has(i));
  const before = totals(items);
  const after = totals(result);
  const totalsCheck = before.size === after.size && [...before].every(([id, amount]) => after.get(id) === amount);
  if (!totalsCheck) return { error: 'TOTALS_MISMATCH' };
  return {
    entries: result, changes,
    report: { rewritten: legacy.length, merged: removed.size, entriesBefore: items.length, entriesAfter: result.length,
      totalsBefore: reportTotals(before), totalsAfter: reportTotals(after), totalsCheck }
  };
}

// Equipment slots hold '' / id strings (v1/v2) or null / {lootId, instance} (v3).
function planEquipment(equipment, prefix) {
  const set = {}; const changes = [];
  for (const slot of EQUIPMENT_SLOTS) {
    const value = equipment?.[slot];
    const lootId = typeof value === 'string' ? value : value && typeof value === 'object' ? value.lootId : undefined;
    if (typeof lootId !== 'string' || normalizeLootId(lootId) === lootId) continue;
    const to = normalizeLootId(lootId);
    set[`inventory.${prefix}.${slot}`] = typeof value === 'string' ? to : { ...structuredClone(value), lootId: to };
    changes.push({ field: `${prefix}.${slot}`, from: lootId, to });
  }
  return { set, changes };
}

function planLegacyItemIds(character) {
  const inventory = character.inventory || {};
  const reservation = inventory.pendingReservation;
  const sources = [
    ['stash', inventory.stash === undefined ? [] : inventory.stash, 'inventory.stash', CAPACITIES.stash],
    ['loadout', inventory.loadout === undefined ? [] : inventory.loadout, 'inventory.loadout', CAPACITIES.loadout]
  ];
  if (reservation != null) {
    sources.push(['pendingReservation.items', reservation.items === undefined ? [] : reservation.items,
      'inventory.pendingReservation.items', Infinity]);
  }
  const set = {}; const lists = {}; const changes = [];
  for (const [field, items, path, capacity] of sources) {
    const planned = planList(items, capacity);
    if (planned.error) return { status: 'aborted', reason: planned.error, field };
    if (!planned.changes.length) continue;
    set[path] = planned.entries; lists[field] = planned.report;
    changes.push(...planned.changes.map(change => ({ field, ...change })));
  }
  for (const [equipment, prefix] of [[inventory.preparedEquipment, 'preparedEquipment'],
    ...(reservation != null ? [[reservation.preparedEquipment, 'pendingReservation.preparedEquipment']] : [])]) {
    const planned = planEquipment(equipment, prefix);
    Object.assign(set, planned.set); changes.push(...planned.changes);
  }
  if (!changes.length) return { status: 'unchanged', changes: [], lists };
  return { status: 'planned', set, lists, changes };
}

async function migrateCharacter(collection, initial, { apply, maxRetries }) {
  let character = initial;
  let conflicts = 0;
  for (let attempt = 0; attempt <= maxRetries; attempt++) {
    const plan = planLegacyItemIds(character);
    const record = { characterId: String(character._id), status: plan.status,
      ...(plan.reason && { reason: plan.reason, field: plan.field }),
      ...(plan.changes?.length && { changes: plan.changes }), ...(plan.lists && Object.keys(plan.lists).length && { lists: plan.lists }) };
    if (plan.status !== 'planned') return { record, conflicts };
    if (character.revision !== undefined &&
        (!Number.isSafeInteger(character.revision) || character.revision < 0 ||
         character.revision === Number.MAX_SAFE_INTEGER)) {
      return { record: { ...record, status: 'aborted', reason: 'INVALID_REVISION' }, conflicts };
    }
    if (!apply) return { record, conflicts };
    const result = await collection.updateOne({
      _id: character._id,
      revision: character.revision === undefined ? { $exists: false } : character.revision
    }, { $set: plan.set, $inc: { revision: 1 } });
    if (result.matchedCount === 1) return { record: { ...record, status: 'migrated' }, conflicts };
    conflicts++;
    if (attempt === maxRetries) {
      return { record: { ...record, status: 'aborted', reason: 'REVISION_CONFLICT' }, conflicts };
    }
    character = await collection.findOne({ _id: initial._id });
    if (!character) {
      return { record: { characterId: String(initial._id), status: 'skipped', reason: 'CHARACTER_REMOVED' }, conflicts };
    }
  }
}

async function migrateLegacyItemIds(collection, {
  apply = false, maxRetries = 3, report = record => console.log(JSON.stringify(record))
} = {}) {
  if (!Number.isSafeInteger(maxRetries) || maxRetries < 0) throw new RangeError('Invalid maxRetries.');
  const summary = { type: 'summary', dryRun: !apply, scanned: 0, planned: 0, migrated: 0,
    unchanged: 0, skipped: 0, aborted: 0, conflicts: 0, exitCode: 0 };
  for await (const character of collection.find({})) {
    summary.scanned++;
    let outcome;
    try {
      outcome = await migrateCharacter(collection, character, { apply, maxRetries });
    } catch {
      // Do not print driver errors: they can contain connection credentials/data.
      outcome = { record: { characterId: String(character._id), status: 'aborted', reason: 'CHARACTER_ERROR' }, conflicts: 0 };
    }
    summary[outcome.record.status]++;
    summary.conflicts += outcome.conflicts;
    report(outcome.record);
  }
  summary.exitCode = summary.aborted ? 1 : 0;
  report(summary);
  return summary;
}

const USAGE = 'Usage: node scripts/migrate_legacy_item_ids.js [--apply] [--help]';

async function main(args = process.argv.slice(2)) {
  if (args.some(arg => arg !== '--apply' && arg !== '--help' && arg !== '--dry-run') ||
      (args.includes('--apply') && args.includes('--dry-run'))) {
    console.error(USAGE);
    return 2;
  }
  if (args.includes('--help')) {
    console.log(`${USAGE}\nMONGODB_URI via environment or .env. Dry-run is the default: --apply is required to write.\n` +
      'Back up the characters collection first. See the script header for the stacking rules.');
    return 0;
  }
  require('dotenv').config({ quiet: true });
  if (!process.env.MONGODB_URI) {
    console.error('Legacy item id migration failed: MONGODB_URI is required.');
    return 1;
  }
  const mongoose = require('mongoose');
  try {
    await mongoose.connect(process.env.MONGODB_URI);
    const summary = await migrateLegacyItemIds(mongoose.connection.db.collection('characters'), {
      apply: args.includes('--apply')
    });
    return summary.exitCode;
  } catch {
    console.error('Legacy item id migration failed. Check database availability and permissions.');
    return 1;
  } finally {
    await mongoose.disconnect();
  }
}

module.exports = { planLegacyItemIds, migrateLegacyItemIds, main };

if (require.main === module) {
  main().then(code => { process.exitCode = code; }).catch(() => {
    console.error('Legacy item id migration failed.');
    process.exitCode = 1;
  });
}
