/* SPDX-License-Identifier: GPL-3.0-only */
import { RefreshCw, Trash2, X } from 'lucide-react';
import { useLocalization } from '../localization';
import { useModalDialog } from './useModalDialog';

export function PokemonYieldConfirmationModal({
  action,
  kind,
  onCancel,
  onConfirm
}: {
  action: 'remove' | 'restore';
  kind: 'ev' | 'exp';
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const { t, translateLiteral } = useLocalization();
  const dialogRef = useModalDialog({ onClose: onCancel });
  const isRestore = action === 'restore';
  const label = translateLiteral(kind === 'exp' ? 'EXP Yield' : 'EV Yield');
  const title = t(isRestore ? 'editorText.yield.restoreTitle' : 'editorText.yield.removeTitle', { label });
  const description = t(isRestore ? 'editorText.yield.restoreDescription' : `editorText.yield.remove.${kind}`, { label });
  const Icon = isRestore ? RefreshCw : Trash2;
  const headingId = `${kind}-yield-confirmation-heading`;

  return (
    <div className="modal-backdrop" role="presentation">
      <section
        aria-labelledby={headingId}
        aria-modal="true"
        className="modal-panel"
        ref={dialogRef}
        role="dialog"
        tabIndex={-1}
      >
        <div className="panel-heading">
          <Icon aria-hidden="true" size={18} />
          <h2 id={headingId}>{title}</h2>
        </div>
        <p className="modal-copy">{description}</p>
        <div className="modal-actions">
          <button
            className={isRestore ? 'primary-button' : 'danger-button'}
            onClick={onConfirm}
            type="button"
          >
            <Icon aria-hidden="true" size={16} />
            <span>{t(isRestore ? 'editorText.yield.confirmRestore' : 'editorText.yield.confirmRemove', { label })}</span>
          </button>
          <button className="secondary-button" onClick={onCancel} type="button">
            <X aria-hidden="true" size={16} />
            <span>Cancel</span>
          </button>
        </div>
      </section>
    </div>
  );
}
