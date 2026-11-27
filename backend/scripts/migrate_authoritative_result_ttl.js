'use strict';

/**
 * Explicit FIX-21 TTL index migration for AuthoritativeExtractionResult.
 * Dry-run by default; --apply drops the legacy unconditional createdAt TTL index,
 * backfills consumedAt on legacy consumed rows where missing, and creates the
 * consumed-only partial TTL index on consumedAt (24h after consumption).
 *
 * Run only with explicit authorization on deployed environments.
 * Exit codes: 0 success/help, 1 database failure, 2 invalid usage.
 */
const Result = require('../src/models/AuthoritativeExtractionResult');

const CONSUMED_RESULT_TTL_SECONDS = Result.CONSUMED_RESULT_TTL_SECONDS || 86400;

function parseArgs(args) {
  for (const arg of args) {
    if (!['--apply', '--dry-run', '--help'].includes(arg)) throw new Error(`Unknown argument: ${arg}`);
  }
  if (args.includes('--apply') && args.includes('--dry-run')) {
    throw new Error('Cannot combine --apply and --dry-run.');
  }
  return { apply: args.includes('--apply'), help: args.includes('--help') };
}

async function migrateAuthoritativeResultTtl(collection, { apply = false, log = console.log } = {}) {
  const indexes = await collection.indexes();

  const legacyCreatedAtTtl = indexes.find(
    index => index.key && index.key.createdAt === 1 && typeof index.expireAfterSeconds === 'number'
  );
  const consumedPartialTtl = indexes.find(
    index => index.key && index.key.consumedAt === 1 &&
      index.expireAfterSeconds === CONSUMED_RESULT_TTL_SECONDS &&
      index.partialFilterExpression && index.partialFilterExpression.consumed === true
  );
  const conflictingConsumedAtIndex = !consumedPartialTtl
    ? indexes.find(index => index.key && index.key.consumedAt === 1)
    : null;

  const missingConsumedAtFilter = { consumed: true, consumedAt: null };
  const consumedMissingConsumedAt = await collection.countDocuments(missingConsumedAtFilter);

  const summary = {
    dryRun: !apply,
    legacyCreatedAtTtlFound: Boolean(legacyCreatedAtTtl),
    legacyCreatedAtTtlDropped: false,
    conflictingConsumedAtIndexDropped: false,
    consumedAtTtlPresent: Boolean(consumedPartialTtl),
    consumedAtTtlCreated: false,
    consumedMissingConsumedAt,
    backfilledConsumedAt: 0
  };

  if (apply) {
    if (legacyCreatedAtTtl) {
      await collection.dropIndex(legacyCreatedAtTtl.name);
      summary.legacyCreatedAtTtlDropped = true;
    }

    if (conflictingConsumedAtIndex) {
      await collection.dropIndex(conflictingConsumedAtIndex.name);
      summary.conflictingConsumedAtIndexDropped = true;
    }

    if (consumedMissingConsumedAt > 0) {
      const now = new Date();
      // Only update already-consumed rows missing consumedAt; never touch unconsumed results.
      const cursor = collection.find(missingConsumedAtFilter, { projection: { _id: 1, createdAt: 1 } });
      const rows = await cursor.toArray();
      let updated = 0;
      for (const row of rows) {
        const res = await collection.updateOne(
          { _id: row._id, consumed: true, consumedAt: null },
          { $set: { consumedAt: row.createdAt instanceof Date ? row.createdAt : now } }
        );
        updated += res.modifiedCount;
      }
      summary.backfilledConsumedAt = updated;
    }

    if (!consumedPartialTtl) {
      await collection.createIndex(
        { consumedAt: 1 },
        {
          expireAfterSeconds: CONSUMED_RESULT_TTL_SECONDS,
          partialFilterExpression: { consumed: true }
        }
      );
      summary.consumedAtTtlCreated = true;
      summary.consumedAtTtlPresent = true;
    }
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
    console.log('Usage: node scripts/migrate_authoritative_result_ttl.js [--dry-run | --apply] [--help]');
    console.log('Default: dry-run. Drops legacy unconditional createdAt TTL, backfills missing consumedAt');
    console.log('on consumed documents, and builds consumed-only partial TTL index on consumedAt (24h).');
    return;
  }

  require('dotenv').config({ quiet: true });
  const mongoose = require('mongoose');
  try {
    if (!process.env.MONGODB_URI) throw new Error('Missing database configuration');
    await mongoose.connect(process.env.MONGODB_URI, { autoIndex: false, autoCreate: false });
    await migrateAuthoritativeResultTtl(Result.collection, options);
  } catch {
    console.error('Authoritative result TTL migration failed. Check database configuration and access.');
    process.exitCode = 1;
  } finally {
    await mongoose.disconnect();
  }
}

if (require.main === module) main();

module.exports = {
  CONSUMED_RESULT_TTL_SECONDS,
  migrateAuthoritativeResultTtl,
  parseArgs
};
