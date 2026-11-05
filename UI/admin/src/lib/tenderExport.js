import { Document, HeadingLevel, Packer, Paragraph } from 'docx';

const xmlSafe = value => String(value).replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\ufffe\uffff]/g, '');

export async function createTenderSummariesDocument(tenders) {
  const children = [
    new Paragraph({ text: 'Tender summaries', heading: HeadingLevel.TITLE }),
    new Paragraph({ text: `${tenders.length} stored tender${tenders.length === 1 ? '' : 's'}` })
  ];

  if (!tenders.length) children.push(new Paragraph({ text: 'No tenders stored.' }));
  for (const tender of tenders) {
    const title = tender.englishTitle?.trim() || 'Untitled tender';
    const summary = tender.summary?.trim() || 'English summary unavailable.';
    children.push(
      new Paragraph({ text: xmlSafe(title), heading: HeadingLevel.HEADING_1 }),
      new Paragraph({ text: xmlSafe(`UZEX ${tender.displayNo || tender.id}`) })
    );
    if (tender.sourceUrl) children.push(new Paragraph({ text: xmlSafe(tender.sourceUrl) }));
    for (const line of summary.split(/\r\n|\r|\n/)) {
      children.push(new Paragraph({ text: xmlSafe(line) }));
    }
  }

  return Packer.toBlob(new Document({
    creator: 'TenderAI',
    title: 'Tender summaries',
    sections: [{ children }]
  }));
}

export async function downloadTenderSummaries(tenders) {
  const blob = await createTenderSummariesDocument(tenders);
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  try {
    link.href = url;
    link.download = 'tender-summaries.docx';
    document.body.appendChild(link);
    link.click();
  } finally {
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
}
