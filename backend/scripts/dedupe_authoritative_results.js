'use strict';

/**
 * Explicit FIX-22 maintenance. Dry-run by default; --apply deletes only losing
 * AuthoritativeExtractionResult documents. Run with writers drained and a backup.
 * Importing does not connect, load .env, build indexes, or run maintenance.
 * Exit codes: 0 success/help, 1 database failure, 2 invalid usage.
 */
const Result = require('../src/models/AuthoritativeExtractionResult');

function parseArgs(args) {
  for (const arg of args) {
    if (!['--apply', '--dry-run', '--help'].includes(arg)) throw new Error(`Unknown argument: ${arg}`);
  }
  if (args.includes('--apply') && args.includes('--dry-run')) {
    throw new Error('Cannot combine --apply and --dry-run.');
  }
  return { apply: args.includes('--apply'), help: args.includes('--help') };
}

async function dedupeAuthoritativeResults(collection, { apply = false, log = console.log } = {}) {
  // Only the identity/priority fields are needed; never load or print reward payloads.
  const rows = await collection.find({}, { projection: {
    _id: 1, resultId: 1, raidId: 1, accountId: 1, consumed: 1, createdAt: 1
  } }).toArray();
  const resultGroups = new Map();
  const raidGroups = new Map();
  const raidKey = row => JSON.stringify([row.raidId, row.accountId]);
  for (const row of rows) {
    resultGroups.set(row.resultId, (resultGroups.get(row.resultId) || 0) + 1);
    const key = raidKey(row);
    raidGroups.set(key, (raidGroups.get(key) || 0) + 1);
  }
  // Resolve overlapping keys in one priority order. Consumed first, then oldest;
  // _id breaks equal/missing-date ties deterministically. A retained row claims
  // both keys, so no loser is counted twice and no unique key can remain duplicated.
  rows.sort((a, b) => Number(b.consumed === true) - Number(a.consumed === true) ||
    (new Date(a.createdAt || 0).getTime() - new Date(b.createdAt || 0).getTime()) ||
    String(a._id).localeCompare(String(b._id)));
  const resultIds = new Set();
  const raidAccounts = new Set();
  const losers = [];
  for (const row of rows) {
    const key = raidKey(row);
    if (resultIds.has(row.resultId) || raidAccounts.has(key)) losers.push(row._id);
    else {
      resultIds.add(row.resultId);
      raidAccounts.add(key);
    }
  }
  const summary = {
    dryRun: !apply,
    scanned: rows.length,
    duplicateResultIds: [...resultGroups.values()].filter(count => count > 1).length,
    duplicateRaidAccounts: [...raidGroups.values()].filter(count => count > 1).length,
    kept: rows.length - losers.length,
    plannedDeletes: losers.length,
    deleted: 0
  };
  if (apply && losers.length) {
    summary.deleted = (await collection.deleteMany({ _id: { $in: losers } })).deletedCount;
  }
  log(JSON.stringify(summary));
  return summary;
}

async function main() {
  let options;
  try {
    options = parseArgs(process.argv.slice(2));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 2;
    return;
  }
  if (options.help) {
    console.log('Usage: node scripts/dedupe_authoritative_results.js [--dry-run | --apply] [--help]');
    console.log('Default: dry-run. Keeps consumed results, otherwise oldest createdAt (_id breaks ties).');
    console.log('Drain writers and back up authoritativeextractionresults before --apply. Does not build indexes.');
    return;
  }
  require('dotenv').config({ quiet: true });
  const mongoose = require('mongoose');
  try {
    if (!process.env.MONGODB_URI) throw new Error('Missing database configuration');
    // Legacy duplicates must be cleaned before unique indexes can be built.
    await mongoose.connect(process.env.MONGODB_URI, { autoIndex: false, autoCreate: false });
    await dedupeAuthoritativeResults(Result.collection, options);
  } catch {
    // Never expose credentials, account identities, or raw driver errors.
    console.error('Authoritative result dedupe failed. Check database configuration and access.');
    process.exitCode = 1;
  } finally {
    await mongoose.disconnect();
  }
}

if (require.main === module) main();

module.exports = { dedupeAuthoritativeResults, parseArgs };
