// src/services/InventoryService.js
const Character = require('../models/Character');
const AuthoritativeExtractionResult = require('../models/AuthoritativeExtractionResult');
const env = require('../config/env');
const { randomUUID } = require('crypto');
const { EQUIPMENT_SLOTS } = require('../config/equipmentSlots');
const {
  normalizeItems,
  normalizeLootId,
  normalizePreparedEquipment
} = require('./InventoryLootIdNormalizer');
const {
  MAX_TRANSACTION_AMOUNT,
  BUYBACK_MAX_ENTRIES,
  BUYBACK_PRICE_MULTIPLIER,
  isBuybackEligible,
  getEquipmentTemplate,
  GEM_CATALOG,
  getBuyOffer,
  getSellOffer
} = require('../config/shopCatalog');

const { inventoryVersion, copyEntry, serializeItems, requireMigratedInventory,
  requireSlotInventory, requireSplitEnabled, requireSourceSlot, assertUnambiguousItems, matchesLootId,
  CAPACITIES, inventoryFull, assertSlot, lowestItemIndex, receiveItem, restoreEntries, addAmount,
  inventoryItems, serializePreparedEquipment } = require('./InventoryEntries');

const { TRAINING_WEAPONS, isTrainingWeapon } = require('./TrainingWeapons');
const { validatePreparedAbilities } = require('./PreparedAbilityValidation');
const {
  assertSlotCompatible,
  assertPreparedEquipmentCategories,
  requirePreparedWeapon
} = require('./PreparedEquipmentValidation');
const { receiptPush, watermarkUpdate, assertWithinRetention } = require('./ShopReceiptRetention');
const EconomyTelemetry = require('./EconomyTelemetry');
const { pendingLootResultFilter } = require('./ResultReservationBinding');
const { getWeeklyRotation, isAllowedByMerchant, getMerchantOffers } = require('./CommerceRotationService');

const { INSTANCE_ID, invalidInventory, createEquipment, validateResultItems, resultEquipmentToInstances,
  equipmentToResult } = require('./ItemInstances');
const { UPGRADE_OPERATIONS, planUpgrade, objectValue } = require('./ItemUpgrades');

const UPGRADE_MERCHANTS = Object.freeze(['general', 'blacksmith', 'arcanist']);

function listsSnapshot(character) {
  return { stash: inventoryItems(character, 'stash'), loadout: inventoryItems(character, 'loadout'), revision: character.revision };
}

async function writeSlotLists(character, expectedRevision, lists, blockReservation = false, receipt = null) {
  const updated = await Character.findOneAndUpdate(
    { accountId: character.accountId, revision: expectedRevision,
      ...(blockReservation ? { 'inventory.pendingReservation': null } : {}) },
    { $set: Object.fromEntries(Object.entries(lists).map(([list, items]) => [`inventory.${list}`, items])), $inc: { revision: 1 },
      ...(receipt ? { $push: { appliedShopReceipts: receiptPush(receipt) }, ...watermarkUpdate(character) } : {}) },
    { returnDocument: 'after' }
  );
  if (!updated) throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict during inventory update.' };
  return listsSnapshot(updated);
}

// Pure per-stack rule shared by single and bulk transfers; mutates only the lists it is given.
function moveStack(from, to, fromList, toList, version, lootId, amount, sourceSlot) {
  const index = from.findIndex(item => item.slot === sourceSlot && matchesLootId(item, lootId));
  if (index === -1 || from[index].amount < amount) {
    throw { statusCode: 409, errorCode: fromList === 'stash' ? 'INSUFFICIENT_STASH_ITEMS' : 'INSUFFICIENT_LOADOUT_ITEMS',
      message: `${fromList === 'stash' ? 'Stash' : 'Loadout'} does not hold ${amount} unit(s) of '${normalizeLootId(lootId)}'.` };
  }
  receiveItem(to, toList, { ...copyEntry(from[index]), amount }, version);
  if (from[index].amount === amount) from.splice(index, 1);
  else from[index].amount -= amount;
}

// While a reservation is pending it owns Inventory and Equipment; callers also filter the write on it.
function rejectPendingReservation(character, message) {
  if (character.inventory.pendingReservation) throw { statusCode: 409, errorCode: 'RESERVATION_PENDING', message };
}

async function transferSlotItems(character, fromList, toList, lootId, amount, expectedRevision, sourceSlot) {
  if (character.revision !== expectedRevision) throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict.' };
  rejectPendingReservation(character, 'Cannot transfer items while a reservation is pending.');
  if (!Number.isSafeInteger(amount) || amount < 1) throw { statusCode: 400, errorCode: 'VALIDATION_FAILED', message: 'Amount must be a positive integer.' };
  const from = inventoryItems(character, fromList);
  const to = inventoryItems(character, toList);
  moveStack(from, to, fromList, toList, inventoryVersion(character), lootId, amount, sourceSlot);
  return writeSlotLists(character, expectedRevision, { [fromList]: from, [toList]: to }, true);
}

const TRANSFER_DIRECTIONS = { toLoadout: ['stash', 'loadout'], toStash: ['loadout', 'stash'] };

// Moves every source stack in slot order under the single-transfer rules; refused stacks are skipped.
async function transferAllSlotItems(character, direction, expectedRevision) {
  const lists = TRANSFER_DIRECTIONS[Object.hasOwn(TRANSFER_DIRECTIONS, direction) ? direction : undefined];
  if (!lists) throw { statusCode: 400, errorCode: 'VALIDATION_FAILED', message: 'Direction must be toLoadout or toStash.' };
  requireSlotInventory(character);
  if (character.revision !== expectedRevision) throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict.' };
  if (character.inventory.pendingReservation) {
    throw { statusCode: 409, errorCode: 'RESERVATION_PENDING', message: 'Cannot transfer all while a reservation is pending.' };
  }
  const [fromList, toList] = lists;
  const version = inventoryVersion(character);
  let from = inventoryItems(character, fromList);
  let to = inventoryItems(character, toList);
  const results = [];
  for (const { slot, lootId, amount } of structuredClone(from)) {
    // Attempt on copies so a refused stack leaves no partial mutation behind.
    const nextFrom = structuredClone(from);
    const nextTo = structuredClone(to);
    try {
      moveStack(nextFrom, nextTo, fromList, toList, version, lootId, amount, slot);
      from = nextFrom; to = nextTo;
      results.push({ slot, lootId, amount, moved: true });
    } catch (err) {
      if (!err?.errorCode) throw err;
      results.push({ slot, lootId, amount, moved: false, errorCode: err.errorCode });
    }
  }
  const snapshot = results.some(result => result.moved)
    ? await writeSlotLists(character, expectedRevision, { [fromList]: from, [toList]: to }, true)
    : listsSnapshot(character);
  return { ...snapshot, results };
}

function restoreReservation(character) {
  const reservation = character.inventory.pendingReservation;
  const version = inventoryVersion(character);
  const equipment = serializePreparedEquipment(character.inventory.preparedEquipment, version);
  const displaced = [];
  for (const slot of EQUIPMENT_SLOTS) {
    const item = reservation.preparedEquipment?.[slot];
    if (!item) continue;
    if (!equipment[slot]) equipment[slot] = item;
    else displaced.push(version === 3 ? { ...copyEntry(item), amount: 1 } : { lootId: item, amount: 1 });
  }
  if (version >= 2) {
    const loadout = restoreEntries(character.inventory.loadout, reservation.items, 'loadout');
    character.inventory.loadout = restoreEntries(loadout, displaced, 'loadout');
  } else {
    // Legacy lists hold one aggregated entry per lootId and no capacity, so restoration merges.
    const loadout = serializeItems(character.inventory.loadout);
    for (const item of [...serializeItems(reservation.items), ...displaced]) receiveItem(loadout, 'loadout', item, version);
    character.inventory.loadout = loadout;
  }
  character.inventory.preparedEquipment = equipment;
}

/**
 * Serializes the pendingReservation subdocument to a plain object, or null.
 */
function serializePendingReservation(res, sorted = false, version = 2) {
  if (!res || !res.reservationId) return null;
  return {
    reservationId:    res.reservationId,
    admittedAt:       res.admittedAt ? res.admittedAt.toISOString() : null,
    startedAt:        res.startedAt ? res.startedAt.toISOString() : null,
    items:            serializeItems(res.items, sorted),
    preparedEquipment: serializePreparedEquipment(res.preparedEquipment, version)
  };
}

// Authoritative results target the owning account, not a reservation/raid identifier.
// Protect both backpack and equipped ownership; only empty ownership is defeat-compatible.
// Empty results must not deadlock reservation loss clears before a defeat XP commit.
async function assertNoPendingExtractionResult(accountId) {
  if (await AuthoritativeExtractionResult.exists({
    accountId,
    consumed: { $ne: true },
    ...pendingLootResultFilter()
  })) {
    throw { statusCode: 409, errorCode: 'EXTRACTION_RESULT_PENDING', message: 'An authoritative extraction result is pending for this character.' };
  }
}

// FIX-95: the raid is started once any reservation bound to it carries the Host's start marker.
async function isRaidMarkedStarted(raidId) {
  if (!raidId) return false;
  return !!(await Character.exists({
    'inventory.pendingReservation.raidId': raidId,
    'inventory.pendingReservation.startedAt': { $ne: null }
  }));
}

// soldAt and the internal epoch are deliberately not exposed to clients.
function serializeBuyback(inventory) {
  return { entries: (inventory.buyback?.entries || []).map(({ entryId, lootId, amount, unitPrice }) => ({ entryId, lootId, amount, unitPrice })) };
}

function buybackEpochFilter(inventory) {
  return inventory.buyback?.sessionEpoch || { $in: ['', null] };
}

function shopSnapshot(character) {
  return {
    revision: character.revision,
    currency: character.inventory.currency || 0,
    stash: inventoryItems(character, 'stash'),
    loadout: inventoryItems(character, 'loadout'),
    preparedEquipment: serializePreparedEquipment(character.inventory.preparedEquipment, inventoryVersion(character)),
    buyback: serializeBuyback(character.inventory)
  };
}

