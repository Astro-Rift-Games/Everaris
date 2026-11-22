#!/usr/bin/env node
'use strict';
// Dev-only testing tool (odd/tasks/dev-grant-tools.md): gives a LOCAL character upgrade materials, test gear,
// catalog items and gold without raiding. It writes straight to the local MongoDB and never modifies backend
// files: backend modules are required read-only so items get valid v3 instances and placement.
//
// Usage (from the repo root, backend stopped or running — the game picks changes up on its next sync):
//   node tools/dev-grant/grant.js --user tester_a --preset upgrade-materials
//   node tools/dev-grant/grant.js --user tester_a --preset upgrade-gear --gold 5000
//   node tools/dev-grant/grant.js --user tester_a --item RES_RUNE_FRAGMENT:10 --item rapier:1
const path = require('node:path');

const backendDir = path.resolve(__dirname, '../../backend');
const fromBackend = relative => require(path.join(backendDir, relative));

const MAX_ITEM_AMOUNT = 999;
const MAX_GOLD = 100000;
const MAX_ITEM_LINES = 50;
const LOCAL_HOSTS = new Set(['localhost', '127.0.0.1', '::1']);

const noScaling = () => ({ gemSocketCount: 0, gems: [] });
// Test material and gear for the Town NPC upgrade services (Doc 23 §9, TASK-303).
const PRESETS = Object.freeze({
  'upgrade-materials': ['RES_RUNE_FRAGMENT', 'RES_ETHER_DUST', 'RES_ATTUNEMENT_GEM', 'RES_BLACK_IRON',
    'RES_TANNED_LEATHER', 'RES_ANCIENT_ARMOR'].map(lootId => ({ lootId, amount: 20 })),
  'upgrade-gear': [
    { lootId: 'rapier', amount: 1, modifiers: noScaling() },
    { lootId: 'rapier', amount: 1, modifiers: { primary: { attribute: 'strength', grade: 'C' }, ...noScaling() } },
    { lootId: 'rapier', amount: 1, modifiers: { gemSocketCount: 1, gems: [null] } },
    { lootId: 'magic_wand', amount: 1, modifiers: noScaling() },
    { lootId: 'heavy_armor_breastplate', amount: 1 },
    { lootId: 'heavy_armor_breastplate', amount: 1, modifiers: { upgradeLevel: 3 } }
  ]
});

function fail(code, message) {
  const error = new Error(message);
  error.code = code;
  throw error;
}
const invalid = message => fail('VALIDATION_FAILED', message);

/** True only for mongodb:// URIs whose every host is localhost, 127.0.0.1 or ::1 (never mongodb+srv). */
function isLocalMongoUri(uri) {
  if (typeof uri !== 'string' || !uri.startsWith('mongodb://')) return false;
  let authority = uri.slice('mongodb://'.length).split(/[/?]/, 1)[0];
  authority = authority.slice(authority.lastIndexOf('@') + 1);
  if (!authority) return false;
  return authority.split(',').every(hostPort => {
    const host = hostPort.startsWith('[') ? hostPort.slice(1, hostPort.indexOf(']')) : hostPort.split(':')[0];
    return LOCAL_HOSTS.has(host.toLowerCase());
  });
}

function parseArgs(argv) {
  const request = { username: undefined, preset: undefined, items: [], gold: 0 };
  for (let i = 0; i < argv.length; i++) {
    const option = argv[i];
    const value = argv[++i];
    if (value === undefined) invalid(`Missing value for ${option}.`);
    if (option === '--user') request.username = value;
    else if (option === '--character') {
      if (!/^[0-9a-f]{24}$/.test(value)) invalid('--character expects a 24-hex character id.');
      request.characterId = value;
    }
    else if (option === '--preset') request.preset = value;
    else if (option === '--gold') request.gold = Number(value);
    else if (option === '--item') {
      const match = /^([^:]+):(\d+)$/.exec(value);
      if (!match) invalid(`--item expects lootId:amount, got '${value}'.`);
      request.items.push({ lootId: match[1], amount: Number(match[2]) });
    } else invalid(`Unknown option '${option}'. Use --user or --character, --preset, --item lootId:amount, --gold.`);
  }
  if (request.username && request.characterId) invalid('Use either --user or --character, not both.');
  if (!request.username && !request.characterId) invalid('--user <username> is required (or --character <id>).');
  return request;
}

