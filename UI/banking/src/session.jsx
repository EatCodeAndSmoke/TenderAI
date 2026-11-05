import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { getApplicationUpdates, getMatches, markApplicationUpdatesRead } from './api.js';
import { matchCategory } from './tenderResults.js';

const SessionContext = createContext(null);
const STORAGE_KEY = 'tenderai.banking.session';
function loadStored() {
  try {
    const saved = JSON.parse(localStorage.getItem(STORAGE_KEY));
    return typeof saved?.customer?.customerId === 'string' ? saved.customer : null;
  } catch { return null; }
}
export function SessionProvider({ children }) {
  const [customer, setCustomer] = useState(loadStored);
  const currentId = useRef(customer?.customerId ?? null);
  const [matches, setMatches] = useState([]);
  const [matchesLoading, setMatchesLoading] = useState(() => !!customer);
  const [matchesError, setMatchesError] = useState('');
  const [updates, setUpdates] = useState([]);
  const [updatesError, setUpdatesError] = useState('');
  const [updatesLoading, setUpdatesLoading] = useState(() => !!customer);
  useEffect(() => {
    try {
      if (customer) localStorage.setItem(STORAGE_KEY, JSON.stringify({ customer }));
      else localStorage.removeItem(STORAGE_KEY);
    } catch { /* The session remains available until refresh. */ }
  }, [customer]);
  const refreshMatches = useCallback(async () => {
    if (!customer) return;
    setMatchesLoading(true);
    const id = customer.customerId;
    try {
      const result = await getMatches(id);
      if (currentId.current === id) { setMatches(result.filter(matchCategory)); setMatchesError(''); }
    }
    catch (err) { if (currentId.current === id) setMatchesError(err.message); throw err; }
    finally { if (currentId.current === id) setMatchesLoading(false); }
  }, [customer]);
  useEffect(() => {
    if (customer) refreshMatches().catch(() => {});
  }, [customer, refreshMatches]);
  const refreshUpdates = useCallback(async () => {
    if (!customer) return;
    const id = customer.customerId;
    try {
      const result = await getApplicationUpdates(id);
      if (currentId.current === id) { setUpdates(result); setUpdatesError(''); }
    } catch (err) {
      if (currentId.current === id) setUpdatesError(err.message);
      throw err;
    } finally {
      if (currentId.current === id) setUpdatesLoading(false);
    }
  }, [customer]);
  useEffect(() => {
    if (!customer) return;
    let active = true;
    let timer;
    async function poll() {
      try { await refreshUpdates(); } catch { /* The error is exposed through updatesError. */ }
      if (active) timer = setTimeout(poll, 3000);
    }
    poll();
    return () => { active = false; clearTimeout(timer); };
  }, [customer, refreshUpdates]);
  const markUpdatesRead = useCallback(async tenderIds => {
    if (!customer) return;
    await markApplicationUpdatesRead(customer.customerId, tenderIds);
    await refreshUpdates();
  }, [customer, refreshUpdates]);
  const login = useCallback(value => { currentId.current = value.customerId; setMatches([]); setUpdates([]); setUpdatesError(''); setUpdatesLoading(true); setMatchesLoading(true); setCustomer(value); }, []);
  const logout = useCallback(() => { currentId.current = null; setCustomer(null); setMatches([]); setMatchesError(''); setUpdates([]); setUpdatesError(''); setUpdatesLoading(false); }, []);
  const rememberMatch = useCallback(match => setMatches(previous => [
    ...previous.filter(m => m.lotId !== match.lotId),
    ...(matchCategory(match) ? [match] : [])
  ]), []);
  const forgetMatch = useCallback(lotId => setMatches(previous => previous.filter(m => m.lotId !== lotId)), []);
  const value = useMemo(() => ({
    customerId: customer?.customerId ?? null, customerName: customer?.name ?? '',
    matches, matchesLoading, matchesError, refreshMatches, rememberMatch, forgetMatch,
    updates, updatesError, updatesLoading, refreshUpdates, markUpdatesRead, login, logout
  }), [customer, matches, matchesLoading, matchesError, refreshMatches, rememberMatch, forgetMatch,
    updates, updatesError, updatesLoading, refreshUpdates, markUpdatesRead, login, logout]);
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}
export const useSession = () => useContext(SessionContext);