// Resolve an existing entry by identity; client modifiers are never accepted by equip.
async function updateInstanceEquipment(character, requested, expectedRevision) {
  const stash = inventoryItems(character, 'stash'); const loadout = inventoryItems(character, 'loadout');
  const old = serializePreparedEquipment(character.inventory.preparedEquipment, 3);
  const available = new Map(Object.values(old).filter(Boolean).map(item => [item.instance.instanceId, item]));
  const selected = new Set(); const equipment = {};
  for (const slot of EQUIPMENT_SLOTS) {
    const intent = requested[slot];
    if (intent == null || intent === '') { equipment[slot] = null; continue; }
    if (typeof intent !== 'object' || typeof intent.lootId !== 'string' || !INSTANCE_ID.test(intent.instanceId || '') ||
        Object.keys(intent).some(key => !['lootId', 'instanceId'].includes(key)) || selected.has(intent.instanceId)) invalidInventory();
    assertSlotCompatible(slot, intent.lootId);
    let item = available.get(intent.instanceId);
    if (!item) {
      const list = [loadout, stash].find(list => list.some(entry => entry.instance?.instanceId === intent.instanceId));
      const index = list?.findIndex(entry => entry.instance?.instanceId === intent.instanceId);
      if (list) { const entry = list.splice(index, 1)[0]; item = { lootId: entry.lootId, instance: copyEntry(entry.instance) }; }
    }
    if (!item) {
      throw { statusCode: 422, errorCode: 'ITEM_NOT_FOUND', message: 'The equipment instance is not present in the loadout, stash or prepared equipment.' };
    }
    if (!matchesLootId(item, intent.lootId)) {
      throw { statusCode: 422, errorCode: 'ITEM_NOT_FOUND', message: 'The equipment instance is not present in the loadout, stash or prepared equipment.' };
    }
    selected.add(intent.instanceId); equipment[slot] = copyEntry(item);
  }
  for (const item of available.values()) {
    if (!selected.has(item.instance.instanceId)) {
      receiveItem(loadout, 'loadout', { ...copyEntry(item), amount: 1 }, 3);
    }
  }
  const snapshot = await writeSlotLists(character, expectedRevision, { stash, loadout, preparedEquipment: equipment }, true);
  return { preparedEquipment: equipment, ...snapshot };
}

// Plan a sale on an in-memory candidate. No persisted state changes until the caller commits.
function applyShopSale(loadout, version, lootId, amount, sourceSlot, weeklyDemandItems = []) {
  const sellOffer = isTrainingWeapon(lootId) ? null :
    version === 3 && getEquipmentTemplate(lootId) ? getEquipmentTemplate(lootId) : getSellOffer(lootId);
  if (!sellOffer) throw { statusCode: 404, errorCode: 'ITEM_NOT_SELLABLE', message: `Item '${lootId}' cannot be sold to the shop.` };
  const index = loadout.findIndex(i => matchesLootId(i, lootId) && (version === 1 || i.slot === sourceSlot));
  const source = loadout[index];
  if (!source || source.amount < amount) throw { statusCode: 409, errorCode: 'INSUFFICIENT_ITEMS', message: `Loadout does not hold ${amount} unit(s) of '${lootId}'.` };

  const isDemand = (weeklyDemandItems || []).some(d => matchesLootId({ lootId: d.lootId || d }, lootId));
  // v3 equipment sells for its ObjectValue (template value + upgrade bonuses, Doc 23 §9.3); other items for the catalog value.
  const listedValue = version === 3 && getEquipmentTemplate(lootId) ? objectValue(lootId, source.instance) : sellOffer.sellValue;
  const baseSellValue = isDemand ? Math.floor(listedValue * 1.2) : listedValue;

  const unitSellValue = baseSellValue + (version === 3 ? (source?.instance?.modifiers?.gems || [])
    .reduce((sum, gem) => sum + (gem ? GEM_CATALOG[gem].sellValue : 0), 0) : 0);
  const revenue = unitSellValue * amount;
  if (!Number.isSafeInteger(revenue)) throw { statusCode: 400, errorCode: 'INTEGER_OVERFLOW', message: 'Calculated transaction value causes integer overflow.' };
  if (source.amount === amount) loadout.splice(index, 1);
  else source.amount -= amount;
  const sale = { lootId, amount, revenue, demandBonus: isDemand, demandBonusGold: (baseSellValue - listedValue) * amount };
  return { revenue, sale, buybackEntry: isBuybackEligible(lootId, version) ? {
    entryId: randomUUID(), lootId, amount, unitPrice: Math.ceil(unitSellValue * BUYBACK_PRICE_MULTIPLIER),
    ...(version === 3 && source?.instance ? { instance: copyEntry(source.instance) } : {}),
    soldAt: new Date()
  } : null };
}

// Removes each material from Inventory stacks in slot order; Stash is never read (Doc 23 §9).
function consumeLoadoutMaterials(loadout, materials) {
  for (const [lootId, amount] of Object.entries(materials)) {
    const stacks = loadout.filter(item => matchesLootId(item, lootId)).sort((a, b) => a.slot - b.slot);
    if (stacks.reduce((sum, item) => sum + item.amount, 0) < amount) {
      throw { statusCode: 409, errorCode: 'INSUFFICIENT_MATERIALS', message: `Inventory does not hold ${amount} unit(s) of '${lootId}'.` };
    }
    let remaining = amount;
    for (const item of stacks) {
      const taken = Math.min(item.amount, remaining);
      item.amount -= taken; remaining -= taken;
      if (item.amount === 0) loadout.splice(loadout.indexOf(item), 1);
      if (remaining === 0) break;
    }
  }
}

// Replay of an applied upgrade: the recorded outcome for the same intent, null when the id is unused.
function recordedUpgrade(character, transactionId, intent) {
  const receipt = (character.appliedShopReceipts || []).find(entry => entry.transactionId === transactionId);
  if (!receipt) return null;
  if (receipt.type !== 'upgrade' || JSON.stringify(receipt.intent) !== JSON.stringify(intent)) {
    throw { statusCode: 409, errorCode: 'TRANSACTION_ID_REUSED', message: 'Transaction ID was previously applied with a different operation or parameters.' };
  }
  return { ...shopSnapshot(character), upgrade: copyEntry(receipt.outcome), alreadySecured: true };
}

function validUpgradeTicket({ transactionId, expectedRevision, merchant, operation, instanceId, location, slot }) {
  return typeof transactionId === 'string' && transactionId.trim().length > 0 && transactionId.length <= 128 &&
    Number.isSafeInteger(expectedRevision) && expectedRevision >= 0 && UPGRADE_MERCHANTS.includes(merchant) &&
    UPGRADE_OPERATIONS.includes(operation) && typeof instanceId === 'string' && INSTANCE_ID.test(instanceId) &&
    ((location === 'loadout' && Number.isSafeInteger(slot) && slot >= 0) ||
     (location === 'equipment' && EQUIPMENT_SLOTS.includes(slot)));
}

function shopPurchaseCost(lootId, amount) {
  const offer = getBuyOffer(lootId);
  if (!offer) throw { statusCode: 404, errorCode: 'ITEM_NOT_OFFERED', message: `Item '${lootId}' is not offered in the shop.` };
  const cost = offer.buyPrice * amount;
  if (!Number.isSafeInteger(cost)) throw { statusCode: 400, errorCode: 'INTEGER_OVERFLOW', message: 'Calculated transaction cost causes integer overflow.' };
  return cost;
}

function safeTradeTotal(value) {
  if (!Number.isSafeInteger(value)) throw { statusCode: 400, errorCode: 'INTEGER_OVERFLOW', message: 'Calculated transaction value causes integer overflow.' };
  return value;
}

