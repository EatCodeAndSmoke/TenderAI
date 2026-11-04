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

export const BellIcon = ({ className = 'size-6' }) => (
  <svg {...stroke} strokeWidth="1.7" className={className}>
    <path d="M6 9.5a6 6 0 0 1 12 0c0 4.5 1.8 6 1.8 6H4.2S6 14 6 9.5z" /><path d="M10 19a2.2 2.2 0 0 0 4 0" />
  </svg>
);

export const BackIcon = ({ className = 'size-4' }) => (
  <svg {...stroke} strokeWidth="2" className={className}><path d="M15 6l-6 6 6 6" /></svg>
);

export const CheckIcon = ({ className = 'mt-0.5 size-4 shrink-0 text-brand-600' }) => (
  <svg {...stroke} strokeWidth="2.2" className={className}><path d="M5 12.5l4.5 4.5L19 7" /></svg>
);
