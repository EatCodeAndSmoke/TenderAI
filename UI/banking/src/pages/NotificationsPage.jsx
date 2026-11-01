import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useSession } from '../session.jsx';
import { BackIcon } from '../components/icons.jsx';
import NotificationCard from '../components/NotificationCard.jsx';

export default function NotificationsPage() {
  const { notifications, read, markRead } = useSession();

  // What was read when the page opened: the "New" labels stay until the next visit
  const [readOnOpen] = useState(() => new Set(read));

  // Seeing the list counts as reading it, so the bell badge clears
  useEffect(() => {
    markRead(notifications.map((n) => n.id));
  }, [notifications, markRead]);

  return (
    <section>
      <Link to="/home" className="inline-flex items-center gap-1.5 text-sm font-medium text-muted transition-colors hover:text-ink">
        <BackIcon />
        Home
      </Link>
      <h1 className="mt-3 text-2xl font-semibold tracking-tight">Notifications</h1>

      <div className="mt-5 space-y-4">
        {notifications.length ? (
          notifications.map((n) => <NotificationCard key={n.id} notification={n} isNew={!readOnOpen.has(n.id)} />)
        ) : (
          <div className="rounded-2xl border border-dashed border-line bg-surface px-6 py-14 text-center">
            <p className="font-medium">No notifications yet</p>
            <p className="mt-1 text-sm text-muted">When we find a state tender that fits your business, it will appear here.</p>
          </div>
        )}
      </div>
    </section>
  );
}
