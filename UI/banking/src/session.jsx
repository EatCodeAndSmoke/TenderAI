import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { getMatches } from './api.js';

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
      if (currentId.current === id) { setMatches(result); setMatchesError(''); }
    }
    catch (err) { if (currentId.current === id) setMatchesError(err.message); throw err; }
    finally { if (currentId.current === id) setMatchesLoading(false); }
  }, [customer]);
  useEffect(() => {
    if (customer) refreshMatches().catch(() => {});
  }, [customer, refreshMatches]);
  const login = useCallback(value => { currentId.current = value.customerId; setMatches([]); setMatchesLoading(true); setCustomer(value); }, []);
  const logout = useCallback(() => { currentId.current = null; setCustomer(null); setMatches([]); setMatchesError(''); }, []);
  const value = useMemo(() => ({
    customerId: customer?.customerId ?? null, customerName: customer?.name ?? '',
    matches, matchesLoading, matchesError, refreshMatches, login, logout
  }), [customer, matches, matchesLoading, matchesError, refreshMatches, login, logout]);
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}
export const useSession = () => useContext(SessionContext);
