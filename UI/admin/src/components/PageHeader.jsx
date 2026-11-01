export default function PageHeader({ title, summary, actions }) {
  return (
    <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
        <p className="mt-1 max-w-xl text-sm text-muted">{summary}</p>
      </div>
      {actions && <div className="flex gap-2">{actions}</div>}
    </div>
  );
}
