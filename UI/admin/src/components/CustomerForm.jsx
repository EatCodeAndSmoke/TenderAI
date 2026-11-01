import { useRef, useState } from 'react';
import { flushSync } from 'react-dom';
import { DIALOG_MODES, SECTIONS, TABS } from '../data/customerForm.js';
import { addCustomer, updateCustomer } from '../lib/api.js';
import { monthsSince } from '../lib/format.js';
import { BTN_GHOST, BTN_PRIMARY, BTN_SECONDARY } from '../lib/styles.js';
import { ModalHeader } from './Modal.jsx';
import { BuildingIcon } from './icons.jsx';
import FormField from './FormField.jsx';
import PaymentsField, { paymentPayload, paymentRow } from './PaymentsField.jsx';

const ALL_FIELDS = Object.values(SECTIONS).flat().flatMap((s) => s.fields);

// First field that fails validation, optionally only inside one tab panel
const firstInvalid = (form, panel) =>
  [...form.elements].find((el) => el.willValidate && !el.checkValidity() && (!panel || el.closest(`[data-panel="${panel}"]`)));

// Starting values: the field defaults for "Add customer", or an existing record for "Details"
function initialValues(record, defaultCustomerId) {
  if (!record) {
    const values = {};
    ALL_FIELDS.forEach((f) => {
      if (f.type === 'payments' || f.readonly) return;
      if (f.type === 'checkbox') values[f.name] = Boolean(f.checked);
      else if (f.type === 'select') values[f.name] = [].concat(f.options[0])[0];
      else values[f.name] = f.value ?? '';
    });
    return { ...values, customerId: defaultCustomerId };
  }

  const { customer: c, riskFeatures: r } = record;
  const values = {
    customerId: c.customerId,
    name: c.name,
    taxCode: c.taxCode,
    type: c.type,
    registrationDate: c.registrationDate,
    customerSince: c.customerSince,
    industryCode: c.industry.industryCode,
    industryName: c.industry.name,
    regionName: c.address.regionName,
    districtName: c.address.districtName,
    languageCode: c.languageCode,
    onRegister: Boolean(record.onRegister),
    avgMonthlyInflow6m: r.avgMonthlyInflow6m,
    outstandingAmount: r.outstandingAmount,
    overdueAmount: r.overdueAmount,
    maxOverdueDays12m: r.maxOverdueDays12m,
    preApprovedLimit: r.preApprovedLimit,
    kycPassed: Boolean(r.kycPassed)
  };
  return Object.fromEntries(Object.entries(values).map(([k, v]) => [k, v ?? '']));
}

// Builds the request body for the API (same shape as GET /api/customers returns)
function buildPayload(values, payments) {
  const v = (name) => String(values[name] ?? '').trim();
  const num = (name) => (v(name) === '' ? null : Number(v(name)));
  const customerId = v('customerId');

  return {
    customer: {
      customerId,
      name: v('name'),
      taxCode: v('taxCode'),
      type: v('type'),
      industry: { industryCode: v('industryCode'), name: v('industryName') },
      address: { regionName: v('regionName'), districtName: v('districtName') },
      languageCode: v('languageCode'),
      registrationDate: v('registrationDate') || null,
      customerSince: v('customerSince') || null
    },
    recentPayments: payments.map(paymentPayload),
    riskFeatures: {
      customerId,
      avgMonthlyInflow6m: num('avgMonthlyInflow6m'),
      monthsWithBank: monthsSince(v('customerSince')),
      outstandingAmount: num('outstandingAmount') ?? 0,
      overdueAmount: num('overdueAmount') ?? 0,
      maxOverdueDays12m: num('maxOverdueDays12m') ?? 0,
      preApprovedLimit: num('preApprovedLimit'),
      kycPassed: Boolean(values.kycPassed)
    },
    onRegister: Boolean(values.onRegister)
  };
}

