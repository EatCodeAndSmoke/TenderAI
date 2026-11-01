import { BTN_GHOST } from '../lib/styles.js';
import { SparklesIcon } from './icons.jsx';
import { ModalHeader } from './Modal.jsx';

function CommentSection({ title, children }) {
  return (
    <section>
      <h3 className="mb-2 text-sm font-semibold">{title}</h3>
      <p className="text-sm leading-relaxed text-ink/80">{children}</p>
    </section>
  );
}

export default function CommentsDialog({ match, onClose }) {
  if (!match) return null;

  return (
    <div className="flex max-h-[88vh] flex-col">
      <ModalHeader
        id="comments-title"
        className="px-6 pt-6 pb-4"
        icon={<SparklesIcon />}
        title="AI comments"
        subtitle={`${match.customerName} and ${match.lotId}, ${match.tenderTitle}`}
        onClose={onClose}
      />

      <div className="space-y-5 overflow-y-auto border-t border-line px-6 py-6">
        <CommentSection title="Why this tender matched">{match.matchReason}</CommentSection>
        <CommentSection title="Loan application decision">{match.loanDecisionReason}</CommentSection>
      </div>

      <div className="flex justify-end border-t border-line bg-subtle px-6 py-4">
        <button type="button" onClick={onClose} className={BTN_GHOST}>Close</button>
      </div>
    </div>
  );
}
