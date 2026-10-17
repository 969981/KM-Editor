/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useMemo, useRef, useState, type Dispatch, type SetStateAction } from 'react';
import { RefreshCw, RotateCcw, Save, X } from 'lucide-react';
import type { EditSession, ProjectPaths } from '../../bridge/contracts';
import type { ProjectBridge } from '../../bridge/projectBridge';
import type { PlaField, PlaKind, PlaPage, PlaRecord, StagePlaRequest } from '../../bridge/plaProjectBridge';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { useLocalization } from '../../localization';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { getWorkbenchCapabilityRegistration } from '../../workbench/capabilityRegistry';
import { workbenchSectionLocalization } from '../../workbench/sectionLocalization';
import { PlaBattleWorkspace, type PlaPokemonPreview } from './PlaBattleWorkspace';
import { PlaDataWorkspace } from './PlaDataWorkspace';
import { PlaPokemonRows } from './PlaPokemonRows';
import { PlaEconomyWorkspace, isPlaEconomyKind } from './PlaEconomyWorkspace';
import { PlaWildWorkspace } from './PlaWildWorkspace';
import './plaEditor.css';

export type PlaDrafts = Record<string, { revision: string; values: Record<string, string>; baseline: Record<string, string> }>;
type Props = {
  section: 'pokemon' | 'moves' | 'text' | 'trainers' | 'giftPokemon' | 'staticEncounters' | 'items' | 'crafting' | 'shops' | 'pokemonDrops' | 'research' | 'encounters'; paths: ProjectPaths; session: EditSession | null; bridge: ProjectBridge;
  drafts: PlaDrafts; setDrafts: Dispatch<SetStateAction<PlaDrafts>>;
  isStarting: boolean; isBusy: boolean; onStart: () => void;
  onStage: (request: Omit<StagePlaRequest, 'paths' | 'session'>) => Promise<boolean>;
  onCancel: (discard: () => void) => void; onDirtyChange: (dirty: boolean) => void;
  renderPokemon?: PlaPokemonPreview;
};
const pokemonKinds: PlaKind[] = ['personal', 'learnsets', 'evolutions', 'compatibility', 'tutors'];

