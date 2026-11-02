import { useCallback, useEffect, useState } from 'react';
import { clearMatches, getMatches, runMatching } from './api.js';

export function useMatchingRun() {
  const [matches, setMatches] = useState([]);
  const [status, setStatus] = useState('loading');
  const [startedAt, setStartedAt] = useState(null);
  const [error, setError] = useState('');
  const [progress, setProgress] = useState({ completed: 0, total: 0, failed: 0 });

  const refresh = useCallback(async () => {
    setStatus('loading');
    setError('');
    try {
      setMatches(await getMatches());
      setStatus('ready');
    } catch (err) {
      setError(err.message);
      setStatus('error');
    }
  }, []);

  useEffect(() => { refresh(); }, [refresh]);

  const start = useCallback(async () => {
    setStatus('running');
    setError('');
    setProgress({ completed: 0, total: 0, failed: 0 });
    let failedCount = 0;
    try {
      await runMatching((event) => {
        if (event.type === 'started') {
          setProgress({ completed: 0, total: event.totalCustomers, failed: 0 });
        } else if (event.type === 'customer-completed') {
          setMatches((previous) => [
            ...previous.filter((match) => match.customerId !== event.customerId),
            ...(event.matches || [])
          ]);
          setProgress({
            completed: event.completedCustomers,
            total: event.totalCustomers,
            failed: event.failedCustomers
          });
        } else if (event.type === 'customer-error') {
          failedCount += 1;
          setProgress({
            completed: event.completedCustomers,
            total: event.totalCustomers,
            failed: event.failedCustomers
          });
        }
      });
      setStartedAt(new Date());
      if (failedCount > 0) {
        setError(`${failedCount} customer ${failedCount === 1 ? 'evaluation failed' : 'evaluations failed'}. Previous results for those customers were kept.`);
      }
      setStatus('ready');
    } catch (err) {
      setError(err.message);
      setStatus('ready');
    }
  }, []);

  const clear = useCallback(async () => {
    setError('');
    try {
      await clearMatches();
      setMatches([]);
      setStartedAt(null);
      setStatus('ready');
    } catch (err) {
      setError(err.message);
    }
  }, []);

  return { matches, status, startedAt, error, progress, refresh, start, clear };
}