class InventoryService {
  /** One ticket, one revision, one receipt. Sales free space and fund purchases before placement. */
  static async shopTrade(accountId, { transactionId, expectedRevision, purchases = [], sales = [], buybacks = [], weekId, merchant }) {
    const character = await Character.findOne({ accountId });
    if (!character) throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    requireMigratedInventory(character);
    const version = inventoryVersion(character);

    // Rotation and weekId validation
    const currentWeek = getWeeklyRotation();
    if (weekId && weekId !== currentWeek.weekId) {
      throw { statusCode: 409, errorCode: 'ROTATION_EXPIRED', message: 'The weekly shop rotation has changed. Please review current offers.' };
    }

    const canonicalLine = line => ({ lootId: normalizeLootId(line.lootId.trim()), amount: line.amount,
      ...(version >= 2 && Object.hasOwn(line, 'sourceSlot') ? { sourceSlot: line.sourceSlot } : {}) });
    const canonicalBuyback = line => ({ entryId: String(line.entryId).trim(), amount: line.amount });

    const intent = {
      purchases: (purchases || []).map(canonicalLine),
      sales: (sales || []).map(canonicalLine),
      buybacks: (buybacks || []).map(canonicalBuyback),
      ...(merchant ? { merchant: String(merchant).toLowerCase().trim() } : {})
    };
    // Ordered, normalized lines are the fingerprint: JSON property insertion order is fixed here.
    const fingerprint = JSON.stringify(intent);
    const receipt = (character.appliedShopReceipts || []).find(r => r.transactionId === transactionId);
    if (receipt) {
      if (receipt.type !== 'trade' || JSON.stringify(receipt.intent) !== fingerprint) {
        throw { statusCode: 409, errorCode: 'TRANSACTION_ID_REUSED', message: 'Transaction ID was previously applied with a different operation or parameters.' };
      }
      return { ...shopSnapshot(character), alreadySecured: true };
    }
    assertWithinRetention(character, transactionId);

    // Validate merchant specialization constraints according to Doc 23 §3
    if (merchant) {
      for (const line of intent.purchases) {
        if (!isAllowedByMerchant(merchant, 'buy', line.lootId)) {
          throw { statusCode: 422, errorCode: 'INVALID_MERCHANT_ITEM', message: `Merchant '${merchant}' does not offer '${line.lootId}'.` };
        }
      }
      for (const line of intent.sales) {
        if (!isAllowedByMerchant(merchant, 'sell', line.lootId)) {
          throw { statusCode: 422, errorCode: 'INVALID_MERCHANT_ITEM', message: `Merchant '${merchant}' does not buy '${line.lootId}'.` };
        }
      }
      for (const line of intent.buybacks) {
        if (!isAllowedByMerchant(merchant, 'buyback', '')) {
          throw { statusCode: 422, errorCode: 'INVALID_MERCHANT_ITEM', message: `Merchant '${merchant}' does not support buyback.` };
        }
      }
    }

    // Validate weekly stock limits for purchases (Doc 23 §5.2)
    const stockLimits = new Map();
    for (const w of currentWeek.normalWeapons) stockLimits.set(normalizeLootId(w.lootId), w.stock || 1);
    if (currentWeek.specialWeapon) stockLimits.set(normalizeLootId(currentWeek.specialWeapon.lootId), currentWeek.specialWeapon.stock || 1);
    for (const a of currentWeek.armors) stockLimits.set(normalizeLootId(a.lootId), a.stock || 1);
    for (const u of currentWeek.utilities) stockLimits.set(normalizeLootId(u.lootId), u.stock || 1);

    const isCurrentWeek = character.inventory.weeklyStock?.weekId === currentWeek.weekId;
    const rawPurchased = isCurrentWeek && character.inventory.weeklyStock?.purchased;
    const playerPurchased = new Map();
    if (rawPurchased) {
      if (rawPurchased instanceof Map) {
        for (const [k, v] of rawPurchased.entries()) playerPurchased.set(k, v);
      } else if (typeof rawPurchased === 'object') {
        for (const [k, v] of Object.entries(rawPurchased)) playerPurchased.set(k, v);
      }
    }

    const pendingPurchased = new Map(playerPurchased);
    for (const line of intent.purchases) {
      if (stockLimits.has(line.lootId)) {
        const limit = stockLimits.get(line.lootId);
        const already = pendingPurchased.get(line.lootId) || 0;
        if (already + line.amount > limit) {
          throw { statusCode: 409, errorCode: 'STOCK_EXHAUSTED', message: `Stock exhausted for '${line.lootId}'. Remaining available: ${Math.max(0, limit - already)}.` };
        }
        pendingPurchased.set(line.lootId, already + line.amount);
      }
    }

    for (const line of intent.sales) requireSourceSlot(character, line.sourceSlot);
    if (version === 1) assertUnambiguousItems(character.inventory.loadout,
      [...intent.sales, ...intent.purchases].map(line => line.lootId));
    if (character.inventory.pendingReservation) throw { statusCode: 409, errorCode: 'RESERVATION_PENDING', message: 'Cannot trade while a raid reservation is pending.' };
    if (character.revision !== expectedRevision) throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict.' };

    const loadout = inventoryItems(character, 'loadout');
    const newBuybackEntries = [];
    const telemetry = { sales: [], purchases: [], buybacks: [], merchant: intent.merchant };
    let revenue = 0; let cost = 0;

    // Process sales (fund cart)
    for (const line of intent.sales) {
      const sale = applyShopSale(loadout, version, line.lootId, line.amount, line.sourceSlot, currentWeek.demandItems);
      revenue = safeTradeTotal(revenue + sale.revenue);
      telemetry.sales.push(sale.sale);
      if (sale.buybackEntry) newBuybackEntries.push(sale.buybackEntry);
    }

    // Process purchases
    for (const line of intent.purchases) {
      const lineCost = shopPurchaseCost(line.lootId, line.amount);
      cost = safeTradeTotal(cost + lineCost);
      telemetry.purchases.push({ lootId: line.lootId, amount: line.amount, cost: lineCost });
    }

    // Process buybacks
    const existingBuybacks = character.inventory.buyback?.entries ? [...character.inventory.buyback.entries] : [];
    for (const line of intent.buybacks) {
      const entryIndex = existingBuybacks.findIndex(e => e.entryId === line.entryId);
      if (entryIndex === -1) throw { statusCode: 404, errorCode: 'BUYBACK_ENTRY_NOT_FOUND', message: 'Buyback entry not found in this Town session.' };
      const entry = existingBuybacks[entryIndex];
      if (isTrainingWeapon(entry.lootId) || (version >= 3 && (!isBuybackEligible(entry.lootId, version) || (getEquipmentTemplate(entry.lootId) && !entry.instance)))) {
        throw { statusCode: 409, errorCode: 'BUYBACK_NOT_ELIGIBLE', message: 'This item cannot be bought back.' };
      }
      if (line.amount > entry.amount) throw { statusCode: 409, errorCode: 'INSUFFICIENT_BUYBACK_UNITS', message: 'Not enough units in this sale line.' };
      const buybackCost = safeTradeTotal(entry.unitPrice * line.amount);
      cost = safeTradeTotal(cost + buybackCost);
      telemetry.buybacks.push({ lootId: entry.lootId, amount: line.amount, cost: buybackCost });

      EconomyTelemetry.trackCapacityBlock(character, 'trade', () => receiveItem(loadout, 'loadout',
        { lootId: entry.lootId, amount: line.amount, ...(entry.instance ? { instance: copyEntry(entry.instance) } : {}) }, version));

      if (line.amount === entry.amount) {
        existingBuybacks.splice(entryIndex, 1);
      } else {
        existingBuybacks[entryIndex] = { ...copyEntry(entry), amount: entry.amount - line.amount };
      }
    }

    const funded = safeTradeTotal((character.inventory.currency || 0) + revenue);
    const currency = safeTradeTotal(funded - cost);
    if (currency < 0) throw { statusCode: 409, errorCode: 'INSUFFICIENT_FUNDS', message: 'Not enough currency to complete trade.' };

    for (const line of intent.purchases) {
      // Reuse the single-buy receiver, including per-unit instance issuance for v3 equipment.
      if (version === 1) {
        const existing = loadout.find(item => matchesLootId(item, line.lootId));
        if (existing) safeTradeTotal(existing.amount + line.amount);
      }
      EconomyTelemetry.trackCapacityBlock(character, 'trade',
        () => receiveItem(loadout, 'loadout', { lootId: line.lootId, amount: line.amount }, version));
    }

    const combinedBuybackEntries = [...existingBuybacks, ...newBuybackEntries];
    const finalBuybackEntries = combinedBuybackEntries.slice(-BUYBACK_MAX_ENTRIES);

    const updated = await Character.findOneAndUpdate({ accountId, revision: expectedRevision,
      'inventory.pendingReservation': null,
      'inventory.currency': { $gte: Math.max(0, cost - revenue) },
      'inventory.buyback.sessionEpoch': buybackEpochFilter(character.inventory)
    }, {
      $set: {
        'inventory.loadout': loadout,
        'inventory.currency': currency,
        'inventory.buyback.entries': finalBuybackEntries,
        'inventory.weeklyStock.weekId': currentWeek.weekId,
        'inventory.weeklyStock.purchased': pendingPurchased
      },
      $inc: { revision: 1 },
      ...watermarkUpdate(character),
      $push: {
        appliedShopReceipts: receiptPush({ transactionId, type: 'trade', intent, totalAmount: cost - revenue, timestamp: new Date() })
      }
    }, { returnDocument: 'after' });
    if (!updated) {
      const latest = await Character.findOne({ accountId });
      if (latest?.inventory?.pendingReservation) throw { statusCode: 409, errorCode: 'RESERVATION_PENDING', message: 'Cannot trade while a raid reservation is pending.' };
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict during shop trade.' };
    }
    EconomyTelemetry.recordCommerce(updated, 'trade', telemetry);
    return { ...shopSnapshot(updated), alreadySecured: false };
  }

  /** Permanently fills the lowest empty socket; receipt and both lists commit together. */
  static async installGem(accountId, body) {
    const { transactionId, weaponList, weaponSlot, instanceId, gemList, gemSlot, gemLootId, expectedRevision } = body;
    if (typeof transactionId !== 'string' || !transactionId.trim() || transactionId.length > 128 ||
        !Object.hasOwn(CAPACITIES, weaponList) || !Object.hasOwn(CAPACITIES, gemList) ||
        typeof instanceId !== 'string' || !INSTANCE_ID.test(instanceId) || typeof gemLootId !== 'string' || !gemLootId ||
        !Number.isSafeInteger(expectedRevision) || expectedRevision < 0) {
      throw { statusCode: 400, errorCode: 'VALIDATION_FAILED', message: 'Invalid input parameters.' };
    }
    assertSlot(weaponSlot); assertSlot(gemSlot);
    const character = await Character.findOne({ accountId });
    if (!character) throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    requireSlotInventory(character);
    if (inventoryVersion(character) !== 3) throw { statusCode: 409, errorCode: 'INVENTORY_MIGRATION_REQUIRED', message: 'Inventory migration is required.' };
    const intent = { weaponList, weaponSlot, instanceId, gemList, gemSlot, gemLootId };
    const receipt = (character.appliedShopReceipts || []).find(receipt => receipt.transactionId === transactionId);
    if (receipt) {
      if (receipt.type !== 'install_gem' || JSON.stringify(receipt.intent) !== JSON.stringify(intent)) {
        throw { statusCode: 409, errorCode: 'TRANSACTION_ID_REUSED', message: 'Transaction ID was previously applied with a different operation or parameters.' };
      }
      return { ...copyEntry(receipt.outcome), alreadySecured: true };
    }
    assertWithinRetention(character, transactionId);
    if (character.revision !== expectedRevision) throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict.' };
    if (character.inventory.pendingReservation) throw { statusCode: 409, errorCode: 'RESERVATION_PENDING', message: 'Cannot install gems while a raid reservation is pending.' };
    const lists = { stash: inventoryItems(character, 'stash'), loadout: inventoryItems(character, 'loadout') };
    const weapon = lists[weaponList].find(item => item.slot === weaponSlot);
    if (getEquipmentTemplate(weapon?.lootId)?.category !== 'Weapon' || weapon?.instance?.instanceId !== instanceId) {
      throw { statusCode: 409, errorCode: 'INSTANCE_MISMATCH', message: 'The slot does not contain the weapon instance.' };
    }
    const gem = lists[gemList].find(item => item.slot === gemSlot && item.lootId === gemLootId);
    if (!gem || !Object.hasOwn(GEM_CATALOG, gemLootId)) throw { statusCode: 409, errorCode: 'GEM_NOT_AVAILABLE', message: 'The gem is not available at the specified slot.' };
    const modifiers = weapon.instance.modifiers;
    if (!modifiers?.gemSocketCount) throw { statusCode: 409, errorCode: 'GEM_NO_SOCKETS', message: 'The weapon has no gem sockets.' };
    const socket = modifiers.gems.indexOf(null);
    if (socket === -1) throw { statusCode: 409, errorCode: 'GEM_NO_EMPTY_SOCKET', message: 'The weapon has no empty gem socket.' };
    modifiers.gems[socket] = gemLootId;
    if (--gem.amount === 0) lists[gemList].splice(lists[gemList].indexOf(gem), 1);
    const outcome = { stash: serializeItems(lists.stash, true), loadout: serializeItems(lists.loadout, true), revision: expectedRevision + 1 };
    return writeSlotLists(character, expectedRevision, lists, true,
      { transactionId, type: 'install_gem', intent, outcome, timestamp: new Date() });
  }

