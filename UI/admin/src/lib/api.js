export class ApiError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
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
  try { body = text ? JSON.parse(text) : null; } catch { /* Proxy errors may not be JSON. */ }
  if (!response.ok) {
    const fieldErrors = body?.errors ? Object.values(body.errors).flat() : [];
    throw new ApiError(response.status, fieldErrors.join(' ') || body?.title || text || `The server returned ${response.status}.`);
  }
  return body;
}

export const getCustomers = () => request('/customers');
export const generateCustomer = scenario => request(`/customers/generate?scenario=${encodeURIComponent(scenario)}`, { method: 'POST' });
export const deleteCustomer = customerId => request(`/customers/${encodeURIComponent(customerId)}`, { method: 'DELETE' });
export const clearCustomers = () => request('/customers', { method: 'DELETE' });
export const getTenders = () => request('/tenders?includeExpired=true');
export const deleteTender = id => request(`/tenders/${id}`, { method: 'DELETE' });
export const getPrompts = () => request('/prompts');
export const updatePrompt = (key, instructions, revision) => request(`/prompts/${encodeURIComponent(key)}`, {
  method: 'PUT',
  body: JSON.stringify({ instructions, revision })
});
