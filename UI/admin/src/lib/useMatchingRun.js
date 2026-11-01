import { useCallback, useEffect, useState } from 'react';

const FIRST_ROW_DELAY = 1200; // ms before the first match appears
const NEXT_ROW_DELAY = 700;   // ms between matches

// Demo run: idle (nothing yet) -> running (rows appear one by one) -> done.
// Pretends to run the agents by revealing the mock matches one at a time.
// Later this calls the server to start a full run and polls for saved matches instead.
export function useMatchingRun(total) {
  const [run, setRun] = useState({ status: 'idle', startedAt: null, shown: 0 });

  useEffect(() => {
    if (run.status !== 'running') return undefined;
    const timer = setTimeout(() => {
      setRun((r) => {
        const shown = r.shown + 1;
        return { ...r, shown, status: shown >= total ? 'done' : 'running' };
      });
    }, run.shown === 0 ? FIRST_ROW_DELAY : NEXT_ROW_DELAY);
    return () => clearTimeout(timer);
  }, [run, total]);

  // Starting again replaces the previous results
  const start = useCallback(() => setRun({ status: 'running', startedAt: new Date(), shown: 0 }), []);

  return [run, start];
}
