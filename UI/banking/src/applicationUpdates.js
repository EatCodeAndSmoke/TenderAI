export function applicationUpdateContent(outcome) {
  if (outcome === 'sent') return {
    title: 'Your demo application was sent',
    detail: 'In this simulation, your information has been sent to UZEX. Nothing was actually submitted.'
  };
  if (outcome === 'needs_details') return {
    title: 'A few more details would be needed',
    detail: 'In this simulation, you would need to provide a business registration document, a price proposal, and a contact name and phone number. These are examples; no files or information are collected.'
  };
  return {
    title: 'Demo update unavailable',
    detail: 'This simulated result could not be recognized. Please try again later.'
  };
}
