export function applicationUpdateContent(outcome) {
  if (outcome === 'sent' || outcome === 'needs_details') return {
    title: 'Your application was sent',
    detail: 'You can return to the opportunity to review your application details.'
  };
  return {
    title: 'Update unavailable',
    detail: 'We couldn’t show this update. Please try again later.'
  };
}
