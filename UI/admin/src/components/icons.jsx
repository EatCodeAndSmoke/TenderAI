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

export const DocIcon = ({ className = 'size-5 shrink-0' }) => (
  <svg {...stroke} strokeWidth="1.6" className={className}>
    <path d="M6 3h8.5L19 7.5V21H6z" /><path d="M14 3v5h5M9 12.5h7M9 16.5h7" />
  </svg>
);

export const LinkIcon = ({ className = 'size-5 shrink-0' }) => (
  <svg {...stroke} strokeWidth="1.6" className={className}>
    <circle cx="7" cy="12" r="3.5" /><circle cx="17" cy="12" r="3.5" /><path d="M10.5 12h3" />
  </svg>
);

export const PlusIcon = ({ className = 'size-4' }) => (
  <svg {...stroke} strokeWidth="2" className={className}><path d="M12 5v14M5 12h14" /></svg>
);

export const XIcon = ({ className = 'size-4', strokeWidth = 2 }) => (
  <svg {...stroke} strokeWidth={strokeWidth} className={className}><path d="M7 7l10 10M17 7 7 17" /></svg>
);

export const CloseIcon = ({ className = 'size-5' }) => (
  <svg {...stroke} strokeWidth="1.8" className={className}><path d="M6 6l12 12M18 6 6 18" /></svg>
);

export const RefreshIcon = ({ className = 'size-4' }) => (
  <svg {...stroke} strokeWidth="2" className={className}>
    <path d="M4.5 12a7.5 7.5 0 0 1 13-5.1M19.5 12a7.5 7.5 0 0 1-13 5.1M17.5 3.5v3.5H14M6.5 20.5V17H10" />
  </svg>
);

export const PlayIcon = ({ className = 'size-4' }) => (
  <svg viewBox="0 0 24 24" fill="currentColor" className={className} aria-hidden="true">
    <path d="M8 5.5v13a1 1 0 0 0 1.5.86l10.5-6.5a1 1 0 0 0 0-1.72L9.5 4.64A1 1 0 0 0 8 5.5z" />
  </svg>
);

export const SpinnerIcon = ({ className = 'size-4' }) => (
  <svg viewBox="0 0 24 24" fill="none" className={`${className} animate-spin motion-reduce:animate-none`} aria-hidden="true">
    <circle cx="12" cy="12" r="9" stroke="currentColor" strokeOpacity="0.25" strokeWidth="2.5" />
    <path d="M21 12a9 9 0 0 0-9-9" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" />
  </svg>
);

export const SparkIcon = ({ className = 'size-4' }) => (
  <svg {...stroke} strokeWidth="1.8" className={className}>
    <path d="M12 3.5l1.8 4.7 4.7 1.8-4.7 1.8L12 16.5l-1.8-4.7L5.5 10l4.7-1.8z" />
  </svg>
);

export const SparklesIcon = ({ className = 'size-5' }) => (
  <svg {...stroke} strokeWidth="1.6" className={className}>
    <path d="M12 3.5l1.8 4.7 4.7 1.8-4.7 1.8L12 16.5l-1.8-4.7L5.5 10l4.7-1.8z" />
    <path d="M18.5 15.5l.8 1.9 1.9.8-1.9.8-.8 1.9-.8-1.9-1.9-.8 1.9-.8z" />
  </svg>
);

export const PassIcon = ({ className = 'size-4 shrink-0 text-emerald-600' }) => (
  <svg {...stroke} strokeWidth="2.2" className={className}><path d="M5 12.5l4.5 4.5L19 7" /></svg>
);

export const FailIcon = ({ className = 'size-4 shrink-0 text-red-600' }) => (
  <svg {...stroke} strokeWidth="2.2" className={className}><path d="M7 7l10 10M17 7 7 17" /></svg>
);

export const TrashIcon = ({ className = 'size-4.5' }) => (
  <svg {...stroke} strokeWidth="1.6" className={className}>
    <path d="M4.5 7h15M10 11v6M14 11v6M6.5 7l.9 12.2a1 1 0 0 0 1 .8h7.2a1 1 0 0 0 1-.8L17.5 7M9.5 7V4.5h5V7" />
  </svg>
);

export const ExternalIcon = ({ className = 'size-3.5' }) => (
  <svg {...stroke} strokeWidth="2" className={className}>
    <path d="M14 5h5v5M19 5l-8 8M17 14v4a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V8a1 1 0 0 1 1-1h4" />
  </svg>
);
