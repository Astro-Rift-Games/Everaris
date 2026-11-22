// src/config/respecConfig.js
//
// TASK-304: attribute respec gold cost (Doc 22 §6 sink, Doc 22 §13 balanceable value).
// The value lives in commerceConfig.json (services.respecGoldCost) so balance can change it
// without touching the respec rule or the Town flow.
'use strict';

const DEFAULT_RESPEC_GOLD_COST = 100;

/** Resolves the respec cost from a commerce config object; throws on an invalid configured value. */
function resolveRespecCost(config) {
  const configured = config?.services?.respecGoldCost;
  if (configured === undefined || configured === null) return DEFAULT_RESPEC_GOLD_COST;
  if (!Number.isSafeInteger(configured) || configured < 0) {
    throw new Error(`commerceConfig.services.respecGoldCost must be a non-negative integer, got ${JSON.stringify(configured)}.`);
  }
  return configured;
}

let commerceConfig = null;
try {
  commerceConfig = require('./commerceConfig.json');
} catch {
  commerceConfig = null;
}

const RESPEC_GOLD_COST = resolveRespecCost(commerceConfig);

module.exports = { DEFAULT_RESPEC_GOLD_COST, RESPEC_GOLD_COST, resolveRespecCost };
