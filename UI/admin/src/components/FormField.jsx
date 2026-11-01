import { CONTROL, CONTROL_EDITABLE, CONTROL_READONLY, NO_SPIN } from '../lib/styles.js';

// One form control driven by a field definition from data/customerForm.js
export default function FormField({ field: f, value, onChange, readOnly = false }) {
  const id = `f-${f.name}`;
  const span = f.full ? 'sm:col-span-2' : '';

  if (f.type === 'checkbox') {
    const tone = f.tone === 'warn'
      ? 'has-checked:border-amber-400 has-checked:bg-amber-50'
      : 'has-checked:border-brand-500 has-checked:bg-brand-50';
    const accent = f.tone === 'warn' ? 'accent-amber-600' : 'accent-brand-600';
    return (
      <label className={`${span} flex cursor-pointer items-start gap-3 rounded-xl border border-line bg-subtle p-4 transition-colors ${tone}`}>
        <input
          id={id}
          name={f.name}
          type="checkbox"
          checked={Boolean(value)}
          onChange={(e) => onChange(e.target.checked)}
          className={`mt-0.5 size-4 shrink-0 ${accent}`}
        />
        <span>
          <span className="block text-sm font-medium">{f.label}</span>
          {f.description && <span className="mt-0.5 block text-xs text-muted">{f.description}</span>}
        </span>
      </label>
    );
  }

  const colours = readOnly ? CONTROL_READONLY : CONTROL_EDITABLE;
  let control;

  if (f.type === 'select') {
    control = (
      <select
        id={id}
        name={f.name}
        required={f.required}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className={`${CONTROL} ${colours} h-10 pr-8`}
      >
        {f.options.map((o) => {
          const [optValue, text] = Array.isArray(o) ? o : [o, o];
          return <option key={optValue} value={optValue}>{text}</option>;
        })}
      </select>
    );
  } else {
    const type = f.type || 'text';
    const input = (
      <input
        id={id}
        name={f.name}
        type={type}
        required={f.required}
        pattern={f.pattern}
        inputMode={f.inputMode}
        placeholder={f.placeholder}
        min={type === 'number' ? '0' : undefined}
        step={type === 'number' ? '1' : undefined}
        readOnly={readOnly}
        tabIndex={f.readonly ? -1 : undefined}
        data-autofocus={f.autofocus || undefined}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className={`${CONTROL} ${colours} h-10 ${type === 'number' ? NO_SPIN : ''} ${f.suffix ? 'pr-16' : ''}`}
      />
    );
    control = f.suffix ? (
      <div className="relative">
        {input}
        <span className="pointer-events-none absolute inset-y-0 right-3 flex items-center text-xs text-muted">{f.suffix}</span>
      </div>
    ) : input;
  }

  return (
    <div className={span}>
      <label htmlFor={id} className="mb-1.5 block text-sm font-medium">
        {f.label}{f.required && <> <span className="text-red-600">*</span></>}
      </label>
      {control}
      {f.hint && <p className="mt-1.5 text-xs text-muted">{f.hint}</p>}
    </div>
  );
}
