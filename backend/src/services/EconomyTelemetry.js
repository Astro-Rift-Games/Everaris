'use strict';

const EconomyTelemetryEvent = require('../models/EconomyTelemetryEvent');
const { PERMANENT_BUY_CATALOG, getEquipmentTemplate, getBuyOffer, getSellOffer } = require('../config/shopCatalog');
const { UTILITY_POOL } = require('./CommerceRotationService');
const { normalizeLootId } = require('./InventoryLootIdNormalizer');
const { EQUIPMENT_SLOTS } = require('../config/equipmentSlots');
const { objectValue } = require('./ItemUpgrades');
const { isTrainingWeapon } = require('./TrainingWeapons');

/*
 * Internal economy telemetry (TASK-305). Callers record only after their guarded write
 * succeeded, so a rejected or replayed operation never reaches this module. Recording is
 * fire-and-forget and at-most-once: a sink failure is logged and never fails or delays the
 * committed operation. Nothing here feeds prices or gameplay (Doc 22 §14).
 */
const pending = new Set();

function buildEvent({ characterId, operation, gold = [], balanceAfter = null, details }) {
  const lines = (gold || []).filter(line => line && line.amount > 0).map(line => ({
    direction: line.direction, category: line.category, amount: line.amount,
    ...(line.offerType ? { offerType: line.offerType } : {})
  }));
  const total = direction => lines.filter(line => line.direction === direction).reduce((sum, line) => sum + line.amount, 0);
  const goldGenerated = total('generated');
  const goldSpent = total('spent');
  return {
    characterId: String(characterId), operation, gold: lines, goldGenerated, goldSpent,
    netGold: goldGenerated - goldSpent,
    balanceAfter: Number.isFinite(balanceAfter) ? balanceAfter : null,
    ...(details ? { details } : {})
  };
}

/** Records one confirmed economic event. Resolves once stored or dropped; never rejects. */
function record(event) {
  const write = (async () => {
    try {
      await EconomyTelemetryEvent.create(buildEvent(event || {}));
    } catch (error) {
      console.warn(`[EconomyTelemetry] dropped ${event?.operation} event: ${error?.message}`);
    }
  })();
  pending.add(write);
  write.finally(() => pending.delete(write));
  return write;
}

/** Waits for in-flight records; used by tests and graceful shutdown. */
async function flush() {
  await Promise.all([...pending]);
}

/** Catalog family of a buy offer, for "distribución de compras por oferta" (Doc 23 §12). */
function purchaseOfferType(lootId) {
  const id = normalizeLootId(lootId);
  if (PERMANENT_BUY_CATALOG[id]) return 'consumable';
  if (UTILITY_POOL.includes(id)) return 'utility';
  const template = getEquipmentTemplate(id);
  if (template?.category === 'Weapon') return 'weapon';
  if (template) return 'armor';
  return 'other';
}

/**
 * Records a committed NPC commerce operation from its server-computed lines:
 * sales [{ lootId, amount, revenue, demandBonus, demandBonusGold }], purchases [{ lootId, amount, cost }],
 * buybacks [{ lootId, amount, cost }].
 */
function recordCommerce(character, operation, { sales = [], purchases = [], buybacks = [], merchant } = {}) {
  const typedPurchases = purchases.map(line => ({ ...line, offerType: purchaseOfferType(line.lootId) }));
  const sum = (lines, key) => lines.reduce((total, line) => total + line[key], 0);
  const purchaseGold = new Map();
  for (const line of typedPurchases) purchaseGold.set(line.offerType, (purchaseGold.get(line.offerType) || 0) + line.cost);
  const bonusSales = sales.filter(line => line.demandBonus);
  return record({
    characterId: character._id,
    operation,
    gold: [
      { direction: 'generated', category: 'npc_sale', amount: sum(sales, 'revenue') },
      ...[...purchaseGold].map(([offerType, amount]) => ({ direction: 'spent', category: 'purchase', offerType, amount })),
      { direction: 'spent', category: 'buyback', amount: sum(buybacks, 'cost') }
    ],
    balanceAfter: character.inventory?.currency,
    details: {
      ...(merchant ? { merchant } : {}),
      sales: sales.map(({ lootId, amount, revenue, demandBonus }) => ({ lootId, amount, revenue, demandBonus })),
      purchases: typedPurchases,
      buybacks,
      demandBonusUnits: sum(bonusSales, 'amount'),
      demandBonusGold: sum(bonusSales, 'demandBonusGold')
    }
  });
}

