/* SPDX-License-Identifier: GPL-3.0-only */
import { useState, type ReactNode } from 'react';
import { Search, ShieldCheck } from 'lucide-react';
import type { PlaField, PlaKind, PlaPage, PlaRecord } from '../../bridge/plaProjectBridge';
import { FieldLabel } from '../../components/FieldLabel';
import { PokemonSearchBox } from '../../components/PokemonSelection';
import { InteractiveTableRow, VirtualTableBody } from '../../components/VirtualTable';
import { Metric } from '../../components/workflowPanels';
import { useLocalization } from '../../localization';
import { getWorkbenchCapabilityRegistration } from '../../workbench/capabilityRegistry';
import type { PlaPokemonPreview } from './PlaBattleWorkspace';

type DataKind = Exclude<PlaKind, 'trainers' | 'gifts' | 'static' | 'items' | 'crafting' | 'shops' | 'ginkgo' | 'drops' | 'research' | 'ranks'>;
type Props = {
  kind: DataKind; page: PlaPage | null; selected: PlaRecord | null; query: string; offset: number; loading: boolean;
  controls: ReactNode; sessionBar: ReactNode; actions: ReactNode; refresh: ReactNode; rowEditor: ReactNode;
  onQuery: (query: string) => void; onOffset: (offset: number) => void; onSelect: (id: string) => void;
  valuesFor: (record: PlaRecord) => Record<string, string>; hasDraft: (record: PlaRecord) => boolean;
  pendingIds: ReadonlySet<string>; fieldLabel: (field: PlaField) => string;
  renderInput: (field: PlaField) => ReactNode; renderPokemon?: PlaPokemonPreview;
};
const statKeys = ['hp', 'attack', 'defense', 'specialAttack', 'specialDefense', 'speed'];

