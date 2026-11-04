import { useCallback, useEffect, useState } from 'react';
import { deleteTender, getTenders } from '../lib/api.js';
import { CARD } from '../lib/styles.js';
import PageHeader from '../components/PageHeader.jsx';

export default function TendersPage({ onToast }) {
  const [tenders, setTenders] = useState([]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(null);
  const load = useCallback(async () => {
    setLoading(true);
    try { setTenders(await getTenders()); setError(''); }
    catch (err) { setError(err.message); }
    finally { setLoading(false); }
  }, []);
  useEffect(() => { load(); }, [load]);
  async function remove(tender) {
    if (!window.confirm(`Delete tender ${tender.displayNo} for all customers?`)) return;
    setBusy(tender.id); setError('');
    try {
      await deleteTender(tender.id);
      setTenders(previous => previous.filter(t => t.id !== tender.id));
      onToast('Tender deleted.');
    } catch (err) { setError(err.message); }
    finally { setBusy(null); }
  }
  return <section>
    <PageHeader title="Tenders" summary="Manage stored UZEX tenders. Deleted tenders are hidden for all customers and will not be imported again." />
    {error && <p role="alert" className="mb-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error} <button onClick={load} className="underline">Reload</button></p>}
    <div className={CARD}>
      {loading ? <p className="p-5 text-sm text-muted">Loading tenders…</p> : !tenders.length ? <p className="p-5 text-sm text-muted">No tenders stored.</p> :
        <div className="divide-y divide-line">{tenders.map(t => <div key={t.id} className="flex flex-wrap items-center justify-between gap-4 p-4">
          <div><p className="font-medium">{t.englishTitle || 'Tender summary unavailable'}</p><p className="text-xs text-muted">UZEX {t.displayNo} · Closes {new Date(t.endDate).toLocaleDateString('en-GB')}</p></div>
          <button onClick={() => remove(t)} disabled={busy !== null} className="rounded-lg border border-red-200 px-3 py-2 text-sm text-red-700 disabled:opacity-50">{busy === t.id ? 'Deleting…' : 'Delete'}</button>
        </div>)}</div>}
    </div>
  </section>;
}