  /**
   * Town NPC upgrade (Doc 23 §9). The item stays where it is (Inventory slot or equipped slot) with the same
   * InstanceID; gold and Inventory materials are debited once in the same revision-guarded write as the receipt.
   */
  static async upgradeItem(accountId, body = {}) {
    if (!validUpgradeTicket(body)) throw { statusCode: 400, errorCode: 'VALIDATION_FAILED', message: 'Invalid upgrade parameters.' };
    const { transactionId, expectedRevision, merchant, operation, instanceId, location, slot } = body;
    const character = await Character.findOne({ accountId });
    if (!character) throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    requireSlotInventory(character);
    if (inventoryVersion(character) !== 3) throw { statusCode: 409, errorCode: 'INVENTORY_MIGRATION_REQUIRED', message: 'Inventory migration is required.' };
    const intent = { merchant, operation, instanceId, location, slot };
    const recorded = recordedUpgrade(character, transactionId, intent);
    if (recorded) return recorded;
    assertWithinRetention(character, transactionId);
    if (character.revision !== expectedRevision) throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict.' };
    rejectPendingReservation(character, 'Cannot upgrade equipment while a raid reservation is pending.');

    const loadout = inventoryItems(character, 'loadout');
    const preparedEquipment = serializePreparedEquipment(character.inventory.preparedEquipment, 3);
    const target = location === 'loadout' ? loadout.find(item => item.slot === slot) : preparedEquipment[slot];
    if (!target?.instance || target.instance.instanceId !== instanceId) {
      throw { statusCode: 409, errorCode: 'INSTANCE_MISMATCH', message: 'The location does not contain the equipment instance.' };
    }
    const plan = planUpgrade({ merchant, operation, lootId: target.lootId, instance: target.instance });
    const currency = character.inventory.currency || 0;
    if (currency < plan.gold) throw { statusCode: 409, errorCode: 'INSUFFICIENT_FUNDS', message: 'Not enough currency for this upgrade.' };
    consumeLoadoutMaterials(loadout, plan.materials);
    target.instance.modifiers = plan.modifiers;

    const outcome = { operation, location, slot, lootId: target.lootId, instance: copyEntry(target.instance),
      gold: plan.gold, materials: plan.materials, objectValue: objectValue(target.lootId, target.instance) };
    const updated = await Character.findOneAndUpdate(
      { accountId, revision: expectedRevision, 'inventory.pendingReservation': null },
      { $set: { 'inventory.loadout': loadout, 'inventory.preparedEquipment': preparedEquipment, 'inventory.currency': currency - plan.gold },
        $inc: { revision: 1 }, ...watermarkUpdate(character),
        $push: { appliedShopReceipts: receiptPush({ transactionId, type: 'upgrade', intent, outcome, totalAmount: plan.gold, timestamp: new Date() }) } },
      { returnDocument: 'after' }
    );
    if (!updated) {
      // Lost the guarded write: a concurrent retry of this very request is already committed and is answered
      // with its recorded outcome; any other loser reports why it lost without having changed anything.
      const latest = await Character.findOne({ accountId });
      const committed = latest && recordedUpgrade(latest, transactionId, intent);
      if (committed) return committed;
      if (latest) rejectPendingReservation(latest, 'Cannot upgrade equipment while a raid reservation is pending.');
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict during equipment upgrade.' };
    }
    EconomyTelemetry.recordUpgrade(updated, { merchant, upgradeOperation: operation, lootId: target.lootId, gold: plan.gold,
      materials: plan.materials, modifiers: target.instance.modifiers, objectValueAfter: outcome.objectValue });
    return { ...shopSnapshot(updated), upgrade: outcome, alreadySecured: false };
  }

  /** Reorders one inventory list without changing item identity or amounts. */
  static async updateOrder(accountId, list, lootIds, expectedRevision, slotOrder) {
    const character = await Character.findOne({ accountId });
    if (!character) {
      throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    }
    if (character.revision !== expectedRevision) {
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict.' };
    }
    if (character.inventory.pendingReservation) {
      throw { statusCode: 409, errorCode: 'RESERVATION_PENDING', message: 'Cannot reorder inventory while a reservation is pending.' };
    }
    requireMigratedInventory(character);
    const items = ['stash', 'loadout'].includes(list) ? character.inventory[list] : null;
    if (inventoryVersion(character) >= 2) {
      if (lootIds !== undefined || slotOrder === undefined) {
        throw { statusCode: 409, errorCode: 'INVENTORY_SLOT_ADDRESS_REQUIRED', message: 'A slot order is required for this inventory operation.' };
      }
      if (!items || !Array.isArray(slotOrder) || slotOrder.length !== items.length ||
          slotOrder.some(slot => !Number.isSafeInteger(slot) || slot < 0) ||
          new Set(slotOrder).size !== slotOrder.length ||
          slotOrder.some(slot => !items.some(item => item.slot === slot))) {
        throw { statusCode: 400, errorCode: 'VALIDATION_FAILED', message: 'Order must be an exact permutation of the occupied slots.' };
      }
      if (items.length > CAPACITIES[list]) inventoryFull();
      const bySlot = new Map(items.map(item => [item.slot, item]));
      return writeSlotLists(character, expectedRevision,
        { [list]: slotOrder.map((slot, index) => ({ ...copyEntry(bySlot.get(slot)), slot: index })) }, true);
    }
    if (slotOrder !== undefined) requireSlotInventory(character);
    assertUnambiguousItems(items);
    if (!items || !Array.isArray(lootIds) || lootIds.length !== items.length ||
        lootIds.some(id => typeof id !== 'string' || !id.trim()) ||
        new Set(lootIds).size !== lootIds.length ||
        lootIds.some(id => !items.some(item => item.lootId === id))) {
      throw { statusCode: 400, errorCode: 'VALIDATION_FAILED', message: 'Order must be an exact permutation of the target inventory list.' };
    }
    const byId = new Map(items.map(item => [item.lootId, item]));
    const ordered = lootIds.map(id => copyEntry(byId.get(id)));
    const updated = await Character.findOneAndUpdate(
      { accountId, revision: expectedRevision, 'inventory.pendingReservation': null },
      { $set: { [`inventory.${list}`]: ordered }, $inc: { revision: 1 } },
      { new: true }
    );
    if (!updated) {
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict during inventory reorder.' };
    }
    return {
      stash: serializeItems(updated.inventory.stash),
      loadout: serializeItems(updated.inventory.loadout),
      revision: updated.revision
    };
  }

  /**
   * Retrieves the full inventory snapshot for the given account's character.
   * Used during login to hydrate the local Unity state.
   * @throws 404 if no character exists for this account.
   */
  static async getInventory(accountId) {
    const character = await Character.findOne({ accountId });
    if (!character) {
      throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    }

    requireMigratedInventory(character);
    const version = inventoryVersion(character);
    const sorted = version >= 2;

    const extractionReceipts = character.inventory.appliedExtractionReceipts || [];
    const lastExtraction = extractionReceipts.length > 0
      ? {
          raidId: extractionReceipts[extractionReceipts.length - 1].raidId,
          resultSequence: extractionReceipts[extractionReceipts.length - 1].resultSequence
        }
      : null;

    return {
      inventorySchemaVersion: version,
      currency:           character.inventory.currency || 0,
      stash:              serializeItems(character.inventory.stash, sorted),
      loadout:            serializeItems(character.inventory.loadout, sorted),
      preparedEquipment:  serializePreparedEquipment(character.inventory.preparedEquipment, version),
      pendingReservation: serializePendingReservation(character.inventory.pendingReservation, sorted, version),
      buyback: serializeBuyback(character.inventory),
      lastAppliedExtractionReceipt: lastExtraction,
      revision: character.revision || 0
    };
  }

  /**
   * Moves every stack of the source list (stash for 'toLoadout', loadout for 'toStash') in one revision-guarded write.
   * @throws 404 if no character found; 409 on revision conflict or pending reservation.
   */
  static async transferAll(accountId, direction, expectedRevision) {
    const character = await Character.findOne({ accountId });
    if (!character) {
      throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    }
    return transferAllSlotItems(character, direction, expectedRevision);
  }

  /**
   * Moves `amount` units of `lootId` from the stash to the loadout.
   * Triggered only by explicit player action in the Town UI (save-on-move).
   * @throws 404 if no character found.
   * @throws 409 if the stash does not hold enough units of that item.
   */
  static async moveToLoadout(accountId, lootId, amount, expectedRevision, sourceSlot) {
    const character = await Character.findOne({ accountId });
    if (!character) {
      throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    }

    requireSourceSlot(character, sourceSlot);
    if (inventoryVersion(character) >= 2) return transferSlotItems(character, 'stash', 'loadout', lootId, amount, expectedRevision, sourceSlot);
    assertUnambiguousItems(character.inventory.stash, [lootId]);
    assertUnambiguousItems(character.inventory.loadout, [lootId]);
    
    if (character.revision !== expectedRevision) {
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict.' };
    }
    rejectPendingReservation(character, 'Cannot transfer items while a reservation is pending.');
    lootId = normalizeLootId(lootId);

    const stash   = character.inventory.stash;
    const loadout = character.inventory.loadout;

    const stashIndex = stash.findIndex(i => matchesLootId(i, lootId));
    const movedEntry = stashIndex === -1 ? null : copyEntry(stash[stashIndex]);
    if (stashIndex === -1 || stash[stashIndex].amount < amount) {
      throw {
        statusCode: 409,
        errorCode: 'INSUFFICIENT_STASH_ITEMS',
        message: `Stash does not hold ${amount} unit(s) of '${lootId}'.`
      };
    }

    // Deduct from stash
    if (stash[stashIndex].amount === amount) {
      stash.splice(stashIndex, 1);
    } else {
      stash[stashIndex].amount -= amount;
    }

    // Add to loadout, carrying the complete stored entry when it changes list.
    const loadoutIndex = loadout.findIndex(i => matchesLootId(i, lootId));
    if (loadoutIndex !== -1) {
      loadout[loadoutIndex].amount += amount;
    } else {
      loadout.push({ ...movedEntry, amount });
    }

    const updated = await Character.findOneAndUpdate(
      { accountId, revision: expectedRevision, 'inventory.pendingReservation': null },
      {
        $set: { 'inventory.stash': stash, 'inventory.loadout': loadout },
        $inc: { revision: 1 }
      },
      { new: true }
    );

    if (!updated) {
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict during move to loadout.' };
    }

    return {
      stash:   serializeItems(updated.inventory.stash),
      loadout: serializeItems(updated.inventory.loadout),
      revision: updated.revision
    };
  }

  /**
   * Moves `amount` units of `lootId` from the loadout to the stash.
   * Triggered only by explicit player action in the Town UI (save-on-move).
   * @throws 404 if no character found.
   * @throws 409 if the loadout does not hold enough units of that item.
   */
  static async moveToStash(accountId, lootId, amount, expectedRevision, sourceSlot) {
    const character = await Character.findOne({ accountId });
    if (!character) {
      throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    }

    requireSourceSlot(character, sourceSlot);
    if (inventoryVersion(character) >= 2) return transferSlotItems(character, 'loadout', 'stash', lootId, amount, expectedRevision, sourceSlot);
    assertUnambiguousItems(character.inventory.stash, [lootId]);
    assertUnambiguousItems(character.inventory.loadout, [lootId]);
    
    if (character.revision !== expectedRevision) {
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict.' };
    }
    rejectPendingReservation(character, 'Cannot transfer items while a reservation is pending.');
    lootId = normalizeLootId(lootId);

    const stash   = character.inventory.stash;
    const loadout = character.inventory.loadout;

    const loadoutIndex = loadout.findIndex(i => matchesLootId(i, lootId));
    const movedEntry = loadoutIndex === -1 ? null : copyEntry(loadout[loadoutIndex]);
    if (loadoutIndex === -1 || loadout[loadoutIndex].amount < amount) {
      throw {
        statusCode: 409,
        errorCode: 'INSUFFICIENT_LOADOUT_ITEMS',
        message: `Loadout does not hold ${amount} unit(s) of '${lootId}'.`
      };
    }

    // Deduct from loadout
    if (loadout[loadoutIndex].amount === amount) {
      loadout.splice(loadoutIndex, 1);
    } else {
      loadout[loadoutIndex].amount -= amount;
    }

    // Add to stash, carrying the complete stored entry when it changes list.
    const stashIndex = stash.findIndex(i => matchesLootId(i, lootId));
    if (stashIndex !== -1) {
      stash[stashIndex].amount += amount;
    } else {
      stash.push({ ...movedEntry, amount });
    }

    const updated = await Character.findOneAndUpdate(
      { accountId, revision: expectedRevision, 'inventory.pendingReservation': null },
      {
        $set: { 'inventory.stash': stash, 'inventory.loadout': loadout },
        $inc: { revision: 1 }
      },
      { new: true }
    );

    if (!updated) {
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict during move to stash.' };
    }

    return {
      stash:   serializeItems(updated.inventory.stash),
      loadout: serializeItems(updated.inventory.loadout),
      revision: updated.revision
    };
  }

