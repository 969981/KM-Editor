/* SPDX-License-Identifier: GPL-3.0-only */
import type { ReactNode } from 'react';
import { Search, Trees } from 'lucide-react';
import type { PlaField, PlaPage, PlaRecord } from '../../bridge/plaProjectBridge';
import { FieldLabel } from '../../components/FieldLabel';
import { InteractiveTableRow, VirtualTableBody } from '../../components/VirtualTable';
import { Metric } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import type { PlaPokemonPreview } from './PlaBattleWorkspace';

type Props = {
  kind: 'encounters' | 'spawns'; page: PlaPage | null; selected: PlaRecord | null;
  query: string; offset: number; loading: boolean; sessionBar: ReactNode; actions: ReactNode; refresh: ReactNode;
  onQuery: (value: string) => void; onOffset: (value: number) => void; onSelect: (id: string) => void;
  onKind: (kind: 'encounters' | 'spawns') => void;
  valuesFor: (record: PlaRecord) => Record<string, string>; hasDraft: (record: PlaRecord) => boolean;
  pendingIds: ReadonlySet<string>; fieldLabel: (field: PlaField) => string;
  renderInput: (field: PlaField) => ReactNode; renderPokemon?: PlaPokemonPreview;
};

export function PlaWildWorkspace({ kind, page, selected, query, offset, loading, sessionBar, actions, refresh,
  onQuery, onOffset, onSelect, onKind, valuesFor, hasDraft, pendingIds, fieldLabel, renderInput, renderPokemon }: Props) {
  const { t, translateLiteral } = useLocalization();
  const records = page?.records ?? [];
  const isSpawn = kind === 'spawns';
  const fields = page?.fields ?? [];
  const values = selected ? valuesFor(selected) : {};
  const name = (record: PlaRecord) => fields.find(field => field.key === 'species')?.choices?.find(choice => choice.value === valuesFor(record).species)?.label
    ?? record.label.split(' / ').at(-1) ?? record.label;
  const status = (record: PlaRecord) => translateLiteral(hasDraft(record) ? 'Draft' : pendingIds.has(record.id) ? 'Pending changes'
    : record.readOnlyFields.length ? 'Read-only' : 'Editable');
  const groups = isSpawn ? records.map(record => ({ value: record.id, label: record.metadata.area + '/' + record.metadata.table })) : page?.groups ?? [];
  const fieldGroups = [
    { key: 'pokemon', title: translateLiteral('Pokemon'), fields: fields.filter(field => ['species', 'form', 'wildMin', 'wildMax', 'wildWeight'].includes(field.key)) },
    { key: 'traits', title: t('pla.wild.traits'), fields: fields.filter(field => field.key.startsWith('effort') || field.key === 'perfectIvs') },
    { key: 'eligibility', title: t('pla.wild.eligibility'), fields: fields.filter(field => field.kind === 'decimal') },
    { key: 'counts', title: t('pla.kind.spawns'), fields: fields.filter(field => field.key.startsWith('spawn')) }
  ].filter(group => group.fields.length);
  const tabs = ['encounters', 'spawns'] as const;
  return <section className="panel wide-panel pla-wild-editor" aria-labelledby="pla-wild-heading">
    <div className="panel-heading"><Trees size={18} aria-hidden="true" /><h2 id="pla-wild-heading">{translateLiteral('Wild Encounters')}</h2>{refresh}</div>
    <div className="items-toolbar encounters-toolbar">
      <label className="search-box items-search"><Search size={18} aria-hidden="true" /><input type="search" aria-label={translateLiteral('Search encounters')}
        placeholder={translateLiteral('Search encounters')} value={query} onChange={event => onQuery(event.target.value)} /></label>
      <Metric label={translateLiteral(isSpawn ? 'Records' : 'Loaded tables')} value={String(page?.total ?? 0)} />
      <Metric label={translateLiteral('Pending changes')} value={String(pendingIds.size)} />
    </div>
    <nav className="text-category-browser" aria-label={t('pla.editor')}><div className="text-category-tabs" role="tablist" aria-label={t('pla.editor')}>
      {tabs.map((value, index) => <button type="button" key={value} role="tab" aria-selected={kind === value} tabIndex={kind === value ? 0 : -1}
        className={kind === value ? 'is-active' : ''} onClick={() => onKind(value)}
        onKeyDown={event => {
          const next = event.key === 'ArrowLeft' || event.key === 'ArrowRight' ? 1 - index : event.key === 'Home' ? 0 : event.key === 'End' ? 1 : null;
          if (next === null) return;
          event.preventDefault(); onKind(tabs[next]); (event.currentTarget.parentElement?.children[next] as HTMLButtonElement | undefined)?.focus();
        }}>{t('pla.kind.' + value)}</button>)}
    </div></nav>
    {sessionBar}
    <p className="field-note">{t('pla.wild.notice')}</p>
    {loading ? <p role="status">{translateLiteral('Loading')}</p> : null}
    <div className="encounters-layout">
      <div className="encounters-table" role="table" aria-label={translateLiteral('Encounter tables')} aria-colcount={2} aria-rowcount={groups.length + 1}>
        <div className="encounters-row pla-wild-row encounters-row-heading" role="row"><span role="columnheader">{translateLiteral('Location')}</span><span role="columnheader">{translateLiteral('ID')}</span></div>
        <VirtualTableBody items={groups} getKey={group => group.value} resetKey={kind + ':' + query} renderRow={group =>
          <InteractiveTableRow className={'encounters-row pla-wild-row ' + ((isSpawn ? selected?.id === group.value : offset === Number(group.value)) ? 'encounters-row-selected' : '')}
            aria-selected={isSpawn ? selected?.id === group.value : offset === Number(group.value)} onClick={() => isSpawn ? onSelect(group.value) : onOffset(Number(group.value))}>
            <span role="cell" data-localization-ignore="true">{group.label.split('/')[0]}</span>
            <span role="cell" data-localization-ignore="true" title={group.label}>{group.label.split('/')[1]}</span>
          </InteractiveTableRow>} />
      </div>
      <aside className="encounter-inspector" aria-label={translateLiteral('Selected Encounter')}>
        <div className="panel-heading"><Trees size={18} aria-hidden="true" /><h3>{translateLiteral('Selected Encounter')}</h3></div>
        {selected ? <>
          <dl className="item-provenance-list">
            <div><dt>{translateLiteral('Location')}</dt><dd data-localization-ignore="true">{selected.metadata.area}</dd></div>
            <div><dt>{translateLiteral('ID')}</dt><dd>{selected.metadata.table}</dd></div>
            {!isSpawn ? <><div><dt>{t('pla.wild.profile')}</dt><dd>{t('pla.wild.' + selected.metadata.alpha)}</dd></div>
              <div><dt>{t('pla.wild.shared')}</dt><dd>{selected.metadata.consumers}</dd></div></> : null}
          </dl>
          {!isSpawn ? <div className="encounter-edit-form">
            <div className="trainer-party-card-grid" aria-label={translateLiteral('Encounter slots')}>
              {records.map(record => <button type="button" key={record.id} className="trainer-party-card" aria-pressed={selected.id === record.id}
                aria-label={translateLiteral('Slot') + ' ' + record.metadata.slot + ': ' + name(record)} onClick={() => onSelect(record.id)}>
                {renderPokemon?.(valuesFor(record), name(record), page?.pokemonSpriteNames[valuesFor(record).species], 'trainer-party-sprite')}
                <strong data-localization-ignore="true">{name(record)}</strong>
                <span>{t('pla.wild.' + record.metadata.alpha)}</span><span>{status(record)}</span>
              </button>)}
            </div>
          </div> : null}
          <div className="encounter-edit-form">
            {selected.readOnlyFields.length ? <p className="field-note">{t('pla.wild.protected')}</p> : null}
            {!isSpawn ? <>
              <p className="field-note">{t('pla.wild.levelHelp', { min: selected.metadata.defaultMin, max: selected.metadata.defaultMax })}</p>
              <p className="field-note">{t('pla.wild.traitHelp')}</p>
              {selected.metadata.condition !== 'none' ? <p className="field-note">{t('pla.wild.condition')}</p> : null}
            </> : <p className="field-note">{t('pla.wild.spawnHelp')}</p>}
            <div className="editable-field-groups">{fieldGroups.map(group =>
              <fieldset className="editable-field-group" key={group.key}><legend>{group.title}</legend>
                {group.key === 'eligibility' ? <p className="field-note">{t('pla.wild.multiplierHelp')}</p> : null}
                <div className="editable-field-grid">{group.fields.map(field => <div className="editable-field" key={field.key}>
                  <FieldLabel htmlFor={'pla-field-' + field.key} label={fieldLabel(field)} />
                  <div className="editable-field-control">{renderInput(field)}</div>
                  {field.kind === 'decimal' && values[field.key] === '-1' ? <span className="field-note">{values.species === selected.values.species && values.form === selected.values.form
                    ? t('pla.wild.inherited', { value: selected.metadata['default.' + field.key] }) : t('pla.wild.inheritedPending')}</span> : null}
                </div>)}</div>
              </fieldset>)}</div>
            {selected.relatedRecords.length ? <details><summary>{t('pla.wild.references')}</summary><ul className="pla-wild-references">
              {selected.relatedRecords.map(related => <li key={related.id} data-localization-ignore="true">{related.label}</li>)}
            </ul></details> : null}
            {actions}
          </div>
        </> : <p className="empty-copy">{translateLiteral('No results')}</p>}
      </aside>
    </div>
    {isSpawn ? <div className="pla-pagination">
      <button type="button" className="secondary-button" disabled={loading || offset === 0} onClick={() => onOffset(Math.max(0, offset - 100))}>{translateLiteral('Previous')}</button>
      <span>{Math.min(offset + 1, page?.total ?? 0)} / {page?.total ?? 0}</span>
      <button type="button" className="secondary-button" disabled={loading || offset + 100 >= (page?.total ?? 0)} onClick={() => onOffset(offset + 100)}>{translateLiteral('Next')}</button>
    </div> : null}
  </section>;
}
