import test from 'node:test';
import assert from 'node:assert/strict';
import { groupTenderResults, matchCategory, matchScore, prependMatch, sortTenderResults } from './tenderResults.js';

test('new streamed matches appear first among ties without duplicating an existing tender', () => {
  const first = { tender: { id: 1 }, match: { matchScore: 90 } };
  const second = { tender: { id: 2 }, match: { matchScore: 90 } };
  const updatedFirst = { tender: { id: 1 }, match: { matchScore: 90, feedback: 'like' } };
  assert.deepEqual(prependMatch(prependMatch([], first), second), [second, first]);
  assert.deepEqual(prependMatch([second, first], updatedFirst), [updatedFirst, second]);
});

test('similar lots share one ranked group without losing their individual state', () => {
  const first = { groupKey: 'same', tender: { id: 1 }, match: { matchScore: 90, appliedAt: 'today' } };
  const second = { groupKey: 'same', tender: { id: 2 }, match: { matchScore: 88 } };
  const separate = { groupKey: 'other', tender: { id: 3 }, match: { matchScore: 85 } };
  const unscored = { groupKey: 'same', tender: { id: 4 }, match: { matchScore: 74 } };
  assert.deepEqual(groupTenderResults([separate, second, first, unscored]), [
    { key: 'same', lots: [first, second] }, { key: 'other', lots: [separate] }
  ]);
  assert.equal(groupTenderResults([{ tender: { id: 5 }, match: { matchScore: 80 } }])[0].key, 'lot:5');
});

test('fit categories use the requested boundaries without inventing legacy scores', () => {
  for (const [score, label] of [[75, 'Might interest you'], [84, 'Might interest you'], [85, 'Best match'], [100, 'Best match']]) {
    assert.equal(matchCategory({ matchScore: score }).label, label);
  }
  for (const score of [1, 69, 70, 74]) assert.equal(matchCategory({ matchScore: score }), null);
  for (const score of [null, undefined, 0, 101, 85.5, '85']) {
    assert.equal(matchScore({ matchScore: score }), null);
    assert.equal(matchCategory({ matchScore: score }), null);
  }
});

test('saved and streamed matches exclude unscored and low fits and order the rest stably', () => {
  const result = (id, score) => ({ tender: { id }, match: { matchScore: score } });
  const low = result(1, 74), high = result(2, 92), same = result(3, 92), legacy = result(4, null), middle = result(5, 75);
  const saved = [low, high, same, legacy, middle];
  assert.deepEqual(sortTenderResults(saved), [high, same, middle]);
  assert.deepEqual(saved, [low, high, same, legacy, middle]);
  const added = result(6, 84);
  assert.deepEqual(prependMatch(sortTenderResults(saved), added), [high, same, added, middle]);
  assert.deepEqual(prependMatch(sortTenderResults(saved), low), [high, same, middle]);
  const updated = result(1, 100);
  assert.deepEqual(prependMatch(saved, updated), [updated, high, same, middle]);
});
