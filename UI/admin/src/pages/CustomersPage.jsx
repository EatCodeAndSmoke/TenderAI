import { useCallback, useEffect, useState } from 'react';
import { LANGUAGE_NAMES } from '../data/customers.js';
import { clearCustomers, deleteCustomer, getCustomers } from '../lib/api.js';
import { initials, money } from '../lib/format.js';
import { BTN_PRIMARY, BTN_SECONDARY, CARD, ROW } from '../lib/styles.js';
import { Chip, OverduePill } from '../components/badges.jsx';
import { PlusIcon, TrashIcon, XIcon } from '../components/icons.jsx';
import PageHeader from '../components/PageHeader.jsx';
import Modal from '../components/Modal.jsx';
import CustomerForm from '../components/CustomerForm.jsx';

const HEADERS = [
  ['Company', 'px-3.5'],
  ['Activity', 'px-3.5'],
  ['Location', 'px-3.5'],
  ['Language', 'px-3.5'],
  ["Monthly inflow, so'm", 'px-4 text-right'],
  ['Overdue, 12 months', 'px-3.5'],
  ["Pre-approved limit, so'm", 'px-4 text-right']
];

const byId = (a, b) => a.customer.customerId.localeCompare(b.customer.customerId);

// Suggested ID for a new customer: one more than the highest number in use (C-07 -> C-08)
function nextCustomerId(customers) {
  const max = Math.max(0, ...customers.map((c) => Number(c.customer.customerId.replace(/\D/g, '')) || 0));
  return `C-${String(max + 1).padStart(2, '0')}`;
}

