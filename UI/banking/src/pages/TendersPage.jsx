import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { applyForTender, getDiscoveries, setFeedback, stream } from '../api.js';
import { BTN_PRIMARY, money } from '../format.js';
import { prependMatch } from '../tenderResults.js';
import { initialTenderProgress, tenderProgressMessage, updateTenderProgress } from '../tenderProgress.js';
import { useSession } from '../session.jsx';
import { BackIcon } from '../components/icons.jsx';

export default function TendersPage() {
  const { customerId, matchesLoading, rememberMatch, forgetMatch } = useSession();
  const [results, setResults] = useState([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  const [reload, setReload] = useState(0);
  const [busy, setBusy] = useState(false);
  const [actionBusy, setActionBusy] = useState(null);
  const [progress, setProgress] = useState(null);
  const [searchFailed, setSearchFailed] = useState(false);
  const [error, setError] = useState('');
  const abort = useRef(null);
  useEffect(() => () => abort.current?.abort(), []);
  useEffect(() => {
    let active = true;
    setLoading(true); setLoadError('');
    getDiscoveries(customerId).then(saved => {
      if (active) setResults(saved);
    }).catch(err => {
      if (active) setLoadError(err.message);
    }).finally(() => {
      if (active) setLoading(false);
    });
    return () => { active = false; };
  }, [customerId, reload]);

  async function findMatches() {
    setBusy(true); setError(''); setSearchFailed(false); setProgress(initialTenderProgress());
    abort.current = new AbortController();
    try {
      await stream(`/customers/${encodeURIComponent(customerId)}/discover/stream`, event => {
        setProgress(previous => updateTenderProgress(previous, event));
        if (event.type === 'match') {
          setResults(previous => prependMatch(previous, { ...event, isOpen: true, isNew: true }));
          rememberMatch(event.match);
        }
      }, abort.current.signal);
    } catch (err) { if (err.name !== 'AbortError') { setSearchFailed(true); setError(err.message); } }
    finally { setBusy(false); }
  }
  async function feedback(item, value) {
    setActionBusy(item.tender.id); setError('');
    try {
      await setFeedback(customerId, item.tender.id, value);
      if (value === 'dislike') {
        setResults(previous => previous.filter(result => result.tender.id !== item.tender.id));
        forgetMatch(item.match.lotId);
      } else {
        const match = { ...item.match, feedback: value };
        setResults(previous => previous.map(result => result.tender.id === item.tender.id ? { ...result, match } : result));
        rememberMatch(match);
      }
    } catch (err) { setError(err.message); }
    finally { setActionBusy(null); }
  }
  async function apply(item) {
    setActionBusy(item.tender.id); setError('');
    try {
      const application = await applyForTender(customerId, item.tender.id);
      const match = { ...item.match, appliedAt: application.appliedAt };
      setResults(previous => previous.map(result => result.tender.id === item.tender.id ? { ...result, match } : result));
      rememberMatch(match);
    } catch (err) { setError(err.message); }
    finally { setActionBusy(null); }
  }
  return <section>
    <Link to="/home" className="inline-flex items-center gap-1.5 text-sm font-medium text-muted"><BackIcon /> Home</Link>
    <div className="mt-4"><p className="text-xs font-semibold uppercase tracking-widest text-brand-700">Business opportunities</p><h1 className="mt-1 text-2xl font-semibold">Tenders for your business</h1><p className="mt-1 text-sm text-muted">Find open opportunities that match your business and incoming payments.</p></div>
    <div className="mt-6 flex flex-wrap items-end gap-3 rounded-2xl border border-line bg-white p-4">
      <button type="button" onClick={findMatches} disabled={busy || loading || matchesLoading || !!loadError || actionBusy !== null} className={`${BTN_PRIMARY} disabled:opacity-50`}>
        {busy ? <span aria-hidden="true" className="size-4 rounded-full border-2 border-white/40 border-t-white motion-safe:animate-spin" /> : <span aria-hidden="true">✦</span>}
        {busy ? 'Finding matches…' : 'Find tenders for me'}
      </button>
    </div>
    {loading && <p role="status" className="mt-3 text-sm text-muted">Loading saved tenders…</p>}
    {loadError && <p role="alert" className="mt-3 rounded-lg bg-red-50 p-3 text-sm text-red-700">{loadError} <button type="button" className="underline" onClick={() => setReload(value => value + 1)}>Retry</button></p>}
    {progress && <div role="status" aria-live="polite" className="mt-3 rounded-xl border border-line bg-white p-4 text-sm">
      <p className="font-medium text-ink">{tenderProgressMessage(progress, searchFailed)}</p>
      <div className="mt-2 flex flex-wrap gap-x-6 gap-y-1 text-muted">
        <span><strong className="text-ink">{progress.checked}</strong> tender{progress.checked === 1 ? '' : 's'} checked</span>
        <span><strong className="text-brand-700">{progress.found}</strong> new match{progress.found === 1 ? '' : 'es'} found</span>
      </div>
    </div>}
    {error && <p role="alert" className="mt-3 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
    <div className="mt-5 space-y-4">{results.map(item => {
      const { tender: t, match } = item;
      return <article key={t.id} className={`rounded-2xl border border-line bg-white p-5 shadow-sm ${item.isNew ? 'tender-arrival' : ''}`}>
        <div className="flex flex-wrap justify-between gap-2 text-xs text-muted"><span>UZEX · {t.displayNo}</span><span>{item.isOpen ? 'Closes' : 'Closed'} {new Date(t.endDate).toLocaleDateString('en-GB')}</span></div>
        <h2 className="mt-2 text-lg font-semibold">{t.englishTitle}</h2><p className="mt-2 text-sm leading-relaxed text-ink/80">{t.summary}</p>
        <div className="mt-4 flex flex-wrap gap-3 text-sm"><span className="rounded-lg bg-subtle px-3 py-2"><span className="text-muted">Budget </span><strong>{money(t.startPrice)} {t.currencyCode}</strong></span>{t.depositAmount != null && <span className="rounded-lg bg-subtle px-3 py-2"><span className="text-muted">Deposit </span><strong>{money(t.depositAmount)} {t.currencyCode}</strong></span>}</div>
        <div className="mt-4 rounded-xl bg-brand-50 p-4"><p className="text-sm font-semibold text-brand-700">✦ Why this is for you</p><p className="mt-1 text-sm">{match.matchReason}</p><p className="mt-3 text-sm text-muted">{match.loanDecisionReason}</p>{item.isOpen && match.offer && <Link className={`${BTN_PRIMARY} mt-3`} to={`/loan/${match.lotId}`}>Explore financing</Link>}</div>
        <div className="mt-4 flex flex-wrap items-center gap-3">
          <button type="button" aria-label={`Like ${t.englishTitle}`} aria-pressed={match.feedback === 'like'} disabled={actionBusy !== null} onClick={() => feedback(item, 'like')} className="rounded-lg border border-line px-3 py-2 text-sm aria-pressed:bg-brand-100 disabled:opacity-50">👍 {match.feedback === 'like' ? 'Liked' : 'Like'}</button>
          <button type="button" aria-label={`Dislike ${t.englishTitle}`} disabled={actionBusy !== null} onClick={() => feedback(item, 'dislike')} className="rounded-lg border border-line px-3 py-2 text-sm disabled:opacity-50">👎 Not for me</button>
          <button type="button" aria-label={`${match.appliedAt ? 'Applied for' : 'Apply for'} ${t.englishTitle}`}
            disabled={actionBusy !== null || !!match.appliedAt || !item.isOpen} onClick={() => apply(item)}
            className={`${BTN_PRIMARY} disabled:opacity-60`}>{match.appliedAt ? '✓ Applied' : item.isOpen ? 'Apply for me' : 'Tender closed'}</button>
          {t.sourceUrl && <a className="ml-auto text-sm font-medium text-brand-700 underline" href={t.sourceUrl} target="_blank" rel="noreferrer">View on UZEX ↗</a>}
        </div>
        {match.appliedAt && <p role="status" className="mt-3 rounded-lg bg-brand-50 p-3 text-sm text-brand-700">Your request is recorded for preparation. This is a demo; nothing has been submitted to UZEX.</p>}
      </article>;
    })}</div>
  </section>;
}
