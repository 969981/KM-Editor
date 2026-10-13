/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useMemo, useRef, useState } from 'react';
import { Bird, RefreshCw, Save, X } from 'lucide-react';
import type { EditSession, ProjectPaths } from '../../bridge/contracts';
import type { RestorationProjectBridgeApi } from '../../bridge/restorationProjectBridge';
import type { RestorationPreparation, RestorationSelection, RestorationWorkflow } from '../../bridge/restorationContracts';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { WorkflowPanelOutputSections, type WorkflowPanelOutput } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import './RestoreMissingPokemonSection.css';
import { readRestorationSources, rememberRestorationSources } from './restorationSourceSettings';

type Props = {
  workflow: RestorationWorkflow | null; paths: ProjectPaths; bridge: RestorationProjectBridgeApi; session: EditSession | null;
  isEditing: boolean; isEditStarting: boolean; isStaging: boolean; isLoading: boolean;
  onStartEditSession: () => void; onCancelEditSession: (discard: () => void) => void;
  onStage: (selections: RestorationSelection[]) => Promise<boolean>;
  onDirtyStateChange: (dirty: boolean) => void; onRefresh: () => void; panelOutput: WorkflowPanelOutput;
};
const preparationByProject = new Map<string, RestorationPreparation>();

export function RestoreMissingPokemonSection({ workflow, paths, bridge, session, isEditing, isEditStarting, isStaging, isLoading,
  onStartEditSession, onCancelEditSession, onStage, onDirtyStateChange, onRefresh, panelOutput }: Props) {
  const { t } = useLocalization();
  const projectKey = JSON.stringify([paths.selectedGame, paths.baseRomFsPath, paths.outputRootPath]);
  const [search, setSearch] = useState('');
  const [savedSources] = useState(() => readRestorationSources(projectKey, paths.scarletVioletSupportFolderPath ?? ''));
  const [donor, setDonor] = useState(savedSources.donor);
  const [support, setSupport] = useState(savedSources.support);
  const [drafts, setDrafts] = useState<Record<string, boolean>>({});
  const [preparation, setPreparation] = useState<RestorationPreparation | null>(() => preparationByProject.get(projectKey) ?? null);
  const [error, setError] = useState<string | null>(null);
  const [isPreparing, setIsPreparing] = useState(false);
  const active = useRef(true); const submittedDrafts = useRef(drafts); submittedDrafts.current = drafts;
  const families = useMemo(() => (workflow?.entries ?? []).filter(e => !e.definitionId || e.speciesId === e.family[0]), [workflow]);
  const pendingCount = session?.pendingEdits.filter(e => e.domain === 'workflow.restoreMissingPokemon').length ?? 0;
  const dirty = Object.keys(drafts).length > 0;
  const canEdit = isEditing && workflow?.canEdit === true;
  const hashes = new Map(families.filter(e => e.definitionId && e.preparedHash).map(e => [e.definitionId!, e.preparedHash!]));
  if (preparation?.preparedHash) hashes.set(preparation.definitionId, preparation.preparedHash);
  const valid = Object.keys(drafts).every(id => hashes.has(id) && families.some(e => e.definitionId === id && e.installation !== 'External'));
  useEffect(() => { onDirtyStateChange(dirty); }, [dirty, onDirtyStateChange]);
  useEffect(() => { active.current = true; return () => { active.current = false; onDirtyStateChange(false); }; }, [onDirtyStateChange]);
  usePublishCommonEditorError({ domain: 'workflow.restoreMissingPokemon', field: 'draft', message: error });
  const remember = (value: RestorationPreparation) => {
    if (preparationByProject.size >= 4 && !preparationByProject.has(projectKey)) preparationByProject.delete(preparationByProject.keys().next().value!);
    preparationByProject.set(projectKey, value); setPreparation(value);
  };
  useEffect(() => {
    if (preparation?.status !== 'preparing') return;
    let canceled = false;
    const timer = window.setTimeout(async () => {
      try {
        const response = await bridge.getRestorationPreparation({ paths, preparationId: preparation.id });
        if (!canceled) {
          remember(response.preparation);
          if (response.preparation.status === 'failed') setError(response.preparation.diagnostics[0]?.message ?? t('restoration.failed'));
        }
      } catch { if (!canceled) setError(t('restoration.failed')); }
    }, 1000);
    return () => { canceled = true; window.clearTimeout(timer); };
    // Each response starts the next poll; project changes unmount this scoped editor.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [preparation, projectKey, bridge]);
  const prepare = async (definitionId: string) => {
    setIsPreparing(true); setError(null);
    rememberRestorationSources(projectKey, { donor: donor.trim(), support: support.trim() });
    try {
      const response = await bridge.prepareRestoration({ paths, definitionId, donorRomFsPath: donor.trim(), compressionSupportFolderPath: support.trim() || null });
      if (active.current) remember(response.preparation);
    } catch { if (active.current) setError(t('restoration.failed')); }
    finally { if (active.current) setIsPreparing(false); }
  };
  const stage = async () => {
    if (!canEdit || !dirty || !valid || isStaging) return;
    const submitted = { ...submittedDrafts.current }; setError(null);
    try {
      if (!await onStage(Object.entries(submitted).map(([definitionId, enabled]) => ({ definitionId, enabled, preparedHash: hashes.get(definitionId)! })))) {
        if (active.current) setError(t('restoration.failed')); return;
      }
      if (active.current) setDrafts(current => Object.fromEntries(Object.entries(current).filter(([id, value]) => submitted[id] !== value)));
    } catch { if (active.current) setError(t('restoration.failed')); }
  };
  const cancelPreparation = async () => {
    if (!preparation) return;
    try { const response = await bridge.cancelRestorationPreparation({ paths, preparationId: preparation.id }); if (active.current) remember(response.preparation); }
    catch { if (active.current) setError(t('restoration.failed')); }
  };
  return <section className="workflow-panel wide-panel restoration-panel" aria-labelledby="restoration-title">
    <header className="workflow-panel-header"><div><h2 id="restoration-title"><Bird size={22} aria-hidden="true" /> {t('restoration.title')}</h2>
      <p>{t('restoration.subtitle')}</p></div><button className="secondary-button" type="button" onClick={onRefresh} disabled={isLoading}>
      <RefreshCw size={16} aria-hidden="true" /> {t('restoration.refresh')}</button></header>
    <EditorSessionBar canEdit={workflow?.canEdit === true} isEditing={isEditing} isStarting={isEditStarting}
      label={t('restoration.title')} onStart={onStartEditSession} readOnlyReason={t('restoration.readOnly')} />
    {isEditing ? <EditorSessionBarActions>
      <button className="primary-button" type="button" onClick={() => void stage()} disabled={!canEdit || !dirty || !valid || isStaging}>
        <Save size={16} aria-hidden="true" /> {t('restoration.stage')}</button>
      <button className="secondary-button" type="button" onClick={() => onCancelEditSession(() => setDrafts({}))} disabled={isStaging}>
        <X size={16} aria-hidden="true" /> {t('restoration.cancel')}</button>
      <span>{t('restoration.drafts', { count: Object.keys(drafts).length, staged: pendingCount })}</span>
    </EditorSessionBarActions> : null}
    <p>{t('restoration.familyHelp')}</p>
    <div className="restoration-sources"><label>{t('restoration.donor')}<input value={donor} onChange={e => setDonor(e.target.value)} /></label>
      <label>{t('restoration.support')}<input value={support} onChange={e => setSupport(e.target.value)} /></label></div>
    {preparation ? <div className="restoration-progress" role="status"><span>{t(`restoration.status.${preparation.status}`)} · {preparation.completedSpecies}/{preparation.totalSpecies}</span>
      {preparation.status === 'preparing' ? <button className="secondary-button" type="button" onClick={() => void cancelPreparation()}>{t('restoration.cancelPreparation')}</button> : null}
      {Object.values(preparation.unavailableSourceMoves).some(ids => ids.length > 0) ? <p>{t('restoration.excludedMoves', {
        count: Object.values(preparation.unavailableSourceMoves).reduce((count, ids) => count + ids.length, 0) })}</p> : null}</div> : null}
    <label className="restoration-search">{t('restoration.search')}<input type="search" value={search} onChange={e => setSearch(e.target.value)} /></label>
    <div className="restoration-list"><table><thead><tr><th scope="col">{t('restoration.enabled')}</th><th scope="col">{t('restoration.pokemon')}</th>
      <th scope="col">{t('restoration.availability')}</th><th scope="col">{t('restoration.actions')}</th></tr></thead><tbody>
      {families.filter(e => `${e.speciesId} ${e.family.map(id => workflow?.entries.find(row => row.speciesId === id)?.name ?? id).join(' ')}`.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase())).map(entry => {
        const id = entry.definitionId;
        const label = entry.family.map(species => workflow?.entries.find(row => row.speciesId === species)?.name ?? species).join(', ');
        const enabled = id && id in drafts ? drafts[id] : entry.selected;
        return <tr key={entry.speciesId}><td><input type="checkbox" aria-label={`${t('restoration.enabled')}: ${label}`} checked={enabled}
          disabled={!canEdit || !id || entry.support === 'NotSupported' || entry.installation === 'External'}
          onChange={event => { if (!id) return; const checked = event.target.checked; setDrafts(current => {
            const next = { ...current, [id]: checked }; if (checked === entry.selected && !isStaging) delete next[id]; return next;
          }); }} /></td><td><strong>{label}</strong><small>#{entry.family.join(', #')}</small></td>
          <td>{t(`restoration.support.${entry.support}`)}<small>{t(`restoration.installation.${entry.installation}`)}{entry.staged ? ` · ${t('restoration.staged')}` : ''}</small></td>
          <td>{id && entry.installation !== 'External' ? <>
            {!hashes.has(id) ? <button className="secondary-button" type="button" disabled={!donor.trim() || isPreparing || preparation?.status === 'preparing'}
              onClick={() => void prepare(id)}>{t('restoration.prepare')}</button> : null}
            {entry.installation === 'NeedsRepair' ? <button className="secondary-button" type="button" disabled={!canEdit || isStaging}
              onClick={() => setDrafts(current => ({ ...current, [id]: true }))}>{t('restoration.repair')}</button> : null}
          </> : null}</td></tr>;
      })}</tbody></table></div>
    <WorkflowPanelOutputSections output={panelOutput} workflowDiagnostics={workflow?.diagnostics ?? []} />
  </section>;
}
