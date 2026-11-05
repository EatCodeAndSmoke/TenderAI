import { useEffect, useRef } from 'react';
import { Link } from 'react-router-dom';
import { BTN_PRIMARY, money } from '../format.js';
import { CheckIcon } from './icons.jsx';

export default function TenderApplicationDialog({ tender, match, onClose }) {
  const dialogRef = useRef(null);

  useEffect(() => {
    if (!dialogRef.current.open) dialogRef.current.showModal();
  }, []);

  return <dialog ref={dialogRef} onClose={onClose} aria-labelledby="application-title" aria-describedby="application-description"
    className="m-auto w-[calc(100%-2rem)] max-w-lg rounded-2xl border border-line bg-white p-0 text-ink shadow-xl backdrop:bg-black/50">
    <div className="p-6 sm:p-8">
      <div className="flex items-start justify-between gap-4">
        <span className="grid size-12 place-items-center rounded-full bg-brand-50 text-brand-700">
          <CheckIcon className="size-6" />
        </span>
        <button type="button" aria-label="Close confirmation" onClick={() => dialogRef.current.close()}
          className="rounded-lg px-2 py-1 text-xl leading-none text-muted hover:bg-subtle focus-visible:outline-2 focus-visible:outline-brand-600">×</button>
      </div>
      <p className="mt-5 text-xs font-semibold uppercase tracking-widest text-brand-700">Tender application</p>
      <h2 id="application-title" className="mt-1 text-xl font-semibold">Your request is recorded</h2>
      <p id="application-description" className="mt-3 text-sm leading-relaxed text-ink/80">
        Your request to prepare your information for <strong>{tender.englishTitle}</strong> on UZEX has been recorded.
        In a full service, we would notify you if additional information or documents were needed.
      </p>
      <p className="mt-3 text-xs leading-relaxed text-muted">
        Demo only: no information has been prepared or sent to UZEX, and notifications are not sent.
      </p>
      {match.offer?.financing > 0 && <div className="mt-5 rounded-xl border border-brand-100 bg-brand-50 p-4">
        <p className="text-sm font-semibold text-brand-700">Financing may be available</p>
        <p className="mt-1 text-sm leading-relaxed text-ink/80">
          You may apply for up to <strong>{money(match.offer.financing)} UZS</strong> in financing for this tender.
          Final approval is subject to bank review.
        </p>
        <Link to={`/loan/${match.lotId}`} className={`${BTN_PRIMARY} mt-3`}>Explore financing</Link>
      </div>}
      <button type="button" autoFocus onClick={() => dialogRef.current.close()}
        className="mt-6 rounded-lg border border-line px-4 py-2.5 text-sm font-medium hover:bg-subtle focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600">
        Back to tenders
      </button>
    </div>
  </dialog>;
}
