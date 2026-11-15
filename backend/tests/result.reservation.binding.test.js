'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const AuthoritativeExtractionResult = require('../src/models/AuthoritativeExtractionResult');
const { publishedResultSequence, isDefeatResult } = require('../src/services/ResultReservationBinding');
const { EQUIPMENT_SLOTS } = require('../src/config/equipmentSlots');
const legacy = resultId => new AuthoritativeExtractionResult({ raidId: 'raid-1', accountId: 'acc1', resultId, experienceGranted: 0 });

test('legacy Unity ID <raidId>:<characterId>:<sequence> recovers the published sequence', () => {
  assert.equal(legacy('raid-1:char9:3').characterId, undefined);
  assert.equal(publishedResultSequence(legacy('raid-1:char9:3')), 3);
  assert.equal(publishedResultSequence(legacy('raid-1:acc1:3')), 3);
});
test('malformed legacy sequences fail closed', () => {
  for (const id of ['raid-1:acc1:03', 'raid-1:acc1:0', 'raid-1:acc1:', 'raid-1:acc1:3:4', 'raid-1:acc1:-3',
    'raid-1:acc1:3.0', 'raid-1:acc1:9007199254740993', 'raid-1:acc1', 'opaque', 'raid-2:acc1:3', 'raid-1::3'])
    assert.equal(publishedResultSequence(legacy(id)), null, id);
});
test('a stored sequence takes precedence over the ID', () => {
  const r = legacy('raid-1:acc1:3'); r.resultSequence = 7;
  assert.equal(publishedResultSequence(r), 7);
});
test('the Unity defeat payload is defeat-compatible; any owned slot is not', () => {
  const doc = eq => new AuthoritativeExtractionResult({ raidId: 'r', accountId: 'a', items: [], preparedEquipment: eq, experienceGranted: 1 });
  assert.equal(isDefeatResult(doc(Object.fromEntries(EQUIPMENT_SLOTS.map(s => [s, null])))), true);
  assert.equal(isDefeatResult(doc({ weaponSlot1: '', armor: '' })), true);
  for (const s of EQUIPMENT_SLOTS) assert.equal(isDefeatResult(doc({ [s]: { lootId: 'x' } })), false, s);
});
for (const empty of [{}, 'null']) {
  test(`empty equipment encoding ${JSON.stringify(empty)} is defeat-compatible in every slot`, () => {
    for (const slot of EQUIPMENT_SLOTS) {
      const result = new AuthoritativeExtractionResult({ raidId: 'r', accountId: 'a', items: [], preparedEquipment: { [slot]: empty } });
      assert.equal(isDefeatResult(result), true, slot);
    }
  });
}
