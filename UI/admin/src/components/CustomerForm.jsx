import { useRef, useState } from 'react';
import { addCustomer } from '../lib/api.js';
import { BTN_PRIMARY, BTN_SECONDARY, CARD } from '../lib/styles.js';

const CONTROL = 'mt-1 block w-full rounded-lg border border-line bg-white px-3 py-2 text-sm outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20';
const MAX_AMOUNT = Number.MAX_SAFE_INTEGER;

function Field({ label, name, children, ...props }) {
  return <label className="block text-sm font-medium">{label}
    {children || <input name={name} className={CONTROL} {...props} />}
  </label>;
}

export default function CustomerForm({ defaultCustomerId, onCancel, onSaved }) {
  const [payments, setPayments] = useState([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const nextPayment = useRef(0);

  async function save(event) {
    event.preventDefault();
    if (saving) return;
    const form = new FormData(event.currentTarget);
    const text = name => String(form.get(name) ?? '').trim();
    const number = name => text(name) === '' ? null : Number(text(name));
    const customerId = text('customerId');
    const payload = {
      customer: {
        customerId, name: text('name'), taxCode: text('taxCode'), type: text('type'),
        industry: { industryCode: text('industryCode'), name: text('industryName') },
        address: { regionName: text('regionName'), districtName: text('districtName') },
        languageCode: text('languageCode'), registrationDate: text('registrationDate') || null,
        customerSince: text('customerSince')
      },
      riskFeatures: {
        customerId, avgMonthlyInflow6m: number('avgMonthlyInflow6m'),
        outstandingAmount: number('outstandingAmount') ?? 0, overdueAmount: number('overdueAmount') ?? 0,
        maxOverdueDays12m: number('maxOverdueDays12m') ?? 0, preApprovedLimit: number('preApprovedLimit'),
        kycPassed: form.has('kycPassed')
      },
      recentPayments: payments.map(id => ({
        date: text(`date-${id}`), isIncoming: text(`direction-${id}`) === 'in',
        purposeDescription: text(`purpose-${id}`), amount: number(`amount-${id}`)
      })),
      onRegister: form.has('onRegister')
    };
    setSaving(true); setError('');
    try { onSaved(await addCustomer(payload)); }
    catch (err) { setError(err.message); setSaving(false); }
  }

  return <form id="add-customer-form" onSubmit={save} className={`${CARD} mb-6 p-5 sm:p-6`}>
    <h2 className="text-lg font-semibold">Add customer</h2>
    <p className="mt-1 text-sm text-muted">Enter a fictional business and its risk and payment information.</p>
    <fieldset disabled={saving} className="mt-5 space-y-6 disabled:opacity-60">
      <div>
        <h3 className="font-semibold">Business profile</h3>
        <div className="mt-3 grid gap-4 sm:grid-cols-2">
          <Field label="Customer ID *" name="customerId" required maxLength={20} defaultValue={defaultCustomerId} />
          <Field label="Company name *" name="name" required maxLength={200} />
          <Field label="TIN (9 digits) *" name="taxCode" required pattern="[0-9]{9}" inputMode="numeric" />
          <Field label="Legal form"><select name="type" className={CONTROL} defaultValue="LLC">
            <option>LLC</option><option>Sole proprietor</option><option>JSC</option><option>Private enterprise</option>
          </select></Field>
          <Field label="OKED code *" name="industryCode" required maxLength={10} />
          <Field label="OKED activity *" name="industryName" required maxLength={200} />
          <Field label="Region *" name="regionName" required maxLength={100} defaultValue="Tashkent" />
          <Field label="District" name="districtName" maxLength={100} />
          <Field label="Preferred language"><select name="languageCode" className={CONTROL} defaultValue="en">
            <option value="en">English</option><option value="uz">Uzbek</option><option value="ru">Russian</option>
          </select></Field>
          <Field label="Registration date" name="registrationDate" type="date" />
          <Field label="Customer since *" name="customerSince" type="date" required />
        </div>
      </div>
      <div>
        <h3 className="font-semibold">Loan and risk information</h3>
        <div className="mt-3 grid gap-4 sm:grid-cols-2">
          <Field label="Average monthly inflow (UZS) *" name="avgMonthlyInflow6m" type="number" min="0" max={MAX_AMOUNT} step="1" defaultValue="0" required />
          <Field label="Outstanding loans (UZS)" name="outstandingAmount" type="number" min="0" max={MAX_AMOUNT} step="1" defaultValue="0" />
          <Field label="Overdue amount (UZS)" name="overdueAmount" type="number" min="0" max={MAX_AMOUNT} step="1" defaultValue="0" />
          <Field label="Maximum overdue days in 12 months" name="maxOverdueDays12m" type="number" min="0" max="2147483647" step="1" defaultValue="0" />
          <Field label="Pre-approved limit (UZS)" name="preApprovedLimit" type="number" min="0" max={MAX_AMOUNT} step="1" />
          <div className="flex flex-col gap-3 pt-2 text-sm">
            <label><input type="checkbox" name="kycPassed" defaultChecked className="mr-2" />KYC passed</label>
            <label><input type="checkbox" name="onRegister" className="mr-2" />On unreliable suppliers register</label>
          </div>
        </div>
      </div>
      <div>
        <h3 className="font-semibold">Recent payments</h3>
        <p className="mt-1 text-xs text-muted">Incoming payments from the past 12 months help the matcher find relevant tenders.</p>
        <div className="mt-3 space-y-3">{payments.map(id => <div key={id} className="rounded-xl border border-line bg-subtle p-4">
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Payment purpose *" name={`purpose-${id}`} required maxLength={500} placeholder="Payment for bread delivery" />
            <Field label="Payment date *" name={`date-${id}`} type="date" required />
            <Field label="Direction"><select name={`direction-${id}`} className={CONTROL}><option value="in">Incoming</option><option value="out">Outgoing</option></select></Field>
            <Field label="Amount (UZS)" name={`amount-${id}`} type="number" min="0" max={MAX_AMOUNT} step="1" />
          </div>
          <button type="button" onClick={() => setPayments(previous => previous.filter(key => key !== id))} className="mt-3 text-sm text-red-700 underline">Remove payment</button>
        </div>)}</div>
        <button type="button" onClick={() => setPayments(previous => [...previous, nextPayment.current++])} className={`${BTN_SECONDARY} mt-3`}>Add payment</button>
      </div>
    </fieldset>
    {error && <p role="alert" className="mt-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
    <div className="mt-5 flex flex-wrap gap-3">
      <button type="submit" disabled={saving} className={`${BTN_PRIMARY} disabled:opacity-50`}>{saving ? 'Saving…' : 'Save customer'}</button>
      <button type="button" disabled={saving} onClick={onCancel} className={`${BTN_SECONDARY} disabled:opacity-50`}>Cancel</button>
    </div>
  </form>;
}
