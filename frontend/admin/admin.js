// Tender AI Admin (demo)
// Everything here is mock data. "Delete", "Load tenders", both "Clear" buttons and the tender
// source links intentionally do nothing yet. "Start matching" plays back mock results.
// "Save customer" and "Update customer" only log JSON to the console.

/* ------------------------------------------------------------------ */
/* Mock data (same shape as data/customers.json + data/risk-features.json) */
/* ------------------------------------------------------------------ */

const customers = [
  {
    customer: {
      customerId: 'C-01',
      name: 'Non Baraka LLC',
      taxCode: '301234567',
      type: 'LLC',
      industry: { industryCode: '10710', name: 'Manufacture of bread' },
      address: { regionName: 'Tashkent', districtName: 'Yunusobod' },
      languageCode: 'en',
      registrationDate: '2019-03-04',
      customerSince: '2024-08-12'
    },
    // monthsWithBank isn't stored here: it is calculated from customer.customerSince
    riskFeatures: {
      avgMonthlyInflow6m: 120000000,
      outstandingAmount: 85000000,
      overdueAmount: 0,
      maxOverdueDays12m: 0,
      preApprovedLimit: 150000000,
      kycPassed: true
    },
    // Same shape as TransactionHistoryItem. The matcher only uses the last 12 months,
    // so the June 2025 catering payment is ignored as too old.
    recentPayments: [
      { date: '2026-10-08', isIncoming: true, purposeDescription: 'Payment for bread delivery, contract No 14', amount: 18500000 },
      { date: '2026-09-14', isIncoming: true, purposeDescription: 'Kindergarten No 7: payment for bread, September', amount: 6200000 },
      { date: '2026-09-03', isIncoming: false, purposeDescription: 'Purchase of flour, invoice 2231', amount: 22000000 },
      { date: '2025-06-20', isIncoming: true, purposeDescription: 'Payment for wedding catering', amount: 4500000 }
    ],
    onRegister: false
  },
  {
    customer: {
      customerId: 'C-02',
      name: 'Sariq Non LLC',
      taxCode: '302345678',
      type: 'LLC',
      industry: { industryCode: '10710', name: 'Manufacture of bread' },
      address: { regionName: 'Tashkent', districtName: 'Chilonzor' },
      languageCode: 'en',
      registrationDate: '2020-06-15',
      customerSince: '2025-04-02'
    },
    riskFeatures: {
      avgMonthlyInflow6m: 90000000,
      outstandingAmount: 40000000,
      overdueAmount: 0,
      maxOverdueDays12m: 45,
      preApprovedLimit: 150000000,
      kycPassed: true
    },
    recentPayments: [
      { date: '2026-10-02', isIncoming: true, purposeDescription: 'Payment for bread and buns, cafe contract No 3', amount: 9800000 },
      { date: '2026-08-21', isIncoming: false, purposeDescription: 'Purchase of flour and yeast, invoice 884', amount: 12400000 }
    ],
    onRegister: false
  },
  {
    customer: {
      customerId: 'C-06',
      name: 'Oziq-Ovqat Savdo LLC',
      taxCode: '306789012',
      type: 'LLC',
      industry: { industryCode: '46390', name: 'Wholesale of food' },
      address: { regionName: 'Tashkent', districtName: 'Mirzo Ulugbek' },
      languageCode: 'en',
      registrationDate: '2015-05-20',
      customerSince: '2021-10-05'
    },
    riskFeatures: {
      avgMonthlyInflow6m: 900000000,
      outstandingAmount: 300000000,
      overdueAmount: 0,
      maxOverdueDays12m: 0,
      preApprovedLimit: 1500000000,
      kycPassed: true
    },
    recentPayments: [
      { date: '2026-10-05', isIncoming: true, purposeDescription: 'Supply of food products to city hospital No 2, contract 118', amount: 310000000 },
      { date: '2026-09-12', isIncoming: true, purposeDescription: 'Groceries for school canteens, contract 44', amount: 145000000 },
      { date: '2026-09-01', isIncoming: false, purposeDescription: 'Purchase of rice and vegetable oil, invoice 5521', amount: 260000000 }
    ],
    onRegister: false
  },
  {
    customer: {
      customerId: 'C-07',
      name: 'Kichik Non',
      taxCode: '307890123',
      type: 'Sole proprietor',
      industry: { industryCode: '10710', name: 'Manufacture of bread' },
      address: { regionName: 'Tashkent', districtName: 'Sergeli' },
      languageCode: 'en',
      registrationDate: '2024-02-15',
      customerSince: '2026-06-10'
    },
    riskFeatures: {
      avgMonthlyInflow6m: 30000000,
      outstandingAmount: 0,
      overdueAmount: 0,
      maxOverdueDays12m: 0,
      preApprovedLimit: null,
      kycPassed: true
    },
    recentPayments: [
      { date: '2026-10-10', isIncoming: true, purposeDescription: 'Payment for bread, Sergeli market stall', amount: 2100000 },
      { date: '2026-09-29', isIncoming: false, purposeDescription: 'Purchase of flour, invoice 117', amount: 1400000 }
    ],
    onRegister: false
  }
];

// Same shape as data/tenders.json (the fields we take from the procurement platform).
// Text fields come in English and Uzbek. sourceUrl will point to the lot on the original
// website once tenders are loaded for real; it is null in the mock data.
const tenders = [
  {
    lotId: 'T-1001',
    title: { en: 'Bread supply for schools of Yunusobod district', uz: 'Yunusobod tumani maktablari uchun non yetkazib berish' },
    buyer: { en: 'Yunusobod district school education department', uz: "Yunusobod tumani maktab ta'limi bo'limi" },
    region: 'Tashkent', startPrice: 240000000, guaranteePercent: 3, paymentTermsDays: 30, deadline: '2026-11-30',
    source: 'UZEX', sourceUrl: null
  },
  {
    lotId: 'T-1002',
    title: { en: 'Bread for kindergarten No. 12', uz: "12-son bolalar bog'chasi uchun non" },
    buyer: { en: 'Kindergarten No. 12', uz: "12-son bolalar bog'chasi" },
    region: 'Tashkent', startPrice: 60000000, guaranteePercent: 3, paymentTermsDays: 30, deadline: '2026-11-20',
    source: 'UZEX', sourceUrl: null
  },
  {
    lotId: 'T-1003',
    title: { en: 'Food products for city hospital No. 5', uz: '5-son shahar shifoxonasi uchun oziq-ovqat mahsulotlari' },
    buyer: { en: 'City hospital No. 5', uz: '5-son shahar shifoxonasi' },
    region: 'Tashkent', startPrice: 2400000000, guaranteePercent: 3, paymentTermsDays: 45, deadline: '2026-12-05',
    source: 'UZEX', sourceUrl: null
  },
  {
    lotId: 'T-1004',
    title: { en: 'Office paper and stationery for a district tax office', uz: "Tuman soliq inspeksiyasi uchun ofis qog'ozi va kanselyariya tovarlari" },
    buyer: { en: 'District tax office', uz: 'Tuman soliq inspeksiyasi' },
    region: 'Tashkent', startPrice: 85000000, guaranteePercent: 3, paymentTermsDays: 30, deadline: '2026-11-25',
    source: 'UZEX', sourceUrl: null
  },
  {
    lotId: 'T-1005',
    title: { en: 'Cleaning services for a district hospital', uz: 'Tuman shifoxonasi uchun tozalash xizmatlari' },
    buyer: { en: 'District medical association', uz: 'Tuman tibbiyot birlashmasi' },
    region: 'Samarkand', startPrice: 150000000, guaranteePercent: 3, paymentTermsDays: 30, deadline: '2026-12-01',
    source: 'UZEX', sourceUrl: null
  },
  {
    lotId: 'T-1006',
    title: { en: 'School roof repair', uz: "Maktab tomini ta'mirlash" },
    buyer: { en: 'Regional education department', uz: "Viloyat maktab ta'limi boshqarmasi" },
    region: 'Fergana', startPrice: 1100000000, guaranteePercent: 5, paymentTermsDays: 60, deadline: '2026-12-10',
    source: 'UZEX', sourceUrl: null
  },
  {
    lotId: 'T-1007',
    title: { en: 'Bread supply for a military unit', uz: 'Harbiy qism uchun non yetkazib berish' },
    buyer: { en: 'Military unit', uz: 'Harbiy qism' },
    region: 'Bukhara', startPrice: 300000000, guaranteePercent: 3, paymentTermsDays: 30, deadline: '2026-11-28',
    source: 'UZEX', sourceUrl: null
  }
];

