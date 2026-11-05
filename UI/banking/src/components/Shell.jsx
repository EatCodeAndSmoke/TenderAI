import { useEffect } from 'react';
import { Link, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useSession } from '../session.jsx';
import { BellIcon, LogoIcon } from './icons.jsx';

// Signed-in layout: header with the bell and "Log out", then the current page
export default function Shell() {
  const { logout, updates, updatesError, updatesLoading } = useSession();
  const unreadCount = updates.filter(update => !update.isRead).length;
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
          <Link to="/home" className="flex items-center gap-2 whitespace-nowrap sm:gap-2.5" aria-label="Business Bank, home">
            <LogoIcon className="size-9 ring-1 ring-white/15" />
            <span className="font-semibold">Business Bank</span>
          </Link>

          <div className="flex items-center gap-1">
            <Link
              to="/notifications"
              aria-label={updatesError ? 'Notifications unavailable' : updatesLoading ? 'Loading notifications' : `Notifications, ${unreadCount} unread`}
              className={`relative grid size-10 place-items-center rounded-full transition-colors hover:bg-white/10 ${onNotifications ? 'bg-white/15' : ''}`}
            >
              <BellIcon className="size-6" />
              {(unreadCount > 0 || updatesError) && <span aria-hidden="true" className="notification-badge absolute right-0 top-0 grid min-h-4 min-w-4 place-items-center rounded-full bg-brand-300 px-1 text-[10px] font-bold text-cobalt">{updatesError ? '!' : unreadCount}</span>}
            </Link>
            <span className="sr-only" aria-live="polite">{!updatesError && unreadCount > 0 ? `${unreadCount} unread notification${unreadCount === 1 ? '' : 's'}` : ''}</span>
            <button
              type="button"
              onClick={handleLogout}
              className="whitespace-nowrap rounded-lg px-3 py-2 text-sm font-medium text-cobalt-soft transition-colors hover:bg-white/10 hover:text-white"
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