export function PlaEditorSection({ section, paths, session, bridge, drafts, setDrafts, isStarting, isBusy,
  onStart, onStage, onCancel, onDirtyChange, renderPokemon }: Props) {
  const { t, translateLiteral } = useLocalization();
  const [pokemonKind, setPokemonKind] = useState<PlaKind>('personal');
  const [economyTab, setEconomyTab] = useState<PlaKind | null>(null);
  const kind = section === 'pokemon' ? pokemonKind : section === 'giftPokemon' ? 'gifts' : section === 'staticEncounters' ? 'static'
    : section === 'pokemonDrops' ? 'drops' : section === 'shops' || section === 'research' || section === 'encounters' ? economyTab ?? section : section;
  const [page, setPage] = useState<PlaPage | null>(null);
  const [query, setQuery] = useState('');
  const [offset, setOffset] = useState(0);
  const [textFile, setTextFile] = useState<string | null>(null);
  const [messageLanguage, setMessageLanguage] = useState(() => ({ es: 'Spanish', fr: 'French', de: 'German', zh: 'Simp_Chinese' })[paths.gameTextLanguage as 'es' | 'fr' | 'de' | 'zh'] ?? 'English');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [staging, setStaging] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [refresh, setRefresh] = useState(0);
  const epoch = useRef(0);
  const requestKey = JSON.stringify({ paths, session, kind, query, offset, textFile, messageLanguage: kind === 'text' ? messageLanguage : null, refresh });
  const dirty = Object.keys(drafts).some(key => key.startsWith(section + '/'));
  useEffect(() => { onDirtyChange(dirty); }, [dirty, onDirtyChange]);
  usePublishCommonEditorError({ domain: `workflow.${section}`, field: 'pla', message: error });
  useEffect(() => {
    const run = ++epoch.current;
    setLoading(true);
    setError(null);
    const request = JSON.parse(requestKey) as { paths: ProjectPaths; session: EditSession | null; kind: PlaKind; query: string; offset: number; textFile: string | null; messageLanguage: string | null };
    const timer = window.setTimeout(() => {
      void bridge.loadPla({ paths: request.paths, session: request.session, kind: request.kind, query: request.query, offset: request.offset, textFile: request.textFile, messageLanguage: request.messageLanguage }).then(response => {
        if (epoch.current !== run) return;
        setPage(response.page);
        setSelectedId(current => response.page.records.some(record => record.id === current) ? current : response.page.records[0]?.id ?? null);
      }).catch(reason => { if (epoch.current === run) { setPage(null); setError(String(reason)); } })
        .finally(() => { if (epoch.current === run) setLoading(false); });
    }, 180);
    return () => { window.clearTimeout(timer); epoch.current++; };
  }, [bridge, requestKey]);

  const selected = page?.records.find(record => record.id === selectedId) ?? null;
  const recordDraftKey = (record: PlaRecord) => `${section}/${kind}/${paths.gameTextLanguage ?? 'English'}/${record.id}`;
  const draftKey = selected ? recordDraftKey(selected) : '';
  const draft = drafts[draftKey];
  const values = { ...selected?.values, ...draft?.values };
  const enabled = session !== null && selected !== null && page?.kind === kind
    && (kind !== 'text' || selected.id.startsWith(`${messageLanguage}/${textFile ?? page.textFile}:`));
  const fields = page?.fields ?? [];
  const hasEditableFields = fields.some(field => !field.readOnly && !selected?.readOnlyFields.includes(field.key));
  const label = (field: PlaField) => field.key.startsWith('tutor.') ? field.label : t(`pla.field.${field.key}`);
  const setValue = (key: string, value: string) => {
    if (!page || !selected || !enabled) return;
    const revision = page.revision;
    setDrafts(current => {
      const nextValues = { ...current[draftKey]?.values, [key]: value };
      if (value === selected.values[key]) delete nextValues[key];
      const next = { ...current };
      if (Object.keys(nextValues).length === 0) delete next[draftKey];
      else next[draftKey] = { revision: current[draftKey]?.revision ?? revision, values: nextValues, baseline: current[draftKey]?.baseline ?? { ...selected.values } };
      return next;
    });
    setError(null);
  };
  const stage = async (restoreBase = false) => {
    if (!page || !selected || !enabled || !hasEditableFields || loading || staging || isBusy || !restoreBase && !draft) return;
    const submitted = draft;
    const key = draftKey;
    const requested = restoreBase
      ? Object.fromEntries(fields.filter(field => !field.readOnly && !selected.readOnlyFields.includes(field.key)).map(field => [field.key, selected.values[field.key]]))
      : { ...draft.values };
    if (!restoreBase) {
      for (const field of fields.filter(field => Object.hasOwn(requested, field.key))) {
        if (field.kind === 'rows') {
          const rows = parseRows(requested[field.key]);
          if (rows.some(row => row.some(value => !/^\d+$/u.test(value)))) { setError(t('pla.invalid')); return; }
          requested[field.key] = JSON.stringify(rows.map(row => row.map(Number)));
        } else if (field.kind === 'decimal' ? !/^-?(?:\d+(?:\.\d*)?|\.\d+)$/u.test(requested[field.key]) || !Number.isFinite(Number(requested[field.key]))
          : field.kind !== 'text' && !/^-?\d+$/u.test(requested[field.key])) { setError(t(field.kind === 'decimal' ? 'pla.wild.invalid' : 'pla.invalid')); return; }
      }
    }
    setStaging(true); setError(null);
    try {
      const baselineIsCurrent = draft && Object.keys(draft.values).every(field => draft.baseline[field] === selected.values[field]);
      const accepted = await onStage({ kind, id: selected.id, values: requested, revision: restoreBase || baselineIsCurrent ? page.revision : draft.revision, restoreBase });
      if (!accepted) { setError(t('pla.failed')); return; }
      setDrafts(current => {
        if (current[key] !== submitted) {
          const newer = current[key];
          return newer && !restoreBase ? { ...current, [key]: { ...newer, baseline: { ...newer.baseline, ...requested } } } : current;
        }
        const next = { ...current }; delete next[key]; return next;
      });
      setRefresh(value => value + 1);
    } catch (reason) { setError(String(reason)); }
    finally { setStaging(false); }
  };
  const rows = useMemo(() => parseRows(values.entries ?? '[]'), [values.entries]);
  const changeRows = (next: string[][]) => setValue('entries', JSON.stringify(next.map(row => row.map(value => /^\d+$/u.test(value) ? Number(value) : value))));
  const addRow = () => {
    if (kind === 'learnsets') {
      const move = page?.rowFields.find(field => field.key === 'move')?.choices?.find(choice => !rows.some(row => row[2] === choice.value));
      if (!move) return;
      const level = rows.at(-1)?.[0] || '1';
      changeRows([...rows, [level, level, move.value]]);
    } else {
      const species = page?.rowFields.find(field => field.key === 'species')?.choices?.find(choice => choice.value !== selected?.id.split(':')[0])?.value ?? '25';
      changeRows([...rows, ['4', '0', species, '0', '1']]);
    }
  };
  const input = (field: PlaField, value: string, change: (value: string) => void, disabled: boolean, id: string) =>
    field.kind === 'boolean' ? <input id={id} type="checkbox" aria-label={label(field)} checked={value === '1'} disabled={disabled} onChange={event => change(event.target.checked ? '1' : '0')} />
      : field.choices ? <SearchableOptionInput id={id} ariaLabel={label(field)} value={value} disabled={disabled} onChange={change}
          options={(field.choices.some(choice => choice.value === value) ? field.choices : [{ value, label: value }, ...field.choices])
            .map(choice => ['method', 'category', 'growthRate', 'gender'].includes(field.key) && field.choices?.some(item => item.value === choice.value)
              ? { ...choice, label: t(`pla.choice.${field.key}.${choice.value}`) }
              : /^move[1-4]$/u.test(field.key) && choice.value === '0' ? { ...choice, label: t('pla.unspecifiedMove') } : choice)} isFiniteCatalog localizeOptions={false} />
        : field.kind === 'text' ? <textarea id={id} value={value} disabled={disabled} rows={8} onChange={event => change(event.target.value)} />
        : <input id={id} type="text" inputMode={field.kind === 'decimal' ? 'decimal' : 'numeric'} value={value} disabled={disabled} onChange={event => change(event.target.value)} />;
  const actions = session ? <EditorSessionBarActions>
        <button type="button" className="primary-button" disabled={!enabled || !draft || loading || staging || isBusy} onClick={() => void stage()}>
          <Save size={16} aria-hidden="true" />{translateLiteral(staging ? 'Staging' : 'Stage')}
        </button>
        <button type="button" className="secondary-button" disabled={!enabled || !hasEditableFields || loading || staging || isBusy} onClick={() => void stage(true)}>
          <RotateCcw size={16} aria-hidden="true" />{t('pla.restore')}
        </button>
        <button type="button" className="secondary-button" disabled={!draft || staging} onClick={() => setDrafts(current => { const next = { ...current }; delete next[draftKey]; return next; })}>
          <X size={16} aria-hidden="true" />{t('pla.discard')}
        </button>
        <button type="button" className="danger-button" disabled={staging || isBusy} onClick={() => onCancel(() => setDrafts({}))}>
          <X size={16} aria-hidden="true" />{translateLiteral('Cancel')}
        </button>
      </EditorSessionBarActions> : null;
  const refreshButton = <button type="button" className="secondary-button compact-button" disabled={loading || staging} onClick={() => setRefresh(value => value + 1)}>
    <RefreshCw size={16} aria-hidden="true" />{translateLiteral('Refresh')}
  </button>;
  if (kind === 'trainers' || kind === 'gifts' || kind === 'static') {
    return <FocusedEditorWorkspace><PlaBattleWorkspace kind={kind} page={page} selected={selected} query={query} offset={offset} loading={loading}
      onQuery={value => { setQuery(value); setOffset(0); }} onOffset={setOffset} onSelect={setSelectedId} refresh={refreshButton}
      sessionBar={<EditorSessionBar canEdit={page !== null && !loading} isEditing={session !== null} isStarting={isStarting} label={translateLiteral(getWorkbenchCapabilityRegistration(section).label)} onStart={onStart} />}
      actions={actions} fieldLabel={label} renderPokemon={renderPokemon}
      valuesFor={record => ({ ...record.values, ...drafts[recordDraftKey(record)]?.values })}
      hasDraft={record => Boolean(drafts[recordDraftKey(record)])}
      pendingIds={new Set(session?.pendingEdits.filter(edit => edit.owner === 'pla.loose.v1' && edit.recordId?.startsWith(kind + '/')).map(edit => edit.recordId!.slice(kind.length + 1)) ?? [])}
      renderInput={field => {
        const disabled = !enabled || field.readOnly || Boolean(selected?.readOnlyFields.includes(field.key));
        return field.kind === 'number' && !field.choices ? <input id={`pla-field-${field.key}`} type="number" min={field.minimum ?? undefined} max={field.maximum ?? undefined}
          value={values[field.key] ?? ''} disabled={disabled} onChange={event => setValue(field.key, event.target.value)} />
          : input(field.kind === 'boolean' ? { ...field, kind: 'number', choices: [{ value: '0', label: translateLiteral('No') }, { value: '1', label: translateLiteral('Yes') }] } : field,
            values[field.key] ?? '', value => setValue(field.key, value), disabled, `pla-field-${field.key}`);
      }} /></FocusedEditorWorkspace>;
  }
  const visiblePage = page?.kind === kind ? page : null;
  if (kind === 'encounters' || kind === 'spawns') return <FocusedEditorWorkspace><PlaWildWorkspace kind={kind} page={visiblePage} selected={visiblePage ? selected : null}
    query={query} offset={offset} loading={loading} refresh={refreshButton} actions={actions} fieldLabel={label} renderPokemon={renderPokemon}
    onQuery={value => { setQuery(value); setOffset(0); }} onOffset={setOffset} onSelect={setSelectedId}
    onKind={value => { setEconomyTab(value); setOffset(0); setQuery(''); }}
    sessionBar={<EditorSessionBar canEdit={visiblePage !== null && !loading} isEditing={session !== null} isStarting={isStarting}
      label={translateLiteral('Wild Encounters')} onStart={onStart} />}
    valuesFor={record => ({ ...record.values, ...drafts[recordDraftKey(record)]?.values })} hasDraft={record => Boolean(drafts[recordDraftKey(record)])}
    pendingIds={new Set(session?.pendingEdits.filter(edit => edit.owner === 'pla.loose.v1' && edit.recordId?.startsWith(kind + '/')).map(edit => edit.recordId!.slice(kind.length + 1)) ?? [])}
    renderInput={field => input(field, values[field.key] ?? '', value => setValue(field.key, value),
      !enabled || field.readOnly || Boolean(selected?.readOnlyFields.includes(field.key)), 'pla-field-' + field.key)} /></FocusedEditorWorkspace>;
  if (isPlaEconomyKind(kind)) {
    const tabs: PlaKind[] = section === 'shops' ? ['shops', 'ginkgo'] : section === 'research' ? ['research', 'ranks'] : [];
    return <FocusedEditorWorkspace><PlaEconomyWorkspace kind={kind} page={visiblePage} selected={visiblePage ? selected : null}
      query={query} offset={offset} loading={loading} onQuery={value => { setQuery(value); setOffset(0); }} onOffset={setOffset} onSelect={setSelectedId}
      refresh={refreshButton} actions={actions} fieldLabel={label}
      sessionBar={<EditorSessionBar canEdit={visiblePage !== null && !loading} isEditing={session !== null} isStarting={isStarting}
        label={t(workbenchSectionLocalization[section].label)} onStart={onStart} />}
      valuesFor={record => ({ ...record.values, ...drafts[recordDraftKey(record)]?.values })} hasDraft={record => Boolean(drafts[recordDraftKey(record)])}
      pendingIds={new Set(session?.pendingEdits.filter(edit => edit.owner === 'pla.loose.v1' && edit.recordId?.startsWith(kind + '/')).map(edit => edit.recordId!.slice(kind.length + 1)) ?? [])}
      renderInput={field => input(field, values[field.key] ?? '', value => setValue(field.key, value), !enabled || field.readOnly || Boolean(selected?.readOnlyFields.includes(field.key)), `pla-field-${field.key}`)}
      controls={tabs.length ? <nav className="text-category-browser" aria-label={t('pla.editor')}><div className="text-category-tabs" role="tablist" aria-label={t('pla.editor')}>
        {tabs.map(value => <button type="button" role="tab" aria-selected={kind === value} tabIndex={kind === value ? 0 : -1}
          className={kind === value ? 'is-active' : ''} key={value} onClick={() => { setEconomyTab(value); setOffset(0); }}
          onKeyDown={event => {
            const index = tabs.indexOf(value);
            const next = event.key === 'ArrowRight' ? (index + 1) % tabs.length : event.key === 'ArrowLeft' ? (index + tabs.length - 1) % tabs.length
              : event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 : null;
            if (next === null) return;
            event.preventDefault(); setEconomyTab(tabs[next]); setOffset(0);
            (event.currentTarget.parentElement?.children[next] as HTMLButtonElement | undefined)?.focus();
          }}>{t(`pla.kind.${value}`)}</button>)}
      </div></nav> : null} /></FocusedEditorWorkspace>;
  }
  return <FocusedEditorWorkspace><PlaDataWorkspace kind={kind} page={visiblePage} selected={visiblePage ? selected : null} query={query} offset={offset} loading={loading}
    onQuery={value => { setQuery(value); setOffset(0); }} onOffset={setOffset} onSelect={setSelectedId} refresh={refreshButton}
    sessionBar={<EditorSessionBar canEdit={visiblePage !== null && !loading} isEditing={session !== null} isStarting={isStarting} label={translateLiteral(getWorkbenchCapabilityRegistration(section).label)} onStart={onStart} />}
    actions={actions} fieldLabel={label} renderPokemon={renderPokemon}
    valuesFor={record => ({ ...record.values, ...drafts[recordDraftKey(record)]?.values })} hasDraft={record => Boolean(drafts[recordDraftKey(record)])}
    pendingIds={new Set(session?.pendingEdits.filter(edit => edit.owner === 'pla.loose.v1' && edit.recordId?.startsWith(kind + '/')).map(edit => edit.recordId!.slice(kind.length + 1)) ?? [])}
    renderInput={field => input(field.kind === 'boolean' && kind !== 'compatibility'
      ? { ...field, kind: 'number', choices: [{ value: '0', label: translateLiteral('No') }, { value: '1', label: translateLiteral('Yes') }] } : field,
      values[field.key] ?? '', value => setValue(field.key, value), !enabled || field.readOnly || Boolean(selected?.readOnlyFields.includes(field.key)), `pla-field-${field.key}`)}
    rowEditor={kind === 'learnsets' || kind === 'evolutions' ? <PlaPokemonRows key={`${kind}:${selected?.id}`} kind={kind} rows={rows} fields={page?.rowFields ?? []}
      enabled={enabled} onChange={changeRows} onAdd={addRow} fieldLabel={label} renderInput={input} /> : null}
    controls={<>
      {section === 'pokemon' ? <nav className="text-category-browser" aria-label={t('pla.editor')}><div className="text-category-tabs" role="tablist" aria-label={t('pla.editor')}>
        {pokemonKinds.map(value => <button type="button" role="tab" id={`pla-data-tab-${value}`} aria-controls={`pla-data-panel-${value}`} aria-selected={kind === value} tabIndex={kind === value ? 0 : -1}
          className={kind === value ? 'is-active' : ''} key={value} onClick={() => { setPokemonKind(value); setOffset(0); }}
          onKeyDown={event => {
            const index = pokemonKinds.indexOf(value);
            const next = event.key === 'ArrowRight' ? (index + 1) % pokemonKinds.length : event.key === 'ArrowLeft' ? (index + pokemonKinds.length - 1) % pokemonKinds.length
              : event.key === 'Home' ? 0 : event.key === 'End' ? pokemonKinds.length - 1 : null;
            if (next === null) return;
            event.preventDefault(); setPokemonKind(pokemonKinds[next]); setOffset(0);
            (event.currentTarget.parentElement?.children[next] as HTMLButtonElement | undefined)?.focus();
          }}>{t(`pla.kind.${value}`)}</button>)}
      </div></nav> : null}
      {kind === 'text' ? <div className="pla-controls text-toolbar">
        {kind === 'text' ? <div className="pla-control"><label htmlFor="pla-language">{t('pla.language')}</label><SearchableOptionInput id="pla-language" ariaLabel={t('pla.language')} disabled={false} value={messageLanguage}
          onChange={value => { setMessageLanguage(value); setTextFile(null); setOffset(0); }}
          options={['English', 'French', 'German', 'Italian', 'JPN', 'JPN_KANJI', 'Korean', 'Simp_Chinese', 'Spanish', 'Trad_Chinese'].map(value => ({ value, label: t(`pla.language.${value}`) }))} isFiniteCatalog localizeOptions={false} /></div> : null}
        {kind === 'text' ? <div className="pla-control"><label htmlFor="pla-message-file">{t('pla.messageFile')}</label><SearchableOptionInput id="pla-message-file" ariaLabel={t('pla.messageFile')} disabled={false} value={textFile ?? page?.textFile ?? ''}
          onChange={value => { setTextFile(value); setOffset(0); }} options={(page?.textFiles ?? []).map(value => ({ value, label: value }))} isFiniteCatalog localizeOptions={false} /></div> : null}
      </div> : null}
    </>} /></FocusedEditorWorkspace>;
}

function parseRows(value: string): string[][] {
  try {
    const rows: unknown = JSON.parse(value);
    return Array.isArray(rows) && rows.every(row => Array.isArray(row)) ? rows.map(row => row.map(String)) : [];
  } catch { return []; }
}
