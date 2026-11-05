import { useLayoutEffect, useRef, useState } from 'react';
import { addCustomer, updateCustomer } from '../lib/api.js';
import { BTN_PRIMARY, BTN_SECONDARY, CARD } from '../lib/styles.js';

const CONTROL = 'mt-1 block w-full rounded-lg border border-line bg-white px-3 py-2 text-sm outline-none focus:border-brand-500 focus:ring-2 focus:ring-brand-500/20';
const MAX_AMOUNT = Number.MAX_SAFE_INTEGER;

function Field({ label, name, children, ...props }) {
  return <label className="block text-sm font-medium">{label}
    {children || <input name={name} className={CONTROL} {...props} />}
  </label>;
}

export default function CustomerForm({ defaultCustomerId, customerRecord, onCancel, onSaved }) {
  const customer = customerRecord?.customer;
  const risk = customerRecord?.riskFeatures;
  const [payments, setPayments] = useState(() => (customerRecord?.recentPayments ?? []).map((payment, id) => ({ id, payment })));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const nextPayment = useRef(payments.length);
  const dialog = useRef(null);
  const editing = Boolean(customerRecord);

  useLayoutEffect(() => {
    const element = dialog.current;
    const trigger = document.activeElement;
    element.showModal();
    return () => {
      element.close();
      trigger?.focus();
    };
  }, []);

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
      recentPayments: payments.map(({ id }) => ({
        date: text(`date-${id}`), isIncoming: text(`direction-${id}`) === 'in',
        purposeDescription: text(`purpose-${id}`), amount: number(`amount-${id}`)
      })),
      onRegister: form.has('onRegister')
    };
    setSaving(true); setError('');
    try { onSaved(editing ? await updateCustomer(customer.customerId, payload) : await addCustomer(payload)); }
    catch (err) { setError(err.message); setSaving(false); }
  }

  return <dialog ref={dialog} aria-labelledby="customer-form-title" onCancel={event => {
    event.preventDefault();
    if (!saving) onCancel();
  }} className="m-auto max-h-[90vh] w-[calc(100%-2rem)] max-w-3xl overflow-y-auto rounded-2xl bg-transparent p-0 backdrop:bg-black/50">
  <form id="add-customer-form" onSubmit={save} className={`${CARD} p-5 sm:p-6`}>
    <h2 id="customer-form-title" className="text-lg font-semibold">{editing ? 'Edit customer' : 'Add customer'}</h2>
    <p className="mt-1 text-sm text-muted">Enter a fictional business and its risk and payment information.</p>
    <fieldset disabled={saving} className="mt-5 space-y-6 disabled:opacity-60">
      <div>
        <h3 className="font-semibold">Business profile</h3>
        <div className="mt-3 grid gap-4 sm:grid-cols-2">
          <Field label="Customer ID *" name="customerId" required maxLength={20} defaultValue={customer?.customerId ?? defaultCustomerId} readOnly={editing} />
          <Field label="Company name *" name="name" required maxLength={200} defaultValue={customer?.name} />
          <Field label="TIN (9 digits) *" name="taxCode" required pattern="[0-9]{9}" inputMode="numeric" defaultValue={customer?.taxCode} />
          <Field label="Legal form"><select name="type" className={CONTROL} defaultValue={customer?.type ?? 'LLC'}>
            {customer?.type && !['LLC', 'Sole proprietor', 'JSC', 'Private enterprise'].includes(customer.type) && <option>{customer.type}</option>}
            <option>LLC</option><option>Sole proprietor</option><option>JSC</option><option>Private enterprise</option>
          </select></Field>
          <Field label="OKED code *" name="industryCode" required maxLength={10} defaultValue={customer?.industry.industryCode} />
          <Field label="OKED activity *" name="industryName" required maxLength={200} defaultValue={customer?.industry.name} />
          <Field label="Region *" name="regionName" required maxLength={100} defaultValue={customer?.address.regionName ?? 'Tashkent'} />
          <Field label="District" name="districtName" maxLength={100} defaultValue={customer?.address.districtName ?? ''} />
          <Field label="Preferred language"><select name="languageCode" className={CONTROL} defaultValue={customer?.languageCode ?? 'en'}>
            <option value="en">English</option><option value="uz">Uzbek</option><option value="ru">Russian</option>
          </select></Field>
          <Field label="Registration date" name="registrationDate" type="date" defaultValue={customer?.registrationDate ?? ''} />
          <Field label="Customer since *" name="customerSince" type="date" required defaultValue={customer?.customerSince ?? ''} />
        </div>
      </div>
      <div>
        <h3 className="font-semibold">Loan and risk information</h3>
        <div className="mt-3 grid gap-4 sm:grid-cols-2">
          <Field label="Average monthly inflow (UZS) *" name="avgMonthlyInflow6m" type="number" min="0" max={MAX_AMOUNT} step="1" defaultValue={risk?.avgMonthlyInflow6m ?? 0} required />
          <Field label="Outstanding loans (UZS)" name="outstandingAmount" type="number" min="0" max={MAX_AMOUNT} step="1" defaultValue={risk?.outstandingAmount ?? 0} />
          <Field label="Overdue amount (UZS)" name="overdueAmount" type="number" min="0" max={MAX_AMOUNT} step="1" defaultValue={risk?.overdueAmount ?? 0} />
          <Field label="Maximum overdue days in 12 months" name="maxOverdueDays12m" type="number" min="0" max="2147483647" step="1" defaultValue={risk?.maxOverdueDays12m ?? 0} />
          <Field label="Pre-approved limit (UZS)" name="preApprovedLimit" type="number" min="0" max={MAX_AMOUNT} step="1" defaultValue={risk?.preApprovedLimit ?? ''} />
          <div className="flex flex-col gap-3 pt-2 text-sm">
            <label><input type="checkbox" name="kycPassed" defaultChecked={risk?.kycPassed ?? true} className="mr-2" />KYC passed</label>
            <label><input type="checkbox" name="onRegister" defaultChecked={customerRecord?.onRegister ?? false} className="mr-2" />On unreliable suppliers register</label>
          </div>
        </div>
      </div>
      <div>
        <h3 className="font-semibold">Recent payments</h3>
        <p className="mt-1 text-xs text-muted">Incoming payments from the past 12 months help the matcher find relevant tenders.</p>
        <div className="mt-3 space-y-3">{payments.map(({ id, payment }) => <div key={id} className="rounded-xl border border-line bg-subtle p-4">
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Payment purpose *" name={`purpose-${id}`} required maxLength={500} placeholder="Payment for bread delivery" defaultValue={payment?.purposeDescription ?? ''} />
            <Field label="Payment date *" name={`date-${id}`} type="date" required defaultValue={payment?.date ?? ''} />
            <Field label="Direction"><select name={`direction-${id}`} className={CONTROL} defaultValue={payment?.isIncoming === false ? 'out' : 'in'}><option value="in">Incoming</option><option value="out">Outgoing</option></select></Field>
            <Field label="Amount (UZS)" name={`amount-${id}`} type="number" min="0" max={MAX_AMOUNT} step="1" defaultValue={payment?.amount ?? ''} />
          </div>
          <button type="button" onClick={() => setPayments(previous => previous.filter(item => item.id !== id))} className="mt-3 text-sm text-red-700 underline">Remove payment</button>
        </div>)}</div>
        <button type="button" onClick={() => {
          const id = nextPayment.current++;
          setPayments(previous => [...previous, { id }]);
        }} className={`${BTN_SECONDARY} mt-3`}>Add payment</button>
      </div>
    </fieldset>
    {error && <p role="alert" className="mt-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
    <div className="mt-5 flex flex-wrap gap-3">
      <button type="submit" disabled={saving} className={`${BTN_PRIMARY} disabled:opacity-50`}>{saving ? 'Saving…' : 'Save customer'}</button>
      <button type="button" disabled={saving} onClick={onCancel} className={`${BTN_SECONDARY} disabled:opacity-50`}>Cancel</button>
    </div>
  </form>
  </dialog>;
}
