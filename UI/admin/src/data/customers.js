// Mock customers for the Matches page only: matching isn't on the API yet.
// The Customers page loads and saves customers through the API (lib/api.js).
// Same shape as GET /api/customers.

export const customers = [
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

export const customerById = (id) => customers.find((c) => c.customer.customerId === id)?.customer;

export const REGIONS = [
  'Tashkent', 'Tashkent region', 'Andijan', 'Bukhara', 'Fergana', 'Jizzakh', 'Karakalpakstan',
  'Kashkadarya', 'Khorezm', 'Namangan', 'Navoi', 'Samarkand', 'Surkhandarya', 'Syrdarya'
];

export const LANGUAGES = [['en', 'English'], ['uz', 'Uzbek'], ['ru', 'Russian']];
export const LANGUAGE_NAMES = Object.fromEntries(LANGUAGES);