// Tender table text in each language
const UZ_MONTHS = ['yanvar', 'fevral', 'mart', 'aprel', 'may', 'iyun', 'iyul', 'avgust', 'sentabr', 'oktabr', 'noyabr', 'dekabr'];
const TENDER_TEXT = {
  en: {
    headers: ['Lot', 'Tender', 'Region', "Start price, so'm", 'Guarantee', 'Payment terms', 'Deadline', 'Source'],
    regions: {},
    days: (n) => `${n} ${n === 1 ? 'day' : 'days'}`,
    inDays: (n) => `In ${n} ${n === 1 ? 'day' : 'days'}`,
    today: 'Closes today',
    closed: 'Closed',
    openSource: 'Open the original tender',
    date: (iso) => formatDate(iso)
  },
  uz: {
    headers: ['Lot', 'Tender', 'Hudud', "Boshlang'ich narx, so'm", 'Kafolat', "To'lov muddati", 'Oxirgi muddat', 'Manba'],
    regions: { Tashkent: 'Toshkent', Samarkand: 'Samarqand', Fergana: "Farg'ona", Bukhara: 'Buxoro' },
    days: (n) => `${n} kun`,
    inDays: (n) => `${n} kundan keyin`,
    today: 'Bugun yopiladi',
    closed: 'Yopilgan',
    openSource: 'Asl tenderni ochish',
    date: (iso) => {
      const d = new Date(iso + 'T00:00:00');
      return `${d.getDate()}-${UZ_MONTHS[d.getMonth()]} ${d.getFullYear()}`;
    }
  }
};

let tenderLang = 'en';

/* ------------------------------------------------------------------ */
/* Mock matching results: what the agents save after a run             */
/* ------------------------------------------------------------------ */
// In the real product these rows come from the save_match (tender matcher) and
// save_loan_offer (loan advisor) tools. Every rule check carries the actual value.
// In the demo they appear one by one after "Start matching" is pressed (see startMatching).

// Loan advisor rule checks per customer (demo thresholds from loan-policy.md)
const CHECKS = {
  'C-01': {
    guarantee: [
      ['KYC passed', 'Yes', true],
      ['At least 6 months with the bank', '26 months', true],
      ['Longest overdue in 12 months at most 30 days', '0 days', true],
      ["Average monthly inflow at least 30 000 000 so'm", "120 000 000 so'm", true]
    ],
    financing: [
      ['KYC passed', 'Yes', true],
      ['At least 12 months with the bank', '26 months', true],
      ['No overdue days in the last 12 months', '0 days', true],
      ['No overdue amount today', "0 so'm", true],
      ["Average monthly inflow at least 80 000 000 so'm", "120 000 000 so'm", true],
      ['Has a pre-approved limit', "150 000 000 so'm", true]
    ]
  },
  'C-02': {
    guarantee: [
      ['KYC passed', 'Yes', true],
      ['At least 6 months with the bank', '18 months', true],
      ['Longest overdue in 12 months at most 30 days', '45 days', false],
      ["Average monthly inflow at least 30 000 000 so'm", "90 000 000 so'm", true]
    ],
    financing: [
      ['KYC passed', 'Yes', true],
      ['At least 12 months with the bank', '18 months', true],
      ['No overdue days in the last 12 months', '45 days', false],
      ['No overdue amount today', "0 so'm", true],
      ["Average monthly inflow at least 80 000 000 so'm", "90 000 000 so'm", true],
      ['Has a pre-approved limit', "150 000 000 so'm", true]
    ]
  },
  'C-06': {
    guarantee: [
      ['KYC passed', 'Yes', true],
      ['At least 6 months with the bank', '60 months', true],
      ['Longest overdue in 12 months at most 30 days', '0 days', true],
      ["Average monthly inflow at least 30 000 000 so'm", "900 000 000 so'm", true]
    ],
    financing: [
      ['KYC passed', 'Yes', true],
      ['At least 12 months with the bank', '60 months', true],
      ['No overdue days in the last 12 months', '0 days', true],
      ['No overdue amount today', "0 so'm", true],
      ["Average monthly inflow at least 80 000 000 so'm", "900 000 000 so'm", true],
      ['Has a pre-approved limit', "1 500 000 000 so'm", true]
    ]
  },
  'C-07': {
    guarantee: [
      ['KYC passed', 'Yes', true],
      ['At least 6 months with the bank', '4 months', false],
      ['Longest overdue in 12 months at most 30 days', '0 days', true],
      ["Average monthly inflow at least 30 000 000 so'm", "30 000 000 so'm", true]
    ],
    financing: [
      ['KYC passed', 'Yes', true],
      ['At least 12 months with the bank', '4 months', false],
      ['No overdue days in the last 12 months', '0 days', true],
      ['No overdue amount today', "0 so'm", true],
      ["Average monthly inflow at least 80 000 000 so'm", "30 000 000 so'm", false],
      ['Has a pre-approved limit', 'None', false]
    ]
  }
};

