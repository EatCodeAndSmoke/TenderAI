import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { getAccounts } from '../api.js';
import { money } from '../format.js';
import { useSession } from '../session.jsx';

export default function HomePage() {
  const { customerId, customerName } = useSession();
  const [accounts, setAccounts] = useState([]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    let active = true;
    getAccounts(customerId).then(data => { if (active) setAccounts(data); })
      .catch(err => { if (active) setError(err.message); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [customerId]);
  return <section>
    <p className="text-sm text-muted">Welcome back</p>
    <h1 className="text-2xl font-semibold tracking-tight">{customerName}</h1>
    <p className="mt-1 text-sm text-muted">Your business, all in one place.</p>

    <Link to="/tenders" className="tender-feature mt-6 block overflow-hidden rounded-3xl p-6 text-white shadow-lg transition hover:-translate-y-0.5">
      <div className="flex items-start justify-between gap-4">
        <div><span className="rounded-full bg-white/20 px-3 py-1 text-xs font-semibold">✦ NEW FOR YOUR BUSINESS</span>
          <h2 className="mt-5 text-2xl font-semibold">Discover your next opportunity</h2>
          <p className="mt-2 max-w-md text-sm text-white/85">Explore open tenders and find opportunities matched to your business with AI.</p>
          <span className="mt-5 inline-block rounded-xl bg-white px-4 py-2 text-sm font-semibold text-cobalt">Explore tenders →</span>
        </div><span aria-hidden="true" className="hidden text-6xl text-white/50 sm:block">✧</span>
      </div>
    </Link>

    <div className="mt-8 flex items-center justify-between"><h2 className="text-lg font-semibold">Accounts</h2><span className="text-xs text-muted">Multicurrency account</span></div>
    <div className="mt-3 rounded-2xl border border-line bg-white p-5 shadow-sm">
      {loading ? <p className="text-sm text-muted">Loading accounts…</p> : error ? <p role="alert" className="text-sm text-red-700">{error}</p> : <>
        <p className="text-xs text-muted">IBAN · Demo account</p><p className="mt-1 break-all font-mono text-sm">{accounts[0]?.iban}</p>
        <div className="mt-5 grid gap-3 sm:grid-cols-3">{accounts.map(a => <div key={a.currency} className="rounded-xl bg-subtle p-4"><p className="text-xs text-muted">{a.currency} balance</p><p className="mt-1 text-lg font-semibold tabular-nums">{money(a.balance.toFixed(2))} <span className="text-xs font-medium text-muted">{a.currency}</span></p></div>)}</div>
      </>}
    </div>
    <div className="mt-8 grid gap-5 sm:grid-cols-2">
      <div><h2 className="mb-3 text-lg font-semibold">Cards</h2>
        <div className="business-card relative overflow-hidden rounded-2xl p-5 text-white shadow-lg">
          <div className="flex justify-between"><span className="text-lg font-bold tracking-tight">tbc <span className="font-normal">business</span></span><span className="text-xs uppercase tracking-widest">BusinessCard</span></div>
          <div className="mt-8 h-7 w-10 rounded-md bg-amber-100/80" aria-hidden="true" />
          <p className="mt-4 font-mono text-sm tracking-widest">••••  ••••  ••••  4821</p>
          <div className="mt-5 flex items-end justify-between"><span className="max-w-[70%] truncate text-xs uppercase tracking-wide">{customerName}</span><span className="text-xl font-black italic">VISA</span></div>
        </div><p className="mt-2 text-xs text-muted">Illustrative card · not a real payment card</p>
      </div>
      <div><h2 className="mb-3 text-lg font-semibold">Loans</h2><div className="flex min-h-48 flex-col justify-center rounded-2xl border border-line bg-white p-5">
        <p className="font-medium">Financing for your next project</p><p className="mt-2 text-sm text-muted">Eligible tenders may include an invitation to apply for financing or a bid guarantee.</p>
        <Link to="/tenders" className="mt-4 text-sm font-semibold text-brand-700">See tender opportunities →</Link>
      </div></div>
    </div>
  </section>;
}
