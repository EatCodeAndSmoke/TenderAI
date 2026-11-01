export function selectInitialAdminRoute(location, history) {
  const url = `${location.pathname}${location.search}#/tenders`;
  history.replaceState(history.state, '', url);
  return url;
}