const matches = [
  {
    customerId: 'C-01', lotId: 'T-1001',
    reasons: [
      'Makes bread: OKED 10710, and regular bread delivery payments in the last 3 months.',
      'Already supplies the state: a payment from Kindergarten No 7 in September 2026.',
      'Based in Yunusobod, Tashkent, the same district as the buyer.',
      "Contract fits its size: 240 000 000 so'm is 2.0 times its average monthly inflow (the limit is 3 times).",
      'Not banned from state tenders, and in business since 2019, so the 1-year requirement is met.'
    ],
    ignored: ['A June 2025 catering payment is older than 12 months and was not used.'],
    offers: [
      { product: 'Bid guarantee', amount: 7200000, calc: "3% of 240 000 000 = 7 200 000 so'm" },
      { product: 'Contract financing', amount: 120000000, calc: "50% of 240 000 000 = 120 000 000 so'm, within the pre-approved limit of 150 000 000" }
    ],
    note: 'Qualifies for both products. The financing is half the contract price and stays within the pre-approved limit.'
  },
  {
    customerId: 'C-01', lotId: 'T-1002',
    reasons: [
      'Makes bread: OKED 10710, and regular bread delivery payments in the last 3 months.',
      'Already supplies kindergartens: a payment from Kindergarten No 7 in September 2026.',
      'Based in Tashkent, the tender’s region.',
      "Contract fits its size: 60 000 000 so'm is 0.5 times its average monthly inflow."
    ],
    ignored: ['A June 2025 catering payment is older than 12 months and was not used.'],
    offers: [
      { product: 'Bid guarantee', amount: 1800000, calc: "3% of 60 000 000 = 1 800 000 so'm" },
      { product: 'Contract financing', amount: 30000000, calc: "50% of 60 000 000 = 30 000 000 so'm, within the pre-approved limit of 150 000 000" }
    ],
    note: 'Qualifies for both products. A small contract compared with its turnover.'
  },
  {
    customerId: 'C-02', lotId: 'T-1001',
    reasons: [
      'Makes bread: OKED 10710, and payments for bread and buns from cafes.',
      'Based in Chilonzor, Tashkent, the tender’s region.',
      "Contract fits its size: 240 000 000 so'm is 2.7 times its average monthly inflow (the limit is 3 times).",
      'Not banned from state tenders, and in business since 2020.'
    ],
    ignored: [],
    offers: [],
    refusal: '45 days overdue in the last 12 months',
    note: 'No loan offered. The KATM report shows a 45-day overdue in the last 12 months, above the 30-day limit for a bid guarantee and the zero-day limit for financing. The tender is still shown to the customer.'
  },
  {
    customerId: 'C-02', lotId: 'T-1002',
    reasons: [
      'Makes bread: OKED 10710, and payments for bread and buns from cafes.',
      'Based in Tashkent, the tender’s region.',
      "Contract fits its size: 60 000 000 so'm is 0.7 times its average monthly inflow."
    ],
    ignored: [],
    offers: [],
    refusal: '45 days overdue in the last 12 months',
    note: 'No loan offered, for the same reason as the schools tender: a 45-day overdue in the last 12 months.'
  },
  {
    customerId: 'C-06', lotId: 'T-1003',
    reasons: [
      'Sells food wholesale: OKED 46390, and large food supply payments in the last 3 months.',
      'Already supplies the state: payments from city hospital No 2 and school canteens.',
      'Based in Tashkent, the tender’s region.',
      "Contract fits its size: 2 400 000 000 so'm is 2.7 times its average monthly inflow (the limit is 3 times)."
    ],
    ignored: [],
    offers: [
      { product: 'Bid guarantee', amount: 72000000, calc: "3% of 2 400 000 000 = 72 000 000 so'm" },
      { product: 'Contract financing', amount: 1200000000, calc: "50% of 2 400 000 000 = 1 200 000 000 so'm, within the pre-approved limit of 1 500 000 000" }
    ],
    note: 'Qualifies for both products. The financing is half the contract price and within the pre-approved limit.'
  },
  {
    customerId: 'C-07', lotId: 'T-1002',
    reasons: [
      'Makes bread: OKED 10710, and bread sales at a Sergeli market stall.',
      'Based in Tashkent, the tender’s region.',
      "Contract fits its size: 60 000 000 so'm is 2.0 times its average monthly inflow.",
      "Larger bread tenders were skipped: T-1001 (240 000 000 so'm) is 8 times its monthly inflow."
    ],
    ignored: [],
    offers: [],
    refusal: 'Only 4 months with the bank',
    note: 'No loan offered. Kichik Non has been with the bank for 4 months, and the bid guarantee needs 6. Financing also needs a pre-approved limit and at least 80 000 000 so’m of monthly inflow.'
  }
];

const REGIONS = [
  'Tashkent', 'Tashkent region', 'Andijan', 'Bukhara', 'Fergana', 'Jizzakh', 'Karakalpakstan',
  'Kashkadarya', 'Khorezm', 'Namangan', 'Navoi', 'Samarkand', 'Surkhandarya', 'Syrdarya'
];

const LANGUAGES = [['en', 'English'], ['uz', 'Uzbek'], ['ru', 'Russian']];
const LANGUAGE_NAMES = Object.fromEntries(LANGUAGES);

/* ------------------------------------------------------------------ */
/* Modal form. Tab 1 = profile (non-loan data), tab 2 = loan and risk  */
/* ------------------------------------------------------------------ */

const SECTIONS = {
  profile: [
    {
      title: 'Company',
      fields: [
        { name: 'customerId', label: 'Customer ID', required: true },
        { name: 'name', label: 'Company name', required: true, placeholder: 'Non Baraka LLC' },
        { name: 'taxCode', label: 'TIN (STIR)', required: true, pattern: '\\d{9}', inputmode: 'numeric', placeholder: '9 digits' },
        { name: 'type', label: 'Legal form', type: 'select', options: ['LLC', 'Sole proprietor', 'JSC', 'Private enterprise'] },
        { name: 'registrationDate', label: 'Registration date', type: 'date' },
        { name: 'customerSince', label: 'Customer since', type: 'date', required: true }
      ]
    },
    {
      title: 'What they sell',
      description: 'The matcher compares this with tender descriptions.',
      fields: [
        { name: 'industryCode', label: 'OKED code', required: true, inputmode: 'numeric', placeholder: '10710' },
        { name: 'industryName', label: 'OKED activity', required: true, placeholder: 'Manufacture of bread' },
        { name: 'recentPayments', label: 'Recent payments', type: 'payments', full: true,
          hint: 'The matcher uses payments from the last 12 months, weighs the last 3 months most, and ignores anything older.' }
      ]
    },
    {
      title: 'Location and language',
      description: 'Notifications are written in the preferred language.',
      fields: [
        { name: 'regionName', label: 'Region', type: 'select', options: REGIONS },
        { name: 'districtName', label: 'District', placeholder: 'Yunusobod' },
        { name: 'languageCode', label: 'Preferred language', type: 'select', options: LANGUAGES }
      ]
    },
    {
      title: 'Compliance',
      fields: [
        { name: 'onRegister', label: 'Banned from state tenders', type: 'checkbox', tone: 'warn', full: true,
          description: "On the government's list of unreliable suppliers. The ban lasts 2 years, and the agents never offer tenders to banned companies." }
      ]
    }
  ],
  loan: [
    {
      title: 'Cash flow',
      fields: [
        { name: 'avgMonthlyInflow6m', label: 'Average monthly inflow, 6 months', type: 'number', suffix: "so'm", required: true },
        { name: 'monthsWithBank', label: 'Months with the bank', type: 'number', suffix: 'months', readonly: true,
          hint: 'Calculated from "Customer since" on the Profile tab.' }
      ]
    },
    {
      title: 'Credit history',
      description: 'Overdue history comes from the KATM credit bureau report.',
      fields: [
        { name: 'outstandingAmount', label: 'Outstanding loans', type: 'number', suffix: "so'm", value: 0 },
        { name: 'overdueAmount', label: 'Overdue amount today', type: 'number', suffix: "so'm", value: 0 },
        { name: 'maxOverdueDays12m', label: 'Longest overdue, 12 months', type: 'number', suffix: 'days', value: 0 }
      ]
    },
    {
      title: 'Eligibility',
      fields: [
        { name: 'preApprovedLimit', label: 'Pre-approved limit', type: 'number', suffix: "so'm", hint: 'Leave empty if there is none.' },
        { name: 'kycPassed', label: 'KYC passed', type: 'checkbox', checked: true, full: true,
          description: 'Required for any loan offer.' }
      ]
    }
  ]
};

