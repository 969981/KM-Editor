/* SPDX-License-Identifier: GPL-3.0-only */

import { Layers } from 'lucide-react';
import { useState } from 'react';
import { useLocalization } from '../../localization';
import { usePublishCommonEditorDiagnostics } from '../../components/CommonEditorDiagnostics';
import { categoryErrorCodes } from '../../errorCodes';
import { CategoryDialog } from './CategoryDialogs';
import { reloadCategoryPreferences, useEditorCategories } from './categoryPreferences';

export function CategorySettings() {
  const { t } = useLocalization();
  const [confirmRestore, setConfirmRestore] = useState(false);
  const { issue } = useEditorCategories();
  usePublishCommonEditorDiagnostics(issue ? [{ code: categoryErrorCodes[issue], domain: 'settings.categories',
    severity: 'error', message: t(`categories.error.${issue}`) }] : []);
  return <section aria-labelledby="category-settings-heading" className="settings-subsection">
    <div className="settings-subsection-heading">
      <Layers aria-hidden="true" size={18} />
      <div><h3 id="category-settings-heading">{t('categories.title')}</h3><p>{t('categories.description')}</p></div>
    </div>
    <button className="danger-button" onClick={() => setConfirmRestore(true)} type="button">{t('categories.restore')}</button>
    {issue ? <div className="category-storage-status">
      <p role="alert">{t(`categories.error.${issue}`)}</p>
      <button onClick={reloadCategoryPreferences} type="button">{t('categories.retry')}</button>
    </div> : null}
    {confirmRestore ? <CategoryDialog action={{ type: 'restore' }} onClose={() => setConfirmRestore(false)} /> : null}
  </section>;
}