const UPGRADE_GOLD_CATEGORY = Object.freeze({
  primary_scaling: 'weapon_upgrade', first_socket: 'weapon_upgrade', armor_level: 'armor_upgrade'
});

/** Level reached by one upgrade: primary grade, socket count or armor upgradeLevel (Doc 23 §9). */
function upgradeLevelAfter(upgradeOperation, modifiers) {
  if (upgradeOperation === 'primary_scaling') return modifiers?.primary?.grade ?? null;
  if (upgradeOperation === 'first_socket') return modifiers?.gemSocketCount ?? 0;
  return modifiers?.upgradeLevel ?? 0;
}

/** Records a committed NPC upgrade: { merchant, upgradeOperation, lootId, gold, materials, modifiers, objectValueAfter }. */
function recordUpgrade(character, { merchant, upgradeOperation, lootId, gold, materials, modifiers, objectValueAfter }) {
  return record({
    characterId: character._id,
    operation: 'upgrade',
    gold: [{ direction: 'spent', category: UPGRADE_GOLD_CATEGORY[upgradeOperation], amount: gold }],
    balanceAfter: character.inventory?.currency,
    details: { merchant, upgradeOperation, lootId, levelAfter: upgradeLevelAfter(upgradeOperation, modifiers), materials, objectValueAfter }
  });
}

/** Records a committed respec: the configured gold cost and the attribute points returned to the pool. */
function recordRespec(character, { cost, pointsReturned }) {
  return record({
    characterId: character._id,
    operation: 'respec',
    gold: [{ direction: 'spent', category: 'respec', amount: cost }],
    balanceAfter: character.inventory?.currency,
    details: { pointsReturned }
  });
}

/** Records a committed authoritative mission reward claim (TASK-310, Doc 22 §6). */
function recordMissionReward(character, { missionId, claimKey, gold }) {
  return record({
    characterId: character._id,
    operation: 'mission_reward',
    gold: gold > 0 ? [{ direction: 'generated', category: 'mission_reward', amount: gold }] : [],
    balanceAfter: character.inventory?.currency,
    details: { missionId, claimKey }
  });
}

/*
 * Entry profile (Doc 22 §5: Naked / Budget / Geared) is approximate (S5 "perfil aproximado"): it is
 * classified from the replacement gold (buy price) of the non-training prepared equipment carried
 * into the raid. The Geared threshold is a PROVISIONAL value taken from the Doc 22 §10 simulation
 * assumptions (Geared ~800 Oro de reemplazo). It is an analytics bucket, not a balance value, and
 * must never feed prices, drops or any gameplay rule. Raw carried values are stored on each event
 * so analysis can reclassify with other thresholds.
 */
const ENTRY_PROFILE_NAKED_MAX_GOLD = 0;
const ENTRY_PROFILE_GEARED_MIN_GOLD = 800;

/** 0 → naked, below the Geared threshold → budget, otherwise geared. */
function classifyEntryProfile(replacementGold) {
  if (!(replacementGold > ENTRY_PROFILE_NAKED_MAX_GOLD)) return 'naked';
  return replacementGold < ENTRY_PROFILE_GEARED_MIN_GOLD ? 'budget' : 'geared';
}

// Reservation and result entries are { lootId, amount, instance? } (items), { lootId, instance } (v3
// equipment) or a bare lootId string (legacy equipment). Anything unrecognized counts as nothing.
function asEntry(value, amount = 1) {
  if (!value) return null;
  if (typeof value === 'string') return { lootId: value, amount };
  return typeof value.lootId === 'string' && value.lootId ? { lootId: value.lootId, amount: value.amount ?? amount, instance: value.instance } : null;
}

