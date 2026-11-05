export const initialTenderProgress = () => ({ checked: 0, found: 0, phase: 'starting', completed: false, limited: false });

export function updateTenderProgress(previous, event) {
  if (!['progress', 'match', 'completed'].includes(event.type)) return previous;
  return {
    checked: event.checked ?? previous.checked,
    found: event.count ?? previous.found,
    phase: event.phase ?? previous.phase,
    completed: event.type === 'completed',
    limited: event.type === 'completed' && !!event.limited
  };
}

export function tenderProgressMessage(progress, failed) {
  if (failed) return 'Search stopped. Your saved tenders are still available.';
  if (progress.completed) return progress.limited
    ? 'Search paused after the import limit. Search again to check more tenders.'
    : 'Search complete.';
  return {
    starting: 'Preparing your search…',
    loading: 'Loading opportunities from UZEX…',
    matching: 'Comparing tenders with your business…',
    summarizing: 'Preparing a matching tender…'
  }[progress.phase] ?? 'Searching for opportunities…';
}