export default function CustomersPage({ onToast }) {
  const [customers, setCustomers] = useState([]);
  const [status, setStatus] = useState('loading');
  const [loadError, setLoadError] = useState('');
  const [clearing, setClearing] = useState(false);
  const [deletingId, setDeletingId] = useState(null);

  // record = null opens an empty "Add customer" form; a record opens its details.
  // formKey changes on every open so the form starts fresh.
  const [dialog, setDialog] = useState({ open: false, record: null, formKey: 0 });
  const openDialog = (record = null) => setDialog((d) => ({ open: true, record, formKey: d.formKey + 1 }));
  const closeDialog = () => setDialog((d) => ({ ...d, open: false }));

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      setCustomers(await getCustomers());
      setStatus('ready');
    } catch (err) {
      setLoadError(err.message);
      setStatus('error');
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  // Put the saved record in the list (new or updated) without reloading everything
  const handleSaved = (message, saved) => {
    setCustomers((prev) => [...prev.filter((c) => c.customer.customerId !== saved.customer.customerId), saved].sort(byId));
    closeDialog();
    onToast(message);
  };

  const handleDelete = async ({ customerId, name }) => {
    if (!window.confirm(`Delete ${name} (${customerId})? Their payments and notifications are removed too. This can't be undone.`)) return;
    setDeletingId(customerId);
    try {
      await deleteCustomer(customerId);
      setCustomers((prev) => prev.filter((c) => c.customer.customerId !== customerId));
      onToast(`${name} deleted.`);
    } catch (err) {
      // Already gone on the server: drop it from the list as well
      if (err.status === 404) setCustomers((prev) => prev.filter((c) => c.customer.customerId !== customerId));
      window.alert(err.status === 404 ? `${name} was already deleted.` : err.message);
    } finally {
      setDeletingId(null);
    }
  };

  const handleClear = async () => {
    if (!window.confirm('Remove all customers? Their payments and notifications are removed too. This can\'t be undone.')) return;
    setClearing(true);
    try {
      await clearCustomers();
      setCustomers([]);
      onToast('All customers removed.');
    } catch (err) {
      window.alert(err.message);
    } finally {
      setClearing(false);
    }
  };

  const n = customers.length;
  const summary = status === 'loading'
    ? 'Loading customers…'
    : `${n} business ${n === 1 ? 'customer' : 'customers'}. The agents match them against open tenders and check them for loan offers.`;

  return (
    <section>
      <PageHeader
        title="Customers"
        summary={summary}
        actions={
          <>
            <button
              type="button"
              onClick={handleClear}
              disabled={status !== 'ready' || n === 0 || clearing}
              className={`${BTN_SECONDARY} cursor-pointer disabled:cursor-not-allowed disabled:opacity-50`}
            >
              <XIcon /> Clear
            </button>
            <button type="button" onClick={() => openDialog()} disabled={status !== 'ready'} className={`${BTN_PRIMARY} disabled:opacity-50`}>
              <PlusIcon /> Add customer
            </button>
          </>
        }
      />

      <div className={CARD}>
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="bg-subtle text-left text-xs text-muted">
              <tr className="border-b border-line">
                {HEADERS.map(([label, cls]) => <th key={label} className={`${cls} py-3 font-medium`}>{label}</th>)}
                <th className="px-3.5 py-3"><span className="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {status !== 'ready' && (
                <tr>
                  <td colSpan={HEADERS.length + 1} className="px-4 py-10 text-center text-sm text-muted">
                    {status === 'loading' ? 'Loading customers…' : (
                      <>
                        <span className="text-red-700">{loadError}</span>{' '}
                        <button type="button" onClick={load} className="font-medium text-brand-700 hover:underline">Try again</button>
                      </>
                    )}
                  </td>
                </tr>
              )}
              {status === 'ready' && n === 0 && (
                <tr>
                  <td colSpan={HEADERS.length + 1} className="px-4 py-10 text-center text-sm text-muted">
                    No customers yet. Add one to get started.
                  </td>
                </tr>
              )}
              {status === 'ready' && customers.map((record) => {
                const { customer: c, riskFeatures: r } = record;
                return (
                  <tr key={c.customerId} className={ROW}>
                    <td className="px-3.5 py-4">
                      <div className="flex items-center gap-3">
                        <span className="grid size-9 shrink-0 place-items-center rounded-full bg-brand-50 text-xs font-semibold text-brand-700">{initials(c.name)}</span>
                        <div className="min-w-0">
                          <div className="font-medium">{c.name}</div>
                          <div className="text-xs whitespace-nowrap text-muted">{c.customerId}, TIN {c.taxCode}</div>
                        </div>
                      </div>
                    </td>
                    <td className="px-3.5 py-4">
                      <div>{c.industry.name}</div>
                      <div className="text-xs text-muted">OKED {c.industry.industryCode}</div>
                    </td>
                    <td className="px-3.5 py-4">
                      <div>{c.address.regionName}</div>
                      <div className="text-xs text-muted">{c.address.districtName}</div>
                    </td>
                    <td className="px-3.5 py-4"><Chip>{LANGUAGE_NAMES[c.languageCode] || c.languageCode}</Chip></td>
                    <td className="px-3.5 py-4 text-right whitespace-nowrap tabular-nums">{money(r.avgMonthlyInflow6m)}</td>
                    <td className="px-3.5 py-4"><OverduePill days={r.maxOverdueDays12m} /></td>
                    <td className="px-3.5 py-4 text-right whitespace-nowrap tabular-nums">
                      {r.preApprovedLimit ? money(r.preApprovedLimit) : <span className="text-muted">None</span>}
                    </td>
                    <td className="px-3 py-4">
                      <div className="flex items-center justify-end gap-1">
                        <button
                          type="button"
                          onClick={() => openDialog(record)}
                          aria-label={`Details for ${c.name}`}
                          className="rounded-lg px-2.5 py-1.5 text-xs font-medium text-brand-700 ring-1 ring-line ring-inset transition-colors hover:bg-brand-50 hover:ring-brand-300"
                        >
                          Details
                        </button>
                        <button
                          type="button"
                          onClick={() => handleDelete(c)}
                          disabled={deletingId === c.customerId}
                          title="Delete"
                          aria-label={`Delete ${c.name}`}
                          className="grid size-8 place-items-center rounded-lg text-muted transition-colors hover:bg-red-50 hover:text-red-600 disabled:cursor-wait disabled:opacity-50"
                        >
                          <TrashIcon />
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </div>

      <Modal open={dialog.open} onClose={closeDialog} labelledBy="dialog-title" dismissOnBackdrop={false}>
        <CustomerForm
          key={dialog.formKey}
          record={dialog.record}
          defaultCustomerId={nextCustomerId(customers)}
          onCancel={closeDialog}
          onSaved={handleSaved}
        />
      </Modal>
    </section>
  );
}
