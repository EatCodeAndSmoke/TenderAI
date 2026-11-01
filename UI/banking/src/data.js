// Loan products for the "Request a loan" page.
// Customers and notifications come from the TenderAI API (see api.js).

export const LOAN_PRODUCTS = {
  financing: { name: 'Contract financing', limitKey: 'financing', hasTerm: true },
  guarantee: { name: 'Bid guarantee', limitKey: 'guarantee', hasTerm: false }
};

export const LOAN_TERMS = [3, 6, 9, 12];
