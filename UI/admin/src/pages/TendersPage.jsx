import { useCallback, useEffect, useState } from 'react';
import { TENDER_LANGS, TENDER_TEXT, tenderText } from '../data/tenders.js';
import { clearTenders, getTenders, loadTenders } from '../lib/api.js';
import { daysUntil, formatDate, money, shortMoney } from '../lib/format.js';
import { BTN_PRIMARY, BTN_ROW, BTN_SECONDARY, CARD, ROW } from '../lib/styles.js';
import { Chip } from '../components/badges.jsx';
import { ExternalIcon, RefreshIcon, SpinnerIcon, XIcon } from '../components/icons.jsx';
import PageHeader from '../components/PageHeader.jsx';
import Modal from '../components/Modal.jsx';
import TenderDetails from '../components/TenderDetails.jsx';

const LOAD_COUNT = 10;
const ALIGN_RIGHT = [3, 4];

const amount = (n) => money(Math.round(n));

function Deadline({ iso, text }) {
  const d = daysUntil(iso.slice(0, 10));
  const when = d < 0 ? text.closed : d === 0 ? text.today : text.inDays(d);
  return (
    <>
      <div className="whitespace-nowrap">{text.dateTime(iso)}</div>
      <div className={`text-xs whitespace-nowrap ${d <= 14 ? 'text-amber-700' : 'text-muted'}`}>{when}</div>
    </>
  );
}

// "60 open lots worth 1.5 trillion so'm and 4 978 817 USD in total. The nearest deadline is 19 Oct 2026."
function summarize(tenders) {
  const n = tenders.length;
  const totals = {};
  for (const t of tenders) totals[t.currencyCode] = (totals[t.currencyCode] || 0) + t.startPrice;
  const worth = Object.entries(totals)
    .sort(([a], [b]) => (a === 'UZS' ? -1 : b === 'UZS' ? 1 : a.localeCompare(b)))
    .map(([code, sum]) => (code === 'UZS' ? `${shortMoney(Math.round(sum))} so'm` : `${amount(sum)} ${code}`))
    .join(' and ');
  const nearest = tenders.map((t) => t.endDate).sort()[0].slice(0, 10);
  return `${n} ${n === 1 ? 'lot' : 'lots'} from UZEX worth ${worth} in total. The nearest deadline is ${formatDate(nearest)}.`;
}