/* ------------------------------------------------------------------ */
/* Helpers                                                             */
/* ------------------------------------------------------------------ */

const $ = (sel, root = document) => root.querySelector(sel);
const $$ = (sel, root = document) => [...root.querySelectorAll(sel)];

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) =>
  ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

// 120000000 -> "120 000 000"
const money = (n) => String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ' ');

// 4335000000 -> "4.3 billion"
const shortMoney = (n) =>
  n >= 1e9 ? `${(n / 1e9).toFixed(1)} billion` : n >= 1e6 ? `${Math.round(n / 1e6)} million` : money(n);

const formatDate = (iso) =>
  new Date(iso + 'T00:00:00').toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });

const daysUntil = (iso) => {
  const today = new Date(); today.setHours(0, 0, 0, 0);
  return Math.round((new Date(iso + 'T00:00:00') - today) / 86400000);
};

const initials = (name) => name.replace(/\b(LLC|JSC)\b/g, '').trim().split(/\s+/)
  .slice(0, 2).map((w) => w[0]).join('').toUpperCase();

// The unit (so'm) lives in the column header, so cells show the number only
const amount = (n) => money(n);

const chip = (text) =>
  `<span class="inline-flex rounded-md bg-subtle px-2 py-1 text-xs font-medium text-ink ring-1 ring-line ring-inset">${esc(text)}</span>`;

const PILL = {
  ok: 'bg-emerald-50 text-emerald-700',
  warn: 'bg-amber-50 text-amber-700',
  neutral: 'bg-subtle text-muted'
};
const pill = (text, tone) =>
  `<span class="inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium whitespace-nowrap ${PILL[tone]}"><span class="size-1.5 rounded-full bg-current"></span>${esc(text)}</span>`;

const ICON_TRASH = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" class="size-4.5" aria-hidden="true"><path d="M4.5 7h15M10 11v6M14 11v6M6.5 7l.9 12.2a1 1 0 0 0 1 .8h7.2a1 1 0 0 0 1-.8L17.5 7M9.5 7V4.5h5V7"/></svg>`;

const CONTROL_CLASS =
  'w-full rounded-lg border border-line bg-surface px-3 text-sm text-ink placeholder:text-muted/60 outline-none transition ' +
  'focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15 user-invalid:border-red-500 user-invalid:focus:ring-red-500/15';

/* ------------------------------------------------------------------ */
/* Pages (hash routing: #customers, #tenders)                          */
/* ------------------------------------------------------------------ */

// Sidebar link styles: the page you're on gets a white highlight
const NAV_ACTIVE = ['bg-white', 'text-cobalt', 'shadow-sm'];
const NAV_IDLE = ['text-cobalt-soft', 'hover:bg-white/10', 'hover:text-white'];

function route() {
  const hashPage = location.hash.slice(1);
  const page = ['customers', 'tenders', 'matches'].includes(hashPage) ? hashPage : 'customers';
  $$('[data-page]').forEach((el) => { el.hidden = el.dataset.page !== page; });
  $$('[data-nav]').forEach((a) => {
    const active = a.dataset.nav === page;
    a.classList.remove(...NAV_ACTIVE, ...NAV_IDLE);
    a.classList.add(...(active ? NAV_ACTIVE : NAV_IDLE));
    a.querySelector('svg').classList.toggle('text-brand-600', active);
    if (active) a.setAttribute('aria-current', 'page');
    else a.removeAttribute('aria-current');
  });
}

/* ------------------------------------------------------------------ */
/* Tables                                                              */
/* ------------------------------------------------------------------ */

function overduePill(days) {
  if (days === 0) return pill('None', 'ok');
  return pill(`${days} days`, days > 30 ? 'warn' : 'neutral');
}

function renderCustomers() {
  const n = customers.length;
  $('#customer-summary').textContent =
    `${n} business ${n === 1 ? 'customer' : 'customers'}. The agents match them against open tenders and check them for loan offers.`;

  $('#customers-body').innerHTML = customers.map(({ customer: c, riskFeatures: r }) => `
    <tr class="transition-colors hover:bg-subtle/70">
      <td class="px-3.5 py-4">
        <div class="flex items-center gap-3">
          <span class="grid size-9 shrink-0 place-items-center rounded-full bg-brand-50 text-xs font-semibold text-brand-700">${esc(initials(c.name))}</span>
          <div class="min-w-0">
            <div class="font-medium">${esc(c.name)}</div>
            <div class="text-xs whitespace-nowrap text-muted">${esc(c.customerId)}, TIN ${esc(c.taxCode)}</div>
          </div>
        </div>
      </td>
      <td class="px-3.5 py-4">
        <div>${esc(c.industry.name)}</div>
        <div class="text-xs text-muted">OKED ${esc(c.industry.industryCode)}</div>
      </td>
      <td class="px-3.5 py-4">
        <div>${esc(c.address.regionName)}</div>
        <div class="text-xs text-muted">${esc(c.address.districtName)}</div>
      </td>
      <td class="px-3.5 py-4">${chip(LANGUAGE_NAMES[c.languageCode] || c.languageCode)}</td>
      <td class="px-3.5 py-4 text-right whitespace-nowrap tabular-nums">${amount(r.avgMonthlyInflow6m)}</td>
      <td class="px-3.5 py-4">${overduePill(r.maxOverdueDays12m)}</td>
      <td class="px-3.5 py-4 text-right whitespace-nowrap tabular-nums">
        ${r.preApprovedLimit ? amount(r.preApprovedLimit) : '<span class="text-muted">None</span>'}
      </td>
      <td class="px-3 py-4">
        <div class="flex items-center justify-end gap-1">
          <button type="button" data-details="${esc(c.customerId)}" aria-label="Details for ${esc(c.name)}"
            class="rounded-lg px-2.5 py-1.5 text-xs font-medium text-brand-700 ring-1 ring-line ring-inset transition-colors hover:bg-brand-50 hover:ring-brand-300">
            Details
          </button>
          <button type="button" data-delete="${esc(c.customerId)}" title="Delete" aria-label="Delete ${esc(c.name)}"
            class="grid size-8 place-items-center rounded-lg text-muted transition-colors hover:bg-red-50 hover:text-red-600">
            ${ICON_TRASH}
          </button>
        </div>
      </td>
    </tr>`).join('');
}

function deadlineCell(iso, text) {
  const d = daysUntil(iso);
  const when = d < 0 ? text.closed : d === 0 ? text.today : text.inDays(d);
  const tone = d <= 14 ? 'text-amber-700' : 'text-muted';
  return `<div class="whitespace-nowrap">${esc(text.date(iso))}</div><div class="text-xs whitespace-nowrap ${tone}">${esc(when)}</div>`;
}

