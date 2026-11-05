import tbcUzLogo from '../assets/tbc-uz-logo.jpg';

const stroke = {
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeLinecap: 'round',
  strokeLinejoin: 'round',
  'aria-hidden': true
};

export const LogoIcon = ({ className = 'size-9' }) => (
  <span className={`relative block shrink-0 overflow-hidden rounded-xl bg-[#110c30] ${className}`} aria-hidden="true">
    <img src={tbcUzLogo} alt="" className="absolute left-1/2 top-1/2 h-[190%] w-[190%] max-w-none -translate-x-1/2 -translate-y-1/2" />
  </span>
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
