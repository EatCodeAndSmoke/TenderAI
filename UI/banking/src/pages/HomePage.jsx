import { useEffect } from 'react';
import { useSession } from '../session.jsx';

// The rest of the banking app would live here
export default function HomePage() {
  const { customerName, unreadCount, ringBell } = useSession();

  // Ring the bell once each time home opens with unread notifications
  useEffect(() => {
    if (unreadCount > 0) ringBell();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <section>
      <p className="text-sm text-muted">Hello,</p>
      <h1 className="text-2xl font-semibold tracking-tight">{customerName}</h1>
    </section>
  );
}
