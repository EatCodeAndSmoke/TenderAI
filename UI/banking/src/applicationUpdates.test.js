import test from 'node:test';
import assert from 'node:assert/strict';
import { applicationUpdateContent } from './applicationUpdates.js';

test('both outcomes clearly describe a simulation, not a real submission or upload', () => {
  const sent = applicationUpdateContent('sent');
  assert.match(sent.title, /demo/i);
  assert.match(sent.detail, /simulation/i);
  assert.match(sent.detail, /Nothing was actually submitted/);
  const details = applicationUpdateContent('needs_details');
  assert.match(details.detail, /simulation/i);
  assert.match(details.detail, /registration document/);
  assert.match(details.detail, /contact name and phone number/);
  assert.match(details.detail, /no files or information are collected/);
  assert.match(applicationUpdateContent('unexpected').title, /unavailable/i);
});
