// Mock matching results: what the agents save after a run.
// In the real product these rows come from the save_match (tender matcher) and
// save_loan_offer (loan advisor) tools. Every rule check carries the actual value.
// In the demo they appear one by one after "Start matching" is pressed (see useMatchingRun).

// Loan advisor rule checks per customer (demo thresholds from loan-policy.md): [rule, actual value, passed]
export const CHECKS = {
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

export const matches = [
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
