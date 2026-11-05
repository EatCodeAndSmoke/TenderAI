import test from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { createServer } from 'vite';

test('application confirmation only offers financing when a current offer exists', async () => {
  const server = await createServer({
    root: fileURLToPath(new URL('../../', import.meta.url)),
    server: { middlewareMode: true },
    appType: 'custom'
  });
  try {
    const { default: Dialog } = await server.ssrLoadModule('/src/components/TenderApplicationDialog.jsx');
    const render = offer => renderToStaticMarkup(React.createElement(MemoryRouter, null,
      React.createElement(Dialog, {
        tender: { englishTitle: 'Weather stations' },
        match: { lotId: '42', offer },
        onClose: () => {}
      })));

    const eligible = render({ financing: 2_500_000_000 });
    assert.match(eligible, /Your request is recorded/);
    assert.match(eligible, /no information has been prepared or sent to UZEX/);
    assert.match(eligible, /notifications are not sent/);
    assert.match(eligible, /2 500 000 000 UZS/);
    assert.match(eligible, /href="\/loan\/42"/);
    assert.match(eligible, /Final approval is subject to bank review/);
    const ineligible = render(null);
    assert.doesNotMatch(ineligible, /Explore financing|Financing may be available|\/loan\/42/);
    assert.doesNotMatch(render({ financing: 0 }), /Explore financing/);
  } finally {
    await server.close();
  }
});