const ICON_EXTERNAL = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="size-3.5" aria-hidden="true"><path d="M14 5h5v5M19 5l-8 8M17 14v4a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V8a1 1 0 0 1 1-1h4"/></svg>`;

function renderTenders() {
  const text = TENDER_TEXT[tenderLang];

  // The summary line stays in English; the table follows the language switch
  const total = tenders.reduce((sum, t) => sum + t.startPrice, 0);
  const nearest = tenders.map((t) => t.deadline).sort()[0];
  $('#tender-summary').textContent =
    `${tenders.length} open lots worth ${shortMoney(total)} so'm in total. The nearest deadline is ${formatDate(nearest)}.`;

  $$('[data-tender-lang]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.tenderLang === tenderLang)));

  const alignRight = [3, 4];
  $('#tenders-head').innerHTML = `
    <tr class="border-b border-line">
      ${text.headers.map((h, i) => `<th class="px-3.5 py-3 font-medium ${alignRight.includes(i) ? 'text-right' : ''}">${esc(h)}</th>`).join('')}
    </tr>`;

  $('#tenders-body').innerHTML = tenders.map((t) => `
    <tr class="transition-colors hover:bg-subtle/70">
      <td class="px-3.5 py-4 text-xs whitespace-nowrap text-muted">${esc(t.lotId)}</td>
      <td class="min-w-52 px-3.5 py-4">
        <div class="font-medium">${esc(t.title[tenderLang])}</div>
        <div class="text-xs text-muted">${esc(t.buyer[tenderLang])}</div>
      </td>
      <td class="px-3.5 py-4">${chip(text.regions[t.region] || t.region)}</td>
      <td class="px-3.5 py-4 text-right font-medium whitespace-nowrap tabular-nums">${amount(t.startPrice)}</td>
      <td class="px-3.5 py-4 text-right whitespace-nowrap tabular-nums">
        <div>${t.guaranteePercent}%</div>
        <div class="text-xs text-muted">${money(t.startPrice * t.guaranteePercent / 100)} so'm</div>
      </td>
      <td class="px-3.5 py-4 whitespace-nowrap">${esc(text.days(t.paymentTermsDays))}</td>
      <td class="px-3.5 py-4">${deadlineCell(t.deadline, text)}</td>
      <td class="px-3.5 py-4">
        <button type="button" data-source="${esc(t.lotId)}" title="${esc(text.openSource)}" aria-label="${esc(text.openSource)}: ${esc(t.lotId)}"
          class="inline-flex items-center gap-1.5 rounded-md bg-subtle px-2 py-1 text-xs font-medium text-ink ring-1 ring-line ring-inset transition-colors hover:bg-brand-50 hover:text-brand-700 hover:ring-brand-300">
          <span class="size-1.5 rounded-full bg-brand-500"></span>${esc(t.source)}${ICON_EXTERNAL}
        </button>
      </td>
    </tr>`).join('');
}

/* ------------------------------------------------------------------ */
/* Matches page and AI comments                                        */
/* ------------------------------------------------------------------ */

const customerById = (id) => customers.find((c) => c.customer.customerId === id)?.customer;
const tenderById = (id) => tenders.find((t) => t.lotId === id);

const formatRunTime = (d) =>
  `${d.toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })} at ` +
  `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;

// Demo run: idle (nothing yet) -> running (rows appear one by one) -> done
let run = { status: 'idle', startedAt: null, shown: 0 };
let runTimer;
const FIRST_ROW_DELAY = 1200; // ms before the first match appears
const NEXT_ROW_DELAY = 700;   // ms between matches

const ICON_SPINNER = `<svg viewBox="0 0 24 24" fill="none" class="size-4 animate-spin motion-reduce:animate-none" aria-hidden="true"><circle cx="12" cy="12" r="9" stroke="currentColor" stroke-opacity="0.25" stroke-width="2.5"/><path d="M21 12a9 9 0 0 0-9-9" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"/></svg>`;
const ICON_PLAY = `<svg viewBox="0 0 24 24" fill="currentColor" class="size-4" aria-hidden="true"><path d="M8 5.5v13a1 1 0 0 0 1.5.86l10.5-6.5a1 1 0 0 0 0-1.72L9.5 4.64A1 1 0 0 0 8 5.5z"/></svg>`;

// Pretends to run the agents: shows the mock matches one at a time.
// Later this calls the server to start a full run and polls for saved matches instead.
function startMatching() {
  clearTimeout(runTimer);
  run = { status: 'running', startedAt: new Date(), shown: 0 };
  renderMatches();

  const showNext = () => {
    run.shown += 1;
    if (run.shown >= matches.length) run.status = 'done';
    renderMatches();
    if (run.status === 'running') runTimer = setTimeout(showNext, NEXT_ROW_DELAY);
  };
  runTimer = setTimeout(showNext, FIRST_ROW_DELAY);
}

function emptyRow(content) {
  return `<tr><td colspan="4" class="px-6 py-14 text-center">${content}</td></tr>`;
}

const ICON_SPARK = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" class="size-4" aria-hidden="true"><path d="M12 3.5l1.8 4.7 4.7 1.8-4.7 1.8L12 16.5l-1.8-4.7L5.5 10l4.7-1.8z"/></svg>`;
const ICON_PASS = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" class="size-4 shrink-0 text-emerald-600" aria-hidden="true"><path d="M5 12.5l4.5 4.5L19 7"/></svg>`;
const ICON_FAIL = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" class="size-4 shrink-0 text-red-600" aria-hidden="true"><path d="M7 7l10 10M17 7 7 17"/></svg>`;