  /** Moves a complete entry: empty target, compatible merge, or swap. */
  static async moveSlot(accountId, fromList, fromSlot, toList, toSlot, lootId, expectedRevision) {
    const character = await Character.findOne({ accountId });
    if (!character) throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    requireSlotInventory(character);
    if (character.revision !== expectedRevision) throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict.' };
    if (character.inventory.pendingReservation) throw { statusCode: 409, errorCode: 'RESERVATION_PENDING', message: 'Cannot move slots while a reservation is pending.' };
    if (!Object.hasOwn(CAPACITIES, fromList) || !Object.hasOwn(CAPACITIES, toList)) {
      throw { statusCode: 400, errorCode: 'VALIDATION_FAILED', message: 'Lists must be stash or loadout.' };
    }
    assertSlot(fromSlot); assertSlot(toSlot);
    if (toSlot >= CAPACITIES[toList]) inventoryFull();
    const from = inventoryItems(character, fromList);
    const to = fromList === toList ? from : inventoryItems(character, toList);
    const source = from.find(item => item.slot === fromSlot && matchesLootId(item, lootId));
    if (!source) throw { statusCode: 422, errorCode: 'ITEM_NOT_FOUND', message: 'Source slot does not contain the guarded item.' };
    if (fromList === toList && fromSlot === toSlot) return listsSnapshot(character);
    const target = to.find(item => item.slot === toSlot);
    if (!target) {
      if (fromList !== toList && to.length >= CAPACITIES[toList]) inventoryFull();
      from.splice(from.indexOf(source), 1);
      to.push({ ...source, slot: toSlot });
    } else if (matchesLootId(target, source.lootId) && !(inventoryVersion(character) === 3 && source.instance)) {
      addAmount(target, source.amount);
      from.splice(from.indexOf(source), 1);
    } else {
      // Swap-back is also a placement: an overflow source cannot accept it.
      if (fromSlot >= CAPACITIES[fromList]) inventoryFull();
      from[from.indexOf(source)] = { ...target, slot: fromSlot };
      to[to.indexOf(target)] = { ...source, slot: toSlot };
    }
    return writeSlotLists(character, expectedRevision, { [fromList]: from, [toList]: to }, true);
  }

  /** Splits one guarded entry into an empty slot in the same list, without merging. */
  static async splitSlot(accountId, list, sourceSlot, amount, destinationSlot, lootId, expectedRevision) {
    requireSplitEnabled();
    const character = await Character.findOne({ accountId });
    if (!character) throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    requireSlotInventory(character);
    if (character.revision !== expectedRevision) throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict.' };
    if (character.inventory.pendingReservation) throw { statusCode: 409, errorCode: 'RESERVATION_PENDING', message: 'Cannot split slots while a reservation is pending.' };
    if (!Object.hasOwn(CAPACITIES, list)) {
      throw { statusCode: 400, errorCode: 'VALIDATION_FAILED', message: 'List must be stash or loadout.' };
    }
    assertSlot(sourceSlot); assertSlot(destinationSlot);
    if (sourceSlot === destinationSlot) {
      throw { statusCode: 400, errorCode: 'VALIDATION_FAILED', message: 'Source and destination slots must differ.' };
    }
    const items = inventoryItems(character, list);
    const source = items.find(item => item.slot === sourceSlot && matchesLootId(item, lootId));
    if (!source) {
      throw { statusCode: 409, errorCode: list === 'stash' ? 'INSUFFICIENT_STASH_ITEMS' : 'INSUFFICIENT_LOADOUT_ITEMS',
        message: 'Source slot does not contain the guarded item.' };
    }
    if (inventoryVersion(character) === 3 && source.instance) {
      throw { statusCode: 409, errorCode: 'INSTANCE_NOT_SPLITTABLE', message: 'Equipment instances cannot be split.' };
    }
    if (!Number.isSafeInteger(amount) || amount < 1 || amount >= source.amount) {
      throw { statusCode: 400, errorCode: 'INVALID_QUANTITY', message: 'Split amount must be an integer between 1 and source amount minus 1.' };
    }
    if (destinationSlot >= CAPACITIES[list]) inventoryFull();
    if (items.some(item => item.slot === destinationSlot)) {
      throw { statusCode: 409, errorCode: 'SLOT_OCCUPIED', message: 'Destination slot is occupied.' };
    }
    // Overflow remains a source only; every new occupied slot counts toward capacity.
    if (items.length >= CAPACITIES[list]) inventoryFull();
    items.push({ ...copyEntry(source), slot: destinationSlot, amount });
    source.amount -= amount;
    return writeSlotLists(character, expectedRevision, { [list]: items }, true);
  }

  /**
   * Replaces the six equipment slot assignments for the character's loadout.
   * Modifies the Loadout (and Stash if needed) by deducting newly assigned items
   * and refunding unassigned items back to the Loadout.
   * @throws 404 if no character found.
   * @throws 422 if a new item is not found in either the Loadout or the Stash.
   */
  static async updatePreparedEquipment(accountId, slots, expectedRevision) {
    const character = await Character.findOne({ accountId });
    if (!character) {
      throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    }

    requireMigratedInventory(character);
    const version = inventoryVersion(character);
    if (version === 1) {
      assertUnambiguousItems(character.inventory.stash);
      assertUnambiguousItems(character.inventory.loadout);
    }
    
    if (character.revision !== expectedRevision) {
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict.' };
    }
    rejectPendingReservation(character, 'Cannot change equipment while a reservation is pending.');
    if (version === 3) return updateInstanceEquipment(character, slots, expectedRevision);
    normalizePreparedEquipment(slots);
    assertPreparedEquipmentCategories(slots);

    const slotNames = EQUIPMENT_SLOTS;

    const oldEquipment = character.inventory.preparedEquipment || {};
    const removedItems = [];
    const addedItems = [];

    // Find the diff
    for (const slot of slotNames) {
      const oldItem = normalizeLootId(oldEquipment[slot] || '');
      const newItem = slots[slot] || '';
      
      if (oldItem !== newItem) {
        if (oldItem) removedItems.push(oldItem);
        if (newItem) addedItems.push(newItem);
      }
    }

    if (version === 2) {
      // Reassign already equipped units first, then deduct Inventory, then admit
      // remaining refunds against final capacity (not an intermediate full list).
      const refunds = removedItems.slice();
      for (const lootId of addedItems) {
        const refundIndex = refunds.indexOf(lootId);
        if (refundIndex !== -1) { refunds.splice(refundIndex, 1); continue; }
        let index = lowestItemIndex(character.inventory.loadout, lootId);
        const list = index !== -1 ? character.inventory.loadout : character.inventory.stash;
        if (index === -1) index = lowestItemIndex(list, lootId);
        if (index === -1) throw { statusCode: 422, errorCode: 'ITEM_NOT_FOUND', message: `'${lootId}' is not present in the loadout or stash.` };
        if (--list[index].amount === 0) list.splice(index, 1);
      }
      for (const lootId of refunds) receiveItem(character.inventory.loadout, 'loadout', { lootId, amount: 1 }, version);
    } else {
      // Legacy equipment behavior is unchanged: refund before deduction.
      for (const lootId of removedItems) {
        const existing = character.inventory.loadout.find(i => matchesLootId(i, lootId));
        if (existing) {
          existing.amount += 1;
        } else {
          character.inventory.loadout.push({ lootId, amount: 1 });
        }
      }

      for (const lootId of addedItems) {
        let deducted = false;
        const loadoutIndex = character.inventory.loadout.findIndex(i => matchesLootId(i, lootId));
        if (loadoutIndex !== -1 && character.inventory.loadout[loadoutIndex].amount > 0) {
          character.inventory.loadout[loadoutIndex].amount -= 1;
          if (character.inventory.loadout[loadoutIndex].amount === 0) {
            character.inventory.loadout.splice(loadoutIndex, 1);
          }
          deducted = true;
        } else {
          const stashIndex = character.inventory.stash.findIndex(i => matchesLootId(i, lootId));
          if (stashIndex !== -1 && character.inventory.stash[stashIndex].amount > 0) {
            character.inventory.stash[stashIndex].amount -= 1;
            if (character.inventory.stash[stashIndex].amount === 0) {
              character.inventory.stash.splice(stashIndex, 1);
            }
            deducted = true;
          }
        }
        if (!deducted) {
          throw { statusCode: 422, errorCode: 'ITEM_NOT_FOUND', message: `'${lootId}' is not present in the loadout or stash.` };
        }
      }
    }

    // Apply the new equipment assignments
    for (const slot of slotNames) {
      character.inventory.preparedEquipment[slot] = slots[slot] || '';
    }

    const updated = await Character.findOneAndUpdate(
      { accountId, revision: expectedRevision, 'inventory.pendingReservation': null },
      {
        $set: {
          'inventory.stash': character.inventory.stash,
          'inventory.loadout': character.inventory.loadout,
          'inventory.preparedEquipment': character.inventory.preparedEquipment
        },
        $inc: { revision: 1 }
      },
      { new: true }
    );

    if (!updated) {
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict during equipment update.' };
    }

    return {
      preparedEquipment: serializePreparedEquipment(updated.inventory.preparedEquipment),
      stash: inventoryItems(updated, 'stash'),
      loadout: inventoryItems(updated, 'loadout'),
      revision: updated.revision
    };
  }

