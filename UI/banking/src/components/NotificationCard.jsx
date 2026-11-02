import { Link } from 'react-router-dom';
import { BTN_PRIMARY, formatDate, money, timeAgo } from '../format.js';
import { CheckIcon, DocIcon, SparkIcon } from './icons.jsx';

function Fact({ label, value }) {
  return (
    <div>
      <dt className="text-xs text-muted">{label}</dt>
      <dd className="mt-0.5 font-medium">{value}</dd>
    </div>
  );
}

function Offer({ notification: n }) {
  const o = n.offer;
  return (
    <div className="border-t border-brand-100 bg-brand-50 px-5 py-4">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <p className="text-xs font-medium text-brand-700">Loan application invitation</p>
          {o.financing > 0 && <p className="mt-1 text-sm font-semibold">Contract financing up to {money(o.financing)} so'm</p>}
          {o.guarantee > 0 && <p className="text-sm text-muted">Bid guarantee of {money(o.guarantee)} so'm</p>}
          <p className="mt-1 text-xs text-muted">Each application amount is within your pre-approved limit of {money(o.preApprovedLimit)} so'm. The bank will review your request.</p>
        </div>
        <Link to={`/loan/${n.lotId}`} className={BTN_PRIMARY}>Request loan</Link>
      </div>
    </div>
  );
}

export default function NotificationCard({ notification: n, isNew }) {
  return (
    <article className={`overflow-hidden rounded-2xl border bg-surface shadow-sm shadow-cobalt/5 ${isNew ? 'border-brand-300' : 'border-line'}`}>
      <div className="p-5">
        <div className="flex items-center justify-between gap-3 text-xs">
          <span className="inline-flex items-center gap-1.5 font-medium text-brand-700"><DocIcon /> New tender for you</span>
          <span className="flex items-center gap-2 text-muted">
            {isNew && <span className="rounded-full bg-brand-600 px-2 py-0.5 font-medium text-white">New</span>}
            {timeAgo(n.createdAt)}
          </span>
        </div>

        <h2 className="mt-2 text-lg font-semibold">{n.title}</h2>
        <p className="mt-1 text-xs text-muted">Buyer: {n.buyer}</p>
        {n.body && n.body !== n.title && (
          <p className="mt-1.5 text-sm leading-relaxed text-ink/80">{n.body}</p>
        )}
        {n.sourceUrl && (
          <a
            href={n.sourceUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="mt-2 inline-flex text-xs font-medium text-brand-700 hover:underline"
          >
            View tender on UZEX
          </a>
        )}

        <dl className="mt-4 grid grid-cols-2 gap-3 rounded-xl bg-subtle px-4 py-3 text-sm sm:grid-cols-4">
          <Fact label="Start price" value={n.currencyCode === 'UZS' ? `${money(n.startPrice)} so'm` : `${money(n.startPrice)} ${n.currencyCode}`} />
          <Fact label="Bids close" value={formatDate(n.deadline)} />
          <Fact label="Payment" value={n.paymentTermsDays == null ? '—' : `${n.paymentTermsDays} days after delivery`} />
          <Fact label="Region" value={n.region} />
        </dl>

        {n.requirements.length > 0 && (
          <>
            <h3 className="mt-4 text-xs font-medium text-muted">What the buyer needs</h3>
            <ul className="mt-1.5 space-y-1">
              {n.requirements.map((r) => (
                <li key={r} className="flex gap-2 text-sm"><CheckIcon /><span>{r}</span></li>
              ))}
            </ul>
          </>
        )}

        <p className="mt-4 flex gap-2 text-sm text-muted">
          <SparkIcon />
          <span><span className="font-medium text-ink">Why this tender matched:</span> {n.matchReason}</span>
        </p>
        <p className="mt-2 pl-6 text-sm text-muted">
          <span className="font-medium text-ink">Loan application:</span> {n.loanDecisionReason}
        </p>
      </div>
      {n.offer && <Offer notification={n} />}
    </article>
  );
}
