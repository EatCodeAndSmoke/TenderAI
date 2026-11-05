import test from 'node:test';
import assert from 'node:assert/strict';
import { strFromU8, unzipSync } from 'fflate';
import { createTenderSummariesDocument } from './tenderExport.js';

async function documentXml(tenders) {
  const blob = await createTenderSummariesDocument(tenders);
  const files = unzipSync(new Uint8Array(await blob.arrayBuffer()));
  assert.ok(files['[Content_Types].xml']);
  assert.ok(files['word/document.xml']);
  return strFromU8(files['word/document.xml']);
}

test('exports every tender summary, preserving text and marking missing summaries', async () => {
  const xml = await documentXml([
    {
      id: 1, displayNo: 'LOT-1', englishTitle: 'Bread & rolls',
      summary: 'Fresh bread <daily>.\nDeliver to café.\u0007', sourceUrl: 'https://example.test/1'
    },
    { id: 2, displayNo: 'LOT-2', englishTitle: null, summary: null }
  ]);
  assert.match(xml, /2 stored tenders/);
  assert.match(xml, /Bread &amp; rolls/);
  assert.match(xml, /Fresh bread &lt;daily&gt;\./);
  assert.match(xml, /Deliver to café\./);
  assert.match(xml, /UZEX LOT-1/);
  assert.match(xml, /https:\/\/example\.test\/1/);
  assert.match(xml, /Untitled tender/);
  assert.match(xml, /UZEX LOT-2/);
  assert.match(xml, /English summary unavailable\./);
  assert.doesNotMatch(xml, /\u0007/);
  assert.ok(xml.indexOf('LOT-1') < xml.indexOf('LOT-2'));
});

test('exports a valid document when there are no tenders', async () => {
  const xml = await documentXml([]);
  assert.match(xml, /0 stored tenders/);
  assert.match(xml, /No tenders stored\./);
});
