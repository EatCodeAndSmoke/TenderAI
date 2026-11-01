import { LANGUAGES, REGIONS } from './customers.js';

// Customer modal form. Tab 1 = profile (non-loan data), tab 2 = loan and risk
export const TABS = [['profile', 'Profile'], ['loan', 'Loan and risk']];

export const SECTIONS = {
  profile: [
    {
      title: 'Company',
      fields: [
        { name: 'customerId', label: 'Customer ID', required: true },
        { name: 'name', label: 'Company name', required: true, placeholder: 'Non Baraka LLC', autofocus: true },
        { name: 'taxCode', label: 'TIN (STIR)', required: true, pattern: '\\d{9}', inputMode: 'numeric', placeholder: '9 digits' },
        { name: 'type', label: 'Legal form', type: 'select', options: ['LLC', 'Sole proprietor', 'JSC', 'Private enterprise'] },
        { name: 'registrationDate', label: 'Registration date', type: 'date' },
        { name: 'customerSince', label: 'Customer since', type: 'date', required: true }
      ]
    },
    {
      title: 'What they sell',
      description: 'The matcher compares this with tender descriptions.',
      fields: [
        { name: 'industryCode', label: 'OKED code', required: true, inputMode: 'numeric', placeholder: '10710' },
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

// Text for the two ways the modal is used
export const DIALOG_MODES = {
  add: {
    title: 'Add customer',
    subtitle: 'Fill in the profile, then the loan and risk details.',
    submit: 'Save customer',
    busy: 'Saving…',
    toast: 'Customer saved.'
  },
  details: {
    title: 'Customer details',
    subtitle: 'Change anything on either tab, then update the customer.',
    submit: 'Update customer',
    busy: 'Updating…',
    toast: 'Customer updated.'
  }
};
