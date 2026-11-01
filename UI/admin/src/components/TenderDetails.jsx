import { TENDER_TEXT } from '../data/tenders.js';
import { money } from '../lib/format.js';
import { BTN_PRIMARY, BTN_SECONDARY } from '../lib/styles.js';
import { DocIcon, ExternalIcon } from './icons.jsx';
import { ModalHeader } from './Modal.jsx';

const amount = (n) => money(Math.round(n));

function Fact({ label, children, note }) {
  return (
    <div>
      <dt className="text-xs font-medium text-muted">{label}</dt>
      <dd className="mt-0.5 text-sm">{children}</dd>
      {note && <dd className="text-xs text-muted">{note}</dd>}
    </div>
  );
}

function Section({ title, children }) {
  return (
    <section>
      <h3 className="mb-2 text-sm font-semibold">{title}</h3>
      {children}
    </section>
  );
}

export default function TenderDetails({ tender: t, onClose }) {
  const text = TENDER_TEXT;
  const d = text.detail;

  return (
    // Header and footer stay put; only the middle scrolls, so a long title never pushes them out of view
    <div className="flex max-h-[calc(100dvh-4rem)] flex-col">
      <ModalHeader
        id="tender-title"
        icon={<DocIcon />}
        title={t.summary || `Tender ${t.displayNo}`}
        subtitle={`${d.lot} ${t.displayNo}`}
        onClose={onClose}
      />

      <div className="mt-5 min-h-0 flex-1 space-y-6 overflow-y-auto border-t border-line px-6 py-5">
        <Section title={d.summary}>
          <p className="text-sm whitespace-pre-line">{t.summary || 'The English summary is not available.'}</p>
          {t.summaryError && <p className="mt-2 text-sm text-red-700">{t.summaryError}</p>}
        </Section>

        <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-2">
          <Fact label={d.startPrice}>
            <span className="font-medium tabular-nums">{amount(t.startPrice)}</span> {t.currencyCode}
          </Fact>
          <Fact label={d.deposit}>
            {t.depositPercent != null
              ? <>{t.depositPercent}% <span className="text-muted">·</span> <span className="tabular-nums">{amount(t.depositAmount)}</span> {t.currencyCode}</>
              : '—'}
          </Fact>
          <Fact label={d.payment}>
            {t.paymentTermDays != null ? text.days(t.paymentTermDays) : '—'}
            {t.advancePaymentPercent > 0 && <div className="text-xs text-muted">{text.advance(t.advancePaymentPercent)}</div>}
          </Fact>
          <Fact label={d.deadline}>{text.dateTime(t.endDate)}</Fact>
        </dl>
      </div>

      <div className="flex shrink-0 flex-wrap justify-end gap-2 border-t border-line bg-subtle px-6 py-4">
        <a href={t.sourceUrl} target="_blank" rel="noopener noreferrer" className={BTN_SECONDARY}>
          {text.openSource} <ExternalIcon />
        </a>
        <button type="button" onClick={onClose} className={BTN_PRIMARY} data-autofocus>{d.close}</button>
      </div>
    </div>
  );
}
