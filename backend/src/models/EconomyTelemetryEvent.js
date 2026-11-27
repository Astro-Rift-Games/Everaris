const mongoose = require('mongoose');

// Internal playtest analytics (TASK-305, Doc 22 §14, Doc 23 §12). Never read by gameplay or pricing.
const GOLD_SOURCES = Object.freeze(['npc_sale', 'mission_reward']);
const GOLD_SINKS = Object.freeze(['purchase', 'buyback', 'weapon_upgrade', 'armor_upgrade', 'respec']);
const OPERATIONS = Object.freeze([
  'sell', 'buy', 'buyback', 'trade', 'upgrade', 'respec', 'mission_reward', 'raid_close', 'capacity_block',
  'transport_block'
]);

const goldLineSchema = new mongoose.Schema({
  direction: { type: String, required: true, enum: ['generated', 'spent'] },
  category: {
    type: String, required: true, enum: [...GOLD_SOURCES, ...GOLD_SINKS],
    validate: {
      validator(category) {
        return this.direction === 'generated' ? GOLD_SOURCES.includes(category) : GOLD_SINKS.includes(category);
      },
      message: 'Gold category does not match its direction.'
    }
  },
  offerType: { type: String, enum: ['consumable', 'utility', 'weapon', 'armor', 'other'] },
  amount: { type: Number, required: true, min: 1 }
}, { _id: false });

const economyTelemetryEventSchema = new mongoose.Schema({
  // Character _id only: pseudonymous; account identity, usernames and tokens are never stored.
  characterId: { type: String, required: true, index: true },
  operation: { type: String, required: true, enum: OPERATIONS, index: true },
  gold: { type: [goldLineSchema], default: [] },
  goldGenerated: { type: Number, required: true, min: 0 },
  goldSpent: { type: Number, required: true, min: 0 },
  netGold: { type: Number, required: true },
  balanceAfter: { type: Number, default: null },
  details: { type: mongoose.Schema.Types.Mixed, default: undefined },
  occurredAt: { type: Date, default: Date.now, index: true }
}, { strict: 'throw', versionKey: false, minimize: false });

module.exports = mongoose.model('EconomyTelemetryEvent', economyTelemetryEventSchema);
module.exports.GOLD_SOURCES = GOLD_SOURCES;
module.exports.GOLD_SINKS = GOLD_SINKS;
module.exports.OPERATIONS = OPERATIONS;
