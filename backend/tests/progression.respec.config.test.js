'use strict';

// TASK-304 S4: the respec cost is adjustable through configuration without code changes.
const test = require('node:test');
const assert = require('node:assert/strict');
const { resolveRespecCost, DEFAULT_RESPEC_GOLD_COST, RESPEC_GOLD_COST } = require('../src/config/respecConfig');
const commerceConfig = require('../src/config/commerceConfig.json');

test('respec cost is read from commerceConfig.services.respecGoldCost', () => {
  assert.equal(resolveRespecCost({ services: { respecGoldCost: 250 } }), 250);
  assert.equal(resolveRespecCost({ services: { respecGoldCost: 0 } }), 0);
  assert.equal(RESPEC_GOLD_COST, commerceConfig.services.respecGoldCost);
});

test('respec cost falls back to the provisional default of 100 when not configured', () => {
  assert.equal(DEFAULT_RESPEC_GOLD_COST, 100);
  assert.equal(resolveRespecCost(null), 100);
  assert.equal(resolveRespecCost({ services: {} }), 100);
});

test('an invalid configured respec cost fails loudly instead of charging a wrong amount', () => {
  for (const invalid of [-1, 1.5, '100', Number.NaN]) {
    assert.throws(() => resolveRespecCost({ services: { respecGoldCost: invalid } }), /respecGoldCost/);
  }
});
