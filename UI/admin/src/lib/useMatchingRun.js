import { useCallback, useEffect, useState } from 'react';
import { clearMatches, getMatches, runMatching } from './api.js';

export function useMatchingRun() {
  const [matches, setMatches] = useState([]);
  const [status, setStatus] = useState('loading');
  const [startedAt, setStartedAt] = useState(null);
  const [error, setError] = useState('');

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
    try {
      setMatches(await runMatching());
      setStartedAt(new Date());
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

  return { matches, status, startedAt, error, refresh, start, clear };
}
