import { NavLink } from 'react-router-dom';
import { BuildingIcon, DocIcon, LinkIcon, LogoIcon } from './icons.jsx';

const NAV = [
  { to: '/tenders', label: 'Tenders', Icon: DocIcon },
  { to: '/customers', label: 'Customers', Icon: BuildingIcon },
  { to: '/matches', label: 'Matches', Icon: LinkIcon }
];

// Sidebar (top bar on small screens). The page you're on gets a white highlight.
export default function Sidebar() {
  return (
    <aside className="relative z-10 flex items-center gap-4 bg-cobalt bg-linear-to-b from-cobalt-top to-cobalt px-4 py-3 text-white md:fixed md:inset-y-0 md:left-0 md:w-60 md:flex-col md:items-stretch md:gap-0 md:px-4 md:py-6">
      <div className="flex items-center gap-3 md:px-2">
        <span className="grid size-9 place-items-center rounded-xl bg-brand-500/20 text-brand-300 ring-1 ring-brand-300/30">
          <LogoIcon />
        </span>
        <div className="hidden leading-tight whitespace-nowrap sm:block">
          <div className="text-sm font-semibold">Tender AI</div>
          <div className="hidden text-xs text-cobalt-soft md:block">Admin console</div>
        </div>
      </div>

      <nav className="flex gap-1 md:mt-10 md:flex-col" aria-label="Main">
        {NAV.map(({ to, label, Icon }) => (
          <NavLink
            key={to}
            to={to}
            className={({ isActive }) =>
              'flex items-center gap-3 rounded-lg px-3 py-2.5 text-sm font-medium transition-colors ' +
              (isActive ? 'bg-white text-cobalt shadow-sm' : 'text-cobalt-soft hover:bg-white/10 hover:text-white')}
          >
            {({ isActive }) => (
              <>
                <Icon className={`size-5 shrink-0 ${isActive ? 'text-brand-600' : ''}`} />
                {label}
              </>
            )}
          </NavLink>
        ))}
      </nav>

      <p className="mt-auto hidden px-3 text-xs leading-relaxed text-cobalt-soft md:block">
        Demo data is saved to the database.
      </p>
    </aside>
  );
}