function renderMatches() {
  const visible = matches.slice(0, run.shown);
  const offered = visible.filter((m) => m.offers.length).length;
  const running = run.status === 'running';

  // Summary line under the title
  $('#match-summary').textContent =
    run.status === 'idle'
      ? `No matches yet. Start matching to check ${customers.length} customers against ${tenders.length} open tenders.`
      : running
        ? `Matching started at ${formatRunTime(run.startedAt).split(' at ')[1]}. ${visible.length} found so far.`
        : `${visible.length} matches from the run on ${formatRunTime(run.startedAt)}. ${offered} come with a loan offer.`;

  // Start button: shows progress while the run is going
  const start = $('#start-matching');
  start.disabled = running;
  start.innerHTML = running ? `${ICON_SPINNER} Matching…` : `${ICON_PLAY} Start matching`;

  if (run.status === 'idle') {
    $('#matches-body').innerHTML = emptyRow(`
      <div class="mx-auto grid size-11 place-items-center rounded-xl bg-brand-50 text-brand-600">${ICON_SPARK.replace('size-4', 'size-5')}</div>
      <p class="mt-3 font-medium">No matches yet</p>
      <p class="mt-1 text-sm text-muted">Press Start matching and the agents will check every customer against the open tenders.</p>`);
    return;
  }
  if (running && visible.length === 0) {
    $('#matches-body').innerHTML = emptyRow(`
      <div class="mx-auto grid size-11 place-items-center rounded-xl bg-brand-50 text-brand-600">${ICON_SPINNER.replace('size-4', 'size-5')}</div>
      <p class="mt-3 font-medium">The agents are matching</p>
      <p class="mt-1 text-sm text-muted">Checking ${customers.length} customers against ${tenders.length} open tenders.</p>`);
    return;
  }

  const rows = visible.map((m, i) => {
    const c = customerById(m.customerId);
    const t = tenderById(m.lotId);
    const loan = m.offers.length
      ? m.offers.map((o) => `
          <div class="whitespace-nowrap tabular-nums">
            <span class="text-xs text-muted">${esc(o.product === 'Bid guarantee' ? 'Guarantee' : 'Financing')}</span>
            <span class="ml-1.5 font-medium">${money(o.amount)}</span>
          </div>`).join('')
      : `${pill('No offer', 'warn')}<div class="mt-1 text-xs text-muted">${esc(m.refusal)}</div>`;
    // The newest row slides in while the run is going
    const isNew = running && i === visible.length - 1;
    return `
      <tr class="transition-colors hover:bg-subtle/70 ${isNew ? 'match-row-in' : ''}">
        <td class="px-3.5 py-4">
          <div class="font-medium whitespace-nowrap">${esc(c.name)}</div>
          <div class="text-xs text-muted">${esc(m.customerId)}</div>
        </td>
        <td class="px-3.5 py-4">
          <div>${esc(t.title.en)}</div>
          <div class="text-xs text-muted">${esc(m.lotId)}</div>
        </td>
        <td class="px-3.5 py-4 text-right">${loan}</td>
        <td class="px-3.5 py-4 text-right">
          <button type="button" data-comments="${i}" aria-label="AI comments for ${esc(c.name)} and ${esc(m.lotId)}"
            class="inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-medium whitespace-nowrap text-brand-700 ring-1 ring-line ring-inset transition-colors hover:bg-brand-50 hover:ring-brand-300">
            ${ICON_SPARK} View
          </button>
        </td>
      </tr>`;
  }).join('');

  // While the run is going, a quiet last row says more may come
  const stillRunning = running ? `
    <tr><td colspan="4" class="px-3.5 py-3">
      <div class="flex items-center gap-2 text-sm text-muted">${ICON_SPINNER} Still matching…</div>
    </td></tr>` : '';

  $('#matches-body').innerHTML = rows + stillRunning;
}

