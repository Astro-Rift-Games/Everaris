'use strict';

const Result = require('../models/AuthoritativeExtractionResult');

// A failed index build (legacy duplicates or a same-name non-unique index) must
// be visible without taking down the server. Never print raw DB errors/keys.
async function checkAuthoritativeResultIndexes(model = Result, logger = console) {
  try {
    await model.createIndexes();
  } catch {
    // Inspect the actual indexes even if the build failed partway through.
  }
  try {
    const indexes = await model.collection.indexes();
    const required = [{ resultId: 1 }, { raidId: 1, accountId: 1 }];
    const missing = required.filter(key => !indexes.some(index =>
      index.unique === true && !index.sparse && !index.partialFilterExpression &&
      JSON.stringify(index.key) === JSON.stringify(key)));

    const hasLegacyCreatedAtTtl = indexes.some(index =>
      index.key && index.key.createdAt === 1 && typeof index.expireAfterSeconds === 'number');
    const hasConsumedOnlyTtl = indexes.some(index =>
      index.key && index.key.consumedAt === 1 &&
      typeof index.expireAfterSeconds === 'number' &&
      index.partialFilterExpression && index.partialFilterExpression.consumed === true);

    let healthy = true;
    if (missing.length) {
      healthy = false;
      logger.error(`[DB] AuthoritativeExtractionResult missing unique indexes: ${missing.map(key => JSON.stringify(key)).join(', ')}. ` +
        'Drain writers, back up results, run scripts/dedupe_authoritative_results.js (dry-run, then --apply), and rebuild the required indexes; publication uniqueness is NOT guaranteed.');
    }
    if (hasLegacyCreatedAtTtl || !hasConsumedOnlyTtl) {
      healthy = false;
      const issues = [];
      if (hasLegacyCreatedAtTtl) issues.push('legacy unconditional createdAt TTL index still present');
      if (!hasConsumedOnlyTtl) issues.push('missing consumed-only partial TTL index on consumedAt');
      logger.error(`[DB] AuthoritativeExtractionResult TTL index mismatch (${issues.join('; ')}). ` +
        'Run scripts/migrate_authoritative_result_ttl.js (dry-run, then --apply with explicit authorization) so unconsumed extraction results never expire.');
    }
    return healthy;
  } catch {
    logger.error('[DB] AuthoritativeExtractionResult unique index verification failed; publication uniqueness is NOT guaranteed. Check database access and scripts/dedupe_authoritative_results.js.');
  }
  return false;
}

module.exports = { checkAuthoritativeResultIndexes };
