import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { BackIcon } from '../components/icons.jsx';
import { applicationUpdateContent } from '../applicationUpdates.js';
import { useSession } from '../session.jsx';

export default function NotificationsPage() {
  const { updates, updatesError, updatesLoading, refreshUpdates, markUpdatesRead } = useSession();
  const [readError, setReadError] = useState('');
  const [marking, setMarking] = useState(false);
  const unread = updates.some(update => !update.isRead);

  useEffect(() => {
    if (!unread || marking || readError) return;
    setMarking(true);
    markUpdatesRead(updates.filter(update => !update.isRead).slice(0, 500).map(update => update.tenderId))
      .catch(err => setReadError(err.message)).finally(() => setMarking(false));
  }, [updates, unread, marking, readError, markUpdatesRead]);

  return <section>
    <Link to="/home" className="inline-flex items-center gap-1.5 text-sm font-medium text-muted"><BackIcon /> Home</Link>
    <h1 className="mt-3 text-2xl font-semibold">Notifications</h1>
    <p className="mt-1 text-sm text-muted">Updates about your simulated tender requests.</p>
    {(updatesError || readError) && <p role="alert" className="mt-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">
      {readError || updatesError}{' '}
      <button type="button" className="underline" onClick={() => {
        setReadError('');
        refreshUpdates().catch(() => {});
      }}>Retry</button>
    </p>}
    {updatesLoading && <p role="status" className="mt-5 text-sm text-muted">Loading updates…</p>}
    {updates.length === 0 && !updatesError && !updatesLoading && <div className="mt-5 rounded-2xl border border-dashed border-line bg-surface px-6 py-14 text-center">
      <p className="font-medium">You have no notifications</p>
      <p className="mt-1 text-sm text-muted">Your demo application updates will appear here.</p>
    </div>}
    <div className="mt-5 space-y-3">{updates.map(update => {
      const content = applicationUpdateContent(update.outcome);
      return <article key={update.tenderId} className="notification-arrival rounded-2xl border border-brand-100 bg-white p-5 shadow-sm">
        <div className="flex items-start gap-3">
          <span aria-hidden="true" className="grid size-10 shrink-0 place-items-center rounded-full bg-brand-50 text-brand-700">{update.outcome === 'sent' ? '✓' : '✦'}</span>
          <div><p className="text-xs font-semibold uppercase tracking-wide text-brand-700">Demo update</p>
            <h2 className="mt-1 font-semibold">{content.title}</h2>
            <p className="mt-1 text-sm font-medium">{update.tenderTitle}</p>
            <p className="mt-2 text-sm leading-relaxed text-ink/80">{content.detail}</p>
            <Link to="/tenders" className="mt-3 inline-block text-sm font-semibold text-brand-700 underline">View your opportunities</Link>
          </div>
        </div>
      </article>;
    })}</div>
  </section>;
}