export default function TendersPage({ lang, onLangChange, onToast }) {
  const text = TENDER_TEXT[lang];
  const [tenders, setTenders] = useState([]);
  const [status, setStatus] = useState('loading');
  const [loadError, setLoadError] = useState('');
  const [busy, setBusy] = useState(null); // 'load' | 'clear' | null
  const [selected, setSelected] = useState(null);

  const fetchTenders = useCallback(async () => {
    setStatus('loading');
    try {
      setTenders(await getTenders());
      setStatus('ready');
      return true;
    } catch (err) {
      setLoadError(err.message);
      setStatus('error');
      return false;
    }
  }, []);

  useEffect(() => { fetchTenders(); }, [fetchTenders]);

  const handleLoad = async () => {
    setBusy('load');
    try {
      const loaded = await loadTenders(LOAD_COUNT);
      if (!(await fetchTenders())) return;
      if (loaded.length === 0) {
        onToast('No new tenders on UZEX. Existing tenders were checked for missing English translations.');
        return;
      }
      onToast(`Loaded ${loaded.length} new ${loaded.length === 1 ? 'tender' : 'tenders'} from UZEX. Existing tenders were checked for missing English translations.`);
    } catch (err) {
      window.alert(err.message);
    } finally {
      setBusy(null);
    }
  };

  const handleClear = async () => {
    if (!window.confirm("Remove all tenders? Their products and translations are removed too. This can't be undone.")) return;
    setBusy('clear');
    try {
      await clearTenders();
      setTenders([]);
      onToast('All tenders removed.');
    } catch (err) {
      window.alert(err.message);
    } finally {
      setBusy(null);
    }
  };

  const n = tenders.length;
  const summary = status === 'loading'
    ? 'Loading tenders…'
    : n === 0 ? 'No tenders yet.' : summarize(tenders);

  // Translations are filled in later; until then every language shows the original UZEX text
  const untranslated = lang !== 'uz' && tenders.some((t) => !t.translations?.[lang]);

  return (
    <section>
      <PageHeader
        title="Tenders"
        summary={summary}
        actions={
          <>
            <button
              type="button"
              onClick={handleClear}
              disabled={status !== 'ready' || n === 0 || busy !== null}
              className={`${BTN_SECONDARY} disabled:cursor-not-allowed disabled:opacity-50`}
            >
              <XIcon /> Clear
            </button>
            <button
              type="button"
              onClick={handleLoad}
              disabled={status !== 'ready' || busy !== null}
              className={`${BTN_PRIMARY} disabled:opacity-50 ${busy === 'load' ? 'disabled:cursor-wait' : 'disabled:cursor-not-allowed'}`}
            >
              {busy === 'load' ? <><SpinnerIcon /> Loading…</> : <><RefreshIcon /> Load tenders</>}
            </button>
          </>
        }
      />

      <div className={CARD}>
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-line px-4 py-3">
          <p className="text-sm text-muted">
            Show tenders in
            {untranslated && <span className="block text-xs">No English translation yet, so tenders show the original text from UZEX.</span>}
          </p>
          <div role="group" aria-label="Tender language" className="inline-flex rounded-lg bg-subtle p-1 text-xs font-medium">
            {TENDER_LANGS.map(([code, label]) => (
              <button
                key={code}
                type="button"
                aria-pressed={code === lang}
                onClick={() => onLangChange(code)}
                className="rounded-md px-3 py-1.5 text-muted transition-colors hover:text-ink aria-pressed:bg-surface aria-pressed:text-ink aria-pressed:shadow-sm"
              >
                {label}
              </button>
            ))}
          </div>
        </div>

        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="bg-subtle text-left text-xs text-muted">
              <tr className="border-b border-line">
                {text.headers.map((h, i) => (
                  <th key={h} className={`px-3.5 py-3 font-medium ${ALIGN_RIGHT.includes(i) ? 'text-right' : ''}`}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {status !== 'ready' && (
                <tr>
                  <td colSpan={text.headers.length} className="px-4 py-10 text-center text-sm text-muted">
                    {status === 'loading' ? 'Loading tenders…' : (
                      <>
                        <span className="text-red-700">{loadError}</span>{' '}
                        <button type="button" onClick={fetchTenders} className="font-medium text-brand-700 hover:underline">Try again</button>
                      </>
                    )}
                  </td>
                </tr>
              )}
              {status === 'ready' && n === 0 && (
                <tr>
                  <td colSpan={text.headers.length} className="px-4 py-10 text-center text-sm text-muted">
                    No tenders yet. Click Load tenders to get the next {LOAD_COUNT} open lots from UZEX.
                  </td>
                </tr>
              )}
              {status === 'ready' && tenders.map((t) => {
                const tx = (field) => tenderText(t, lang, field);
                return (
                  <tr key={t.id} className={ROW}>
                    <td className="px-3.5 py-4 text-xs whitespace-nowrap text-muted">{t.displayNo}</td>
                    <td className="min-w-72 px-3.5 py-4">
                      <div className="line-clamp-3 font-medium" title={tx('name')}>{tx('name')}</div>
                      <div className="mt-0.5 text-xs text-muted">{tx('buyerName')}</div>
                      {lang === 'en' && t.translationError && (
                        <div
                          className="mt-1 line-clamp-2 text-xs text-red-700"
                          title={t.translationError}
                          role="status"
                        >
                          English translation failed: {t.translationError}
                        </div>
                      )}
                    </td>
                    <td className="px-3.5 py-4">
                      {tx('regionName') ? <Chip>{tx('regionName')}</Chip> : <span className="text-muted">—</span>}
                      {tx('districtName') && <div className="mt-1 text-xs text-muted">{tx('districtName')}</div>}
                    </td>
                    <td className="px-3.5 py-4 text-right whitespace-nowrap tabular-nums">
                      <div className="font-medium">{amount(t.startPrice)}</div>
                      <div className="text-xs text-muted">{t.currencyCode}</div>
                    </td>
                    <td className="px-3.5 py-4 text-right whitespace-nowrap tabular-nums">
                      {t.depositPercent != null ? (
                        <>
                          <div>{t.depositPercent}%</div>
                          <div className="text-xs text-muted">{amount(t.depositAmount)} {t.currencyCode}</div>
                        </>
                      ) : <span className="text-muted">—</span>}
                    </td>
                    <td className="px-3.5 py-4 whitespace-nowrap">
                      <div>{t.paymentTermDays != null ? text.days(t.paymentTermDays) : '—'}</div>
                      {t.advancePaymentPercent > 0 && <div className="text-xs text-muted">{text.advance(t.advancePaymentPercent)}</div>}
                    </td>
                    <td className="px-3.5 py-4"><Deadline iso={t.endDate} text={text} /></td>
                    <td className="px-3.5 py-4">
                      <a
                        href={t.sourceUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        title={text.openSource}
                        aria-label={`${text.openSource}: ${t.displayNo}`}
                        className="inline-flex items-center gap-1.5 rounded-md bg-subtle px-2 py-1 text-xs font-medium text-ink ring-1 ring-line ring-inset transition-colors hover:bg-brand-50 hover:text-brand-700 hover:ring-brand-300"
                      >
                        <span className="size-1.5 rounded-full bg-brand-500" />
                        {t.source}
                        <ExternalIcon />
                      </a>
                    </td>
                    <td className="px-3.5 py-4">
                      <button
                        type="button"
                        onClick={() => setSelected(t)}
                        aria-label={`${text.details}: ${t.displayNo}`}
                        className={BTN_ROW}
                      >
                        {text.details}
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>

      <Modal open={selected !== null} onClose={() => setSelected(null)} labelledBy="tender-title" width="max-w-4xl">
        {selected && <TenderDetails tender={selected} lang={lang} onClose={() => setSelected(null)} />}
      </Modal>
    </section>
  );
}
