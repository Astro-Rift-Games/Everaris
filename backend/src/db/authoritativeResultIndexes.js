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
    if (!missing.length) return true;
    logger.error(`[DB] AuthoritativeExtractionResult missing unique indexes: ${missing.map(key => JSON.stringify(key)).join(', ')}. ` +
      'Drain writers, back up results, run scripts/dedupe_authoritative_results.js (dry-run, then --apply), and rebuild the required indexes; publication uniqueness is NOT guaranteed.');
  } catch {
    logger.error('[DB] AuthoritativeExtractionResult unique index verification failed; publication uniqueness is NOT guaranteed. Check database access and scripts/dedupe_authoritative_results.js.');
  }
  return false;
}

module.exports = { checkAuthoritativeResultIndexes };
