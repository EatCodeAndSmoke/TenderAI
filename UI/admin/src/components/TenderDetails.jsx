import { tenderRequirements, tenderText, TENDER_TEXT } from '../data/tenders.js';
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

const join = (...parts) => parts.filter(Boolean).join(', ');

// The facts about a tender that matter for matching it with customers and loan offers
export default function TenderDetails({ tender: t, lang, onClose }) {
  const text = TENDER_TEXT[lang];
  const d = text.detail;
  const tx = (field) => tenderText(t, lang, field);
  const requirements = tenderRequirements(t, lang);

  const paymentNote = join(
    t.advancePaymentPercent != null && text.advance(t.advancePaymentPercent),
    t.paymentTermDays != null && text.days(t.paymentTermDays)
  );
  const depositNote = join(tx('depositName'), `${d.bankGuarantee}: ${t.bankGuaranteeAccepted ? d.yes : d.no}`);

  return (
    // Header and footer stay put; only the middle scrolls, so a long title never pushes them out of view
    <div className="flex max-h-[calc(100dvh-4rem)] flex-col">
      <ModalHeader
        id="tender-title"
        icon={<DocIcon />}
        title={tx('name')}
        subtitle={`${d.lot} ${t.displayNo}`}
        onClose={onClose}
      />

      <div className="mt-5 min-h-0 flex-1 space-y-6 overflow-y-auto border-t border-line px-6 py-5">
        <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-2">
          <Fact label={d.buyer} note={t.buyerTin && `${d.tin} ${t.buyerTin}`}>{tx('buyerName')}</Fact>
          <Fact label={d.procedure}>{join(tx('procedureType'), tx('evaluationMethod')) || '—'}</Fact>
          <Fact label={d.startPrice}>
            <span className="font-medium tabular-nums">{amount(t.startPrice)}</span> {t.currencyCode}
          </Fact>
          <Fact label={d.deposit} note={depositNote}>
            {t.depositPercent != null
              ? <>{t.depositPercent}% <span className="text-muted">·</span> <span className="tabular-nums">{amount(t.depositAmount)}</span> {t.currencyCode}</>
              : '—'}
          </Fact>
          <Fact label={d.payment} note={paymentNote}>{tx('paymentType') || '—'}</Fact>
          <Fact label={d.financing}>{tx('financingSource') || '—'}</Fact>
          <Fact label={d.delivery} note={tx('deliveryAddress')}>
            {join(tx('deliveryRegion'), tx('deliveryDistrict')) || join(tx('regionName'), tx('districtName')) || '—'}
          </Fact>
          <Fact label={d.languages}>
            {t.bidLanguages.length ? t.bidLanguages.map((code) => text.languageNames[code] || code).join(', ') : '—'}
          </Fact>
          <Fact label={d.period}>{text.dateTime(t.startDate)} – {text.dateTime(t.endDate)}</Fact>
        </dl>

        {t.products.length > 0 && (
          <Section title={`${d.products} (${t.products.length})`}>
            <div className="overflow-x-auto rounded-lg border border-line">
              <table className="min-w-full text-sm">
                <thead className="bg-subtle text-left text-xs text-muted">
                  <tr>
                    <th className="px-3 py-2 font-medium">{d.product}</th>
                    <th className="px-3 py-2 text-right font-medium">{d.quantity}</th>
                    <th className="px-3 py-2 text-right font-medium">{d.price}</th>
                    <th className="px-3 py-2 text-right font-medium">{d.cost}</th>
                    <th className="px-3 py-2 font-medium">{d.deliveryTerm}</th>
                    <th className="px-3 py-2 font-medium">{d.warranty}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-line">
                  {t.products.map((p) => {
                    const px = (field) => tenderText(p, lang, field);
                    const description = px('description');
                    return (
                      <tr key={p.id} className="align-top">
                        <td className="min-w-56 px-3 py-2.5">
                          <div className="font-medium">{px('productName')}</div>
                          {description && description !== px('productName') && <div className="text-xs">{description}</div>}
                          <div className="text-xs text-muted">{join(p.productCode && `${d.code} ${p.productCode}`, px('categoryName'))}</div>
                        </td>
                        <td className="px-3 py-2.5 text-right whitespace-nowrap tabular-nums">{money(p.quantity)} {px('unit')}</td>
                        <td className="px-3 py-2.5 text-right whitespace-nowrap tabular-nums">{amount(p.price)}</td>
                        <td className="px-3 py-2.5 text-right whitespace-nowrap tabular-nums">{amount(p.cost)}</td>
                        <td className="px-3 py-2.5 whitespace-nowrap">{p.deliveryTermDays != null ? text.days(p.deliveryTermDays) : '—'}</td>
                        <td className="px-3 py-2.5 whitespace-nowrap">{p.warrantyTerm != null ? `${p.warrantyTerm} ${px('warrantyTermUnit') ?? ''}` : '—'}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </Section>
        )}

        {requirements.length > 0 && (
          <Section title={d.requirements}>
            <ol className="list-decimal space-y-1 pl-5 text-sm">
              {requirements.map((r, i) => <li key={i}>{r}</li>)}
            </ol>
          </Section>
        )}

        {tx('description') && (
          <Section title={d.description}>
            <p className="text-sm whitespace-pre-line">{tx('description')}</p>
          </Section>
        )}
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
