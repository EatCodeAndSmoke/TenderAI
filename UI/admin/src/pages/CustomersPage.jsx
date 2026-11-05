import { useCallback, useEffect, useState } from 'react';
import { clearCustomers, deleteCustomer, generateCustomer, getCustomers } from '../lib/api.js';
import { BTN_PRIMARY, BTN_SECONDARY, CARD } from '../lib/styles.js';
import PageHeader from '../components/PageHeader.jsx';
import CustomerForm from '../components/CustomerForm.jsx';

export default function CustomersPage({ onToast }) {
  const [customers, setCustomers] = useState([]);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [selected, setSelected] = useState(null);
  const [adding, setAdding] = useState(false);
  const nextId = (() => {
    const ids = new Set(customers.map(record => record.customer.customerId.toUpperCase()));
    let number = 1;
    while (ids.has(`C-${String(number).padStart(2, '0')}`)) number++;
    return `C-${String(number).padStart(2, '0')}`;
  })();
  const load = useCallback(async () => {
    setLoading(true);
    try { setCustomers(await getCustomers()); setError(''); }
    catch (err) { setError(err.message); }
    finally { setLoading(false); }
  }, []);
  useEffect(() => { load(); }, [load]);

  async function generate() {
    setBusy(true); setError('');
    try {
      const saved = await generateCustomer();
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
      <PageHeader title="Customers" summary="Generate a fictional business or enter a customer manually." />
      <div className={`${CARD} mb-6 p-5`}>
        <div className="mt-2 flex flex-wrap gap-3">
          <button type="button" className={`${BTN_PRIMARY} disabled:opacity-50`} disabled={loading || busy || adding} onClick={generate}>{busy ? 'Working…' : 'Generate customer'}</button>
          <button type="button" className={`${BTN_SECONDARY} disabled:opacity-50`} disabled={loading || busy || adding} aria-haspopup="dialog" onClick={() => setAdding(true)}>Add customer</button>
          <button type="button" className={`${BTN_SECONDARY} disabled:opacity-50`} disabled={loading || busy || adding || !customers.length} onClick={clear}>Delete all</button>
        </div>
        <p className="mt-3 text-xs text-muted">Generated customers use fictional profiles and payment data. Matching depends on available tenders.</p>
      </div>
      {(adding || selected) && <CustomerForm defaultCustomerId={nextId} customerRecord={selected} onCancel={() => { setAdding(false); setSelected(null); }} onSaved={saved => {
        setCustomers(previous => [...previous.filter(record => record.customer.customerId !== saved.customer.customerId), saved]
          .sort((a, b) => a.customer.customerId.localeCompare(b.customer.customerId)));
        setAdding(false); setSelected(null);
        onToast(selected ? 'Customer updated.' : 'Customer saved.');
      }} />}
      {error && <p role="alert" className="mb-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error} <button onClick={load} className="underline">Reload list</button></p>}
      <div className={`${CARD} overflow-x-auto`}>
        <table className="w-full text-left text-sm">
          <thead className="bg-subtle text-xs text-muted"><tr><th className="p-4">Customer</th><th className="p-4">Activity</th><th className="p-4">Location</th><th className="p-4">Actions</th></tr></thead>
          <tbody>{customers.map(record => {
            const c = record.customer;
            return <tr key={c.customerId} className="border-t border-line"><td className="p-4"><strong>{c.name}</strong><div className="text-xs text-muted">{c.customerId} · TIN {c.taxCode}</div></td><td className="p-4">{c.industry.name}<div className="text-xs text-muted">OKED {c.industry.industryCode}</div></td><td className="p-4">{c.address.regionName}</td><td className="p-4 whitespace-nowrap"><button type="button" aria-haspopup="dialog" disabled={busy || adding} className="mr-3 text-brand-700 underline disabled:opacity-50" onClick={() => setSelected(record)}>Details</button><button disabled={busy || adding} className="text-red-700 underline disabled:opacity-50" onClick={() => remove(c.customerId)}>Delete</button></td></tr>;
          })}</tbody>
        </table>
        {!customers.length && <p className="p-6 text-center text-sm text-muted">No customers yet.</p>}
      </div>
    </section>
  );
}
