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

export const getAccounts = customerId => request(`/customers/${encodeURIComponent(customerId)}/accounts`);
export const getMatches = customerId => request(`/customers/${encodeURIComponent(customerId)}/matches`);
export const getTenders = () => request('/tenders');

export async function stream(path, onEvent, signal) {
  let response;
  try { response = await fetch(`/api${path}`, { method: 'POST', headers: { Accept: 'text/event-stream' }, signal }); }
  catch (err) {
    if (err.name === 'AbortError') throw err;
    throw new ApiError(0, "Can't reach the server. Check that the API is running.");
  }
  if (!response.ok) {
    let body;
    try { body = await response.json(); } catch { body = null; }
    throw new ApiError(response.status, body?.title || `The server returned ${response.status}.`);
  }
  if (!response.body) throw new ApiError(response.status, 'The server did not start a result stream.');
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '', completed = false;
  function dispatch(block) {
    const data = block.split('\n').filter(line => line.startsWith('data:')).map(line => line.slice(5).trimStart()).join('\n');
    if (!data) return;
    let event;
    try { event = JSON.parse(data); } catch { throw new ApiError(response.status, 'The server sent an invalid event.'); }
    if (event.type === 'error') throw new ApiError(response.status, event.message || 'The stream stopped.');
    if (event.type === 'customer-error') throw new ApiError(response.status, event.error || 'Matching failed.');
    if (event.type === 'completed') completed = true;
    onEvent(event);
  }
  try {
    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });
      buffer = buffer.replace(/\r\n/g, '\n');
      let end;
      while ((end = buffer.indexOf('\n\n')) >= 0) { dispatch(buffer.slice(0, end)); buffer = buffer.slice(end + 2); }
    }
    buffer += decoder.decode();
    if (buffer.trim()) dispatch(buffer);
  } finally { reader.releaseLock(); }
  if (!completed) throw new ApiError(response.status, 'The stream ended before completion.');
}
