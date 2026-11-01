// Calls to the TenderAI API. In development Vite forwards /api to http://localhost:5240.

export class ApiError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

// Turns an RFC 7807 problem response into one readable message
function problemMessage(status, body) {
  const fieldErrors = body?.errors ? Object.values(body.errors).flat() : [];
  if (fieldErrors.length) return fieldErrors.join(' ');
  if (body?.title) return body.title;
  return status >= 500
    ? "The server isn't responding. Check that the API is running and try again."
    : `The server returned ${status}.`;
}

async function request(path, options = {}) {
  let response;
  try {
    response = await fetch(`/api${path}`, {
      ...options,
      headers: { 'Content-Type': 'application/json', ...options.headers }
    });
  } catch {
    throw new ApiError(0, "Can't reach the server. Check that the API is running and try again.");
  }

  const text = await response.text();
  let body = null;
  try {
    body = text ? JSON.parse(text) : null;
  } catch {
    // Not JSON, e.g. the dev server's proxy error when the API is down
  }
  if (!response.ok) throw new ApiError(response.status, problemMessage(response.status, body));
  return body;
}

const json = (method, data) => ({ method, body: JSON.stringify(data) });

// Records have the shape { customer, riskFeatures, recentPayments, onRegister }
export const getCustomers = () => request('/customers');

export const addCustomer = (record) => request('/customers', json('POST', record));

export const generateCustomers = () => request('/customers/generate', { method: 'POST' });

export const updateCustomer = (record) =>
  request(`/customers/${encodeURIComponent(record.customer.customerId)}`, json('PUT', record));

export const deleteCustomer = (customerId) =>
  request(`/customers/${encodeURIComponent(customerId)}`, { method: 'DELETE' });

// Removes every customer, with their payments and notifications
export const clearCustomers = () => request('/customers', { method: 'DELETE' });

// Tenders loaded from UZEX, nearest deadline first
export const getTenders = () => request('/tenders');

// Loads up to count lots that aren't in the database yet and returns only those (empty when none are new)
export const loadTenders = (count = 5) => request(`/tenders/load?count=${count}`, { method: 'POST' });

// Removes every tender, with its products
export const clearTenders = () => request('/tenders', { method: 'DELETE' });
