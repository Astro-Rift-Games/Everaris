'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { MISSION_CATALOG, getMissionDefinition } = require('../src/config/missionCatalog');

const missionsDir = path.resolve(__dirname, '../../Project Grimhold/Assets/Scriptable Objects/Missions');
const TYPE_BY_ENUM = Object.freeze({ 0: 'Normal', 1: 'Unica', 2: 'Semanal' });
const RANK_BY_ENUM = Object.freeze({ 0: 'E', 1: 'D', 2: 'C', 3: 'B', 4: 'A', 5: 'S' });

function missionAssetFiles(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const file = path.join(directory, entry.name);
    if (entry.isDirectory()) return missionAssetFiles(file);
    return entry.name.startsWith('Mission_') && entry.name.endsWith('.asset') ? [file] : [];
  });
}

function parseGoldReward(yamlText) {
  const rewardsIdx = yamlText.indexOf('Rewards:');
  if (rewardsIdx === -1) return 0;
  const rewardsSection = yamlText.slice(rewardsIdx);
  let totalGold = 0;
  const matches = rewardsSection.matchAll(/- Type:\s*(\d+)\s*\r?\n\s*Amount:\s*(\d+)/g);
  for (const match of matches) {
    const rewardType = Number(match[1]);
    const amount = Number(match[2]);
    if (rewardType === 2) totalGold += amount; // RewardDefinition.RewardType.Gold == 2
  }
  return totalGold;
}

test('backend missionCatalog mirrors every Unity MissionDefinition gold reward, type, and rank', t => {
  if (!fs.existsSync(missionsDir)) {
    t.skip('Unity Missions folder is absent (backend deployed alone); parity cannot be checked.');
    return;
  }

  const unityIds = new Set();
  const mismatches = [];

  for (const file of missionAssetFiles(missionsDir)) {
    const text = fs.readFileSync(file, 'utf8');
    const id = text.match(/^\s*_id:\s*(\S+)\s*$/m)?.[1];
    if (!id) continue;
    unityIds.add(id);

    const typeEnum = Number(text.match(/^\s*Type:\s*(\d+)\s*$/m)?.[1] ?? 0);
    const rankEnum = Number(text.match(/^\s*RankRequired:\s*(\d+)\s*$/m)?.[1] ?? 0);
    const expectedType = TYPE_BY_ENUM[typeEnum];
    const expectedRank = RANK_BY_ENUM[rankEnum];
    const expectedGold = parseGoldReward(text);

    const entry = getMissionDefinition(id);
    if (!entry) {
      mismatches.push(`${id}: missing from backend/src/config/missionCatalog.js`);
      continue;
    }
    if (entry.type !== expectedType) mismatches.push(`${id}: type expected ${expectedType}, got ${entry.type}`);
    if (entry.rank !== expectedRank) mismatches.push(`${id}: rank expected ${expectedRank}, got ${entry.rank}`);
    if (entry.goldReward !== expectedGold) mismatches.push(`${id}: goldReward expected ${expectedGold}, got ${entry.goldReward}`);
  }

  for (const backendId of Object.keys(MISSION_CATALOG)) {
    if (!unityIds.has(backendId)) {
      mismatches.push(`${backendId}: present in missionCatalog.js but missing in Unity assets`);
    }
  }

  assert.ok(unityIds.size > 0, 'Expected at least one Unity MissionDefinition asset');
  assert.equal(mismatches.length, 0, `Mission catalog parity mismatches:\n${mismatches.join('\n')}`);
});
