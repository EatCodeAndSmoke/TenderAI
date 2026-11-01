import { useState } from 'react';
import { customerById, customers } from '../data/customers.js';
import { tenderById, tenders } from '../data/tenders.js';
import { matches } from '../data/matches.js';
import { formatRunTime, money } from '../lib/format.js';
import { BTN_PRIMARY, BTN_SECONDARY, CARD, ROW } from '../lib/styles.js';
import { Pill } from '../components/badges.jsx';
import { PlayIcon, SparkIcon, SpinnerIcon, XIcon } from '../components/icons.jsx';
import PageHeader from '../components/PageHeader.jsx';
import Modal from '../components/Modal.jsx';
import CommentsDialog from '../components/CommentsDialog.jsx';

function EmptyRow({ icon, title, text }) {
  return (
    <tr>
      <td colSpan={4} className="px-6 py-14 text-center">
        <div className="mx-auto grid size-11 place-items-center rounded-xl bg-brand-50 text-brand-600">{icon}</div>
        <p className="mt-3 font-medium">{title}</p>
        <p className="mt-1 text-sm text-muted">{text}</p>
      </td>
    </tr>
  );
}

function LoanCell({ match: m }) {
  if (!m.offers.length) {
    return (
      <>
        <Pill tone="warn">No offer</Pill>
        <div className="mt-1 text-xs text-muted">{m.refusal}</div>
      </>
    );
  }
  return m.offers.map((o) => (
    <div key={o.product} className="whitespace-nowrap tabular-nums">
      <span className="text-xs text-muted">{o.product === 'Bid guarantee' ? 'Guarantee' : 'Financing'}</span>
      <span className="ml-1.5 font-medium">{money(o.amount)}</span>
    </div>
  ));
}

export default function MatchesPage({ run, onStart }) {
  const [comments, setComments] = useState({ open: false, index: null });

  const visible = matches.slice(0, run.shown);
  const offered = visible.filter((m) => m.offers.length).length;
  const running = run.status === 'running';

  const summary =
    run.status === 'idle'
      ? `No matches yet. Start matching to check ${customers.length} customers against ${tenders.length} open tenders.`
      : running
        ? `Matching started at ${formatRunTime(run.startedAt).split(' at ')[1]}. ${visible.length} found so far.`
        : `${visible.length} matches from the run on ${formatRunTime(run.startedAt)}. ${offered} come with a loan offer.`;

  let body;
  if (run.status === 'idle') {
    body = (
      <EmptyRow
        icon={<SparkIcon className="size-5" />}
        title="No matches yet"
        text="Press Start matching and the agents will check every customer against the open tenders."
      />
    );
  } else if (running && visible.length === 0) {
    body = (
      <EmptyRow
        icon={<SpinnerIcon className="size-5" />}
        title="The agents are matching"
        text={`Checking ${customers.length} customers against ${tenders.length} open tenders.`}
      />
    );
  } else {
    body = (
      <>
        {visible.map((m, i) => {
          const c = customerById(m.customerId);
          const t = tenderById(m.lotId);
          // The newest row slides in while the run is going
          const isNew = running && i === visible.length - 1;
          return (
            <tr key={`${m.customerId}-${m.lotId}`} className={`${ROW} ${isNew ? 'match-row-in' : ''}`}>
              <td className="px-3.5 py-4">
                <div className="font-medium whitespace-nowrap">{c.name}</div>
                <div className="text-xs text-muted">{m.customerId}</div>
              </td>
              <td className="px-3.5 py-4">
                <div>{t.title.en}</div>
                <div className="text-xs text-muted">{m.lotId}</div>
              </td>
              <td className="px-3.5 py-4 text-right"><LoanCell match={m} /></td>
              <td className="px-3.5 py-4 text-right">
                <button
                  type="button"
                  onClick={() => setComments({ open: true, index: i })}
                  aria-label={`AI comments for ${c.name} and ${m.lotId}`}
                  className="inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-medium whitespace-nowrap text-brand-700 ring-1 ring-line ring-inset transition-colors hover:bg-brand-50 hover:ring-brand-300"
                >
                  <SparkIcon /> View
                </button>
              </td>
            </tr>
          );
        })}
        {/* While the run is going, a quiet last row says more may come */}
        {running && (
          <tr>
            <td colSpan={4} className="px-3.5 py-3">
              <div className="flex items-center gap-2 text-sm text-muted"><SpinnerIcon /> Still matching…</div>
            </td>
          </tr>
        )}
      </>
    );
  }

  return (
    <section>
      <PageHeader
        title="Matches"
        summary={summary}
        actions={
          <>
            {/* "Clear" intentionally does nothing yet: later it deletes the saved results (not customers' notifications) */}
            <button type="button" className={BTN_SECONDARY}><XIcon /> Clear</button>
            <button
              type="button"
              onClick={onStart}
              disabled={running}
              className={`${BTN_PRIMARY} disabled:cursor-wait disabled:bg-brand-600/85`}
            >
              {running ? <><SpinnerIcon /> Matching…</> : <><PlayIcon /> Start matching</>}
            </button>
          </>
        }
      />

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

      <Modal open={comments.open} onClose={() => setComments((s) => ({ ...s, open: false }))} labelledBy="comments-title">
        <CommentsDialog
          key={comments.index}
          match={comments.index === null ? null : matches[comments.index]}
          startedAt={run.startedAt}
          onClose={() => setComments((s) => ({ ...s, open: false }))}
        />
      </Modal>
    </section>
  );
}
