import { formatDate } from '../lib/format.js';

// Mock tenders for the Matches page (and its comments dialog) until matching runs on the API.
// The Tenders page loads real lots from the API.
export const tenders = [
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

export const tenderById = (id) => tenders.find((t) => t.lotId === id);

// A tender's text in the chosen language. UZEX publishes the original text (Uzbek, often with Russian);
// until a translation exists, every language shows the original.
export const tenderText = (item, lang, field) => item.translations?.[lang]?.[field] ?? item[field];

export const tenderRequirements = (tender, lang) => {
  const translated = tender.translations?.[lang]?.requirements;
  return translated?.length ? translated : tender.requirements;
};

// Tender table text in each language
const UZ_MONTHS = ['yanvar', 'fevral', 'mart', 'aprel', 'may', 'iyun', 'iyul', 'avgust', 'sentabr', 'oktabr', 'noyabr', 'dekabr'];

export const TENDER_LANGS = [['en', 'English'], ['uz', "O'zbekcha"]];

// Dates from UZEX are Tashkent local time without an offset: "2026-10-19T10:14:20"
const withTime = (date) => (iso) => `${date(iso.slice(0, 10))}, ${iso.slice(11, 16)}`;

const enDate = (iso) => formatDate(iso);
const uzDate = (iso) => {
  const d = new Date(iso + 'T00:00:00');
  return `${d.getDate()}-${UZ_MONTHS[d.getMonth()]} ${d.getFullYear()}`;
};

export const TENDER_TEXT = {
  en: {
    headers: ['Lot', 'Tender', 'Region', 'Start price', 'Deposit', 'Payment terms', 'Deadline', 'Source', 'Details'],
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
      buyer: 'Buyer',
      tin: 'TIN',
      procedure: 'Procedure',
      startPrice: 'Start price',
      deposit: 'Bid deposit',
      bankGuarantee: 'Bank guarantee accepted',
      yes: 'Yes',
      no: 'No',
      payment: 'Payment',
      financing: 'Financing',
      delivery: 'Delivery',
      languages: 'Bid languages',
      period: 'Bidding period',
      products: 'What the buyer wants',
      product: 'Product',
      code: 'Code',
      quantity: 'Quantity',
      price: 'Unit price',
      cost: 'Total',
      deliveryTerm: 'Delivery',
      warranty: 'Warranty',
      requirements: 'Requirements for bidders',
      description: 'Description',
      close: 'Close'
    },
    languageNames: { ru: 'Russian', uz: 'Uzbek', 'uz-Cyrl': 'Uzbek (Cyrillic)', en: 'English' }
  },
  uz: {
    headers: ['Lot', 'Tender', 'Hudud', "Boshlang'ich narx", 'Zakalat', "To'lov muddati", 'Oxirgi muddat', 'Manba', 'Tafsilotlar'],
    days: (n) => `${n} kun`,
    inDays: (n) => `${n} kundan keyin`,
    advance: (p) => `${p}% avans`,
    today: 'Bugun yopiladi',
    closed: 'Yopilgan',
    openSource: 'Lotni UZEX saytida ochish',
    details: 'Tafsilotlar',
    date: uzDate,
    dateTime: withTime(uzDate),
    detail: {
      lot: 'Lot',
      buyer: 'Buyurtmachi',
      tin: 'STIR',
      procedure: 'Xarid turi',
      startPrice: "Boshlang'ich narx",
      deposit: 'Zakalat',
      bankGuarantee: 'Bank kafolati qabul qilinadi',
      yes: 'Ha',
      no: "Yo'q",
      payment: "To'lov",
      financing: 'Moliyalashtirish manbai',
      delivery: 'Yetkazib berish',
      languages: 'Taklif tillari',
      period: 'Takliflar qabul qilish muddati',
      products: 'Xarid qilinadigan mahsulotlar',
      product: 'Mahsulot',
      code: 'Kod',
      quantity: 'Miqdori',
      price: 'Birlik narxi',
      cost: 'Jami',
      deliveryTerm: 'Yetkazib berish',
      warranty: 'Kafolat',
      requirements: 'Ishtirokchilarga talablar',
      description: 'Tavsif',
      close: 'Yopish'
    },
    languageNames: { ru: 'Rus', uz: "O'zbek", 'uz-Cyrl': "O'zbek (kirill)", en: 'Ingliz' }
  }
};
