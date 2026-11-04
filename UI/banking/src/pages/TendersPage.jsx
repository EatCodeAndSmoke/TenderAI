import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { getTenders, stream } from '../api.js';
import { BTN_PRIMARY, money } from '../format.js';
import { useSession } from '../session.jsx';
import { BackIcon } from '../components/icons.jsx';

export default function TendersPage() {
  const { customerId, matches, matchesLoading, matchesError, refreshMatches } = useSession();
  const [tenders, setTenders] = useState([]);
  const [count, setCount] = useState(5);
  const [forMe, setForMe] = useState(false);
  const [busy, setBusy] = useState('');
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const abort = useRef(null);
  useEffect(() => {
    let active = true;
    getTenders().then(items => { if (active) setTenders(items); }).catch(err => { if (active) setError(err.message); });
    return () => { active = false; abort.current?.abort(); };
  }, []);

  async function load() {
    setBusy('load'); setError(''); setMessage('Connecting to UZEX…');
    let loaded = 0;
    abort.current = new AbortController();
    try {
      await stream(`/tenders/load/stream?count=${count}`, event => {
        if (event.type === 'tender') {
          loaded++;
          setTenders(prev => [...prev.filter(t => t.id !== event.tender.id), event.tender]);
          setMessage(`Loaded ${loaded} tender${loaded === 1 ? '' : 's'}…`);
        }
      }, abort.current.signal);
      setTenders(await getTenders());
      setMessage(loaded ? `Loaded ${loaded} new tender${loaded === 1 ? '' : 's'}.` : 'No new open tenders are available.');
    } catch (err) { if (err.name !== 'AbortError') setError(err.message); }
    finally { setBusy(''); }
  }
  async function findMatches() {
    setForMe(true); setBusy('match'); setError(''); setMessage('Comparing your business with open tenders…');
    abort.current = new AbortController();
    try {
      await stream(`/customers/${encodeURIComponent(customerId)}/matches/stream`, event => {
        if (event.type === 'started') setMessage(`Checking ${event.totalTenders} open tenders…`);
      }, abort.current.signal);
      await refreshMatches();
      setMessage('Your personalized results are ready.');
    } catch (err) { if (err.name !== 'AbortError') setError(err.message); }
    finally { setBusy(''); }
  }
  const matchesById = new Map(matches.map(m => [m.lotId, m]));
  const shown = forMe ? tenders.filter(t => matchesById.has(String(t.id))) : tenders;
  return <section>
    <Link to="/home" className="inline-flex items-center gap-1.5 text-sm font-medium text-muted"><BackIcon /> Home</Link>
    <div className="mt-4 flex flex-wrap items-end justify-between gap-4">
      <div><p className="text-xs font-semibold uppercase tracking-widest text-brand-700">Business opportunities</p><h1 className="mt-1 text-2xl font-semibold">Tenders</h1><p className="mt-1 text-sm text-muted">Explore open UZEX tenders, explained in English.</p></div>
      <button type="button" onClick={findMatches} disabled={!!busy} className={`${BTN_PRIMARY} rounded-xl`}>✦ {busy === 'match' ? 'Finding matches…' : 'Find tenders for me'}</button>
    </div>
    <div className="mt-6 flex flex-wrap items-center gap-3 rounded-2xl border border-line bg-white p-4">
      <label htmlFor="count" className="text-sm font-medium">Load from UZEX</label>
      <input id="count" type="number" min="1" max="5" value={count} onChange={e => setCount(Number(e.target.value))} className="w-16 rounded-lg border border-line px-2 py-2 text-sm" />
      <button type="button" onClick={load} disabled={!!busy || count < 1 || count > 5 || !Number.isInteger(count)} className="rounded-lg bg-cobalt px-4 py-2 text-sm font-medium text-white disabled:opacity-50">{busy === 'load' ? 'Loading…' : 'Load tenders'}</button>
      <div className="ml-auto flex rounded-lg bg-subtle p-1 text-sm" role="group" aria-label="Tender view">
        <button onClick={() => setForMe(false)} aria-pressed={!forMe} className="rounded-md px-3 py-1.5 aria-pressed:bg-white aria-pressed:shadow-sm">All</button>
        <button onClick={() => setForMe(true)} aria-pressed={forMe} className="rounded-md px-3 py-1.5 aria-pressed:bg-white aria-pressed:shadow-sm">✦ For me</button>
      </div>
    </div>
    {message && <p role="status" className="mt-3 text-sm text-muted">{message}</p>}
    {(error || matchesError) && <p role="alert" className="mt-3 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error || matchesError}</p>}
    <div className="mt-5 space-y-4">
      {forMe && matchesLoading && <p className="text-sm text-muted">Loading your matches…</p>}
      {!shown.length && !busy && !matchesLoading && <div className="rounded-2xl border border-dashed border-line bg-white p-10 text-center text-sm text-muted">{forMe ? 'No matching tenders yet. Select “Find tenders for me” to check the latest opportunities.' : 'No tenders loaded yet. Load tenders from UZEX to get started.'}</div>}
      {shown.map(t => {
        const match = matchesById.get(String(t.id));
        return <article key={t.id} className="rounded-2xl border border-line bg-white p-5 shadow-sm">
          <div className="flex flex-wrap justify-between gap-2 text-xs text-muted"><span>UZEX · {t.displayNo}</span><span>Closes {new Date(t.endDate).toLocaleDateString('en-GB')}</span></div>
          <h2 className="mt-2 text-lg font-semibold">{t.englishTitle || 'Tender summary unavailable'}</h2>
          <p className="mt-2 text-sm leading-relaxed text-ink/80">{t.summary || (t.summaryError ? 'Summary unavailable. Check the API configuration and try loading again.' : 'Summary pending.')}</p>
          <div className="mt-4 flex flex-wrap gap-3 text-sm"><span className="rounded-lg bg-subtle px-3 py-2"><span className="text-muted">Budget </span><strong>{money(t.startPrice)} {t.currencyCode}</strong></span>{t.depositAmount != null && <span className="rounded-lg bg-subtle px-3 py-2"><span className="text-muted">Deposit </span><strong>{money(t.depositAmount)} {t.currencyCode}</strong></span>}</div>
          {forMe && match && <div className="mt-4 rounded-xl bg-brand-50 p-4"><p className="text-sm font-semibold text-brand-700">✦ Why this is for you</p><p className="mt-1 text-sm">{match.matchReason}</p><p className="mt-3 text-sm text-muted">{match.loanDecisionReason}</p>{match.offer && <Link className={`${BTN_PRIMARY} mt-3`} to={`/loan/${match.lotId}`}>Explore financing</Link>}</div>}
          {t.sourceUrl && <a className="mt-4 inline-block text-sm font-medium text-brand-700 underline" href={t.sourceUrl} target="_blank" rel="noreferrer">View on UZEX ↗</a>}
        </article>;
      })}
    </div>
  </section>;
}
