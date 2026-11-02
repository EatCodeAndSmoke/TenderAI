import { useState } from 'react';
import { formatRunTime, money } from '../lib/format.js';
import { BTN_PRIMARY, BTN_SECONDARY, CARD, ROW } from '../lib/styles.js';
import { useMatchingRun } from '../lib/useMatchingRun.js';
import { Pill } from '../components/badges.jsx';
import { PlayIcon, SparkIcon, SpinnerIcon, XIcon } from '../components/icons.jsx';
import PageHeader from '../components/PageHeader.jsx';
import Modal from '../components/Modal.jsx';
import CommentsDialog from '../components/CommentsDialog.jsx';

function EmptyRow({ icon, title, text, action }) {
  return (
    <tr>
      <td colSpan={4} className="px-6 py-14 text-center">
        <div className="mx-auto grid size-11 place-items-center rounded-xl bg-brand-50 text-brand-600">{icon}</div>
        <p className="mt-3 font-medium">{title}</p>
        <p className="mt-1 text-sm text-muted">{text}</p>
        {action}
      </td>
    </tr>
  );
}

function LoanCell({ match }) {
  if (!match.offer) {
    return (
      <>
        <Pill tone="warn">No offer</Pill>
        <div className="mt-1 max-w-xs text-xs text-muted">{match.loanDecisionReason}</div>
      </>
    );
  }

  return (
    <>
      {match.offer.financing > 0 && (
        <div className="whitespace-nowrap tabular-nums">
          <span className="text-xs text-muted">Financing</span>
          <span className="ml-1.5 font-medium">{money(match.offer.financing)}</span>
        </div>
      )}
      {match.offer.guarantee > 0 && (
        <div className="whitespace-nowrap tabular-nums">
          <span className="text-xs text-muted">Guarantee</span>
          <span className="ml-1.5 font-medium">{money(match.offer.guarantee)}</span>
        </div>
      )}
    </>
  );
}

export default function MatchesPage() {
  const matching = useMatchingRun();
  const { matches, status, startedAt, error, progress, refresh, start, clear } = matching;
  const [selected, setSelected] = useState(null);
  const [clearing, setClearing] = useState(false);
  const offered = matches.filter((match) => match.offer).length;
  const running = status === 'running';
  const loading = status === 'loading';

  const summary = loading
    ? 'Loading saved matches…'
    : running
      ? `Evaluated ${progress.completed} of ${progress.total} customers; ${matches.length} saved matches are listed.`
      : `${matches.length} ${matches.length === 1 ? 'match' : 'matches'} from the latest run. ${offered} include a loan application invitation.`;

  let body;
  if (loading) {
    body = (
      <EmptyRow
        icon={<SpinnerIcon className="size-5" />}
        title="Loading saved matches"
        text="Reading matching results from the database."
      />
    );
  } else if (matches.length === 0) {
    body = (
      <EmptyRow
        icon={<SparkIcon className="size-5" />}
        title={status === 'error' ? 'Matches could not be loaded' : 'No matches yet'}
        text={status === 'error' ? error : 'Start matching to check stored customers and open tenders from UZEX.'}
        action={status === 'error' && (
          <button type="button" onClick={refresh} className="mt-3 font-medium text-brand-700 hover:underline">
            Try again
          </button>
        )}
      />
    );
  } else {
    body = matches.map((match) => (
      <tr key={`${match.customerId}-${match.lotId}`} className={ROW}>
        <td className="px-3.5 py-4">
          <div className="font-medium whitespace-nowrap">{match.customerName}</div>
          <div className="text-xs text-muted">{match.customerId}</div>
        </td>
        <td className="px-3.5 py-4">
          <div>{match.tenderTitle}</div>
          <div className="text-xs text-muted">{match.lotId}</div>
        </td>
        <td className="px-3.5 py-4 text-right"><LoanCell match={match} /></td>
        <td className="px-3.5 py-4 text-right">
          <button
            type="button"
            onClick={() => setSelected(match)}
            aria-label={`AI comments for ${match.customerName} and ${match.lotId}`}
            className="inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-medium whitespace-nowrap text-brand-700 ring-1 ring-line ring-inset transition-colors hover:bg-brand-50 hover:ring-brand-300"
          >
            <SparkIcon /> View
          </button>
        </td>
      </tr>
    ));
  }

  const handleClear = async () => {
    if (!window.confirm('Remove all saved matches and their Business Bank notifications? This can’t be undone.')) return;
    setClearing(true);
    await clear();
    setClearing(false);
  };

  return (
    <section>
      <PageHeader
        title="Matches"
        summary={summary}
        actions={
          <>
            <button
              type="button"
              onClick={handleClear}
              disabled={matches.length === 0 || running || clearing}
              className={`${BTN_SECONDARY} disabled:cursor-not-allowed disabled:opacity-50`}
            >
              {clearing ? <><SpinnerIcon /> Clearing…</> : <><XIcon /> Clear</>}
            </button>
            <button
              type="button"
              onClick={start}
              disabled={running || loading}
              className={`${BTN_PRIMARY} disabled:cursor-wait disabled:bg-brand-600/85`}
            >
              {running ? <><SpinnerIcon /> Matching…</> : <><PlayIcon /> Start matching</>}
            </button>
          </>
        }
      />

      {error && status !== 'error' && (
        <p role="alert" className="mb-4 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{error}</p>
      )}
      {running && (
        <p role="status" className="mb-4 rounded-lg border border-brand-200 bg-brand-50 px-4 py-3 text-sm text-brand-800">
          Evaluated {progress.completed} of {progress.total} customers
          {progress.failed > 0 ? `; ${progress.failed} customer evaluations failed.` : '.'}
          {' '}Each completed customer’s results are saved and shown as they arrive.
        </p>
      )}
      {startedAt && status !== 'running' && (
        <p className="mb-3 text-xs text-muted">Last run: {formatRunTime(startedAt)}</p>
      )}

      <div className={CARD}>
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="bg-subtle text-left text-xs whitespace-nowrap text-muted">
              <tr className="border-b border-line">
                <th className="px-3.5 py-3 font-medium">Customer</th>
                <th className="px-3.5 py-3 font-medium">Tender</th>
                <th className="px-3.5 py-3 text-right font-medium">Suggested loan, so'm</th>
                <th className="px-3.5 py-3 text-right font-medium">AI comments</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">{body}</tbody>
          </table>
        </div>
      </div>

      <Modal open={selected !== null} onClose={() => setSelected(null)} labelledBy="comments-title">
        <CommentsDialog match={selected} onClose={() => setSelected(null)} />
      </Modal>
    </section>
  );
}
