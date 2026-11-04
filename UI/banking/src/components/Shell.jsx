import { useEffect } from 'react';
import { Link, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useSession } from '../session.jsx';
import { BellIcon, LogoIcon } from './icons.jsx';

// Signed-in layout: header with the bell and "Log out", then the current page
export default function Shell() {
  const { logout } = useSession();
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const onNotifications = pathname === '/notifications';

  useEffect(() => {
    window.scrollTo(0, 0);
  }, [pathname]);

  const handleLogout = () => {
    logout();
    navigate('/login');
  };

  return (
    <div>
      <header className="bg-cobalt bg-linear-to-r from-cobalt-top to-cobalt text-white">
        <div className="mx-auto flex h-16 max-w-3xl items-center justify-between px-4">
          <Link to="/home" className="flex items-center gap-2.5" aria-label="Business Bank, home">
            <span className="grid size-9 place-items-center rounded-xl bg-white/10 text-brand-300 ring-1 ring-white/15">
              <LogoIcon className="size-5" />
            </span>
            <span className="font-semibold">Business Bank</span>
          </Link>

          <div className="flex items-center gap-1">
            <Link
              to="/notifications"
              aria-label="Notifications, 0 new"
              className={`relative grid size-10 place-items-center rounded-full transition-colors hover:bg-white/10 ${onNotifications ? 'bg-white/15' : ''}`}
            >
              <BellIcon className="size-6" />
            </Link>
            <button
              type="button"
              onClick={handleLogout}
              className="rounded-lg px-3 py-2 text-sm font-medium text-cobalt-soft transition-colors hover:bg-white/10 hover:text-white"
            >
              Log out
            </button>
          </div>
        </div>
      </header>

      <main className="mx-auto max-w-3xl px-4 py-8">
        <Outlet />
      </main>
    </div>
  );
}
