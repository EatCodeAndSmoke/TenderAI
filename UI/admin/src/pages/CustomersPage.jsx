import { useCallback, useEffect, useState } from 'react';
import { clearCustomers, deleteCustomer, generateCustomer, getCustomers } from '../lib/api.js';
import { BTN_PRIMARY, BTN_SECONDARY, CARD } from '../lib/styles.js';
import PageHeader from '../components/PageHeader.jsx';

export default function CustomersPage({ onToast }) {
  const [customers, setCustomers] = useState([]);
  const [scenario, setScenario] = useState('offer');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [selected, setSelected] = useState(null);
  const load = useCallback(async () => {
    try { setCustomers(await getCustomers()); setError(''); }
    catch (err) { setError(err.message); }
  }, []);
  useEffect(() => { load(); }, [load]);

  async function generate() {
    setBusy(true); setError('');
    try {
      const saved = await generateCustomer(scenario);
      if (!Array.isArray(saved) || saved.length !== 1) throw new Error('The server did not confirm one customer was created.');
      setCustomers(prev => [...prev, saved[0]].sort((a, b) => a.customer.customerId.localeCompare(b.customer.customerId)));
      onToast('Demo customer created.');
    } catch (err) { setError(err.message); }
    finally { setBusy(false); }
  }
  async function remove(id) {
    if (!window.confirm(`Delete ${id} and its demo data?`)) return;
    setBusy(true);
    try { await deleteCustomer(id); setCustomers(prev => prev.filter(c => c.customer.customerId !== id)); setSelected(null); }
    catch (err) { setError(err.message); }
    finally { setBusy(false); }
  }
  async function clear() {
    if (!window.confirm('Delete all customers and their demo data?')) return;
    setBusy(true);
    try { await clearCustomers(); setCustomers([]); setSelected(null); }
    catch (err) { setError(err.message); }
    finally { setBusy(false); }
  }
  return (
    <section>
      <PageHeader title="Customers" summary="Create one fictional business for the selected demo scenario. If needed, open tenders are loaded from UZEX first." />
      <div className={`${CARD} mb-6 p-5`}>
        <label htmlFor="scenario" className="block text-sm font-semibold">Demo scenario</label>
        <div className="mt-2 flex flex-wrap gap-3">
          <select id="scenario" value={scenario} onChange={e => setScenario(e.target.value)} disabled={busy} className="rounded-lg border border-line bg-white px-3 py-2 text-sm">
            <option value="offer">Matching tenders + loan offer</option>
            <option value="match">Matching tenders, no loan offer</option>
            <option value="none">No matching tenders</option>
          </select>
          <button type="button" className={BTN_PRIMARY} disabled={busy} onClick={generate}>{busy ? 'Working…' : 'Generate customer'}</button>
          <button type="button" className={BTN_SECONDARY} disabled={busy || !customers.length} onClick={clear}>Delete all</button>
        </div>
        <p className="mt-3 text-xs text-muted">Matches are evaluated by AI against the open tender list; actual results depend on the available tenders and the evaluation.</p>
      </div>
      {error && <p role="alert" className="mb-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error} <button onClick={load} className="underline">Reload list</button></p>}
      <div className={`${CARD} overflow-x-auto`}>
        <table className="w-full text-left text-sm">
          <thead className="bg-subtle text-xs text-muted"><tr><th className="p-4">Customer</th><th className="p-4">Activity</th><th className="p-4">Location</th><th className="p-4">Actions</th></tr></thead>
          <tbody>{customers.map(record => {
            const c = record.customer;
            return <tr key={c.customerId} className="border-t border-line"><td className="p-4"><strong>{c.name}</strong><div className="text-xs text-muted">{c.customerId} · TIN {c.taxCode}</div></td><td className="p-4">{c.industry.name}<div className="text-xs text-muted">OKED {c.industry.industryCode}</div></td><td className="p-4">{c.address.regionName}</td><td className="p-4 whitespace-nowrap"><button className="mr-3 text-brand-700 underline" onClick={() => setSelected(selected === record ? null : record)}>Details</button><button disabled={busy} className="text-red-700 underline" onClick={() => remove(c.customerId)}>Delete</button></td></tr>;
          })}</tbody>
        </table>
        {!customers.length && <p className="p-6 text-center text-sm text-muted">No customers yet.</p>}
      </div>
      {selected && <div className={`${CARD} mt-4 p-5`}><h2 className="font-semibold">{selected.customer.name}</h2><pre className="mt-3 overflow-x-auto text-xs">{JSON.stringify(selected, null, 2)}</pre></div>}
    </section>
  );
}
