import test from 'node:test';
import assert from 'node:assert/strict';
import { initialTenderProgress, tenderProgressMessage, updateTenderProgress } from './tenderProgress.js';

test('progress reports checked tenders and matches without mistaking the target for results', () => {
  let state = initialTenderProgress();
  state = updateTenderProgress(state, { type: 'started', count: 3 });
  assert.equal(state.found, 0);
  state = updateTenderProgress(state, { type: 'progress', count: 0, checked: 5, phase: 'matching' });
  assert.equal(state.checked, 5);
  state = updateTenderProgress(state, { type: 'match', count: 1, checked: 5 });
  assert.equal(state.found, 1);
  state = updateTenderProgress(state, { type: 'completed', count: 1, checked: 5, limited: true });
  assert.equal(state.completed, true);
  assert.match(tenderProgressMessage(state, false), /Search again/);
  assert.match(tenderProgressMessage(state, true), /stopped/);
});

test('a time-limited search completes without appearing to fail', () => {
  const state = updateTenderProgress(initialTenderProgress(), {
    type: 'completed', count: 1, checked: 5, timeLimited: true
  });
  assert.equal(state.timeLimited, true);
  assert.match(tenderProgressMessage(state, false), /paused after finding a match/);
});