// "Add customer" and "Details" both use this form, as two steps: Profile, then Loan and risk.
// Next checks the profile fields before moving on; only the last step saves.
// Saving sends the customer to the API; onSaved gets the toast text and the saved record.
export default function CustomerForm({ record, defaultCustomerId, onCancel, onSaved }) {
  const mode = DIALOG_MODES[record ? 'details' : 'add'];
  const formRef = useRef(null);
  const [values, setValues] = useState(() => initialValues(record, defaultCustomerId));
  const [payments, setPayments] = useState(() => (record?.recentPayments || []).map(paymentRow));
  const [tab, setTab] = useState('profile');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const setValue = (name, value) => setValues((prev) => ({ ...prev, [name]: value }));

  // "Months with the bank" always follows "Customer since"
  const monthsWithBank = monthsSince(values.customerSince) ?? '';

  const goToLoan = () => {
    const invalid = firstInvalid(formRef.current, 'profile');
    if (invalid) {
      invalid.reportValidity();
      return;
    }
    flushSync(() => setTab('loan'));
    formRef.current.querySelector('[data-panel="loan"] input:not([readonly])')?.focus();
  };

  const selectTab = (name) => (name === 'loan' ? goToLoan() : setTab(name));

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (saving) return;

    // Enter on the Profile step acts like Next
    if (tab === 'profile') {
      goToLoan();
      return;
    }

    // A required field may sit on the hidden tab: switch to it before showing the error
    const invalid = firstInvalid(e.currentTarget);
    if (invalid) {
      const panel = invalid.closest('[data-panel]')?.dataset.panel;
      if (panel) flushSync(() => setTab(panel));
      invalid.reportValidity();
      return;
    }

    const payload = buildPayload(values, payments);
    setSaving(true);
    setError('');
    try {
      const saved = await (record ? updateCustomer(payload) : addCustomer(payload));
      onSaved(mode.toast, saved);
    } catch (err) {
      setError(err.message);
      setSaving(false);
    }
  };

  const renderField = (f) => {
    if (f.type === 'payments') {
      return <PaymentsField key={f.name} field={f} payments={payments} onChange={setPayments} />;
    }
    if (f.name === 'monthsWithBank') {
      return <FormField key={f.name} field={f} value={monthsWithBank} onChange={() => {}} readOnly />;
    }
    // The ID is the key for an existing customer, so it can't change in details mode
    const readOnly = f.name === 'customerId' && Boolean(record);
    return <FormField key={f.name} field={f} value={values[f.name]} onChange={(v) => setValue(f.name, v)} readOnly={readOnly} />;
  };

  return (
    <form ref={formRef} noValidate onSubmit={handleSubmit} className="flex max-h-[88vh] flex-col">
      <ModalHeader
        id="dialog-title"
        icon={<BuildingIcon className="size-5" />}
        title={mode.title}
        subtitle={mode.subtitle}
        onClose={onCancel}
      />

      <div className="px-6 pt-5">
        <div role="tablist" aria-label="Customer details" className="grid grid-cols-2 gap-1 rounded-xl bg-subtle p-1 text-sm">
          {TABS.map(([name, label]) => (
            <button
              key={name}
              type="button"
              role="tab"
              aria-selected={tab === name}
              aria-controls={`panel-${name}`}
              onClick={() => selectTab(name)}
              className="rounded-lg px-3 py-2 font-medium text-muted transition-colors hover:text-ink aria-selected:bg-surface aria-selected:text-ink aria-selected:shadow-sm"
            >
              {label}
            </button>
          ))}
        </div>
      </div>

      <div className="overflow-y-auto px-6 py-6">
        {TABS.map(([name]) => (
          <div key={name} role="tabpanel" id={`panel-${name}`} data-panel={name} hidden={tab !== name} className="space-y-7">
            {SECTIONS[name].map((s) => (
              <div key={s.title}>
                <h3 className="text-sm font-semibold">{s.title}</h3>
                {s.description && <p className="mt-0.5 text-xs text-muted">{s.description}</p>}
                <div className="mt-3 grid grid-cols-1 gap-4 sm:grid-cols-2">{s.fields.map(renderField)}</div>
              </div>
            ))}
          </div>
        ))}
      </div>

      {error && (
        <p role="alert" className="border-t border-red-100 bg-red-50 px-6 py-3 text-sm text-red-700">{error}</p>
      )}

      <div className="flex items-center justify-between gap-3 border-t border-line bg-subtle px-6 py-4">
        <p className="text-xs text-muted">
          Step {tab === 'profile' ? 1 : 2} of 2 · <span className="text-red-600">*</span> Required
        </p>
        {/* Distinct keys: React must not turn the clicked Next button into the submit button */}
        {tab === 'profile' ? (
          <div className="flex gap-2">
            <button key="cancel" type="button" onClick={onCancel} className={BTN_GHOST}>Cancel</button>
            <button key="next" type="button" onClick={goToLoan} className={BTN_PRIMARY}>Next</button>
          </div>
        ) : (
          <div className="flex gap-2">
            <button key="previous" type="button" onClick={() => setTab('profile')} disabled={saving} className={BTN_SECONDARY}>
              Previous
            </button>
            <button key="submit" type="submit" disabled={saving} className={`${BTN_PRIMARY} disabled:cursor-wait disabled:opacity-70`}>
              {saving ? mode.busy : mode.submit}
            </button>
          </div>
        )}
      </div>
    </form>
  );
}
