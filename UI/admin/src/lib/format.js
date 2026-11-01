// 120000000 -> "120 000 000"
export const money = (n) => String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ' ');

// 4335000000 -> "4.3 billion"
export const shortMoney = (n) =>
  n >= 1e12 ? `${(n / 1e12).toFixed(1)} trillion`
    : n >= 1e9 ? `${(n / 1e9).toFixed(1)} billion` : n >= 1e6 ? `${Math.round(n / 1e6)} million` : money(n);

export const formatDate = (iso) =>
  new Date(iso + 'T00:00:00').toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });

export const daysUntil = (iso) => {
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  return Math.round((new Date(iso + 'T00:00:00') - today) / 86400000);
};

export const initials = (name) => name.replace(/\b(LLC|JSC)\b/g, '').trim().split(/\s+/)
  .slice(0, 2).map((w) => w[0]).join('').toUpperCase();

export const formatRunTime = (d) =>
  `${d.toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })} at ` +
  `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;

// Full months between a date (YYYY-MM-DD) and today; null when there is no date
export function monthsSince(iso) {
  if (!iso) return null;
  const from = new Date(iso + 'T00:00:00');
  const now = new Date();
  let months = (now.getFullYear() - from.getFullYear()) * 12 + (now.getMonth() - from.getMonth());
  if (now.getDate() < from.getDate()) months -= 1;
  return Math.max(0, months);
}

export const today = () => new Date().toISOString().slice(0, 10);
