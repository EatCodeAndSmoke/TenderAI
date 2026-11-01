import { useEffect, useRef } from 'react';

// Native <dialog> shown as a modal. Escape and onClose always close it; a backdrop click
// closes it too unless dismissOnBackdrop is false (forms, so typed data isn't lost).
// After opening, the element marked data-autofocus (if any) gets focus.
export default function Modal({ open, onClose, labelledBy, dismissOnBackdrop = true, width = 'max-w-2xl', children }) {
  const ref = useRef(null);

  useEffect(() => {
    const dialog = ref.current;
    if (open && !dialog.open) {
      dialog.showModal();
      dialog.querySelector('[data-autofocus]')?.focus();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open]);

  return (
    <dialog
      ref={ref}
      aria-labelledby={labelledBy}
      onClose={onClose}
      onClick={(e) => { if (dismissOnBackdrop && e.target === e.currentTarget) e.currentTarget.close(); }}
      className={`m-auto w-[calc(100%-2rem)] ${width} overflow-hidden rounded-2xl border border-line bg-surface p-0 text-ink shadow-2xl backdrop:bg-[#0A1F42]/60 backdrop:backdrop-blur-[2px]`}
    >
      {children}
    </dialog>
  );
}

export function ModalHeader({ id, icon, title, subtitle, onClose, className = 'px-6 pt-6' }) {
  return (
    <div className={`flex items-start justify-between gap-4 ${className}`}>
      <div className="flex items-start gap-3">
        <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-brand-50 text-brand-600">{icon}</span>
        <div>
          <h2 id={id} className="text-lg font-semibold">{title}</h2>
          <p className="text-sm text-muted">{subtitle}</p>
        </div>
      </div>
      <button
        type="button"
        onClick={onClose}
        aria-label="Close"
        className="grid size-8 shrink-0 place-items-center rounded-lg text-muted transition-colors hover:bg-subtle hover:text-ink"
      >
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" className="size-5" aria-hidden="true">
          <path d="M6 6l12 12M18 6 6 18" />
        </svg>
      </button>
    </div>
  );
}
