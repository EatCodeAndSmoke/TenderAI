import { useEffect, useState } from 'react';
import PageHeader from '../components/PageHeader.jsx';
import { getPrompts, updatePrompt } from '../lib/api.js';
import { BTN_PRIMARY, BTN_SECONDARY, CARD } from '../lib/styles.js';

const descriptions = {
  matching: {
    title: 'Tender matching',
    detail: 'Guides how customer activity and payment evidence are compared with tenders.'
  },
  summarization: {
    title: 'Tender summarization',
    detail: 'Guides the English title and summary generated from UZEX tender information.'
  }
};

function PromptEditor({ initial, onToast }) {
  const [saved, setSaved] = useState(initial);
  const [instructions, setInstructions] = useState(initial.instructions);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const description = descriptions[initial.key];
  const dirty = instructions !== saved.instructions;
  const valid = instructions.trim().length > 0 && instructions.length <= 20000;

  async function save(event) {
    event.preventDefault();
    if (!dirty || !valid || busy) return;
    setBusy(true);
    setError('');
    try {
      const updated = await updatePrompt(saved.key, instructions, saved.revision);
      setSaved(updated);
      setInstructions(updated.instructions);
      onToast(`${description.title} prompt saved.`);
    } catch (err) {
      setError(err.status === 409
        ? 'This prompt changed elsewhere. Copy your edits, then reopen Prompts to load the latest version.'
        : err.message);
    } finally {
      setBusy(false);
    }
  }

  return <form onSubmit={save} className={`${CARD} p-5 sm:p-6`}>
    <div className="mb-4">
      <h2 className="text-lg font-semibold">{description.title}</h2>
      <p className="mt-1 text-sm text-muted">{description.detail}</p>
    </div>
    <label htmlFor={`prompt-${saved.key}`} className="mb-2 block text-sm font-medium">Instructions</label>
    <textarea id={`prompt-${saved.key}`} value={instructions} onChange={event => setInstructions(event.target.value)}
      disabled={busy} maxLength={20000} rows={18} spellCheck={false}
      className="w-full rounded-lg border border-line bg-surface p-3 font-mono text-sm leading-relaxed text-ink focus:outline-2 focus:outline-brand-600 disabled:opacity-60" />
    <p className="mt-2 text-xs text-muted">
      {instructions.length.toLocaleString()} / 20,000 characters{dirty ? ' · Unsaved changes' : ' · Saved'}
    </p>
    <details className="mt-4 text-sm">
      <summary className="cursor-pointer font-medium">Required instructions added by the application</summary>
      <pre className="mt-2 whitespace-pre-wrap rounded-lg bg-subtle p-3 font-mono text-xs leading-relaxed text-muted">{saved.requiredInstructions}</pre>
    </details>
    {saved.key === 'matching' && saved.instructions !== saved.defaultInstructions && <p className="mt-3 text-xs text-muted">
      A custom matching prompt is saved. To use the latest recommended instructions, select Restore default and save.
    </p>}
    {!valid && <p role="alert" className="mt-2 text-sm text-red-700">Instructions cannot be empty.</p>}
    {error && <p role="alert" className="mt-3 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
    <div className="mt-4 flex flex-wrap gap-3">
      <button type="submit" className={`${BTN_PRIMARY} disabled:opacity-50`} disabled={!dirty || !valid || busy}>
        {busy ? 'Saving…' : 'Save prompt'}
      </button>
      <button type="button" className={`${BTN_SECONDARY} disabled:opacity-50`} disabled={busy || instructions === saved.defaultInstructions}
        onClick={() => { setInstructions(saved.defaultInstructions); setError(''); }}>Restore default</button>
    </div>
  </form>;
}

export default function PromptsPage({ onToast }) {
  const [prompts, setPrompts] = useState(null);
  const [error, setError] = useState('');
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let active = true;
    getPrompts().then(data => {
      if (active) { setPrompts(data); setError(''); }
    }).catch(err => {
      if (active) setError(err.message);
    });
    return () => { active = false; };
  }, [attempt]);

  return <section>
    <PageHeader title="Prompts" summary="Review and edit the instructions used by tender matching and summarization." />
    <div className="mb-6 rounded-xl border border-line bg-surface p-4 text-sm text-muted">
      Saved instructions apply to future AI requests. Existing tender summaries are not regenerated automatically.
      The application adds the source data and required JSON response format to each prompt.
    </div>
    {error ? <p role="alert" className="rounded-lg bg-red-50 p-3 text-sm text-red-700">
      {error} <button type="button" onClick={() => { setError(''); setAttempt(value => value + 1); }} className="underline">Retry</button>
    </p> : !prompts ? <p className="text-sm text-muted">Loading prompts…</p> :
      <div className="space-y-6">{prompts.map(prompt => <PromptEditor key={prompt.key} initial={prompt} onToast={onToast} />)}</div>}
  </section>;
}
