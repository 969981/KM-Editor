/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useMemo, useRef, useState, type Dispatch, type SetStateAction } from 'react';
import { Dna, Plus, RefreshCw, RotateCcw, Save, Trash2, X } from 'lucide-react';
import type { EditSession, ProjectPaths } from '../../bridge/contracts';
import type { ProjectBridge } from '../../bridge/projectBridge';
import type { PlaField, PlaKind, PlaPage, StagePlaRequest } from '../../bridge/plaProjectBridge';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { FocusedEditorWorkspace } from '../../components/FocusedEditorWorkspace';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { useLocalization } from '../../localization';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import './plaEditor.css';

export type PlaDrafts = Record<string, { revision: string; values: Record<string, string>; baseline: Record<string, string> }>;
type Props = {
  section: 'pokemon' | 'moves' | 'text'; paths: ProjectPaths; session: EditSession | null; bridge: ProjectBridge;
  drafts: PlaDrafts; setDrafts: Dispatch<SetStateAction<PlaDrafts>>;
  isStarting: boolean; isBusy: boolean; onStart: () => void;
  onStage: (request: Omit<StagePlaRequest, 'paths' | 'session'>) => Promise<boolean>;
  onCancel: (discard: () => void) => void; onDirtyChange: (dirty: boolean) => void;
};
const pokemonKinds: PlaKind[] = ['personal', 'learnsets', 'evolutions', 'compatibility', 'tutors'];
const protectedEvolutionMethods = new Set([1, 2, 3, 4, 8, 17, 18, 21, 22, 23, 24]);

