import test from 'node:test';
import assert from 'node:assert/strict';
import { selectInitialAdminRoute } from './initialRoute.js';

test('selects Tenders on page load even if the previous hash points to Customers', () => {
  const location = { pathname: '/admin/', search: '?tenant=demo', hash: '#/customers' };
  let replacement;
  const history = {
    state: { restore: true },
    replaceState: (state, title, url) => { replacement = { state, title, url }; }
  };

  const url = selectInitialAdminRoute(location, history);

  assert.equal(url, '/admin/?tenant=demo#/tenders');
  assert.deepEqual(replacement, {
    state: { restore: true },
    title: '',
    url: '/admin/?tenant=demo#/tenders'
  });
});