function checksHtml(title, checks, offer) {
  const passed = checks.every(([, , ok]) => ok);
  return `
    <div class="rounded-xl border border-line">
      <div class="flex items-center justify-between gap-3 border-b border-line bg-subtle px-4 py-2.5">
        <span class="text-sm font-medium">${esc(title)}</span>
        ${passed ? pill(offer ? `${money(offer.amount)} so'm` : 'Offered', 'ok') : pill('Not offered', 'warn')}
      </div>
      <ul class="divide-y divide-line">
        ${checks.map(([rule, value, ok]) => `
          <li class="flex items-center gap-3 px-4 py-2 text-sm">
            ${ok ? ICON_PASS : ICON_FAIL}
            <span class="flex-1">${esc(rule)}</span>
            <span class="text-xs whitespace-nowrap ${ok ? 'text-muted' : 'font-medium text-red-600'}">${esc(value)}</span>
          </li>`).join('')}
      </ul>
    </div>`;
}

function commentSection(agent, title, body) {
  return `
    <section>
      <div class="mb-2 flex items-baseline justify-between gap-3">
        <h3 class="text-sm font-semibold">${esc(title)}</h3>
        <span class="text-xs text-muted">${esc(agent)}</span>
      </div>
      ${body}
    </section>`;
}

function openComments(index) {
  const m = matches[index];
  const c = customerById(m.customerId);
  const t = tenderById(m.lotId);
  const checks = CHECKS[m.customerId];
  const offerFor = (product) => m.offers.find((o) => o.product === product);

  $('#comments-subtitle').textContent = `${c.name} and ${m.lotId}, ${t.title.en}`;
  $('#comments-run').textContent = `From the run on ${formatRunTime(run.startedAt)}`;

  const reasons = `
    <ul class="space-y-2">
      ${m.reasons.map((r) => `<li class="flex gap-2.5 text-sm">${ICON_PASS}<span>${esc(r)}</span></li>`).join('')}
    </ul>
    ${m.ignored.length ? `<p class="mt-3 rounded-lg bg-subtle px-3 py-2 text-xs text-muted">${m.ignored.map(esc).join(' ')}</p>` : ''}`;

  const decision = `
    <p class="mb-3 rounded-xl px-4 py-3 text-sm ${m.offers.length ? 'bg-emerald-50 text-emerald-900' : 'bg-amber-50 text-amber-900'}">${esc(m.note)}</p>
    <div class="space-y-3">
      ${checksHtml('Bid guarantee', checks.guarantee, offerFor('Bid guarantee'))}
      ${checksHtml('Contract financing', checks.financing, offerFor('Contract financing'))}
    </div>`;

  const amounts = m.offers.length
    ? `<ul class="space-y-1.5 text-sm">${m.offers.map((o) =>
        `<li><span class="font-medium">${esc(o.product)}:</span> ${esc(o.calc)}</li>`).join('')}</ul>`
    : '<p class="text-sm text-muted">Not calculated, because no product passed all its rules.</p>';

  $('#comments-body').innerHTML = [
    commentSection('Tender matcher', 'Why this tender fits', reasons),
    commentSection('Loan advisor', 'Loan decision', decision),
    commentSection('Loan advisor', 'How the amounts were calculated', amounts)
  ].join('');

  $('#comments-dialog').showModal();
  $('#comments-body').scrollTop = 0;
}

/* ------------------------------------------------------------------ */
/* Modal form                                                          */
/* ------------------------------------------------------------------ */

// One editable payment: purpose on top; date, direction and amount below
function paymentRowHtml(p = {}) {
  const date = p.date || new Date().toISOString().slice(0, 10);
  const incoming = p.isIncoming !== false;
  return `
    <div data-payment-row class="rounded-xl border border-line bg-subtle/60 p-3 transition-opacity">
      <div class="flex items-center gap-2">
        <input data-p="purpose" type="text" required aria-label="Payment purpose" value="${esc(p.purposeDescription || '')}"
          placeholder="Payment for bread delivery, contract No 14" class="${CONTROL_CLASS} h-9 flex-1">
        <button type="button" data-remove-payment aria-label="Remove payment" title="Remove"
          class="grid size-9 shrink-0 place-items-center rounded-lg text-muted transition-colors hover:bg-red-50 hover:text-red-600">${ICON_TRASH}</button>
      </div>
      <div class="mt-2 grid grid-cols-3 gap-2">
        <input data-p="date" type="date" required aria-label="Payment date" value="${esc(date)}" class="${CONTROL_CLASS} h-9">
        <select data-p="direction" aria-label="Direction" class="${CONTROL_CLASS} h-9 pr-8">
          <option value="in" ${incoming ? 'selected' : ''}>Incoming</option>
          <option value="out" ${incoming ? '' : 'selected'}>Outgoing</option>
        </select>
        <div class="relative">
          <input data-p="amount" type="number" min="0" step="1" aria-label="Amount" value="${p.amount ?? ''}" placeholder="Amount"
            class="${CONTROL_CLASS} h-9 pr-12 [appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none">
          <span class="pointer-events-none absolute inset-y-0 right-3 flex items-center text-xs text-muted">so'm</span>
        </div>
      </div>
      <p data-old-note class="mt-2 hidden text-xs font-medium text-amber-700">Older than 12 months, so the matcher ignores it.</p>
    </div>`;
}

// Dims a payment and shows a note when it falls outside the 12-month window
function updatePaymentRow(row) {
  const months = monthsSince(row.querySelector('[data-p="date"]').value);
  const old = months !== null && months >= 12;
  row.classList.toggle('opacity-60', old);
  row.querySelector('[data-old-note]').classList.toggle('hidden', !old);
}

function renderPayments(list = []) {
  const box = $('[data-payments]');
  box.innerHTML = list.map(paymentRowHtml).join('');
  $$('[data-payment-row]', box).forEach(updatePaymentRow);
  $('[data-payments-empty]').hidden = list.length > 0;
}

function collectPayments() {
  return $$('[data-payment-row]').map((row) => {
    const val = (k) => row.querySelector(`[data-p="${k}"]`).value.trim();
    return {
      date: val('date'),
      isIncoming: val('direction') === 'in',
      purposeDescription: val('purpose'),
      amount: val('amount') === '' ? null : Number(val('amount'))
    };
  });
}

function fieldHtml(f) {
  const id = `f-${f.name}`;
  const span = f.full ? 'sm:col-span-2' : '';
  const req = f.required ? 'required' : '';
  const hint = f.hint ? `<p class="mt-1.5 text-xs text-muted">${esc(f.hint)}</p>` : '';

  if (f.type === 'payments') {
    return `
      <div class="${span}">
        <div class="mb-1.5 text-sm font-medium">${esc(f.label)}</div>
        <p class="mb-3 text-xs text-muted">${esc(f.hint)}</p>
        <div data-payments class="space-y-2"></div>
        <p data-payments-empty class="rounded-xl border border-dashed border-line px-3 py-4 text-center text-sm text-muted">No payments yet.</p>
        <button type="button" data-add-payment
          class="mt-2 inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-sm font-medium text-brand-700 transition-colors hover:bg-brand-50">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" class="size-4" aria-hidden="true"><path d="M12 5v14M5 12h14"/></svg>
          Add payment
        </button>
      </div>`;
  }

  if (f.type === 'checkbox') {
    const tone = f.tone === 'warn'
      ? 'has-checked:border-amber-400 has-checked:bg-amber-50'
      : 'has-checked:border-brand-500 has-checked:bg-brand-50';
    const accent = f.tone === 'warn' ? 'accent-amber-600' : 'accent-brand-600';
    return `
      <label class="${span} flex cursor-pointer items-start gap-3 rounded-xl border border-line bg-subtle p-4 transition-colors ${tone}">
        <input id="${id}" name="${f.name}" type="checkbox" ${f.checked ? 'checked' : ''} class="mt-0.5 size-4 shrink-0 ${accent}">
        <span>
          <span class="block text-sm font-medium">${esc(f.label)}</span>
          ${f.description ? `<span class="mt-0.5 block text-xs text-muted">${esc(f.description)}</span>` : ''}
        </span>
      </label>`;
  }

  let control;
  if (f.type === 'select') {
    const opts = f.options.map((o) => {
      const [value, text] = Array.isArray(o) ? o : [o, o];
      return `<option value="${esc(value)}">${esc(text)}</option>`;
    }).join('');
    control = `<select id="${id}" name="${f.name}" ${req} class="${CONTROL_CLASS} h-10 pr-8">${opts}</select>`;
  } else if (f.type === 'textarea') {
    control = `<textarea id="${id}" name="${f.name}" rows="3" ${req} class="${CONTROL_CLASS} py-2"
      placeholder="Payment for bread delivery, contract No 14&#10;Purchase of flour, invoice 2231"></textarea>`;
  } else {
    const type = f.type || 'text';
    const attrs = [
      f.pattern ? `pattern="${f.pattern}"` : '',
      f.inputmode ? `inputmode="${f.inputmode}"` : '',
      f.placeholder ? `placeholder="${esc(f.placeholder)}"` : '',
      f.value != null ? `value="${esc(f.value)}"` : '',
      type === 'number' ? 'min="0" step="1"' : '',
      f.readonly ? 'readonly tabindex="-1"' : ''
    ].join(' ');
    const noSpin = type === 'number'
      ? '[appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none'
      : '';
    const readonlyLook = f.readonly ? 'bg-subtle text-muted focus:ring-0 focus:border-line' : '';
    const input = `<input id="${id}" name="${f.name}" type="${type}" ${req} ${attrs} class="${CONTROL_CLASS} h-10 ${noSpin} ${readonlyLook} ${f.suffix ? 'pr-16' : ''}">`;
    control = f.suffix
      ? `<div class="relative">${input}<span class="pointer-events-none absolute inset-y-0 right-3 flex items-center text-xs text-muted">${esc(f.suffix)}</span></div>`
      : input;
  }

  return `
    <div class="${span}">
      <label for="${id}" class="mb-1.5 block text-sm font-medium">
        ${esc(f.label)}${f.required ? ' <span class="text-red-600">*</span>' : ''}
      </label>
      ${control}
      ${hint}
    </div>`;
}

function sectionHtml(s) {
  return `
    <div>
      <h3 class="text-sm font-semibold">${esc(s.title)}</h3>
      ${s.description ? `<p class="mt-0.5 text-xs text-muted">${esc(s.description)}</p>` : ''}
      <div class="mt-3 grid grid-cols-1 gap-4 sm:grid-cols-2">${s.fields.map(fieldHtml).join('')}</div>
    </div>`;
}

function showTab(name) {
  $$('[data-tab]').forEach((b) => b.setAttribute('aria-selected', String(b.dataset.tab === name)));
  $$('[data-panel]').forEach((p) => { p.hidden = p.dataset.panel !== name; });
}

function nextCustomerId() {
  const max = Math.max(0, ...customers.map((c) => Number(c.customer.customerId.replace(/\D/g, ''))));
  return `C-${String(max + 1).padStart(2, '0')}`;
}

// Text for the two ways the modal is used
const DIALOG_MODES = {
  add: {
    title: 'Add customer',
    subtitle: 'Fill in both tabs. Saving logs the customer as JSON in the browser console.',
    submit: 'Save customer',
    logLabel: 'New customer submission:',
    toast: 'Customer saved. The JSON is in the browser console.'
  },
  details: {
    title: 'Customer details',
    subtitle: 'Change anything on either tab. Updating logs the customer as JSON in the browser console.',
    submit: 'Update customer',
    logLabel: 'Customer update:',
    toast: 'Customer updated. The JSON is in the browser console.'
  }
};
let dialogMode = 'add';

// Full months between a date (YYYY-MM-DD) and today; null when there is no date
function monthsSince(iso) {
  if (!iso) return null;
  const from = new Date(iso + 'T00:00:00');
  const now = new Date();
  let months = (now.getFullYear() - from.getFullYear()) * 12 + (now.getMonth() - from.getMonth());
  if (now.getDate() < from.getDate()) months -= 1;
  return Math.max(0, months);
}

// "Months with the bank" always follows "Customer since"
function syncMonthsWithBank(form) {
  const months = monthsSince(form.elements.customerSince.value);
  form.elements.monthsWithBank.value = months ?? '';
}

// Puts an existing customer record into the form
function fillForm(form, record) {
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
    onRegister: record.onRegister,
    avgMonthlyInflow6m: r.avgMonthlyInflow6m,
    outstandingAmount: r.outstandingAmount,
    overdueAmount: r.overdueAmount,
    maxOverdueDays12m: r.maxOverdueDays12m,
    preApprovedLimit: r.preApprovedLimit,
    kycPassed: r.kycPassed
  };
  Object.entries(values).forEach(([name, value]) => {
    const el = form.elements[name];
    if (el.type === 'checkbox') el.checked = Boolean(value);
    else el.value = value ?? '';
  });
  renderPayments(record.recentPayments);
}

// record = undefined opens an empty "Add customer" form; a record opens its details
function openDialog(record) {
  const form = $('#customer-form');
  dialogMode = record ? 'details' : 'add';
  const mode = DIALOG_MODES[dialogMode];

  form.reset();
  if (record) fillForm(form, record);
  else {
    form.elements.customerId.value = nextCustomerId();
    renderPayments([]);
  }
  syncMonthsWithBank(form);

  // The ID is the key for an existing customer, so it can't change in details mode
  const idField = form.elements.customerId;
  idField.readOnly = Boolean(record);
  idField.classList.toggle('bg-subtle', Boolean(record));
  idField.classList.toggle('text-muted', Boolean(record));

  $('#dialog-title').textContent = mode.title;
  $('#dialog-subtitle').textContent = mode.subtitle;
  $('#dialog-submit').textContent = mode.submit;

  showTab('profile');
  $('#customer-dialog').showModal();
  form.elements.name.focus();
}

// Builds the same shape as the mock data files, ready to send to the server later
function buildPayload(form) {
  const v = (name) => form.elements[name].value.trim();
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
    recentPayments: collectPayments(),
    riskFeatures: {
      customerId,
      avgMonthlyInflow6m: num('avgMonthlyInflow6m'),
      monthsWithBank: monthsSince(v('customerSince')),
      outstandingAmount: num('outstandingAmount') ?? 0,
      overdueAmount: num('overdueAmount') ?? 0,
      maxOverdueDays12m: num('maxOverdueDays12m') ?? 0,
      preApprovedLimit: num('preApprovedLimit'),
      kycPassed: form.elements.kycPassed.checked
    },
    onRegister: form.elements.onRegister.checked
  };
}

function handleSubmit(e) {
  e.preventDefault();
  const form = e.target;

  // A required field may sit on the hidden tab: jump to it before showing the error
  const invalid = [...form.elements].find((el) => el.willValidate && !el.checkValidity());
  if (invalid) {
    showTab(invalid.closest('[data-panel]').dataset.panel);
    invalid.reportValidity();
    return;
  }

  const mode = DIALOG_MODES[dialogMode];
  const payload = buildPayload(form);
  console.log(mode.logLabel, payload);
  console.log(JSON.stringify(payload, null, 2));

  $('#customer-dialog').close();
  toast(mode.toast);
}

let toastTimer;
function toast(message) {
  const el = $('#toast');
  $('#toast-text').textContent = message;
  el.classList.replace('opacity-0', 'opacity-100');
  el.classList.replace('translate-y-2', 'translate-y-0');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => {
    el.classList.replace('opacity-100', 'opacity-0');
    el.classList.replace('translate-y-0', 'translate-y-2');
  }, 2800);
}

/* ------------------------------------------------------------------ */
/* Wire everything up                                                  */
/* ------------------------------------------------------------------ */

function init() {
  $('[data-panel="profile"]').innerHTML = SECTIONS.profile.map(sectionHtml).join('');
  $('[data-panel="loan"]').innerHTML = SECTIONS.loan.map(sectionHtml).join('');

  renderCustomers();
  renderTenders();
  renderMatches();
  route();
  window.addEventListener('hashchange', route);

  $('#add-customer').addEventListener('click', () => openDialog());
  $$('[data-close]').forEach((b) => b.addEventListener('click', () => b.closest('dialog').close()));
  $$('[data-tab]').forEach((b) => b.addEventListener('click', () => showTab(b.dataset.tab)));
  $('#customer-form').addEventListener('submit', handleSubmit);
  $('#customer-form').elements.customerSince.addEventListener('input', (e) => syncMonthsWithBank(e.target.form));

  // "Details" opens the same modal, filled in. Later this can load the customer from the server.
  $('#customers-body').addEventListener('click', (e) => {
    const btn = e.target.closest('[data-details]');
    if (!btn) return;
    const record = customers.find((c) => c.customer.customerId === btn.dataset.details);
    if (record) openDialog(record);
  });

  // English / Uzbek switch for the tenders table
  $$('[data-tender-lang]').forEach((b) => b.addEventListener('click', () => {
    tenderLang = b.dataset.tenderLang;
    renderTenders();
  }));

  // Recent payments in the customer form: add, remove, and flag old ones as the date changes
  const profilePanel = $('[data-panel="profile"]');
  profilePanel.addEventListener('click', (e) => {
    if (e.target.closest('[data-add-payment]')) {
      $('[data-payments]').insertAdjacentHTML('beforeend', paymentRowHtml());
      $('[data-payments-empty]').hidden = true;
      $$('[data-payment-row]').at(-1).querySelector('[data-p="purpose"]').focus();
    }
    const remove = e.target.closest('[data-remove-payment]');
    if (remove) {
      remove.closest('[data-payment-row]').remove();
      $('[data-payments-empty]').hidden = $$('[data-payment-row]').length > 0;
    }
  });
  profilePanel.addEventListener('input', (e) => {
    if (e.target.matches('[data-p="date"]')) updatePaymentRow(e.target.closest('[data-payment-row]'));
  });

  // "Start matching" runs the demo: the mock matches appear one by one.
  // Pressing it again starts a fresh run, which replaces the previous results.
  $('#start-matching').addEventListener('click', startMatching);

  // "View" in the matches table opens the AI comments for that match
  $('#matches-body').addEventListener('click', (e) => {
    const btn = e.target.closest('[data-comments]');
    if (btn) openComments(Number(btn.dataset.comments));
  });

  // Close either modal when clicking the dimmed backdrop
  $$('dialog').forEach((d) => d.addEventListener('click', (e) => {
    if (e.target === e.currentTarget) e.currentTarget.close();
  }));

  // Intentionally no-ops for the demo:
  // - "Delete" buttons in the customers table (data-delete)
  // - "Load tenders" (#load-tenders) and "Clear" (#clear-tenders)
  // - "Clear" on the matches page (#clear-matches): later deletes the saved results (not customers' notifications)
  // - Source links in the tenders table (data-source). Later:
  //     const t = tenders.find((x) => x.lotId === btn.dataset.source);
  //     if (t?.sourceUrl) window.open(t.sourceUrl, '_blank', 'noopener');
}

init();