function equipmentEntries(preparedEquipment) {
  return EQUIPMENT_SLOTS.map(slot => asEntry(preparedEquipment?.[slot])).filter(Boolean);
}

/** Material value of one stack: equipment by object value, other items by NPC sell value, training weapons 0. */
function entryValue({ lootId, amount, instance }) {
  if (isTrainingWeapon(lootId)) return 0;
  const unit = getEquipmentTemplate(lootId) ? objectValue(lootId, instance) : getSellOffer(lootId)?.sellValue ?? 0;
  return unit * amount;
}

const sumValues = entries => entries.reduce((total, entry) => total + entryValue(entry), 0);

function replacementGold(preparedEquipment) {
  return equipmentEntries(preparedEquipment)
    .filter(entry => !isTrainingWeapon(entry.lootId))
    .reduce((total, entry) => total + (getBuyOffer(entry.lootId)?.buyPrice ?? 0), 0);
}

function carriedEntries(reservation) {
  const items = [...(reservation?.items || [])].map(item => asEntry(item)).filter(Boolean);
  return [...items, ...equipmentEntries(reservation?.preparedEquipment)];
}

/**
 * Records the close of one expedition after its guarded write succeeded. `outcome` comes from the
 * service that committed it: 'extraction' (items/equipment brought back in `extracted`), 'defeat' or
 * 'abandon' (everything carried is lost; an abandon that lost nothing records nothing). Never throws: a malformed snapshot only drops the event.
 */
function recordRaidClose(character, { outcome, raidId, reservation, extracted, experienceGranted = 0 }) {
  try {
    const carried = carriedEntries(reservation);
    if (outcome === 'abandon' && carried.length === 0) return Promise.resolve(); // nothing was lost
    const carriedItemsValue = sumValues(carried);
    const equipmentGold = replacementGold(reservation?.preparedEquipment);
    const lost = new Map();
    if (outcome !== 'extraction') {
      for (const { lootId, amount } of carried) lost.set(lootId, (lost.get(lootId) || 0) + amount);
    }
    return record({
      characterId: character._id,
      operation: 'raid_close',
      balanceAfter: character.inventory?.currency,
      details: {
        outcome, raidId: raidId ?? null, entryProfile: classifyEntryProfile(equipmentGold),
        carriedEquipmentReplacementGold: equipmentGold, carriedItemsValue,
        ...(outcome === 'extraction'
          ? { extractedItemsValue: sumValues(extracted || []), lostValue: 0 }
          : { lostValue: carriedItemsValue, lostItems: [...lost].map(([lootId, amount]) => ({ lootId, amount })) }),
        experienceGranted
      }
    });
  } catch (error) {
    console.warn(`[EconomyTelemetry] dropped raid_close event: ${error?.message}`);
    return Promise.resolve();
  }
}

/** Entries of an extraction's final loadout and prepared equipment, as valued by `recordRaidClose`. */
function extractedEntries(loadout, preparedEquipment) {
  return [...(loadout || [])].map(item => asEntry(item)).filter(Boolean).concat(equipmentEntries(preparedEquipment));
}

/** Records that a new operation was refused for lack of inventory capacity (Doc 23 §12 "tasa de bloqueo"). */
function recordCapacityBlock(character, source) {
  return record({ characterId: character._id, operation: 'capacity_block', details: { source, limit: 'capacity' } });
}

/**
 * Runs a loadout placement; when it is refused with INVENTORY_FULL the block is recorded and the
 * original error is rethrown unchanged, so the caller's rejection (and its unwritten state) is intact.
 */
function trackCapacityBlock(character, source, place) {
  try {
    return place();
  } catch (error) {
    if (error?.errorCode === 'INVENTORY_FULL') recordCapacityBlock(character, source);
    throw error;
  }
}

module.exports = {
  record, recordCommerce, recordUpgrade, recordRespec, recordMissionReward, recordRaidClose, recordCapacityBlock, trackCapacityBlock,
  flush, purchaseOfferType, buildEvent, classifyEntryProfile, extractedEntries,
  ENTRY_PROFILE_NAKED_MAX_GOLD, ENTRY_PROFILE_GEARED_MIN_GOLD
};
