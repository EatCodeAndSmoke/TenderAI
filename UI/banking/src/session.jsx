import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { getNotifications } from './api.js';

const SessionContext = createContext(null);

// The session survives page refreshes until the customer logs out
const STORAGE_KEY = 'tenderai.banking.session';

function loadStored() {
  try {
    const saved = JSON.parse(localStorage.getItem(STORAGE_KEY));
    if (typeof saved?.customer?.customerId !== 'string') return null;
    return {
      customer: saved.customer,
      notifications: Array.isArray(saved.notifications) ? saved.notifications : [],
      read: new Set(Array.isArray(saved.read) ? saved.read : [])
    };
  } catch {
    return null;
  }
}

function store(session) {
  try {
    if (session) localStorage.setItem(STORAGE_KEY, JSON.stringify({ ...session, read: [...session.read] }));
    else localStorage.removeItem(STORAGE_KEY);
  } catch {
    // Storage full or blocked: the session still works until the page is refreshed
  }
}

// Who is logged in, their notifications from the API, and which ones they have seen.
// Kept in localStorage, so a refresh doesn't log the customer out.
export function SessionProvider({ children }) {
  const [stored] = useState(loadStored);
  // { customerId, name } from the API, or null when logged out
  const [customer, setCustomer] = useState(stored?.customer ?? null);
  const [notifications, setNotifications] = useState(stored?.notifications ?? []);
  const [read, setRead] = useState(() => stored?.read ?? new Set());
  // Bumping this restarts the bell animation
  const [ringKey, setRingKey] = useState(0);

  const currentId = useRef(customer?.customerId ?? null);

  useEffect(() => {
    currentId.current = customer?.customerId ?? null;
    store(customer ? { customer, notifications, read } : null);
  }, [customer, notifications, read]);

  const login = useCallback((loggedIn, loaded) => {
    setCustomer(loggedIn);
    setNotifications(loaded);
    setRead(new Set());
  }, []);

  const logout = useCallback(() => {
    setCustomer(null);
    setNotifications([]);
    setRead(new Set());
  }, []);

  // After a refresh, show the saved notifications straight away and fetch fresh ones.
  // If the customer no longer exists, log out; if the API is down, keep the saved ones.
  useEffect(() => {
    const restoredId = stored?.customer.customerId;
    if (!restoredId) return;
    getNotifications(restoredId)
      .then((fresh) => {
        if (currentId.current === restoredId) setNotifications(fresh);
      })
      .catch((err) => {
        if (err.status === 404 && currentId.current === restoredId) logout();
      });
  }, [stored, logout]);

  const markRead = useCallback((ids) => {
    setRead((prev) => {
      if (ids.every((id) => prev.has(id))) return prev;
      const next = new Set(prev);
      ids.forEach((id) => next.add(id));
      return next;
    });
  }, []);

  const ringBell = useCallback(() => setRingKey((k) => k + 1), []);

  const value = useMemo(() => ({
    customerId: customer?.customerId ?? null,
    customerName: customer?.name ?? '',
    notifications,
    read,
    unreadCount: notifications.filter((n) => !read.has(n.id)).length,
    ringKey,
    login,
    logout,
    markRead,
    ringBell
  }), [customer, notifications, read, ringKey, login, logout, markRead, ringBell]);

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export const useSession = () => useContext(SessionContext);
