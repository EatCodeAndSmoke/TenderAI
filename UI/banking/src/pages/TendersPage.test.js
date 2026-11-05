import test from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { createServer } from 'vite';

test('match explanation omits loan decision text but keeps eligible financing link', async () => {
  const server = await createServer({
    root: fileURLToPath(new URL('../../', import.meta.url)),
    server: { middlewareMode: true },
    appType: 'custom'
  });
  try {
    const { TenderMatchExplanation } = await server.ssrLoadModule('/src/pages/TendersPage.jsx');
    const render = (offer, isOpen = true) => renderToStaticMarkup(React.createElement(MemoryRouter, null,
      React.createElement(TenderMatchExplanation, {
        match: { lotId: '42', matchReason: 'Relevant tender evidence.', loanDecisionReason: 'Private loan decision explanation.', offer },
        isOpen
      })));
    const eligible = render({ financing: 100 });
    assert.match(eligible, /Relevant tender evidence/);
    assert.doesNotMatch(eligible, /Private loan decision explanation/);
    assert.match(eligible, /href="\/loan\/42"/);
    assert.doesNotMatch(render(null), /Explore financing|Private loan decision explanation/);
    assert.doesNotMatch(render({ financing: 100 }, false), /Explore financing|Private loan decision explanation/);
  } finally {
    await server.close();
  }
});
