export function selectInitialAdminRoute(location, history) {
  const url = `${location.pathname}${location.search}#/customers`;
  history.replaceState(history.state, '', url);
  return url;
}