  static async requestTrainingWeapon(accountId, weaponId, destinationSlot = '', expectedRevision) {
    weaponId = normalizeLootId(weaponId);
    if (!TRAINING_WEAPONS.includes(weaponId)) {
      throw { statusCode: 400, errorCode: 'INVALID_ITEM', message: `'${weaponId}' is not a valid training weapon.` };
    }
    if (destinationSlot && !['weaponSlot1', 'weaponSlot2'].includes(destinationSlot)) {
      throw { statusCode: 400, errorCode: 'INVALID_SLOT', message: `'${destinationSlot}' is not a valid weapon slot.` };
    }

    const character = await Character.findOne({ accountId });
    if (!character) {
      throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    }

    requireMigratedInventory(character);
    const version = inventoryVersion(character);

    requireSlotInventory(character);
    if (character.inventory.pendingReservation) {
      throw { statusCode: 409, errorCode: 'RESERVATION_PENDING', message: 'Cannot request a training weapon while a raid reservation is pending.' };
    }
    if (character.revision !== expectedRevision) {
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict.' };
    }

    const stash = inventoryItems(character, 'stash');
    const loadout = inventoryItems(character, 'loadout');
    const equipment = serializePreparedEquipment(character.inventory.preparedEquipment, version);

    for (const list of [stash, loadout]) {
      for (let i = list.length - 1; i >= 0; i--) {
        if (isTrainingWeapon(list[i].lootId)) list.splice(i, 1);
      }
    }

    for (const slot of EQUIPMENT_SLOTS) {
      const current = equipment[slot];
      const currentLootId = version === 3 ? current?.lootId : current;
      if (isTrainingWeapon(currentLootId)) {
        equipment[slot] = version === 3 ? null : '';
      }
    }

    if (destinationSlot) {
      const previous = equipment[destinationSlot];
      if (previous && (version === 3 ? previous.lootId : previous)) {
        const previousLootId = version === 3 ? previous.lootId : previous;
        if (!isTrainingWeapon(previousLootId)) {
          const displacedEntry = version === 3 ? { ...copyEntry(previous), amount: 1 } : { lootId: previous, amount: 1 };
          receiveItem(loadout, 'loadout', displacedEntry, version);
        }
      }

      if (version === 3) {
        equipment[destinationSlot] = createEquipment(weaponId);
      } else {
        equipment[destinationSlot] = weaponId;
      }
    } else {
      if (version === 3) {
        const item = { ...createEquipment(weaponId), amount: 1 };
        receiveItem(loadout, 'loadout', item, version);
      } else {
        receiveItem(loadout, 'loadout', { lootId: weaponId, amount: 1 }, version);
      }
    }

    const snapshot = await writeSlotLists(character, expectedRevision, {
      stash, loadout, preparedEquipment: equipment
    }, true);
    return { preparedEquipment: equipment, ...snapshot };
  }

  /**
   * Persists a raid reservation snapshot.
   * Called when the player commits a loadout for a raid.
   * Only a non-admitted reservation can recover ownership after an aborted launch.
   * An admitted reservation is replaced as a loss unless extraction is pending.
   * @throws 404 if no character found.
   */
  static async savePendingReservation(accountId, reservationId, preparedAbilities = null) {
    const character = await Character.findOne({ accountId });
    if (!character) {
      throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    }

    requireMigratedInventory(character);
    const version = inventoryVersion(character);
    if (version === 1) {
      assertUnambiguousItems(character.inventory.stash);
      assertUnambiguousItems(character.inventory.loadout);
      assertUnambiguousItems(character.inventory.pendingReservation?.items);
    }
    const existingReservation = character.inventory.pendingReservation;
    if (existingReservation?.admittedAt && existingReservation.reservationId !== reservationId) {
      await assertNoPendingExtractionResult(character.accountId);
    }

    // Valid same-ID retries are read-only; invalid legacy reservations fail closed.
    if (character.inventory.pendingReservation && character.inventory.pendingReservation.reservationId === reservationId) {
      requirePreparedWeapon(character.inventory.pendingReservation.preparedEquipment);
      return {
        pendingReservation: serializePendingReservation(character.inventory.pendingReservation, version >= 2, version),
        revision: character.revision || 0
      };
    }

    // FIX-32: reject unknown or unowned abilities before restoring or writing anything.
    const resolvedAbilities = validatePreparedAbilities(
      preparedAbilities ?? {
        slot1: character.preparedAbilities?.slot1 || '',
        slot2: character.preparedAbilities?.slot2 || ''
      },
      character.unlockedAbilities
    );

    // An unadmitted reservation from an aborted launch is restored before the new one is derived,
    // whatever the active loadout and equipment hold, so none of its items are lost.
    if (character.inventory.pendingReservation && !character.inventory.pendingReservation.admittedAt) {
      restoreReservation(character);
    }
    if (version >= 2 && character.inventory.loadout.length > CAPACITIES.loadout) inventoryFull();

    // Validate after aborted-launch restoration, before any persisted write.
    // Ownership in Stash/Loadout does not imply an equipped weapon.
    requirePreparedWeapon(character.inventory.preparedEquipment);

    // Derive the reservation from the current persisted loadout and preparedEquipment
    const items = inventoryItems(character, 'loadout');
    const preparedEquipment = serializePreparedEquipment(character.inventory.preparedEquipment, version);

    const pendingReservation = {
      reservationId,
      admittedAt: null,
      startedAt: null,
      items,
      preparedEquipment,
      preparedAbilities: resolvedAbilities
    };

    const updated = await Character.findOneAndUpdate(
      { accountId, revision: character.revision },
      {
        $set: {
          'inventory.stash': character.inventory.stash,
          'inventory.pendingReservation': pendingReservation,
          'inventory.loadout': [],
          'inventory.preparedEquipment': version === 3 ? serializePreparedEquipment({}, version) : {},
          ...(character.inventory.buyback ? { 'inventory.buyback.entries': [] } : {})
        },
        $inc: { revision: 1 }
      },
      { new: true }
    );

    if (!updated) {
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict during reservation.' };
    }

    return {
      pendingReservation: serializePendingReservation(updated.inventory.pendingReservation, version >= 2, version),
      revision: updated.revision
    };
  }

  /** Marks admission without consuming reserved ownership; same-id retries do not write. */
  static async admitPendingReservation(accountId, reservationId) {
    const character = await Character.findOne({ accountId });
    if (!character) {
      throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    }

    requireMigratedInventory(character);
    const reservation = character.inventory.pendingReservation;
    if (!reservation || reservation.reservationId !== reservationId) {
      throw { statusCode: 422, errorCode: 'NO_ACTIVE_RESERVATION', message: 'No active reservation matches this reservation id.' };
    }
    if (reservation.admittedAt) {
      return { pendingReservation: serializePendingReservation(reservation, inventoryVersion(character) >= 2, inventoryVersion(character)), revision: character.revision };
    }

    // FIX-95: admission after the Host's start marker inherits it, so a late teammate cannot refund.
    const raidStarted = await isRaidMarkedStarted(reservation.raidId);
    const admittedAt = new Date();
    let updated = await Character.findOneAndUpdate(
      { accountId, revision: character.revision,
        'inventory.pendingReservation.reservationId': reservationId,
        'inventory.pendingReservation.admittedAt': null },
      { $set: { 'inventory.pendingReservation.admittedAt': admittedAt,
                ...(raidStarted ? { 'inventory.pendingReservation.startedAt': admittedAt } : {}) },
        $inc: { revision: 1 } },
      { new: true }
    );
    if (!updated) {
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict during reservation admission.' };
    }
    // The marker may have landed between the check and the write (it only marks admitted reservations).
    if (!raidStarted && await isRaidMarkedStarted(reservation.raidId)) {
      updated = await Character.findOneAndUpdate(
        { accountId, 'inventory.pendingReservation.reservationId': reservationId,
          'inventory.pendingReservation.startedAt': null },
        { $set: { 'inventory.pendingReservation.startedAt': new Date() }, $inc: { revision: 1 } },
        { new: true }
      ) || updated;
    }
    return { pendingReservation: serializePendingReservation(updated.inventory.pendingReservation, inventoryVersion(updated) >= 2, inventoryVersion(updated)), revision: updated.revision };
  }

  /**
   * FIX-95 start marker. The Host reports that the raid effectively started; every admitted
   * reservation bound to the raid is marked once, which closes its pre-start refund.
   * The caller must itself hold an admitted reservation bound to `raidId` (Bounded Player-Host
   * Authority). Retries mark nothing.
   * @throws 404 if no character found, 403 HOST_NOT_IN_RAID if the caller is not in the raid.
   */
  static async markRaidStarted(accountId, raidId) {
    const character = await Character.findOne({ accountId });
    if (!character) {
      throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    }
    const reservation = character.inventory.pendingReservation;
    if (!reservation?.admittedAt || reservation.raidId !== raidId) {
      throw { statusCode: 403, errorCode: 'HOST_NOT_IN_RAID', message: 'The caller has no admitted reservation in this raid.' };
    }

    const result = await Character.updateMany(
      { 'inventory.pendingReservation.raidId': raidId,
        'inventory.pendingReservation.admittedAt': { $ne: null },
        'inventory.pendingReservation.startedAt': null },
      { $set: { 'inventory.pendingReservation.startedAt': new Date() }, $inc: { revision: 1 } }
    );
    return { raidId, marked: result.modifiedCount };
  }

  /**
   * FIX-95 pre-start refund. Restores the reserved items exactly like a pre-admission rollback and
   * clears the reservation, but only while the raid has not started and, for an admitted
   * reservation, within `env.reservationRefundWindowMs` of admission. Rejections never write.
   * No active reservation is an idempotent no-op (retry after a lost response) reporting
   * `refunded: false`; `refunded: true` means this call actually restored the items.
   * @throws 404 if no character found; 409 RESERVATION_MISMATCH, RAID_ALREADY_STARTED,
   *         REFUND_WINDOW_EXPIRED, EXTRACTION_RESULT_PENDING, RESULT_ALREADY_CONSUMED, REVISION_CONFLICT.
   */
  static async refundPendingReservation(accountId, reservationId) {
    const character = await Character.findOne({ accountId });
    if (!character) {
      throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    }

    requireMigratedInventory(character);
    const reservation = character.inventory.pendingReservation;
    if (!reservation) return { pendingReservation: null, revision: character.revision, refunded: false };
    if (reservation.reservationId !== reservationId) {
      throw { statusCode: 409, errorCode: 'RESERVATION_MISMATCH', message: 'A different reservation is active.' };
    }
    if (reservation.startedAt) {
      throw { statusCode: 409, errorCode: 'RAID_ALREADY_STARTED', message: 'The raid has started; the reservation can no longer be refunded.' };
    }
    if (reservation.admittedAt) {
      if (Date.now() - reservation.admittedAt.getTime() > env.reservationRefundWindowMs) {
        throw { statusCode: 409, errorCode: 'REFUND_WINDOW_EXPIRED', message: 'The refund window for this reservation has expired.' };
      }
      await assertNoPendingExtractionResult(character.accountId);
    }
    if (reservation.raidId && (character.inventory.appliedExtractionReceipts || [])
      .some(receipt => receipt.raidId === reservation.raidId)) {
      throw { statusCode: 409, errorCode: 'RESULT_ALREADY_CONSUMED', message: 'The raid result was already applied for this reservation.' };
    }

    restoreReservation(character);
    const updated = await Character.findOneAndUpdate(
      { accountId, revision: character.revision,
        'inventory.pendingReservation.reservationId': reservationId,
        'inventory.pendingReservation.startedAt': null },
      {
        $set: {
          'inventory.pendingReservation': null,
          'inventory.loadout': character.inventory.loadout,
          'inventory.preparedEquipment': character.inventory.preparedEquipment
        },
        $inc: { revision: 1 }
      },
      { new: true }
    );
    if (!updated) {
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict refunding reservation.' };
    }
    return { pendingReservation: null, revision: updated.revision, refunded: true };
  }

