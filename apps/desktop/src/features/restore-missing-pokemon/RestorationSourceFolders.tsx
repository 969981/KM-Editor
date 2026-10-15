/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState } from 'react';
import { FolderOpen, Search, X } from 'lucide-react';
import { DesktopServiceError, type DesktopServices } from '../../desktopServices';
import { desktopErrorCodes, type KmErrorCode } from '../../errorCodes';
import { usePublishCommonEditorDiagnostics } from '../../components/CommonEditorDiagnostics';
import { useModalDialog } from '../../components/useModalDialog';
import { useLocalization } from '../../localization';
import { RestorationDonorStatus } from './RestorationDonorStatus';
import type { RestorationProjectBridgeApi } from '../../bridge/restorationProjectBridge';
import type { ProjectPaths } from '../../bridge/contracts';
import type { ApiDiagnostic } from '../../bridge/contracts';
import type { RestorationSources } from './restorationSourceSettings';

type Props = { sources: RestorationSources; onChange: (sources: RestorationSources) => void; desktop: DesktopServices; paths?: ProjectPaths; bridge?: RestorationProjectBridgeApi };
type Field = keyof RestorationSources;

export function RestorationSourceFolders({ sources, onChange, desktop, paths, bridge }: Props) {
  const { t } = useLocalization();
  const latest = useRef(sources); latest.current = sources;
  const active = useRef(true);
  const versions = useRef({ donor: 0, donorZa: 0, support: 0 });
  const searchRun = useRef<object | null>(null);
  const [picking, setPicking] = useState<Partial<Record<Field, boolean>>>({});
  const [searching, setSearching] = useState(false);
  const [confirmSearch, setConfirmSearch] = useState(false);
  const [diagnostics, setDiagnostics] = useState<ApiDiagnostic[]>([]);
  usePublishCommonEditorDiagnostics(diagnostics);
  useEffect(() => {
    active.current = true;
    return () => {
      active.current = false;
      if (searchRun.current) { searchRun.current = null; void desktop.cancelSupportFileSearch().catch(() => undefined); }
    };
  }, [desktop]);
  const change = (field: Field, value: string) => {
    versions.current[field] += 1;
    const next = { ...latest.current, [field]: value }; latest.current = next; onChange(next);
  };
  const report = (error: unknown, fallback: string, code: KmErrorCode = desktopErrorCodes.folderPickerFailed) => setDiagnostics([{
    code: error instanceof DesktopServiceError ? error.code : code,
    domain: 'desktop', severity: 'error', message: t(fallback),
  }]);
  const browse = async (field: Field, title: string) => {
    const version = ++versions.current[field]; setPicking(current => ({ ...current, [field]: true })); setDiagnostics([]);
    try {
      const path = await desktop.pickFolder({ title, defaultPath: latest.current[field].trim() || undefined });
      if (active.current && versions.current[field] === version && path !== null) change(field, path);
    } catch (error) { if (active.current) report(error, 'restoration.browseFailed'); }
    finally { if (active.current) setPicking(current => ({ ...current, [field]: false })); }
  };
  const cancelSearch = () => {
    searchRun.current = null; setSearching(false);
    void desktop.cancelSupportFileSearch().catch(error => { if (active.current) report(error, 'restoration.searchFailed', desktopErrorCodes.supportFileSearchCancelFailed); });
  };
  const find = async () => {
    if (searchRun.current) return;
    const run = {}; searchRun.current = run;
    const version = ++versions.current.support;
    setConfirmSearch(false); setSearching(true); setDiagnostics([]);
    try {
      const path = await desktop.findSupportFileFolder();
      if (!active.current || searchRun.current !== run) return;
      if (versions.current.support !== version) return;
      if (path) change('support', path);
      else setDiagnostics([{ domain: 'desktop', severity: 'warning', message: t('restoration.notFound') }]);
    } catch (error) {
      if (active.current && searchRun.current === run && !(error instanceof DesktopServiceError && error.code === desktopErrorCodes.supportFileSearchCanceled))
        report(error, 'restoration.searchFailed', desktopErrorCodes.supportFileSearchFailed);
    } finally {
      if (active.current && searchRun.current === run) { searchRun.current = null; setSearching(false); }
    }
  };
  return <>
    <div className="restoration-sources">
      {(['donor', 'donorZa', 'support'] as const).map(field => <div className="restoration-source" key={field}>
        <label htmlFor={`restoration-${field}`}>{t(`restoration.${field}`)}</label>
        <div className="restoration-source-controls">
          <input id={`restoration-${field}`} value={sources[field]} onChange={event => change(field, event.target.value)} />
          <button className="secondary-button icon-button" type="button" disabled={!desktop.isAvailable || picking[field]}
            aria-label={t('restoration.browse', { folder: t(`restoration.${field}`) })} title={t('restoration.browse', { folder: t(`restoration.${field}`) })}
            onClick={() => void browse(field, t(`restoration.${field}`))}><FolderOpen size={18} aria-hidden="true" /></button>
        </div>
        {field !== 'support' && paths && bridge ? <RestorationDonorStatus donor={field === 'donor' ? 'ScarletViolet' : 'LegendsZA'}
          path={sources[field]} support={sources.support} paths={paths} bridge={bridge} /> : null}
        {field === 'support' ? <div className="restoration-source-actions">
          <button className="secondary-button" type="button" disabled={!desktop.isAvailable || searching} aria-busy={searching || undefined}
            onClick={() => setConfirmSearch(true)}><Search size={16} aria-hidden="true" />{t(searching ? 'restoration.finding' : 'restoration.find')}</button>
          {searching ? <button className="secondary-button" type="button" onClick={cancelSearch}><X size={16} aria-hidden="true" />{t('restoration.cancelSearch')}</button> : null}
        </div> : null}
      </div>)}
    </div>
    {confirmSearch ? <SearchConfirmation onCancel={() => setConfirmSearch(false)} onConfirm={() => void find()} /> : null}
  </>;
}

function SearchConfirmation({ onCancel, onConfirm }: { onCancel: () => void; onConfirm: () => void }) {
  const { t } = useLocalization(); const ref = useModalDialog({ onClose: onCancel });
  return <div className="modal-backdrop" role="presentation"><section ref={ref} className="modal-panel" role="dialog" aria-modal="true"
    aria-labelledby="restoration-search-title" tabIndex={-1}>
    <h2 id="restoration-search-title">{t('restoration.searchTitle')}</h2><p className="modal-copy">{t('restoration.searchHelp')}</p>
    <div className="modal-actions"><button className="secondary-button" type="button" onClick={onCancel}>{t('restoration.cancel')}</button>
      <button className="primary-button" type="button" onClick={onConfirm}><Search size={16} aria-hidden="true" />{t('restoration.startSearch')}</button></div>
  </section></div>;
}
