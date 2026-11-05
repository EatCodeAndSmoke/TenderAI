export const initialTenderProgress = () => ({ checked: 0, found: 0, phase: 'starting', completed: false, limited: false, timeLimited: false });

export function updateTenderProgress(previous, event) {
  if (!['progress', 'match', 'completed'].includes(event.type)) return previous;
  return {
    checked: event.checked ?? previous.checked,
    found: event.count ?? previous.found,
    phase: event.phase ?? previous.phase,
    completed: event.type === 'completed',
    limited: event.type === 'completed' && !!event.limited,
    timeLimited: event.type === 'completed' && !!event.timeLimited
  };
}

export function tenderProgressMessage(progress, failed) {
  if (failed) return 'Search stopped. Your saved tenders are still available.';
  if (progress.completed) {
    if (progress.limited) return 'Search paused after the import limit. Search again to check more tenders.';
    if (progress.timeLimited) return 'Search paused after finding a match. Search again to check more tenders.';
    return 'Search complete.';
  }
  return {
    starting: 'Preparing your search…',
    loading: 'Loading opportunities from UZEX…',
    matching: 'Comparing tenders with your business…',
    summarizing: 'Preparing a matching tender…'
  }[progress.phase] ?? 'Searching for opportunities…';
}