  /**
   * Consumes a reservation as a loss, or rolls back pre-admission ownership.
   * Admitted reservations can never roll back, and pending extraction blocks loss.
   * @throws 404 if no character found.
   */
  static async clearPendingReservation(accountId, { rollback = false, expectedReservationId } = {}) {
    const guarded = expectedReservationId !== undefined;
    if (guarded && (typeof expectedReservationId !== 'string' ||
        expectedReservationId.length < 1 || expectedReservationId.length > 128 ||
        !/^[A-Za-z0-9_-]+$/.test(expectedReservationId))) {
      throw { statusCode: 400, errorCode: 'VALIDATION_FAILED', message: 'Invalid expected reservation ID.' };
    }
    const character = await Character.findOne({ accountId });
    if (!character) {
      throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    }

    requireMigratedInventory(character);
    if (character.inventory.pendingReservation?.admittedAt) {
      if (rollback) {
        throw { statusCode: 409, errorCode: 'RESERVATION_ADMITTED', message: 'An admitted reservation cannot be rolled back.' };
      }
      await assertNoPendingExtractionResult(character.accountId);
    }

    if (guarded) {
      if (!character.inventory.pendingReservation)
        return { pendingReservation: null, revision: character.revision };
      if (character.inventory.pendingReservation.reservationId !== expectedReservationId)
        throw { statusCode: 409, errorCode: 'RESERVATION_MISMATCH', message: 'A different reservation is active.' };
    }
    const reservation = character.inventory.pendingReservation;
    const setFields = { 'inventory.pendingReservation': null };
    if (rollback && character.inventory.pendingReservation) {
      restoreReservation(character);
      setFields['inventory.loadout'] = character.inventory.loadout;
      setFields['inventory.preparedEquipment'] = character.inventory.preparedEquipment;
    }

    const updated = await Character.findOneAndUpdate(
      { accountId, revision: character.revision,
        ...(guarded ? { 'inventory.pendingReservation.reservationId': expectedReservationId } : {}) },
      {
        $set: setFields,
        $inc: { revision: 1 }
      },
      { new: true }
    );

    if (!updated) {
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Internal revision conflict clearing reservation.' };
    }

    // A rollback restores ownership (a refund, not an expedition); only a loss closes the raid as abandoned.
    if (!rollback && reservation) {
      EconomyTelemetry.recordRaidClose(updated, { outcome: 'abandon', raidId: reservation.raidId, reservation });
    }
    return { pendingReservation: null, revision: updated.revision };
  }

  /**
   * Persists the loot obtained from a successful raid extraction.
   *
   * The operation is idempotent: if the same (raidId, resultSequence) pair
   * was already applied, the method returns { alreadySecured: true } without
   * modifying the document. This allows Unity to safely retry on network failure.
   *
   * Mirrors the invariant enforced by LocalProfileStore.TryCommitExtraction in Unity:
   * the loadout MUST be empty before a new extraction can be committed. This
   * guarantees ítems are never silently duplicated across multiple extractions.
   *
   * @param {string} accountId
   * @param {string} raidId        - RaidGenerationId from the Fusion session
   * @param {number} resultSequence - ResultSequence from the Fusion participant
   * @param {{ lootId: string, amount: number }[]} items - May be empty (no loot run)
   * @throws 404 if no character found.
   * @throws 409 LOADOUT_NOT_EMPTY if the loadout already has items.
   */
  static async commitExtraction(accountId, raidId, resultSequence, items) {
    const character = await Character.findOne({ accountId });
    if (!character) {
      throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    }

    requireMigratedInventory(character);
    const version = inventoryVersion(character);
    items = version === 3 ? validateResultItems(items) : normalizeItems(items);

    // Idempotency check: has this exact extraction already been applied?
    const receipts = character.inventory.appliedExtractionReceipts || [];
    const alreadyApplied = receipts.some(
      r => r.raidId === raidId && r.resultSequence === resultSequence
    );
    if (alreadyApplied) {
      return {
        alreadySecured: true,
        loadout: inventoryItems(character, 'loadout')
      };
    }

    if (character.inventory.loadout && character.inventory.loadout.length > 0) {
      throw { statusCode: 409, errorCode: 'LOADOUT_NOT_EMPTY', message: 'Cannot commit extraction while loadout is not empty.' };
    }

    if (version >= 2) {
      const loadout = [];
      for (const item of items) receiveItem(loadout, 'loadout', item, version);
      const updated = await Character.findOneAndUpdate(
        { accountId, revision: character.revision,
          'inventory.appliedExtractionReceipts': { $not: { $elemMatch: { raidId } } } },
        { $set: { 'inventory.loadout': loadout,
            ...(character.inventory.pendingReservation ? { 'inventory.preparedEquipment': version === 3
              ? resultEquipmentToInstances(equipmentToResult(character.inventory.pendingReservation.preparedEquipment))
              : character.inventory.pendingReservation.preparedEquipment } : {}) },
          $unset: { 'inventory.pendingReservation': 1 }, $inc: { revision: 1 },
          $push: { 'inventory.appliedExtractionReceipts': { raidId, resultSequence } } },
        { returnDocument: 'after' }
      );
      if (!updated) throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict during extraction commit.' };
      return { alreadySecured: false, loadout: inventoryItems(updated, 'loadout') };
    }

    // FIX-33: Atomic revision-guarded write for v1 legacy extraction so concurrent
    // shop operations cannot silently overwrite extracted loot between read and $set.
    const nextLoadout = [];
    if (items && items.length > 0) {
      for (const item of items) {
        const existing = nextLoadout.find(i => i.lootId === item.lootId);
        if (existing) {
          existing.amount += item.amount;
        } else {
          nextLoadout.push({ lootId: item.lootId, amount: item.amount });
        }
      }
    }

    const setFields = { 'inventory.loadout': nextLoadout };
    if (character.inventory.pendingReservation) {
      setFields['inventory.preparedEquipment'] = character.inventory.pendingReservation.preparedEquipment || {};
      setFields['inventory.pendingReservation'] = null;
    }

    const expectedRevision = character.revision ?? 0;
    const updated = await Character.findOneAndUpdate(
      {
        accountId,
        revision: expectedRevision,
        'inventory.loadout': { $size: 0 },
        'inventory.appliedExtractionReceipts': { $not: { $elemMatch: { raidId, resultSequence } } }
      },
      {
        $set: setFields,
        $inc: { revision: 1 },
        $push: { 'inventory.appliedExtractionReceipts': { raidId, resultSequence } }
      },
      { returnDocument: 'after' }
    );

    if (!updated) {
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict during extraction commit.' };
    }

    return {
      alreadySecured: false,
      loadout: serializeItems(updated.inventory.loadout)
    };
  }

  /**
  /**
   * Processes a shop sale. Removes items from the loadout and adds authoritative currency.
   * 
   * INVARIANT: Currency balance is calculated strictly from the server's authoritative
   * catalog (shopCatalog.js), ignoring any client declared value.
   */
  static async shopSell(accountId, lootId, amount, declaredSellValue, expectedRevision, transactionId, sourceSlot) {
    const character = await Character.findOne({ accountId });
    if (!character) {
      throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    }

    requireSourceSlot(character, sourceSlot);
    const version = inventoryVersion(character);
    if (version === 1) assertUnambiguousItems(character.inventory.loadout, [lootId]);

    if (!Number.isInteger(amount) || amount < 1 || amount > MAX_TRANSACTION_AMOUNT) {
      throw {
        statusCode: 400,
        errorCode: 'INVALID_QUANTITY',
        message: `Amount must be an integer between 1 and ${MAX_TRANSACTION_AMOUNT}.`
      };
    }

    lootId = normalizeLootId(lootId);
    
    // 1. Idempotency check with strict payload binding:
    // If already applied with identical intent, return immediately.
    // If transactionId was reused with a different intent, reject as conflict.
    if (character.appliedShopReceipts) {
      const existingReceipt = character.appliedShopReceipts.find(r => r.transactionId === transactionId);
      if (existingReceipt) {
        if (existingReceipt.type !== 'sell' ||
            (version >= 2 && existingReceipt.sourceSlot !== sourceSlot) ||
            (existingReceipt.lootId && existingReceipt.lootId !== lootId) ||
            (existingReceipt.amount && existingReceipt.amount !== amount)) {
          throw {
            statusCode: 409,
            errorCode: 'TRANSACTION_ID_REUSED',
            message: 'Transaction ID was previously applied with a different operation or parameters.'
          };
        }
        return { ...shopSnapshot(character), alreadySecured: true };
      }
    }
    assertWithinRetention(character, transactionId);

    // Applied receipts remain replayable during a reservation; only new mutations are blocked.
    if (character.inventory.pendingReservation) {
      throw { statusCode: 409, errorCode: 'RESERVATION_PENDING', message: 'Cannot trade while a raid reservation is pending.' };
    }

    // 2. Validate revision for new mutations
    if (character.revision !== expectedRevision) {
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict.' };
    }

    const loadout = character.inventory.loadout;
    // Same weekly demand (+20%, Doc 23 §7) as cart sales; buyback derives from the gold actually paid.
    const { revenue: authoritativeRevenue, sale, buybackEntry } =
      applyShopSale(loadout, version, lootId, amount, sourceSlot, getWeeklyRotation().demandItems);
    const buybackPush = buybackEntry ? {
      'inventory.buyback.entries': { $each: [buybackEntry], $slice: -BUYBACK_MAX_ENTRIES }
    } : {};
    const updated = await Character.findOneAndUpdate(
      { accountId, revision: expectedRevision, 'inventory.pendingReservation': null,
        'inventory.buyback.sessionEpoch': buybackEpochFilter(character.inventory) },
      {
        $set: { 'inventory.loadout': loadout },
        $inc: { revision: 1, 'inventory.currency': authoritativeRevenue },
        ...watermarkUpdate(character),
        $push: {
          ...buybackPush,
          appliedShopReceipts: receiptPush({ transactionId, type: 'sell', lootId, amount, ...(version >= 2 ? { sourceSlot } : {}), totalAmount: authoritativeRevenue, timestamp: new Date() })
        }
      },
      { new: true, returnDocument: 'after' }
    );

    if (!updated) {
      const latest = await Character.findOne({ accountId });
      if (latest?.inventory?.pendingReservation) {
        throw { statusCode: 409, errorCode: 'RESERVATION_PENDING', message: 'Cannot trade while a raid reservation is pending.' };
      }
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict during shop sale.' };
    }

    EconomyTelemetry.recordCommerce(updated, 'sell', { sales: [sale] });
    return shopSnapshot(updated);
  }

