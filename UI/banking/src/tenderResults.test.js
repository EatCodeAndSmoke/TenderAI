import test from 'node:test';
import assert from 'node:assert/strict';
import { prependMatch } from './tenderResults.js';

test('new streamed matches appear first without duplicating an existing tender', () => {
  const first = { tender: { id: 1 } };
  const second = { tender: { id: 2 } };
  const updatedFirst = { tender: { id: 1 }, match: { feedback: 'like' } };
  assert.deepEqual(prependMatch(prependMatch([], first), second), [second, first]);
  assert.deepEqual(prependMatch([second, first], updatedFirst), [updatedFirst, second]);
});
