import { formatDate } from '../lib/format.js';

// Dates from UZEX are Tashkent local time without an offset: "2026-10-19T10:14:20"
const withTime = (date) => (iso) => `${date(iso.slice(0, 10))}, ${iso.slice(11, 16)}`;

const enDate = (iso) => formatDate(iso);

export const TENDER_TEXT = {
  headers: ['Lot', 'Tender', 'Start price', 'Deposit', 'Payment terms', 'Deadline', 'Source', 'Details'],
  days: (n) => `${n} ${n === 1 ? 'day' : 'days'}`,
  inDays: (n) => `In ${n} ${n === 1 ? 'day' : 'days'}`,
  advance: (p) => `${p}% advance`,
  today: 'Closes today',
  closed: 'Closed',
  openSource: 'Open the lot on UZEX',
  details: 'Details',
  date: enDate,
  dateTime: withTime(enDate),
  detail: {
    lot: 'Lot',
    startPrice: 'Start price',
    deposit: 'Bid deposit',
    payment: 'Payment terms',
    summary: 'Plain-English summary',
    deadline: 'Bid deadline',
    close: 'Close'
  }
};
