import { useRef } from 'react';
import { monthsSince, today } from '../lib/format.js';
import { CONTROL, CONTROL_EDITABLE, NO_SPIN } from '../lib/styles.js';
import { PlusIcon, TrashIcon } from './icons.jsx';

let lastKey = 0;

// One editable payment row in form state
export const paymentRow = (p = {}) => ({
  key: ++lastKey,
  purpose: p.purposeDescription || '',
  date: p.date || today(),
  direction: p.isIncoming === false ? 'out' : 'in',
  amount: p.amount ?? ''
});

// Same shape as TransactionHistoryItem
export const paymentPayload = (row) => ({
  date: row.date.trim(),
  isIncoming: row.direction === 'in',
  purposeDescription: row.purpose.trim(),
  amount: String(row.amount).trim() === '' ? null : Number(row.amount)
});

const control = `${CONTROL} ${CONTROL_EDITABLE} h-9`;

// Recent payments: add, remove, and flag ones older than 12 months as the date changes
export default function PaymentsField({ field, payments, onChange }) {
  const addedKey = useRef(null);

  const add = () => {
    const row = paymentRow();
    addedKey.current = row.key;
    onChange([...payments, row]);
  };
  const remove = (key) => onChange(payments.filter((p) => p.key !== key));
  const update = (key, patch) => onChange(payments.map((p) => (p.key === key ? { ...p, ...patch } : p)));

  return (
    <div className={field.full ? 'sm:col-span-2' : ''}>
      <div className="mb-1.5 text-sm font-medium">{field.label}</div>
      <p className="mb-3 text-xs text-muted">{field.hint}</p>

      <div className="space-y-2">
        {payments.map((p) => {
          const months = monthsSince(p.date);
          const old = months !== null && months >= 12;
          return (
            <div key={p.key} className={`rounded-xl border border-line bg-subtle/60 p-3 transition-opacity ${old ? 'opacity-60' : ''}`}>
              <div className="flex items-center gap-2">
                <input
                  type="text"
                  required
                  aria-label="Payment purpose"
                  placeholder="Payment for bread delivery, contract No 14"
                  autoFocus={p.key === addedKey.current}
                  value={p.purpose}
                  onChange={(e) => update(p.key, { purpose: e.target.value })}
                  className={`${control} flex-1`}
                />
                <button
                  type="button"
                  onClick={() => remove(p.key)}
                  aria-label="Remove payment"
                  title="Remove"
                  className="grid size-9 shrink-0 place-items-center rounded-lg text-muted transition-colors hover:bg-red-50 hover:text-red-600"
                >
                  <TrashIcon />
                </button>
              </div>
              <div className="mt-2 grid grid-cols-3 gap-2">
                <input
                  type="date"
                  required
                  aria-label="Payment date"
                  value={p.date}
                  onChange={(e) => update(p.key, { date: e.target.value })}
                  className={control}
                />
                <select
                  aria-label="Direction"
                  value={p.direction}
                  onChange={(e) => update(p.key, { direction: e.target.value })}
                  className={`${control} pr-8`}
                >
                  <option value="in">Incoming</option>
                  <option value="out">Outgoing</option>
                </select>
                <div className="relative">
                  <input
                    type="number"
                    min="0"
                    step="1"
                    aria-label="Amount"
                    placeholder="Amount"
                    value={p.amount}
                    onChange={(e) => update(p.key, { amount: e.target.value })}
                    className={`${control} pr-12 ${NO_SPIN}`}
                  />
                  <span className="pointer-events-none absolute inset-y-0 right-3 flex items-center text-xs text-muted">so'm</span>
                </div>
              </div>
              {old && <p className="mt-2 text-xs font-medium text-amber-700">Older than 12 months, so the matcher ignores it.</p>}
            </div>
          );
        })}
      </div>

      {payments.length === 0 && (
        <p className="rounded-xl border border-dashed border-line px-3 py-4 text-center text-sm text-muted">No payments yet.</p>
      )}

      <button
        type="button"
        onClick={add}
        className="mt-2 inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-sm font-medium text-brand-700 transition-colors hover:bg-brand-50"
      >
        <PlusIcon /> Add payment
      </button>
    </div>
  );
}
