// 120000000 -> "120 000 000"
export const money = (n) => String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ' ');

export const formatDate = (iso) =>
  new Date(iso + 'T00:00:00').toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });

// ISO timestamp -> "25 min ago", "2 hours ago", "3 days ago"
export const timeAgo = (iso) => {
  const minutes = Math.max(0, Math.floor((Date.now() - Date.parse(iso)) / 60000));
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} ${hours === 1 ? 'hour' : 'hours'} ago`;
  const days = Math.round(hours / 24);
  return `${days} ${days === 1 ? 'day' : 'days'} ago`;
};

export const BTN_PRIMARY =
  'inline-flex items-center justify-center gap-2 rounded-lg bg-brand-600 px-4 py-2.5 text-sm font-medium text-white shadow-sm ' +
  'transition-colors hover:bg-brand-700 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600';

export const INPUT =
  'h-10 w-full rounded-lg border border-line bg-surface px-3 text-sm text-ink outline-none transition ' +
  'focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15';
