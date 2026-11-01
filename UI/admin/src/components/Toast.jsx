import { useCallback, useEffect, useRef, useState } from 'react';

// Confirmation toast in the bottom-right corner
export function useToast() {
  const [state, setState] = useState({ message: '', visible: false });
  const timer = useRef();

  const show = useCallback((message) => {
    setState({ message, visible: true });
    clearTimeout(timer.current);
    timer.current = setTimeout(() => setState((s) => ({ ...s, visible: false })), 2800);
  }, []);

  useEffect(() => () => clearTimeout(timer.current), []);

  return [state, show];
}

export default function Toast({ message, visible }) {
  return (
    <div
      role="status"
      aria-live="polite"
      className={`pointer-events-none fixed right-6 bottom-6 z-50 flex items-center gap-2.5 rounded-xl bg-cobalt px-4 py-3 text-sm text-white shadow-lg transition duration-200 ${visible ? 'translate-y-0 opacity-100' : 'translate-y-2 opacity-0'}`}
    >
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="size-4 text-brand-300" aria-hidden="true">
        <path d="M5 12.5l4.5 4.5L19 7" />
      </svg>
      <span>{message}</span>
    </div>
  );
}
