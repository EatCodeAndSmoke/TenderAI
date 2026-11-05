export function matchScore(match) {
  const score = match?.matchScore;
  return Number.isInteger(score) && score >= 1 && score <= 100 ? score : null;
}

export function matchCategory(match) {
  const score = matchScore(match);
  if (score === null || score < 75) return null;
  if (score >= 85) return { label: 'Best match', tone: 'best' };
  return { label: 'Might interest you', tone: 'explore' };
}

export function sortTenderResults(results) {
  return results.filter(item => matchCategory(item.match))
    .sort((a, b) => matchScore(b.match) - matchScore(a.match));
}

export function prependMatch(results, match) {
  return sortTenderResults([match, ...results.filter(item => item.tender.id !== match.tender.id)]);
}
