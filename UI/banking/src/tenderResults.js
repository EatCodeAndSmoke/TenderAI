export function prependMatch(results, match) {
  return [match, ...results.filter(item => item.tender.id !== match.tender.id)];
}
