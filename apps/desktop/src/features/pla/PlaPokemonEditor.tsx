/* SPDX-License-Identifier: GPL-3.0-only */
import { plaFieldHelp } from './plaFieldHelp';
import { useState } from 'react';
import { Dna, RefreshCw, RotateCcw, Save, Search, ShieldCheck, Trash2, X } from 'lucide-react';
import type { PlaField } from '../../bridge/plaProjectBridge';
import { EditorSessionBar, EditorSessionBarActions } from '../../components/EditorSessionBar';
import { FieldLabel } from '../../components/FieldLabel';
import { PokemonSearchBox, PokemonTable } from '../../components/PokemonSelection';
import { PokemonYieldConfirmationModal } from '../../components/PokemonYieldConfirmationModal';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { Metric } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import type { PlaEditorProps } from './PlaEditorSection';
import { PlaPokemonRows } from './PlaPokemonRows';
import { isPlaFieldValueValid, parsePlaRows } from './plaDraftValidation';
import { usePlaPokemonEditor, type PlaPokemonKind } from './usePlaPokemonEditor';

const stats = ['hp', 'attack', 'defense', 'specialAttack', 'specialDefense', 'speed'];
const personalGroups = [
  { label: 'Base Stats', keys: stats },
  { label: 'Types', keys: ['type1', 'type2'] },
  { label: 'Traits', keys: ['catchRate', 'baseFriendship', 'growthRate', 'baseExperience'] }
];

