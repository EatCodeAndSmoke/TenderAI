// Shared Tailwind class strings

export const BTN_PRIMARY =
  'inline-flex items-center gap-2 rounded-lg bg-brand-600 px-4 py-2.5 text-sm font-medium text-white shadow-sm transition-colors ' +
  'hover:bg-brand-700 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600';

export const BTN_SECONDARY =
  'inline-flex items-center gap-2 rounded-lg border border-line bg-surface px-4 py-2.5 text-sm font-medium text-ink shadow-sm transition-colors ' +
  'hover:bg-subtle focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600';

export const BTN_GHOST = 'rounded-lg px-4 py-2.5 text-sm font-medium text-ink transition-colors hover:bg-line/60';

export const BTN_ROW =
  'rounded-lg px-2.5 py-1.5 text-xs font-medium text-brand-700 ring-1 ring-line ring-inset transition-colors hover:bg-brand-50 hover:ring-brand-300';

export const CARD = 'overflow-hidden rounded-2xl border border-line bg-surface shadow-sm shadow-cobalt/5';

export const ROW = 'transition-colors hover:bg-subtle/70';

// Form controls. Colours are added separately so read-only fields can swap them.
export const CONTROL =
  'w-full rounded-lg border border-line px-3 text-sm placeholder:text-muted/60 outline-none transition ' +
  'focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15 user-invalid:border-red-500 user-invalid:focus:ring-red-500/15';
export const CONTROL_EDITABLE = 'bg-surface text-ink';
export const CONTROL_READONLY = 'bg-subtle text-muted focus:ring-0 focus:border-line';

export const NO_SPIN =
  '[appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none';
