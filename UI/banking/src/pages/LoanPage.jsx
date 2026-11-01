import { useRef, useState } from 'react';
import { Link, Navigate, useParams } from 'react-router-dom';
import { LOAN_PRODUCTS, LOAN_TERMS } from '../data.js';
import { useSession } from '../session.jsx';
import { BTN_PRIMARY, INPUT, money } from '../format.js';
import { BackIcon, CheckIcon } from '../components/icons.jsx';

export default function LoanPage() {
  const { lotId } = useParams();
  const { customerId, notifications } = useSession();
  const [sent, setSent] = useState(null);

  const n = notifications.find((x) => x.lotId === lotId);
  if (!n || !n.offer || (n.offer.financing <= 0 && n.offer.guarantee <= 0)) {
    return <Navigate to="/notifications" replace />;
  }

  return sent
    ? <LoanSent notification={n} request={sent} />
    : <LoanForm notification={n} customerId={customerId} onSent={setSent} />;
}

function LoanForm({ notification: n, customerId, onSent }) {
  const [product, setProduct] = useState(() => n.offer.financing > 0 ? 'financing' : 'guarantee');
  const [amount, setAmount] = useState(() => money(n.offer.financing > 0 ? n.offer.financing : n.offer.guarantee));
  const [term, setTerm] = useState('6');
  const [error, setError] = useState('');
  const amountRef = useRef(null);

  const availableProducts = Object.entries(LOAN_PRODUCTS)
    .filter(([key, item]) => n.offer[item.limitKey] > 0 && (key !== 'guarantee' || n.offer.guarantee > 0));
  const p = LOAN_PRODUCTS[product];
  const limit = n.offer[p.limitKey];

  // Picking a loan type fills in its pre-approved limit
  const selectProduct = (key) => {
    setProduct(key);
    setAmount(money(n.offer[LOAN_PRODUCTS[key].limitKey]));
    setError('');
  };

  // Keep the amount readable: digits only, grouped with spaces
  const handleAmountChange = (e) => {
    const digits = e.target.value.replace(/\D/g, '');
    setAmount(digits ? money(Number(digits)) : '');
  };

  const handleSubmit = (e) => {
    e.preventDefault();
    const value = Number(amount.replace(/\D/g, ''));

    if (!value || value > limit) {
      setError(value ? `The amount can't be more than ${money(limit)} so'm.` : 'Enter an amount.');
      amountRef.current?.focus();
      return;
    }

    const request = {
      customerId,
      lotId: n.lotId,
      product,
      amount: value,
      termMonths: p.hasTerm ? Number(term) : null,
      requestedAt: new Date().toISOString()
    };
    console.log('Loan request:', request);
    console.log(JSON.stringify(request, null, 2));

    onSent(request);
  };

  return (
    <section>
      <Link to="/notifications" className="inline-flex items-center gap-1.5 text-sm font-medium text-muted transition-colors hover:text-ink">
        <BackIcon /> Notifications
      </Link>
      <h1 className="mt-3 text-2xl font-semibold tracking-tight">Request a loan</h1>
      <p className="mt-1 text-sm text-muted">For the tender {n.lotId}: {n.title}</p>

      <form noValidate onSubmit={handleSubmit} className="mt-5 overflow-hidden rounded-2xl border border-line bg-surface shadow-sm shadow-cobalt/5">
        <div className="space-y-5 p-5">
          <div>
            <span className="mb-1.5 block text-sm font-medium">Loan type</span>
            <div role="group" aria-label="Loan type" className="grid grid-cols-2 gap-1 rounded-xl bg-subtle p-1 text-sm">
              {availableProducts.map(([key, lp]) => (
                <button
                  key={key}
                  type="button"
                  aria-pressed={key === product}
                  onClick={() => selectProduct(key)}
                  className="rounded-lg px-3 py-2 font-medium text-muted transition-colors hover:text-ink aria-pressed:bg-surface aria-pressed:text-ink aria-pressed:shadow-sm"
                >
                  {lp.name}
                </button>
              ))}
            </div>
          </div>

          <div>
            <label htmlFor="loan-amount" className="mb-1.5 block text-sm font-medium">Amount</label>
            <div className="relative">
              <input
                ref={amountRef}
                id="loan-amount"
                type="text"
                inputMode="numeric"
                autoComplete="off"
                value={amount}
                onChange={handleAmountChange}
                className={`${INPUT} pr-14 tabular-nums`}
              />
              <span className="pointer-events-none absolute inset-y-0 right-3 flex items-center text-xs text-muted">so'm</span>
            </div>
            <p className="mt-1.5 text-xs text-muted">Up to {money(limit)} so'm, within your pre-approved limit. The bank will review the application.</p>
            {error && <p role="alert" className="mt-1.5 text-xs font-medium text-red-600">{error}</p>}
          </div>

          {p.hasTerm && (
            <div>
              <label htmlFor="loan-term" className="mb-1.5 block text-sm font-medium">Term</label>
              <select id="loan-term" value={term} onChange={(e) => setTerm(e.target.value)} className={`${INPUT} pr-8`}>
                {LOAN_TERMS.map((m) => <option key={m} value={m}>{m} months</option>)}
              </select>
            </div>
          )}
        </div>

        <div className="flex flex-wrap items-center justify-between gap-3 border-t border-line bg-subtle px-5 py-4">
          <p className="text-xs text-muted">A loan manager reviews every request and contacts you.</p>
          <button type="submit" className={BTN_PRIMARY}>Send request</button>
        </div>
      </form>
    </section>
  );
}

function LoanSent({ notification: n, request }) {
  const [number] = useState(() => `LR-${String(Date.now()).slice(-5)}`);

  return (
    <div className="mx-auto max-w-md rounded-2xl border border-line bg-surface px-6 py-10 text-center shadow-sm shadow-cobalt/5">
      <span className="mx-auto grid size-12 place-items-center rounded-full bg-brand-50 text-brand-600">
        <CheckIcon className="size-6" />
      </span>
      <h1 className="mt-4 text-xl font-semibold">Request sent</h1>
      <p className="mt-2 text-sm text-muted">
        We've received your request for {money(request.amount)} so'm of {LOAN_PRODUCTS[request.product].name.toLowerCase()} for
        the tender {n.lotId}. A loan manager will contact you soon.
      </p>
      <p className="mt-4 text-xs text-muted">Request number <span className="font-medium text-ink">{number}</span></p>
      <Link to="/notifications" className={`${BTN_PRIMARY} mt-6`}>Back to notifications</Link>
    </div>
  );
}