export function PlaDataWorkspace({ kind, page, selected, query, offset, loading, controls, sessionBar, actions, refresh, rowEditor,
  onQuery, onOffset, onSelect, valuesFor, hasDraft, pendingIds, fieldLabel, renderInput, renderPokemon }: Props) {
  const { t, translateLiteral } = useLocalization();
  const [compatibilitySearch, setCompatibilitySearch] = useState('');
  const isPokemon = ['personal', 'learnsets', 'evolutions', 'compatibility'].includes(kind);
  const section = kind === 'moves' || kind === 'text' ? kind : 'pokemon';
  const registration = getWorkbenchCapabilityRegistration(section);
  const Icon = registration.icon;
  const title = translateLiteral(registration.label);
  const rowClass = kind === 'text' ? 'text-row' : kind === 'moves' || kind === 'tutors' ? 'moves-row' : 'items-row';
  const tableClass = kind === 'text' ? 'text-table' : kind === 'moves' || kind === 'tutors' ? 'moves-table' : 'items-table pokemon-table';
  const layoutClass = kind === 'text' ? 'text-layout' : kind === 'moves' || kind === 'tutors' ? 'items-layout moves-layout' : 'items-layout';
  const inspectorTitle = kind === 'text' ? 'Selected Text' : kind === 'moves' ? 'Selected Move' : kind === 'tutors' ? t('pla.kind.tutors') : 'Selected Pokemon';
  const choiceLabel = (key: string, value: string) => key === 'category' ? t(`pla.choice.category.${value}`)
    : page?.fields.find(field => field.key === key)?.choices?.find(choice => choice.value === value)?.label ?? value;
  const recordName = (record: PlaRecord) => record.label.replace(/^\d+[:\s]+/u, '');
  const status = (record: PlaRecord) => translateLiteral(hasDraft(record) ? 'Draft' : pendingIds.has(record.id) ? 'Pending changes' : 'Editable');
  const columns = kind === 'text' ? ['Message key', 'File', 'Line', 'Value', 'Status']
    : kind === 'moves' ? ['ID', 'Name', 'Type', 'Category', 'Power', 'Accuracy', 'PP', 'Status']
    : kind === 'tutors' ? ['Slot', 'Move', 'Price', 'Status'] : ['ID', 'Name', kind === 'personal' ? 'Types' : 'Form'];
  const cells = (record: PlaRecord) => {
    const value = valuesFor(record);
    if (kind === 'text') return [record.label, page?.textFile ?? '', record.id.slice(record.id.lastIndexOf(':') + 1), value.text, status(record)];
    if (kind === 'moves') return [record.id, recordName(record), choiceLabel('type', value.type), choiceLabel('category', value.category), value.power, value.accuracy, value.pp, status(record)];
    if (kind === 'tutors') return [record.id, choiceLabel('move', value.move), value.price, status(record)];
    const types = [...new Set([choiceLabel('type1', value.type1), choiceLabel('type2', value.type2)])].join(' / ');
    return [record.id, recordName(record), kind === 'personal' ? types : record.id.split(':')[1]];
  };
  const fields = page?.fields ?? [];
  const groups = kind === 'personal' ? [
    { title: translateLiteral('Base Stats'), fields: fields.filter(field => statKeys.includes(field.key)) },
    { title: translateLiteral('Traits'), fields: fields.filter(field => !statKeys.includes(field.key)) }
  ] : kind === 'moves' ? [
    { title: translateLiteral('Battle'), fields: fields.filter(field => ['type', 'category', 'power', 'accuracy', 'pp', 'priority'].includes(field.key)) },
    { title: t('pla.group.styles'), fields: fields.filter(field => ['canStyle', 'agilePower', 'strongPower', 'strongAccuracy'].includes(field.key)) },
    { title: t('pla.group.actionDelays'), fields: fields.filter(field => field.key.startsWith('actionSpeed')) }
  ] : [{ title: t(`pla.kind.${kind}`), fields }];
  const renderField = (field: PlaField) => {
    const changed = selected && valuesFor(selected)[field.key] !== selected.values[field.key];
    const readOnly = field.readOnly || selected?.readOnlyFields.includes(field.key);
    return <div className={`path-field editable-field-control ${changed ? 'editable-field-changed' : ''} ${readOnly ? 'editable-field-disabled' : ''}`} key={field.key}>
      <FieldLabel htmlFor={`pla-field-${field.key}`} label={fieldLabel(field)} />{renderInput(field)}
      {readOnly || changed ? <small className="editable-field-status">{translateLiteral(readOnly ? 'Read-only' : 'Changed')}</small> : null}
    </div>;
  };
  const identity = selected?.id.split(':') ?? [];
  const stats = selected ? statKeys.map(key => valuesFor(selected)[key]) : [];
  const statTotal = stats.length === statKeys.length && stats.every(value => /^\d+$/u.test(value))
    ? String(stats.reduce((sum, value) => sum + Number(value), 0)) : '-';
  return <section className={`panel wide-panel pla-editor pla-data-editor pla-data-${kind}`} aria-labelledby="pla-editor-title">
    <div className="panel-heading"><Icon size={18} aria-hidden="true" /><h2 id="pla-editor-title">{title}</h2>{refresh}</div>
    {controls}
    <div className={`items-toolbar ${isPokemon ? 'pokemon-toolbar' : kind === 'text' ? 'text-toolbar' : 'moves-toolbar'}`}>
      {isPokemon ? <PokemonSearchBox disabled={false} value={query} onChange={onQuery} /> : <label className="search-box items-search"><Search size={18} aria-hidden="true" />
        <input type="search" aria-label={translateLiteral(kind === 'moves' ? 'Search moves' : 'Search')} placeholder={translateLiteral(kind === 'moves' ? 'Search moves' : 'Search')}
          value={query} onChange={event => onQuery(event.target.value)} /></label>}
      <Metric label={translateLiteral('Loaded records')} value={String(page?.total ?? 0)} />
      <Metric label={translateLiteral('Pending changes')} value={String(pendingIds.size)} />
    </div>
    {sessionBar}{actions}
    {page?.notices.length ? <p className="field-note">{t(`pla.notice.${kind}`)}</p> : null}
    {loading ? <p role="status">{translateLiteral('Loading')}</p> : null}
    <div className={layoutClass} id={`pla-data-panel-${kind}`} role={section === 'pokemon' ? 'tabpanel' : undefined} aria-labelledby={section === 'pokemon' ? `pla-data-tab-${kind}` : undefined}>
      <div className={tableClass} role="table" aria-label={t(`pla.kind.${kind}`)} aria-colcount={columns.length} aria-rowcount={(page?.records.length ?? 0) + 1}>
        <div className={`${rowClass} ${rowClass}-heading pla-data-row`} role="row">{columns.map(column => <span role="columnheader" key={column}>{column === 'Message key' ? t('text.messageKey') : translateLiteral(column)}</span>)}</div>
        <VirtualTableBody items={page?.records ?? []} getKey={record => record.id} resetKey={`${kind}:${offset}:${query}`}
          renderRow={record => <InteractiveTableRow aria-selected={selected?.id === record.id}
            className={`${rowClass} pla-data-row ${selected?.id === record.id ? `${rowClass}-selected` : ''} ${hasDraft(record) || pendingIds.has(record.id) ? `${rowClass}-pending` : ''}`}
            onClick={() => onSelect(record.id)}>{cells(record).map((value, i) => <span key={i} role="cell" title={value} data-localization-ignore="true"
              className={kind === 'text' && i === 3 ? 'text-value-summary' : undefined}>{value}</span>)}</InteractiveTableRow>} />
      </div>
      <aside className={kind === 'text' ? 'text-inspector' : 'item-inspector'} aria-label={translateLiteral(inspectorTitle)}>
        <div className="panel-heading"><ShieldCheck size={18} aria-hidden="true" /><h3>{translateLiteral(inspectorTitle)}</h3></div>
        {selected ? <>
          {isPokemon ? <div className="pokemon-summary-card">
            {renderPokemon?.({ species: identity[0], form: identity[1] }, recordName(selected), page?.pokemonSpriteNames[identity[0]], 'pokemon-summary-sprite')}
            <div className="pokemon-summary-main"><strong className="pokemon-summary-title" data-localization-ignore="true">{recordName(selected)}</strong>
              <span className="pokemon-summary-subtitle">{translateLiteral('ID')}: {selected.id}</span></div>
          </div> : null}
          <dl className="item-provenance-list">
            <div><dt>{kind === 'text' ? t('text.messageKey') : translateLiteral('Name')}</dt><dd data-localization-ignore="true">{kind === 'tutors' ? choiceLabel('move', valuesFor(selected).move) : selected.label}</dd></div>
            {kind === 'text' ? <div><dt>{translateLiteral('File')}</dt><dd>{page?.textFile}</dd></div> : <div><dt>{translateLiteral('Status')}</dt><dd>{status(selected)}</dd></div>}
          </dl>
          <div className={kind === 'text' ? 'text-edit-form' : 'item-edit-form'}>
            {kind === 'learnsets' || kind === 'evolutions' ? rowEditor : kind === 'compatibility' ? <div className="compatibility-editor">
              <h4>{t('pla.kind.compatibility')}</h4>
              <label className="search-box compatibility-search"><Search size={16} aria-hidden="true" /><input type="search" value={compatibilitySearch}
                aria-label={t('pla.searchCompatibility')} placeholder={t('pla.searchCompatibility')} onChange={event => setCompatibilitySearch(event.target.value)} /></label>
              <ul className="compatibility-list">{fields.filter(field => fieldLabel(field).toLocaleLowerCase().includes(compatibilitySearch.toLocaleLowerCase())).map(field =>
                <li key={field.key}><label className="compatibility-toggle">{renderInput(field)}<span data-localization-ignore="true">{fieldLabel(field)}</span>
                  <small>{field.key.slice('tutor.'.length)}</small></label></li>)}</ul>
            </div> : kind === 'text' ? fields.map(renderField) : <div className="editable-field-groups">
              {groups.map(group => <fieldset className="editable-field-group" key={group.title}><legend>{group.title}
                {kind === 'personal' && group.fields.some(field => field.key === 'hp') ? <span className="pokemon-stat-total">{translateLiteral('Total')}: {statTotal}</span> : null}
              </legend><div className="editable-field-grid">{group.fields.map(renderField)}</div></fieldset>)}
            </div>}
          </div>
        </> : <p className="empty-copy">{t('pla.empty')}</p>}
      </aside>
    </div>
    <nav className="text-page-bar pla-pagination" aria-label={t('text.pagination.ariaLabel')}>
      <span>{page ? `${Math.min(offset + 1, page.total)} - ${Math.min(offset + 100, page.total)} / ${page.total}` : ''}</span>
      <div><button type="button" className="secondary-button compact-button" disabled={offset === 0 || loading} onClick={() => onOffset(Math.max(0, offset - 100))}>{translateLiteral('Previous')}</button>
        <button type="button" className="secondary-button compact-button" disabled={!page || offset + 100 >= page.total || loading} onClick={() => onOffset(offset + 100)}>{translateLiteral('Next')}</button></div>
    </nav>
  </section>;
}