export function PlaPokemonEditor(props: PlaEditorProps) {
  const { t, translateLiteral } = useLocalization();
  const editor = usePlaPokemonEditor(props);
  const [compatibilitySearch, setCompatibilitySearch] = useState('');
  const [yieldAction, setYieldAction] = useState<'remove' | 'restore' | null>(null);
  const { pages, loading, staging, recordFor, valuesFor, setValue, currentKinds } = editor;
  const selected = recordFor('personal');
  const enabled = props.session !== null && editor.isCurrentScope;
  const busy = staging || props.isBusy;
  const fieldLabel = (field: PlaField) => field.key.startsWith('tutor.') ? field.label : t(`pla.field.${field.key}`);
  const input = (field: PlaField, value: string, change: (value: string) => void, disabled: boolean, id: string) => {
    const invalid = !isPlaFieldValueValid(field, value);
    if (field.kind === 'boolean') return <input type="checkbox" id={id} aria-label={fieldLabel(field)} checked={value === '1'} disabled={disabled} onChange={event => change(event.target.checked ? '1' : '0')} />;
    if (field.choices) return <SearchableOptionInput id={id} ariaLabel={fieldLabel(field)} ariaInvalid={invalid || undefined} value={value} disabled={disabled} onChange={change}
      options={(field.choices.some(choice => choice.value === value) ? field.choices : [{ value, label: value }, ...field.choices]).map(choice =>
        ['method', 'growthRate'].includes(field.key) && field.choices?.some(item => item.value === choice.value)
          ? { ...choice, label: t(`pla.choice.${field.key}.${choice.value}`) }
          : field.key === 'method' ? { ...choice, label: t('pla.preservedMethod', { id: choice.value }) }
          : field.key === 'form' && choice.value === '65535' ? { ...choice, label: t('pla.keepForm') } : choice)} isFiniteCatalog localizeOptions={false} />;
    return <input type="text" inputMode="numeric" id={id} aria-invalid={invalid || undefined} value={value} disabled={disabled} onChange={event => change(event.target.value)} />;
  };
  const renderField = (kind: PlaPokemonKind, field: PlaField) => {
    const record = recordFor(kind);
    if (!record) return null;
    const value = valuesFor(kind, record)[field.key];
    const changed = value !== record.values[field.key], invalid = changed && !isPlaFieldValueValid(field, value);
    const readOnly = field.readOnly || record.readOnlyFields.includes(field.key);
    return <div className={`path-field editable-field-control ${changed ? 'editable-field-changed' : ''} ${invalid ? 'editable-field-invalid' : ''}`} key={field.key}>
      <FieldLabel htmlFor={`pla-${kind}-${field.key}`} label={fieldLabel(field)} help={plaFieldHelp(t, kind, field, fieldLabel(field))} />
      {input(field, value, next => setValue(kind, field.key, next), !enabled || readOnly, `pla-${kind}-${field.key}`)}
      {invalid ? <small className="editable-field-error">{t('pla.invalid')}</small> : changed || readOnly ? <small className="editable-field-status">{translateLiteral(readOnly ? 'Read-only' : 'Changed')}</small> : null}
    </div>;
  };
  const recordName = (label: string) => label.replace(/^\d+[:\s]+/u, '');
  const choiceLabel = (key: string, value: string) => key === 'growthRate' ? t('pla.choice.growthRate.' + value)
    : pages.personal?.fields.find(field => field.key === key)?.choices?.find(choice => choice.value === value)?.label ?? value;
  const records = editor.matchingRecords.map(record => {
    const [species, form] = record.id.split(':').map(Number), values = valuesFor('personal', record);
    return { ...record, personalId: species * 256 + form, speciesId: species, form, name: recordName(record.label), formLabel: record.metadata?.formName ? `${record.metadata.formName} (${form})` : String(form),
      displayId: record.id, type1: choiceLabel('type1', values.type1), type2: choiceLabel('type2', values.type2) };
  });
  const pending = new Set(records.filter(record => currentKinds.some(kind => editor.hasDraft(kind, record))
    || props.session?.pendingEdits.some(edit => edit.owner === 'pla.loose.v1' && currentKinds.some(kind => edit.recordId === `${kind}/${record.id}`)))
    .map(record => record.personalId));
  const values = selected ? valuesFor('personal', selected) : {};
  const selectedIdentity = selected?.id.split(':') ?? [];
  const rowEditor = (kind: 'learnsets' | 'evolutions') => {
    const record = recordFor(kind), page = pages[kind];
    if (!record || !page) return <p className="empty-copy">{t('pla.empty')}</p>;
    const rows = parsePlaRows(valuesFor(kind, record).entries) ?? [];
    const change = (next: string[][]) => setValue(kind, 'entries', JSON.stringify(next.map(row => row.map(value => /^\d+$/u.test(value) ? Number(value) : value))));
    return <PlaPokemonRows key={`${kind}:${record.id}`} kind={kind} rows={rows} fields={page.rowFields} enabled={enabled}
      source={props} recordId={record.id} recordLabel={recordName(record.label)} busy={busy || loading}
      onChange={change} fieldLabel={fieldLabel} renderInput={input} />;
  };
  const refresh = <button type="button" className="secondary-button compact-button" disabled={loading || busy} onClick={editor.refresh}><RefreshCw size={16} aria-hidden="true" />{translateLiteral('Refresh')}</button>;
  return <><section className="panel wide-panel pla-editor pla-data-editor pla-pokemon-section" aria-labelledby="pla-editor-title">
    <div className="panel-heading"><Dna size={18} aria-hidden="true" /><h2 id="pla-editor-title">{translateLiteral('Pokemon')}</h2>{refresh}</div>
    <div className="items-toolbar pokemon-toolbar"><PokemonSearchBox disabled={false} value={editor.query} onChange={editor.onQuery} />
      <div className="pokemon-toolbar-side"><div className="pokemon-toolbar-metrics">
        <Metric label={translateLiteral('Loaded records')} value={String(pages.personal?.total ?? 0)} />
        <Metric label={translateLiteral('Pending changes')} value={String(pending.size)} />
      </div><div className="pokemon-toolbar-actions">
        <button type="button" className="primary-button compact-button" disabled={!enabled || loading || busy || editor.dirty} onClick={() => setYieldAction('remove')}><Trash2 size={14} aria-hidden="true" />{translateLiteral('Remove EXP Yield')}</button>
        <button type="button" className="primary-button compact-button" disabled={!enabled || loading || busy || editor.dirty} onClick={() => setYieldAction('restore')}><RefreshCw size={14} aria-hidden="true" />{translateLiteral('Restore EXP Yield')}</button>
      </div></div>
    </div>
    {loading ? <p role="status">{translateLiteral('Loading')}</p> : null}
    <div className="items-layout pla-pokemon-layout">
      <aside className="item-inspector pla-pokemon-inspector" aria-label={translateLiteral('Selected Pokemon')}>
        <EditorSessionBar canEdit={Boolean(selected) && !loading} isEditing={props.session !== null} isStarting={props.isStarting} label="Pokemon" onStart={props.onStart}
          selection={selected ? <div className="pokemon-session-selection">
            {props.renderPokemon?.({ species: selectedIdentity[0], form: selectedIdentity[1] }, recordName(selected.label), pages.personal?.pokemonSpriteNames[selectedIdentity[0]], 'pokemon-session-sprite')}
            <strong data-localization-ignore="true">{selected.label}</strong>
          </div> : null} />
        {props.session ? <EditorSessionBarActions>
          <button type="button" className="primary-button" aria-busy={staging || undefined} disabled={!enabled || loading || busy || !editor.changedCount(currentKinds) || editor.invalidKinds.some(kind => kind !== 'tutors')} onClick={() => void editor.stage(currentKinds)}>
            {staging ? <RefreshCw className="button-busy-icon" size={16} aria-hidden="true" /> : <Save size={16} aria-hidden="true" />}{translateLiteral(staging ? 'Staging' : 'Stage')}
          </button>
          {editor.nextId ? <button type="button" className="secondary-button" disabled={busy} onClick={editor.reviewNext}>{t('editorDrafts.reviewNext')}</button> : null}
          <button type="button" className="secondary-button" disabled={!enabled || loading || busy || !selected} onClick={() => void editor.stage(currentKinds, true)}><RotateCcw size={16} aria-hidden="true" />{t('pla.restore')}</button>
          <button type="button" className="secondary-button" disabled={busy || !editor.changedCount(currentKinds)} onClick={() => editor.discard(currentKinds)}>{t('pla.discard')}</button>
          <button type="button" className="danger-button" disabled={busy} onClick={() => props.onCancel(() => props.setDrafts({}))}><X size={16} aria-hidden="true" />{translateLiteral('Cancel')}</button>
          <span className="draft-action-summary">{t('editorDrafts.summary.pokemon', { count: editor.outstandingIds.length })}</span>
        </EditorSessionBarActions> : null}
        <div className="pla-pokemon-selected-stack">
          <div className="panel-heading"><ShieldCheck size={18} aria-hidden="true" /><h3>{translateLiteral('Selected Pokemon')}</h3></div>
          {selected ? <>
            <dl className="item-provenance-list pokemon-summary-metadata">
              <div><dt>{translateLiteral('ID')}</dt><dd>{selected.id}</dd></div>
              <div><dt>{translateLiteral('Types')}</dt><dd>{[...new Set([choiceLabel('type1', values.type1), choiceLabel('type2', values.type2)])].join(' / ')}</dd></div>
            </dl>
            <div className="inspector-block pla-pokemon-stats-block"><h4>{translateLiteral('Base Stats')}</h4><dl className="item-provenance-list compact-dl">
              {stats.map(key => <div key={key}><dt>{t('pla.field.' + key)}</dt><dd>{values[key]}</dd></div>)}
              <div><dt>{translateLiteral('Total')}</dt><dd>{stats.every(key => /^\d+$/u.test(values[key])) ? stats.reduce((sum, key) => sum + Number(values[key]), 0) : '-'}</dd></div>
            </dl></div>
            <div className="inspector-block pla-pokemon-traits-block"><h4>{translateLiteral('Traits')}</h4><dl className="item-provenance-list compact-dl">
              {personalGroups[2].keys.map(key => <div key={key}><dt>{t('pla.field.' + key)}</dt><dd>{choiceLabel(key, values[key])}</dd></div>)}
            </dl></div>
            <div className="inspector-block pla-pokemon-personal-edit-block"><h4>{translateLiteral('Personal Edit')}</h4><div className="editable-field-groups">
              {personalGroups.map(group => <fieldset className="editable-field-group" key={group.label}><legend>{translateLiteral(group.label)}</legend><div className="editable-field-grid">
                {pages.personal?.fields.filter(field => group.keys.includes(field.key)).map(field => renderField('personal', field))}
              </div></fieldset>)}
            </div></div>
          </> : <p className="empty-copy">{t('pla.empty')}</p>}
        </div>
        <div className="pla-pokemon-left-stack">
          <PokemonTable records={records} selectedId={records.find(record => record.id === editor.selectedId)?.personalId} pendingIds={pending}
            onSelect={id => editor.setSelectedId(records.find(record => record.personalId === id)?.id ?? null)} formatName={record => record.label} resetKey={editor.query} />
          {selected ? <>
            <div className="inspector-block pla-pokemon-learnset-block">{rowEditor('learnsets')}<p className="field-note">{t('pla.notice.learnsets')}</p></div>
            <div className="inspector-block pla-pokemon-evolutions-block">{rowEditor('evolutions')}<p className="field-note">{t('pla.notice.evolutions')}</p></div>
            <div className="inspector-block pla-pokemon-compatibility-block"><h4>{t('pla.kind.compatibility')}</h4>
              <label className="search-box compatibility-search"><Search size={16} aria-hidden="true" /><input type="search" value={compatibilitySearch} aria-label={t('pla.searchCompatibility')} placeholder={t('pla.searchCompatibility')} onChange={event => setCompatibilitySearch(event.target.value)} /></label>
              <ul className="compatibility-list">{pages.compatibility?.fields.filter(field => fieldLabel(field).toLocaleLowerCase().includes(compatibilitySearch.toLocaleLowerCase())).map(field => {
                const record = recordFor('compatibility');
                return record ? <li key={field.key}><label className="compatibility-toggle">
                  {input(field, valuesFor('compatibility', record)[field.key], value => setValue('compatibility', field.key, value), !enabled, 'pla-compatibility-' + field.key)}
                  <span data-localization-ignore="true">{fieldLabel(field)}</span><small>{field.key.slice(6)}</small>
                </label></li> : null;
              })}</ul><p className="field-note">{t('pla.notice.compatibility')}</p>
            </div>
          </> : null}
          <div className="inspector-block pla-pokemon-tutors-block"><h4>{t('pla.kind.tutors')}</h4><p className="field-note">{t('pla.notice.tutors')}</p>
            <SearchableOptionInput ariaLabel={t('pla.kind.tutors')} value={editor.tutorId ?? ''} disabled={false} onChange={editor.setTutorId}
              options={(pages.tutors?.records ?? []).map(record => ({ value: record.id, label: record.label }))} isFiniteCatalog localizeOptions={false} />
            <div className="editable-field-grid">{pages.tutors?.fields.map(field => renderField('tutors', field))}</div>
            <div className="draft-action-row"><button type="button" className="primary-button" disabled={!enabled || loading || busy || !editor.hasDraft('tutors') || editor.invalidKinds.includes('tutors')} onClick={() => void editor.stage(['tutors'])}><Save size={16} aria-hidden="true" />{translateLiteral('Stage')}</button>
              <button type="button" className="secondary-button" disabled={!enabled || loading || busy} onClick={() => void editor.stage(['tutors'], true)}>{t('pla.restore')}</button>
              <button type="button" className="secondary-button" disabled={busy || !editor.hasDraft('tutors')} onClick={() => editor.discard(['tutors'])}>{t('pla.discard')}</button></div>
          </div>
        </div>
      </aside>
    </div>
  </section>{yieldAction ? <PokemonYieldConfirmationModal action={yieldAction} kind="exp" onCancel={() => setYieldAction(null)} onConfirm={() => {
    const restore = yieldAction === 'restore'; setYieldAction(null); void editor.stageExperienceYield(restore);
  }} /> : null}</>;
}
