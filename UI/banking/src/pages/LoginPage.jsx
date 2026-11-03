import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { authenticate } from '../api.js';
import { useSession } from '../session.jsx';
import { LogoIcon } from '../components/icons.jsx';

const FIELD =
  'h-10 w-full rounded-lg border border-line bg-surface px-3 text-sm outline-none transition ' +
  'focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15';

export default function LoginPage() {
  const { login } = useSession();
  const navigate = useNavigate();
  const [customerId, setCustomerId] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (busy) return;
    const id = customerId.trim().toUpperCase();

    if (!id || !password) {
      setError('Enter your customer ID and password.');
      return;
    }

    setBusy(true);
    setError('');
    try {
      const customer = await authenticate(id, password);
      login(customer);
      navigate('/home');
    } catch (err) {
      setError(err.status === 401
        ? 'That customer ID or password is wrong. Check both and try again.'
        : err.message);
      setBusy(false);
    }
  };

  return (
    <section className="flex min-h-screen items-center justify-center px-4 py-10">
      <div className="w-full max-w-sm">
        <div className="mb-6 flex items-center justify-center gap-3">
          <span className="grid size-10 place-items-center rounded-xl bg-cobalt text-brand-300">
            <LogoIcon className="size-6" />
          </span>
          <span className="text-lg font-semibold">Business Bank</span>
        </div>

        <form noValidate onSubmit={handleSubmit} className="rounded-2xl border border-line bg-surface p-6 shadow-sm shadow-cobalt/5">
          <h1 className="text-xl font-semibold">Log in</h1>
          <p className="mt-1 text-sm text-muted">Online banking for your business.</p>

          <label htmlFor="login-id" className="mt-6 mb-1.5 block text-sm font-medium">Customer ID</label>
          <input
            id="login-id"
            name="customerId"
            type="text"
            autoComplete="username"
            autoCapitalize="characters"
            autoFocus
            value={customerId}
            onChange={(e) => setCustomerId(e.target.value)}
            className={`${FIELD} placeholder:text-muted/60`}
          />

          <label htmlFor="login-password" className="mt-4 mb-1.5 block text-sm font-medium">Password</label>
          <input
            id="login-password"
            name="password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className={FIELD}
          />

          {error && (
            <p role="alert" className="mt-3 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>
          )}

          <button
            type="submit"
            disabled={busy}
            className="mt-6 h-10 w-full rounded-lg bg-brand-600 text-sm font-medium text-white shadow-sm transition-colors hover:bg-brand-700 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600 disabled:cursor-wait disabled:opacity-70"
          >
            {busy ? 'Logging in…' : 'Log in'}
          </button>
        </form>

        <p className="mt-4 text-center text-xs text-muted">Prototype access: use an existing customer ID and the password 123.</p>
      </div>
    </section>
  );
}
