// Calls to the TenderAI API. In development Vite forwards /api to http://localhost:5240.

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
  if (!response.ok) {
    const fallback = response.status >= 500
      ? "The server isn't responding. Check that the API is running and try again."
      : `The server returned ${response.status}.`;
    throw new ApiError(response.status, body?.title || fallback);
  }
  return body;
}

export class ApiError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

// Returns { customerId, name }; throws ApiError with status 401 for a wrong ID or password
export const authenticate = (customerId, password) =>
  request('/customers/auth', { method: 'POST', body: JSON.stringify({ customerId, password }) });

export const getNotifications = (customerId) =>
  request(`/customers/${encodeURIComponent(customerId)}/notifications`);