export function PlaEditorSection({ section, paths, session, bridge, drafts, setDrafts, isStarting, isBusy,
  onStart, onStage, onCancel, onDirtyChange }: Props) {
  const { t, translateLiteral } = useLocalization();
  const [pokemonKind, setPokemonKind] = useState<PlaKind>('personal');
  const kind = section === 'pokemon' ? pokemonKind : section;
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
  const draftKey = selected ? `${section}/${kind}/${paths.gameTextLanguage ?? 'English'}/${selected.id}` : '';
  const draft = drafts[draftKey];
  const values = { ...selected?.values, ...draft?.values };
  const enabled = session !== null && selected !== null && page?.kind === kind
    && (kind !== 'text' || selected.id.startsWith(`${messageLanguage}/${textFile ?? page.textFile}:`));
  const fields = page?.fields ?? [];
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
    if (!page || !selected || !enabled || loading || staging || isBusy || !restoreBase && !draft) return;
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
        } else if (field.kind !== 'text' && !/^-?\d+$/u.test(requested[field.key])) { setError(t('pla.invalid')); return; }
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
    field.kind === 'boolean' ? <input id={id} type="checkbox" checked={value === '1'} disabled={disabled} onChange={event => change(event.target.checked ? '1' : '0')} />
      : field.choices ? <SearchableOptionInput id={id} ariaLabel={label(field)} value={value} disabled={disabled} onChange={change}
          options={(field.choices.some(choice => choice.value === value) ? field.choices : [{ value, label: value }, ...field.choices])
            .map(choice => ['method', 'category', 'growthRate'].includes(field.key) && field.choices?.some(item => item.value === choice.value)
              ? { ...choice, label: t(`pla.choice.${field.key}.${choice.value}`) } : choice)} isFiniteCatalog localizeOptions={false} />
        : field.kind === 'text' ? <textarea id={id} value={value} disabled={disabled} rows={8} onChange={event => change(event.target.value)} />
        : <input id={id} type="text" inputMode="numeric" value={value} disabled={disabled} onChange={event => change(event.target.value)} />;
  return <FocusedEditorWorkspace>
    <section className="panel wide-panel pla-editor" aria-labelledby="pla-editor-title">
      <div className="panel-heading"><Dna size={18} aria-hidden="true" /><h2 id="pla-editor-title">{t(`pla.kind.${kind}`)}</h2>
        <button type="button" className="secondary-button compact-button" disabled={loading || staging} onClick={() => setRefresh(value => value + 1)}>
          <RefreshCw size={16} aria-hidden="true" />{translateLiteral('Refresh')}
        </button>
      </div>
      <EditorSessionBar canEdit={page !== null && !loading} isEditing={session !== null} isStarting={isStarting} label={t(`pla.kind.${kind}`)} onStart={onStart} />
      {session ? <EditorSessionBarActions>
        <button type="button" className="primary-button" disabled={!enabled || !draft || loading || staging || isBusy} onClick={() => void stage()}>
          <Save size={16} aria-hidden="true" />{translateLiteral(staging ? 'Staging' : 'Stage')}
        </button>
        <button type="button" className="secondary-button" disabled={!enabled || loading || staging || isBusy} onClick={() => void stage(true)}>
          <RotateCcw size={16} aria-hidden="true" />{t('pla.restore')}
        </button>
        <button type="button" className="secondary-button" disabled={!draft || staging} onClick={() => setDrafts(current => { const next = { ...current }; delete next[draftKey]; return next; })}>
          <X size={16} aria-hidden="true" />{t('pla.discard')}
        </button>
        <button type="button" className="danger-button" disabled={staging || isBusy} onClick={() => onCancel(() => setDrafts({}))}>
          <X size={16} aria-hidden="true" />{translateLiteral('Cancel')}
        </button>
      </EditorSessionBarActions> : null}
      <div className="pla-controls">
        {section === 'pokemon' ? <div className="pla-control"><label htmlFor="pla-kind">{t('pla.editor')}</label><SearchableOptionInput id="pla-kind" ariaLabel={t('pla.editor')} disabled={false} value={kind}
          onChange={value => { setPokemonKind(value as PlaKind); setOffset(0); }}
          options={pokemonKinds.map(value => ({ value, label: t(`pla.kind.${value}`) }))} isFiniteCatalog /></div> : null}
        {kind === 'text' ? <div className="pla-control"><label htmlFor="pla-language">{t('pla.language')}</label><SearchableOptionInput id="pla-language" ariaLabel={t('pla.language')} disabled={false} value={messageLanguage}
          onChange={value => { setMessageLanguage(value); setTextFile(null); setOffset(0); }}
          options={['English', 'French', 'German', 'Italian', 'JPN', 'JPN_KANJI', 'Korean', 'Simp_Chinese', 'Spanish', 'Trad_Chinese'].map(value => ({ value, label: t(`pla.language.${value}`) }))} isFiniteCatalog localizeOptions={false} /></div> : null}
        {kind === 'text' ? <div className="pla-control"><label htmlFor="pla-message-file">{t('pla.messageFile')}</label><SearchableOptionInput id="pla-message-file" ariaLabel={t('pla.messageFile')} disabled={false} value={textFile ?? page?.textFile ?? ''}
          onChange={value => { setTextFile(value); setOffset(0); }} options={(page?.textFiles ?? []).map(value => ({ value, label: value }))} isFiniteCatalog localizeOptions={false} /></div> : null}
        <label>{translateLiteral('Search')}<input type="search" value={query} onChange={event => { setQuery(event.target.value); setOffset(0); }} /></label>
      </div>
      {page?.notices.length ? <p className="muted">{t(`pla.notice.${kind}`)}</p> : null}
      {loading ? <p role="status">{translateLiteral('Loading')}</p> : null}
      <div className="pla-record-layout">
        <div>
          <p>{t('pla.record')}</p>
          <div className="pla-record-list" aria-label={t('pla.record')}>
            {page?.records.map(record => <button type="button" className="secondary-button compact-button" key={record.id} aria-pressed={record.id === selectedId}
              onClick={() => setSelectedId(record.id)}>{record.label}</button>)}
          </div>
          <div className="pla-pagination">
            <button type="button" className="secondary-button compact-button" disabled={offset === 0 || loading} onClick={() => setOffset(value => Math.max(0, value - 100))}>{translateLiteral('Previous')}</button>
            <span>{page ? `${Math.min(offset + 1, page.total)} - ${Math.min(offset + 100, page.total)} / ${page.total}` : ''}</span>
            <button type="button" className="secondary-button compact-button" disabled={!page || offset + 100 >= page.total || loading} onClick={() => setOffset(value => value + 100)}>{translateLiteral('Next')}</button>
          </div>
        </div>
        <div className="pla-fields">
          {selected ? fields.map(field => field.kind === 'rows' ? <div className="pla-rows" key={field.key}>
            <table><thead><tr>{page?.rowFields.map(column => <th key={column.key}>{label(column)}</th>)}<th>{t('pla.actions')}</th></tr></thead>
              <tbody>{rows.map((row, index) => {
                const locked = kind === 'evolutions' && !protectedEvolutionMethods.has(Number(row[0]));
                return <tr key={index}>{page?.rowFields.map((column, columnIndex) => <td key={column.key}>
                  <label className="sr-only" htmlFor={`pla-row-${index}-${columnIndex}`}>{label(column)} {index + 1}</label>
                  {input(column.choicesByMethod ? { ...column, choices: column.choicesByMethod[row[0]] ?? null } : column,
                    row[columnIndex] ?? '', value => changeRows(rows.map((item, itemIndex) => itemIndex === index ? item.map((cell, cellIndex) => cellIndex === columnIndex ? value : cell) : item)), !enabled || locked, `pla-row-${index}-${columnIndex}`)}
                </td>)}<td><button type="button" className="secondary-button compact-button" disabled={!enabled || locked} aria-label={t('pla.remove')} onClick={() => changeRows(rows.filter((_, i) => i !== index))}><Trash2 size={16} aria-hidden="true" /></button></td></tr>;
              })}</tbody></table>
            <button type="button" className="secondary-button compact-button" disabled={!enabled} onClick={addRow}><Plus size={16} aria-hidden="true" />{t('pla.add')}</button>
          </div> : <div key={field.key} className={field.kind === 'text' ? 'pla-field pla-text-field' : 'pla-field'}>
            <label htmlFor={`pla-field-${field.key}`}>{label(field)}{selected.readOnlyFields.includes(field.key) ? ` (${translateLiteral('Read-only')})` : ''}</label>
            {input(field, values[field.key] ?? '', value => setValue(field.key, value), !enabled || field.readOnly || selected.readOnlyFields.includes(field.key), `pla-field-${field.key}`)}
          </div>) : <p>{t('pla.empty')}</p>}
        </div>
      </div>
    </section>
  </FocusedEditorWorkspace>;
}

function parseRows(value: string): string[][] {
  try {
    const rows: unknown = JSON.parse(value);
    return Array.isArray(rows) && rows.every(row => Array.isArray(row)) ? rows.map(row => row.map(String)) : [];
  } catch { return []; }
}
