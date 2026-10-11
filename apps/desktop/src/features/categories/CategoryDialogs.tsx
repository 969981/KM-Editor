/* SPDX-License-Identifier: GPL-3.0-only */

import { useState } from 'react';
import { createPortal } from 'react-dom';
import { useModalDialog } from '../../components/useModalDialog';
import { usePublishCommonEditorDiagnostics } from '../../components/CommonEditorDiagnostics';
import { categoryErrorCodes } from '../../errorCodes';
import { useLocalization } from '../../localization';
import { updateEditorCategories, useEditorCategories } from './categoryPreferences';
import type { CategoryIssue } from './categoryLayout';
import './categories.css';

export function CategoryDiagnostics() {
  const { issue } = useEditorCategories();
  const { t } = useLocalization();
  usePublishCommonEditorDiagnostics(issue ? [{ code: categoryErrorCodes[issue], domain: 'settings.categories',
    severity: 'error', message: t(`categories.error.${issue}`) }] : []);
  return null;
}

export type CategoryDialogAction = { type: 'create' } | { type: 'rename' | 'delete'; id: string; name: string } | { type: 'restore' };

export function CategoryDialog({ action, onClose }: { action: CategoryDialogAction; onClose: () => void }) {
  const { t, translateLiteral } = useLocalization();
  const [name, setName] = useState('name' in action ? action.name : '');
  const [error, setError] = useState<CategoryIssue | null>(null);
  const { issue } = useEditorCategories();
  const dialogRef = useModalDialog<HTMLDivElement>({ onClose });
  const naming = action.type === 'create' || action.type === 'rename';
  usePublishCommonEditorDiagnostics(error ? [{ code: categoryErrorCodes[error], domain: 'settings.categories',
    field: 'name', severity: 'error', message: t(`categories.error.${error}`) }] : []);
  const submit = () => {
    if (naming && !name.trim()) { setError('name'); return; }
    const operation = action.type === 'create' ? { type: 'create' as const, id: `custom:${crypto.randomUUID()}`, name }
      : action.type === 'rename' ? { ...action, name } : action;
    if (updateEditorCategories(operation)) onClose();
  };
  const title = t(`categories.${action.type}`);
  return createPortal(
    <div className="category-dialog-backdrop">
      <div aria-labelledby="category-dialog-title" aria-describedby={naming ? undefined : 'category-dialog-description'}
        aria-modal="true" className="category-dialog" ref={dialogRef} role={naming ? 'dialog' : 'alertdialog'} tabIndex={-1}>
        <h2 id="category-dialog-title">{title}</h2>
        <form onSubmit={event => { event.preventDefault(); submit(); }}>
          {naming ? <label className="category-name-field">
            <span>{t('categories.name')}</span>
            <textarea aria-invalid={error === 'name' || undefined} data-localization-ignore="true"
              onChange={event => { setName(event.target.value); setError(null); }} rows={3} value={name} />
          </label> : <p id="category-dialog-description">{t(`categories.${action.type}Warning`)}</p>}
          {error || issue ? <p className="category-error" role="alert">{t(`categories.error.${error ?? issue}`)}</p> : null}
          <div className="category-dialog-actions">
            <button onClick={onClose} type="button">
              {translateLiteral(naming ? 'Cancel' : 'No')}
            </button>
            <button className={naming ? 'primary-button' : 'danger-button'} type="submit">
              {translateLiteral(naming ? 'Save' : 'Yes')}
            </button>
          </div>
        </form>
      </div>
    </div>, document.body);
}
