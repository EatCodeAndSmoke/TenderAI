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
  if (failed) return 'We couldn’t finish this search. Your saved opportunities are still here.';
  if (progress.completed) {
    if (progress.limited) return 'We’ve checked the maximum for this search. Search again to explore more.';
    if (progress.timeLimited) return 'We’ve found an opportunity for you. Search again to explore more.';
    if (progress.found === 0) return 'No new matches this time. Try again later for new opportunities.';
    return 'Your search is complete. Take a look at your new opportunities below.';
  }
  return {
    starting: 'Getting your search ready…',
    loading: 'Discovering opportunities on UZEX…',
    matching: 'Finding opportunities that fit your business…',
    summarizing: 'Putting the details together for you…'
  }[progress.phase] ?? 'Looking for your next opportunity…';
}
