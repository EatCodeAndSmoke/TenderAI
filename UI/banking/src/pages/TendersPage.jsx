import { useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { applyForTender, getDiscoveries, setFeedback, stream } from '../api.js';
import { BTN_PRIMARY, money } from '../format.js';
import { groupTenderResults, matchCategory, matchScore, prependMatch, sortTenderResults } from '../tenderResults.js';
import { initialTenderProgress, tenderProgressMessage, updateTenderProgress } from '../tenderProgress.js';
import { applicationUpdateContent } from '../applicationUpdates.js';
import { useSession } from '../session.jsx';
import { BackIcon } from '../components/icons.jsx';
import TenderApplicationDialog from '../components/TenderApplicationDialog.jsx';

export default function TendersPage() {
  const { customerId, matchesLoading, rememberMatch, forgetMatch, updates } = useSession();
  const [searchParams] = useSearchParams();
  const requestedLot = Number(searchParams.get('lot'));
  const [results, setResults] = useState([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  const [reload, setReload] = useState(0);
  const [busy, setBusy] = useState(false);
  const [actionBusy, setActionBusy] = useState(null);
  const [progress, setProgress] = useState(null);
  const [searchFailed, setSearchFailed] = useState(false);
  const [error, setError] = useState('');
  const [confirmation, setConfirmation] = useState(null);
  const [leaving, setLeaving] = useState(() => new Set());
  const [expanded, setExpanded] = useState({});
  const [selectedLots, setSelectedLots] = useState({});
  const [feedbackTenderId, setFeedbackTenderId] = useState(null);
  const [feedbackComment, setFeedbackComment] = useState('');
  const abort = useRef(null);
  const removalTimers = useRef(new Set());
  const hiddenGroups = useRef(new Set());
  useEffect(() => () => {
    abort.current?.abort();
    removalTimers.current.forEach(clearTimeout);
    removalTimers.current.clear();
  }, []);
  useEffect(() => {
    let active = true;
    setLoading(true); setLoadError('');
    getDiscoveries(customerId).then(saved => {
      if (active) setResults(sortTenderResults(saved));
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
        if (event.type === 'match' && !hiddenGroups.current.has(event.groupKey)) {
          setResults(previous => prependMatch(previous, { ...event, isOpen: true, isNew: true }));
          rememberMatch(event.match);
        }
      }, abort.current.signal);
      const saved = await getDiscoveries(customerId);
      const additions = saved.filter(item => !hiddenGroups.current.has(item.groupKey));
      additions.forEach(item => rememberMatch(item.match));
      setResults(previous => {
        const ids = new Set(previous.map(item => item.tender.id));
        return sortTenderResults([...previous, ...additions.filter(item => !ids.has(item.tender.id))]);
      });
    } catch (err) { if (err.name !== 'AbortError') { setSearchFailed(true); setError(err.message); } }
    finally { setBusy(false); }
  }
  async function feedback(item, value, comment = null, group) {
    setActionBusy(item.tender.id); setError('');
    try {
      await setFeedback(customerId, item.tender.id, value, comment);
      if (value === 'dislike') {
        setFeedbackTenderId(null); setFeedbackComment('');
        hiddenGroups.current.add(group.key);
        group.lots.forEach(lot => forgetMatch(lot.match.lotId));
        const remove = () => {
          setResults(previous => previous.filter(result => (result.groupKey ?? `lot:${result.tender.id}`) !== group.key));
          setLeaving(previous => {
            const next = new Set(previous);
            next.delete(group.key);
            return next;
          });
        };
        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
          remove();
        } else {
          setLeaving(previous => new Set(previous).add(group.key));
          const timer = setTimeout(() => {
            removalTimers.current.delete(timer);
            remove();
          }, 300);
          removalTimers.current.add(timer);
        }
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
      const match = { ...item.match, appliedAt: application.appliedAt, offer: application.offer,
        loanDecisionReason: application.loanDecisionReason, simulationScheduled: application.simulationScheduled };
      setResults(previous => previous.map(result => result.tender.id === item.tender.id ? { ...result, match } : result));
      rememberMatch(match);
      setExpanded(previous => ({ ...previous, [item.tender.id]: false }));
      setConfirmation({ tender: item.tender, match });
    } catch (err) { setError(err.message); }
    finally { setActionBusy(null); }
  }
  return <section>
    <Link to="/home" className="inline-flex items-center gap-1.5 text-sm font-medium text-muted"><BackIcon /> Home</Link>
    <div className="mt-4"><p className="text-xs font-semibold uppercase tracking-widest text-brand-700">Business opportunities</p><h1 className="mt-1 text-2xl font-semibold">Tenders for your business</h1><p className="mt-1 text-sm text-muted">Discover opportunities that fit what your business does best.</p></div>
    <div className="tender-search mt-6 flex flex-wrap items-center justify-between gap-4 rounded-2xl p-5 text-white shadow-sm">
      <div className="flex items-center gap-3">
        <span aria-hidden="true" className={`search-sparkle flex size-11 shrink-0 items-center justify-center rounded-xl bg-white/15 text-2xl ${busy ? 'search-active' : ''}`}>✦</span>
        <div><p className="font-semibold">Let’s find your next opportunity</p><p className="mt-1 text-sm text-white/85">We’ll look at your business and incoming payments to find a fit.</p></div>
      </div>
      <button type="button" onClick={findMatches} disabled={busy || loading || matchesLoading || !!loadError || actionBusy !== null} className="inline-flex items-center justify-center gap-2 rounded-xl bg-white px-4 py-2.5 text-sm font-semibold text-brand-700 shadow-sm hover:bg-brand-50 disabled:opacity-60">
        {busy ? <span aria-hidden="true" className="size-4 rounded-full border-2 border-brand-700/30 border-t-brand-700 motion-safe:animate-spin" /> : <span aria-hidden="true">✦</span>}
        {busy ? 'Searching for you…' : 'Find tenders for me'}
      </button>
    </div>
    {loading && <p role="status" className="mt-3 text-sm text-muted">Loading saved tenders…</p>}
    {loadError && <p role="alert" className="mt-3 rounded-lg bg-red-50 p-3 text-sm text-red-700">{loadError} <button type="button" className="underline" onClick={() => setReload(value => value + 1)}>Retry</button></p>}
    {progress && <div role="status" aria-live="polite" className={`search-progress mt-3 overflow-hidden rounded-xl border border-brand-100 bg-white p-4 text-sm shadow-sm ${busy ? 'is-searching' : ''}`}>
      <div className="flex items-start gap-3">
        <span aria-hidden="true" className={`progress-orbit mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-full bg-brand-50 text-brand-700 ${busy ? 'is-searching' : ''}`}>{progress.completed ? '✓' : '✦'}</span>
        <div><p className="font-medium text-ink">{tenderProgressMessage(progress, searchFailed)}</p>
        <div className="mt-2 flex flex-wrap gap-x-6 gap-y-1 text-muted">
          <span><strong className="text-ink">{progress.checked}</strong> tender{progress.checked === 1 ? '' : 's'} checked</span>
          <span><strong className="text-brand-700">{progress.found}</strong> new match{progress.found === 1 ? '' : 'es'} found</span>
        </div></div>
      </div>
      {busy && <div aria-hidden="true" className="search-track mt-4 h-1 overflow-hidden rounded-full bg-brand-50"><span className="search-tracer block h-full w-1/3 rounded-full bg-brand-400" /></div>}
    </div>}
    {error && <p role="alert" className="mt-3 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
    {results.length > 0 && <p className="mt-5 text-xs text-muted">Fit scores are AI estimates, not guarantees. Review each opportunity before applying.</p>}
    <div className="mt-5 space-y-4">{groupTenderResults(results).map(group => {
      const item = group.lots.find(lot => lot.tender.id === selectedLots[group.key])
        ?? group.lots.find(lot => lot.tender.id === requestedLot)
        ?? group.lots.find(lot => lot.isOpen) ?? group.lots[0];
      const { tender: t, match } = item;
      const isLeaving = leaving.has(group.key);
      const isExpanded = expanded[t.id] ?? (t.id === requestedLot || !match.appliedAt);
      const update = updates.find(entry => entry.tenderId === t.id);
      const appliedCount = group.lots.filter(lot => lot.match.appliedAt).length;
      return <article key={group.key} className={`rounded-2xl border bg-white p-5 shadow-sm ${appliedCount ? 'tender-applied' : 'border-line'} ${isLeaving ? 'tender-leaving' : group.lots.some(lot => lot.isNew) ? 'tender-arrival-new' : 'tender-arrival'}`}>
        <div className="flex flex-wrap justify-between gap-2 text-xs text-muted"><span>UZEX · {t.displayNo}</span><span>{item.isOpen ? 'Closes' : 'Closed'} {new Date(t.endDate).toLocaleDateString('en-GB')}</span></div>
        <div className="flex flex-wrap items-center gap-2"><TenderFitBadge match={match} />
          {match.appliedAt && <span className="mt-3 rounded-full bg-brand-50 px-3 py-1 text-xs font-semibold text-brand-700">✓ Applied</span>}
        </div>
        <h2 className="mt-2 text-lg font-semibold"><button type="button" aria-expanded={isExpanded} aria-controls={`tender-details-${t.id}`}
          onClick={() => setExpanded(previous => ({ ...previous, [t.id]: !isExpanded }))}
          className="flex w-full items-center justify-between gap-3 rounded-lg text-left focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600">
          <span>{t.englishTitle}</span><span className="shrink-0 text-xs font-medium text-brand-700">{isExpanded ? 'Hide details' : 'View details'} <span aria-hidden="true">{isExpanded ? '⌃' : '⌄'}</span></span>
        </button></h2>
        {group.lots.length > 1 && <div className="mt-3 rounded-xl border border-brand-100 bg-brand-50 p-3">
          <label htmlFor={`lots-${group.key}`} className="block text-sm font-semibold text-brand-700">{group.lots.length} similar lots</label>
          <p className="mt-1 text-xs text-muted">Review each lot separately before applying.{appliedCount > 0 && ` ${appliedCount} of ${group.lots.length} applied.`}</p>
          <select id={`lots-${group.key}`} value={t.id} disabled={actionBusy !== null || isLeaving}
            onChange={event => { setSelectedLots(previous => ({ ...previous, [group.key]: Number(event.target.value) })); setFeedbackTenderId(null); setFeedbackComment(''); }}
            className="mt-2 w-full rounded-lg border border-line bg-white px-3 py-2 text-sm focus-visible:outline-2 focus-visible:outline-brand-600">
            {group.lots.map(lot => <option key={lot.tender.id} value={lot.tender.id}>{lot.tender.displayNo}{lot.match.appliedAt ? ' · Applied' : ''}{!lot.isOpen ? ' · Closed' : ''}</option>)}
          </select>
        </div>}
        {match.appliedAt && <p className="mt-2 text-xs text-brand-700">{update ? applicationUpdateContent(update.outcome).title : 'Your request is saved.'}</p>}
        <div id={`tender-details-${t.id}`} className={`tender-details ${isExpanded ? 'is-expanded' : ''}`} aria-hidden={!isExpanded} inert={!isExpanded}>
        <div className="tender-details-inner"><p className="mt-2 text-sm leading-relaxed text-ink/80">{t.summary}</p>
        <div className="mt-4 flex flex-wrap gap-3 text-sm"><span className="rounded-lg bg-subtle px-3 py-2"><span className="text-muted">Budget </span><strong>{money(t.startPrice)} {t.currencyCode}</strong></span>{t.depositAmount != null && <span className="rounded-lg bg-subtle px-3 py-2"><span className="text-muted">Deposit </span><strong>{money(t.depositAmount)} {t.currencyCode}</strong></span>}</div>
        <TenderMatchExplanation match={match} isOpen={item.isOpen} />
        <div className="mt-4 flex flex-wrap items-center gap-3">
          <button type="button" aria-label={`Like ${t.englishTitle}`} aria-pressed={match.feedback === 'like'} disabled={actionBusy !== null || isLeaving} onClick={() => feedback(item, 'like', null, group)} className="rounded-lg border border-line px-3 py-2 text-sm aria-pressed:bg-brand-100 disabled:opacity-50">👍 {match.feedback === 'like' ? 'Liked' : 'Like'}</button>
          <button type="button" id={`feedback-trigger-${t.id}`} aria-label={`Dislike ${t.englishTitle}`} aria-expanded={feedbackTenderId === t.id}
            aria-controls={feedbackTenderId === t.id ? `feedback-form-${t.id}` : undefined}
            disabled={actionBusy !== null || isLeaving} onClick={() => { setFeedbackTenderId(t.id); setFeedbackComment(''); }} className="rounded-lg border border-line px-3 py-2 text-sm disabled:opacity-50">👎 Not for me</button>
          <button type="button" aria-label={`${match.appliedAt ? 'Applied for' : 'Apply for'} ${t.englishTitle}`}
            disabled={actionBusy !== null || isLeaving || matchesLoading || !!match.appliedAt || !item.isOpen} onClick={() => apply(item)}
            className={`${BTN_PRIMARY} disabled:opacity-60`}>{match.appliedAt ? '✓ Applied' : item.isOpen ? 'Apply for me' : 'Tender closed'}</button>
          {t.sourceUrl && <a className="ml-auto text-sm font-medium text-brand-700 underline" href={t.sourceUrl} target="_blank" rel="noreferrer">View on UZEX ↗</a>}
        </div>
        {feedbackTenderId === t.id && <form id={`feedback-form-${t.id}`} className="feedback-arrival mt-4 rounded-xl border border-line bg-subtle p-4" onSubmit={event => {
          event.preventDefault();
          feedback(item, 'dislike', feedbackComment, group);
        }}>
          <label htmlFor={`feedback-${t.id}`} className="text-sm font-semibold">Help us understand <span className="font-normal text-muted">(optional)</span></label>
          <p className="mt-1 text-xs text-muted">What made this opportunity less relevant to your business?</p>
          <textarea autoFocus id={`feedback-${t.id}`} value={feedbackComment} onChange={event => setFeedbackComment(event.target.value)} maxLength={500}
            rows={3} placeholder="Share a little feedback, or leave this blank" className="mt-2 w-full rounded-lg border border-line bg-white p-3 text-sm focus-visible:outline-2 focus-visible:outline-brand-600" />
          <div className="mt-2 flex flex-wrap items-center gap-3">
            <button type="submit" disabled={actionBusy !== null} className={`${BTN_PRIMARY} disabled:opacity-50`}>Remove opportunity</button>
            <button type="button" disabled={actionBusy !== null} onClick={() => { setFeedbackTenderId(null); setFeedbackComment(''); document.getElementById(`feedback-trigger-${t.id}`)?.focus(); }} className="text-sm font-medium text-muted underline disabled:opacity-50">Cancel</button>
          </div>
        </form>}
        {match.appliedAt && <p className="mt-3 rounded-lg bg-brand-50 p-3 text-sm text-brand-700">
          {update ? applicationUpdateContent(update.outcome).detail : 'Your request is saved. Check Notifications for updates.'}
        </p>}
        </div></div>
      </article>;
    })}</div>
    {confirmation && <TenderApplicationDialog {...confirmation} onClose={() => setConfirmation(null)} />}
  </section>;
}

export function TenderFitBadge({ match }) {
  const category = matchCategory(match);
  const score = matchScore(match);
  if (!category) return null;
  return <div className={`tender-fit tender-fit-${category.tone} mt-3 inline-flex flex-wrap items-center gap-2 rounded-full px-3 py-1 text-xs font-semibold`}>
    <span>{category.label}</span><span className="font-medium">{score}/100 AI fit</span>
  </div>;
}

export function TenderMatchExplanation({ match, isOpen }) {
  return <div className="mt-4 rounded-xl bg-brand-50 p-4">
    <p className="text-sm font-semibold text-brand-700">✦ Why this is for you</p>
    <p className="mt-1 text-sm">{match.matchReason}</p>
    {isOpen && match.offer && <Link className={`${BTN_PRIMARY} mt-3`} to={`/loan/${match.lotId}`}>Explore financing</Link>}
  </div>;
}