  /**
   * Processes a shop purchase. Removes currency and adds items to the loadout.
   * 
   * INVARIANT: Currency balance is calculated strictly from the server's authoritative
   * catalog (shopCatalog.js), ignoring any client declared price.
   * Enforced locally and atomically by the MongoDB query filter
   * ('inventory.currency': { $gte: authoritativeCost }).
   */
  static async shopBuy(accountId, lootId, amount, declaredPrice, expectedRevision, transactionId) {
    const character = await Character.findOne({ accountId });
    if (!character) {
      throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    }

    requireMigratedInventory(character);
    const version = inventoryVersion(character);
    if (version === 1) assertUnambiguousItems(character.inventory.loadout, [lootId]);

    if (!Number.isInteger(amount) || amount < 1 || amount > MAX_TRANSACTION_AMOUNT) {
      throw {
        statusCode: 400,
        errorCode: 'INVALID_QUANTITY',
        message: `Amount must be an integer between 1 and ${MAX_TRANSACTION_AMOUNT}.`
      };
    }

    lootId = normalizeLootId(lootId);
    
    // 1. Idempotency check with strict payload binding:
    // If already applied with identical intent, return immediately.
    // If transactionId was reused with a different intent, reject as conflict.
    if (character.appliedShopReceipts) {
      const existingReceipt = character.appliedShopReceipts.find(r => r.transactionId === transactionId);
      if (existingReceipt) {
        if (existingReceipt.type !== 'buy' ||
            (existingReceipt.lootId && existingReceipt.lootId !== lootId) ||
            (existingReceipt.amount && existingReceipt.amount !== amount)) {
          throw {
            statusCode: 409,
            errorCode: 'TRANSACTION_ID_REUSED',
            message: 'Transaction ID was previously applied with a different operation or parameters.'
          };
        }
        return { ...shopSnapshot(character), alreadySecured: true };
      }
    }
    assertWithinRetention(character, transactionId);

    // Applied receipts remain replayable during a reservation; only new mutations are blocked.
    if (character.inventory.pendingReservation) {
      throw { statusCode: 409, errorCode: 'RESERVATION_PENDING', message: 'Cannot trade while a raid reservation is pending.' };
    }

    // 2. Validate revision for new mutations
    if (character.revision !== expectedRevision) {
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict.' };
    }

    const authoritativeCost = shopPurchaseCost(lootId, amount);
    
    const currentCurrency = character.inventory.currency || 0;
    if (currentCurrency < authoritativeCost) {
      throw {
        statusCode: 409,
        errorCode: 'INSUFFICIENT_FUNDS',
        message: `Not enough currency to purchase '${lootId}'. Required: ${authoritativeCost}, available: ${currentCurrency}.`
      };
    }

    const loadout = character.inventory.loadout;
    EconomyTelemetry.trackCapacityBlock(character, 'purchase', () => receiveItem(loadout, 'loadout', { lootId, amount }, version));

    const updated = await Character.findOneAndUpdate(
      { accountId, revision: expectedRevision, 'inventory.pendingReservation': null, 'inventory.currency': { $gte: authoritativeCost } },
      {
        $set: { 'inventory.loadout': loadout },
        $inc: { revision: 1, 'inventory.currency': -authoritativeCost },
        ...watermarkUpdate(character),
        $push: {
          appliedShopReceipts: receiptPush({ transactionId, type: 'buy', lootId, amount, totalAmount: authoritativeCost, timestamp: new Date() })
        }
      },
      { new: true, returnDocument: 'after' }
    );

    if (!updated) {
      const latest = await Character.findOne({ accountId });
      if (latest?.inventory?.pendingReservation) {
        throw { statusCode: 409, errorCode: 'RESERVATION_PENDING', message: 'Cannot trade while a raid reservation is pending.' };
      }
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict or insufficient funds during shop purchase.' };
    }

    EconomyTelemetry.recordCommerce(updated, 'buy', { purchases: [{ lootId, amount, cost: authoritativeCost }] });
    return shopSnapshot(updated);
  }
  /** Rebuys a sale line at its frozen price, within the current Town epoch. */
  static async shopBuyback(accountId, entryId, amount, expectedRevision, transactionId) {
    const character = await Character.findOne({ accountId });
    if (!character) throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    requireMigratedInventory(character);
    const version = inventoryVersion(character);
    if (!Number.isInteger(amount) || amount < 1 || amount > MAX_TRANSACTION_AMOUNT) {
      throw { statusCode: 400, errorCode: 'INVALID_QUANTITY', message: 'Invalid buyback quantity.' };
    }
    const receipt = (character.appliedShopReceipts || []).find(r => r.transactionId === transactionId);
    if (receipt) {
      if (receipt.type !== 'buyback' || receipt.entryId !== entryId || receipt.amount !== amount) {
        throw { statusCode: 409, errorCode: 'TRANSACTION_ID_REUSED', message: 'Transaction ID was previously applied with different parameters.' };
      }
      return { ...shopSnapshot(character), alreadySecured: true };
    }
    assertWithinRetention(character, transactionId);
    if (character.inventory.pendingReservation) {
      throw { statusCode: 409, errorCode: 'RESERVATION_PENDING', message: 'Cannot trade while a raid reservation is pending.' };
    }
    if (character.revision !== expectedRevision) {
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict.' };
    }
    const entry = character.inventory.buyback?.entries?.find(e => e.entryId === entryId);
    if (!entry) throw { statusCode: 404, errorCode: 'BUYBACK_ENTRY_NOT_FOUND', message: 'Buyback entry not found in this Town session.' };
    // v3 disables equipment buy-back, including ledger entries sold before the instance migration.
    if (isTrainingWeapon(entry.lootId) || (version >= 3 && (!isBuybackEligible(entry.lootId, version) || (getEquipmentTemplate(entry.lootId) && !entry.instance)))) {
      throw { statusCode: 409, errorCode: 'BUYBACK_NOT_ELIGIBLE', message: 'This item cannot be bought back.' };
    }
    if (amount > entry.amount) throw { statusCode: 409, errorCode: 'INSUFFICIENT_BUYBACK_UNITS', message: 'Not enough units in this sale line.' };
    const cost = entry.unitPrice * amount;
    if (!Number.isSafeInteger(cost) || cost < 0) throw { statusCode: 400, errorCode: 'INTEGER_OVERFLOW', message: 'Invalid transaction cost.' };
    if ((character.inventory.currency || 0) < cost) throw { statusCode: 409, errorCode: 'INSUFFICIENT_FUNDS', message: 'Not enough currency.' };
    if (version === 1) assertUnambiguousItems(character.inventory.loadout, [entry.lootId]);
    const loadout = inventoryItems(character, 'loadout');
    EconomyTelemetry.trackCapacityBlock(character, 'buyback', () => receiveItem(loadout, 'loadout',
      { lootId: entry.lootId, amount, ...(entry.instance ? { instance: copyEntry(entry.instance) } : {}) }, version));
    const entryUpdate = amount === entry.amount
      ? { $pull: { 'inventory.buyback.entries': { entryId } } }
      : {};
    const increments = { revision: 1, 'inventory.currency': -cost };
    if (amount < entry.amount) increments['inventory.buyback.entries.$.amount'] = -amount;
    const updated = await Character.findOneAndUpdate({
      accountId, revision: expectedRevision, 'inventory.pendingReservation': null,
      'inventory.currency': { $gte: cost },
      'inventory.buyback.sessionEpoch': buybackEpochFilter(character.inventory),
      'inventory.buyback.entries': { $elemMatch: { entryId, amount: { $gte: amount } } }
    }, {
      ...entryUpdate,
      $set: { 'inventory.loadout': loadout },
      $inc: increments,
      ...watermarkUpdate(character),
      $push: { appliedShopReceipts: receiptPush({ transactionId, type: 'buyback', entryId,
        lootId: entry.lootId, amount, totalAmount: cost, timestamp: new Date() }) }
    }, { returnDocument: 'after' });
    if (!updated) {
      const latest = await Character.findOne({ accountId });
      if (latest?.inventory?.pendingReservation) throw { statusCode: 409, errorCode: 'RESERVATION_PENDING', message: 'Cannot trade while a raid reservation is pending.' };
      throw { statusCode: 409, errorCode: 'REVISION_CONFLICT', message: 'Revision conflict during buyback.' };
    }
    EconomyTelemetry.recordCommerce(updated, 'buyback', { buybacks: [{ lootId: entry.lootId, amount, cost }] });
    return shopSnapshot(updated);
  }

  /**
   * Returns commercial state for a merchant or all merchants including rotation, offers,
   * active demand, remaining player personal stock, and shared buyback ledger.
   */
  static async getCommercialState(accountId, merchantType = '') {
    const character = await Character.findOne({ accountId });
    if (!character) throw { statusCode: 404, errorCode: 'CHARACTER_NOT_FOUND', message: 'No character found for this account.' };
    requireMigratedInventory(character);

    const currentWeek = getWeeklyRotation();
    const isCurrentWeek = character.inventory.weeklyStock?.weekId === currentWeek.weekId;
    const rawPurchased = isCurrentWeek && character.inventory.weeklyStock?.purchased;
    const playerPurchased = new Map();
    if (rawPurchased) {
      if (rawPurchased instanceof Map) {
        for (const [k, v] of rawPurchased.entries()) playerPurchased.set(k, v);
      } else if (typeof rawPurchased === 'object') {
        for (const [k, v] of Object.entries(rawPurchased)) playerPurchased.set(k, v);
      }
    }

    const type = (merchantType || '').toLowerCase().trim();
    const offers = getMerchantOffers(type);

    const attachRemainingStock = (item) => {
      if (item.stock === 'unlimited') return { ...item, remainingStock: 'unlimited' };
      const normalized = normalizeLootId(item.lootId);
      const purchasedCount = playerPurchased.get(normalized) || 0;
      return {
        ...item,
        remainingStock: Math.max(0, (item.stock || 1) - purchasedCount)
      };
    };

    let decoratedOffers = {};
    if (type === 'general') {
      decoratedOffers = {
        permanent: (offers.permanent || []).map(attachRemainingStock),
        rotating: (offers.rotating || []).map(attachRemainingStock),
        demand: offers.demand || []
      };
    } else if (type === 'blacksmith') {
      decoratedOffers = {
        weapons: (offers.weapons || []).map(attachRemainingStock),
        specialWeapon: offers.specialWeapon ? attachRemainingStock(offers.specialWeapon) : null,
        armors: (offers.armors || []).map(attachRemainingStock)
      };
    } else if (type === 'arcanist') {
      decoratedOffers = {
        weapons: (offers.weapons || []).map(attachRemainingStock),
        specialWeapon: offers.specialWeapon ? attachRemainingStock(offers.specialWeapon) : null
      };
    } else {
      decoratedOffers = {
        normalWeapons: (currentWeek.normalWeapons || []).map(attachRemainingStock),
        specialWeapon: currentWeek.specialWeapon ? attachRemainingStock(currentWeek.specialWeapon) : null,
        armors: (currentWeek.armors || []).map(attachRemainingStock),
        utilities: (currentWeek.utilities || []).map(attachRemainingStock),
        demandItems: currentWeek.demandItems || [],
        permanentOffers: (currentWeek.permanentOffers || []).map(attachRemainingStock)
      };
    }

    return {
      merchant: type || 'all',
      weekId: currentWeek.weekId,
      nextRotationTimestamp: currentWeek.nextRotationTimestamp,
      currency: character.inventory.currency || 0,
      revision: character.revision,
      offers: decoratedOffers,
      buyback: serializeBuyback(character.inventory)
    };
  }
}

module.exports = InventoryService;
