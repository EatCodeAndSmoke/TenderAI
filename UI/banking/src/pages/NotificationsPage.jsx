import { Link } from 'react-router-dom';
import { BackIcon } from '../components/icons.jsx';

export default function NotificationsPage() {
  return <section>
    <Link to="/home" className="inline-flex items-center gap-1.5 text-sm font-medium text-muted"><BackIcon /> Home</Link>
    <h1 className="mt-3 text-2xl font-semibold">Notifications</h1>
    <div className="mt-5 rounded-2xl border border-dashed border-line bg-surface px-6 py-14 text-center">
      <p className="font-medium">You have no notifications</p>
      <p className="mt-1 text-sm text-muted">Notifications are not active in this demo.</p>
    </div>
  </section>;
}
