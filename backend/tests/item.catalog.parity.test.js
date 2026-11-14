'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { isKnownLootId } = require('../src/config/itemCatalog');

const definitions = path.resolve(__dirname, '../../Project Grimhold/Assets/Scriptable Objects/Loot/Definitions');

function assetFiles(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const file = path.join(directory, entry.name);
    return entry.isDirectory() ? assetFiles(file) : entry.name.endsWith('.asset') ? [file] : [];
  });
}

// FIX-18: published results are validated against the backend registry, so every
// item Unity can drop must be known to it, or legitimate extractions are rejected.
test('the backend item registry knows every Unity LootDefinition id', t => {
  if (!fs.existsSync(definitions)) {
    t.skip('Unity LootDefinition folder is absent (backend deployed alone); registry parity cannot be checked.');
    return;
  }

  const unityIds = assetFiles(definitions)
    .map(file => fs.readFileSync(file, 'utf8').match(/^\s*_id:\s*(\S+)\s*$/m)?.[1])
    .filter(Boolean);

  assert.ok(unityIds.length > 0, 'Expected at least one Unity loot id');
  assert.deepEqual(unityIds.filter(id => !isKnownLootId(id)), []);
});

test('the backend item registry rejects identifiers that name no item', () => {
  for (const lootId of ['forged_dragon_scale', '', '  ', 'ARMING_SWORD', null, undefined, 42]) {
    assert.equal(isKnownLootId(lootId), false, String(lootId));
  }
  assert.equal(isKnownLootId('longsword'), true, 'legacy aliases resolve to their canonical item');
});
