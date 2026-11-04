import test from 'node:test';
import assert from 'node:assert/strict';
import { selectInitialAdminRoute } from './initialRoute.js';

test('selects Customers on page load', () => {
  const location = { pathname: '/admin/', search: '?tenant=demo', hash: '#/customers' };
  let replacement;
  const history = {
    state: { restore: true },
    replaceState: (state, title, url) => { replacement = { state, title, url }; }
  };

  const url = selectInitialAdminRoute(location, history);

  assert.equal(url, '/admin/?tenant=demo#/customers');
  assert.deepEqual(replacement, {
    state: { restore: true },
    title: '',
    url: '/admin/?tenant=demo#/customers'
  });
});
