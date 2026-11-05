import test from 'node:test';
import assert from 'node:assert/strict';
import { applicationUpdateContent } from './applicationUpdates.js';

test('sent and legacy updates show the same natural application message', () => {
  const sent = applicationUpdateContent('sent');
  assert.equal(sent.title, 'Your application was sent');
  assert.doesNotMatch(`${sent.title} ${sent.detail}`, /demo|simulat/i);
  assert.deepEqual(applicationUpdateContent('needs_details'), sent);
  assert.match(applicationUpdateContent('unexpected').title, /unavailable/i);
});
