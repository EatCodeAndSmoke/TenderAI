import { customerById } from '../data/customers.js';
import { tenderById } from '../data/tenders.js';
import { CHECKS } from '../data/matches.js';
import { formatRunTime, money } from '../lib/format.js';
import { BTN_GHOST } from '../lib/styles.js';
import { Pill } from './badges.jsx';
import { FailIcon, PassIcon, SparklesIcon } from './icons.jsx';
import { ModalHeader } from './Modal.jsx';

function Checks({ title, checks, offer }) {
  const passed = checks.every(([, , ok]) => ok);
  return (
    <div className="rounded-xl border border-line">
      <div className="flex items-center justify-between gap-3 border-b border-line bg-subtle px-4 py-2.5">
        <span className="text-sm font-medium">{title}</span>
        {passed
          ? <Pill tone="ok">{offer ? `${money(offer.amount)} so'm` : 'Offered'}</Pill>
          : <Pill tone="warn">Not offered</Pill>}
      </div>
      <ul className="divide-y divide-line">
        {checks.map(([rule, value, ok]) => (
          <li key={rule} className="flex items-center gap-3 px-4 py-2 text-sm">
            {ok ? <PassIcon /> : <FailIcon />}
            <span className="flex-1">{rule}</span>
            <span className={`text-xs whitespace-nowrap ${ok ? 'text-muted' : 'font-medium text-red-600'}`}>{value}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function CommentSection({ agent, title, children }) {
  return (
    <section>
      <div className="mb-2 flex items-baseline justify-between gap-3">
        <h3 className="text-sm font-semibold">{title}</h3>
        <span className="text-xs text-muted">{agent}</span>
      </div>
      {children}
    </section>
  );
}

// AI comments for one match (read-only), shown inside <Modal>
export default function CommentsDialog({ match: m, startedAt, onClose }) {
  if (!m) return null;

  const c = customerById(m.customerId);
  const t = tenderById(m.lotId);
  const checks = CHECKS[m.customerId];
  const offerFor = (product) => m.offers.find((o) => o.product === product);

  return (
    <div className="flex max-h-[88vh] flex-col">
      <ModalHeader
        id="comments-title"
        className="px-6 pt-6 pb-4"
        icon={<SparklesIcon />}
        title="AI comments"
        subtitle={`${c.name} and ${m.lotId}, ${t.title.en}`}
        onClose={onClose}
      />

      <div className="space-y-6 overflow-y-auto border-t border-line px-6 py-6">
        <CommentSection agent="Tender matcher" title="Why this tender fits">
          <ul className="space-y-2">
            {m.reasons.map((r) => <li key={r} className="flex gap-2.5 text-sm"><PassIcon /><span>{r}</span></li>)}
          </ul>
          {m.ignored.length > 0 && (
            <p className="mt-3 rounded-lg bg-subtle px-3 py-2 text-xs text-muted">{m.ignored.join(' ')}</p>
          )}
        </CommentSection>

        <CommentSection agent="Loan advisor" title="Loan decision">
          <p className={`mb-3 rounded-xl px-4 py-3 text-sm ${m.offers.length ? 'bg-emerald-50 text-emerald-900' : 'bg-amber-50 text-amber-900'}`}>{m.note}</p>
          <div className="space-y-3">
            <Checks title="Bid guarantee" checks={checks.guarantee} offer={offerFor('Bid guarantee')} />
            <Checks title="Contract financing" checks={checks.financing} offer={offerFor('Contract financing')} />
          </div>
        </CommentSection>

        <CommentSection agent="Loan advisor" title="How the amounts were calculated">
          {m.offers.length ? (
            <ul className="space-y-1.5 text-sm">
              {m.offers.map((o) => <li key={o.product}><span className="font-medium">{o.product}:</span> {o.calc}</li>)}
            </ul>
          ) : (
            <p className="text-sm text-muted">Not calculated, because no product passed all its rules.</p>
          )}
        </CommentSection>
      </div>

      <div className="flex items-center justify-between gap-3 border-t border-line bg-subtle px-6 py-4">
        <p className="text-xs text-muted">{startedAt && `From the run on ${formatRunTime(startedAt)}`}</p>
        <button type="button" onClick={onClose} className={BTN_GHOST}>Close</button>
      </div>
    </div>
  );
}