function requestedLines({ preset, items = [] }) {
  const { isKnownLootId } = fromBackend('src/config/itemCatalog');
  const { normalizeLootId } = fromBackend('src/services/InventoryLootIdNormalizer');
  const lines = [];
  if (preset !== undefined) {
    if (!Object.hasOwn(PRESETS, preset)) invalid(`Unknown preset. Use one of: ${Object.keys(PRESETS).join(', ')}.`);
    for (const line of PRESETS[preset]) lines.push(structuredClone(line));
  }
  if (!Array.isArray(items) || items.length > MAX_ITEM_LINES) invalid(`At most ${MAX_ITEM_LINES} --item entries.`);
  for (const item of items) {
    const lootId = normalizeLootId(String(item.lootId));
    if (!isKnownLootId(lootId)) invalid(`'${item.lootId}' is not a catalog lootId.`);
    if (!Number.isSafeInteger(item.amount) || item.amount < 1 || item.amount > MAX_ITEM_AMOUNT) {
      invalid(`amount must be an integer between 1 and ${MAX_ITEM_AMOUNT}.`);
    }
    lines.push({ lootId, amount: item.amount });
  }
  return lines;
}

/**
 * Grants presets, catalog items and gold to the character of `username` in one revision-guarded write.
 * Everything is validated and placed in the Inventory (loadout) first; on any error nothing is written.
 */
async function grant({ username, characterId, preset, items = [], gold = 0 }) {
  if (!Number.isSafeInteger(gold) || gold < 0 || gold > MAX_GOLD) invalid(`gold must be an integer between 0 and ${MAX_GOLD}.`);
  const lines = requestedLines({ preset, items });
  if (lines.length === 0 && gold === 0) invalid('Nothing to grant: use --preset, --item or --gold.');

  const Account = fromBackend('src/models/Account');
  const Character = fromBackend('src/models/Character');
  const { inventoryVersion, serializeItems, receiveItem } = fromBackend('src/services/InventoryEntries');
  const { validInventory } = fromBackend('src/services/ItemInstances');

  let character;
  if (characterId) {
    character = await Character.findById(characterId);
    if (!character) fail('CHARACTER_NOT_FOUND', `No character '${characterId}'.`);
  } else {
    const account = await Account.findOne({ username });
    if (!account) fail('ACCOUNT_NOT_FOUND', `No account '${username}'.`);
    character = await Character.findOne({ accountId: account._id });
    if (!character) fail('CHARACTER_NOT_FOUND', `Account '${username}' has no character.`);
  }
  if (inventoryVersion(character) !== 3) fail('INVENTORY_MIGRATION_REQUIRED', 'The character needs inventory schema v3.');
  if (character.inventory.pendingReservation) fail('RESERVATION_PENDING', 'A raid reservation is pending; finish or refund it first.');
  if (!validInventory(character.inventory)) fail('INVALID_INVENTORY', 'The stored inventory is invalid; refusing to write it back.');

  const loadout = serializeItems(character.inventory.loadout, true);
  try {
    for (const line of lines) receiveItem(loadout, 'loadout', line, 3);
  } catch (error) {
    fail(error.errorCode || 'INVALID_INVENTORY', error.message || 'The items do not fit.');
  }
  const currency = (character.inventory.currency || 0) + gold;
  if (!Number.isSafeInteger(currency)) fail('INTEGER_OVERFLOW', 'Gold would exceed the safe integer range.');

  // The revision bump makes the running game reconcile instead of overwriting the grant.
  const updated = await Character.findOneAndUpdate(
    { _id: character._id, revision: character.revision, 'inventory.pendingReservation': null },
    { $set: { 'inventory.loadout': loadout, 'inventory.currency': currency }, $inc: { revision: 1 } },
    { returnDocument: 'after' });
  if (!updated) fail('REVISION_CONFLICT', 'The character changed while granting; run the command again.');
  return { revision: updated.revision, currency: updated.inventory.currency, granted: lines.map(({ lootId, amount }) => ({ lootId, amount })), gold };
}

async function main(argv) {
  const request = parseArgs(argv);
  // Load the local backend configuration without printing it; the script only uses MONGODB_URI.
  require(require.resolve('dotenv', { paths: [backendDir] })).config({ path: path.join(backendDir, '.env'), quiet: true });
  const uri = process.env.MONGODB_URI;
  if (!isLocalMongoUri(uri)) fail('NOT_LOCAL', 'MONGODB_URI is not a local mongodb:// URI (localhost, 127.0.0.1, ::1). Refusing to write.');
  const mongoose = require(require.resolve('mongoose', { paths: [backendDir] }));
  await mongoose.connect(uri);
  try {
    const result = await grant(request);
    console.log(`[dev-grant] OK revision ${result.revision}, gold ${result.currency}, granted ${JSON.stringify(result.granted)}`);
    console.log('[dev-grant] Re-enter the Town (or relaunch) so the game syncs the new Inventory.');
  } finally {
    await mongoose.disconnect();
  }
}

if (require.main === module) {
  main(process.argv.slice(2)).catch(error => {
    console.error(`[dev-grant] ${error.code || 'ERROR'}: ${error.message}`);
    process.exit(1);
  });
}

module.exports = { PRESETS, isLocalMongoUri, parseArgs, grant };
