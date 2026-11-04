const stroke = {
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeLinecap: 'round',
  strokeLinejoin: 'round',
  'aria-hidden': true
};

export const LogoIcon = ({ className = 'size-6' }) => (
  <svg viewBox="0 0 24 24" fill="currentColor" className={className} aria-hidden="true">
    <path d="M6 6h12v12H6z" /><path d="M12 3.5 20.5 12 12 20.5 3.5 12z" />
  </svg>
);

export const BuildingIcon = ({ className = 'size-5 shrink-0' }) => (
  <svg {...stroke} strokeWidth="1.6" className={className}>
    <rect x="4.5" y="3" width="15" height="18" rx="1.5" />
    <path d="M9 7.5h1.5M13.5 7.5H15M9 11.5h1.5M13.5 11.5H15M9 15.5h1.5M13.5 15.5H15M10.5 21v-2.5h3V21" />
  </svg>
);
