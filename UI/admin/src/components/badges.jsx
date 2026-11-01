const PILL_TONES = {
  ok: 'bg-emerald-50 text-emerald-700',
  warn: 'bg-amber-50 text-amber-700',
  neutral: 'bg-subtle text-muted'
};

export function Pill({ tone, children }) {
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium whitespace-nowrap ${PILL_TONES[tone]}`}>
      <span className="size-1.5 rounded-full bg-current" />
      {children}
    </span>
  );
}

export function Chip({ children }) {
  return (
    <span className="inline-flex rounded-md bg-subtle px-2 py-1 text-xs font-medium text-ink ring-1 ring-line ring-inset">
      {children}
    </span>
  );
}

export function OverduePill({ days }) {
  if (days === 0) return <Pill tone="ok">None</Pill>;
  return <Pill tone={days > 30 ? 'warn' : 'neutral'}>{days} days</Pill>;
}
