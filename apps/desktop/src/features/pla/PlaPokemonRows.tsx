/* SPDX-License-Identifier: GPL-3.0-only */
import { useState, type ReactNode } from 'react';
import { Plus, Trash2 } from 'lucide-react';
import type { PlaField } from '../../bridge/plaProjectBridge';
import { FieldLabel } from '../../components/FieldLabel';
import { useLocalization } from '../../localization';

const editableEvolutionMethods = new Set([1, 2, 3, 4, 8, 17, 18, 21, 22, 23, 24]);
type Props = {
  kind: 'learnsets' | 'evolutions'; rows: string[][]; fields: PlaField[]; enabled: boolean;
  onChange: (rows: string[][]) => void; onAdd: () => void; fieldLabel: (field: PlaField) => string;
  renderInput: (field: PlaField, value: string, change: (value: string) => void, disabled: boolean, id: string) => ReactNode;
};

export function PlaPokemonRows({ kind, rows, fields, enabled, onChange, onAdd, fieldLabel, renderInput }: Props) {
  const { t, translateLiteral } = useLocalization();
  const [requestedIndex, setRequestedIndex] = useState(0);
  const index = Math.min(requestedIndex, Math.max(0, rows.length - 1));
  const selected = rows[index];
  const isEvolution = kind === 'evolutions';
  const locked = (row: string[]) => isEvolution && !editableEvolutionMethods.has(Number(row[0]));
  const choiceLabel = (key: string, value: string) => {
    const field = fields.find(item => item.key === key);
    return field?.choices?.find(choice => choice.value === value)?.label ?? value;
  };
  const renderFields = (row: string[], rowIndex: number, inline: boolean) => fields.map((field, column) => {
    const id = `pla-row-${rowIndex}-${column}`;
    const inlineClass = inline ? `learnset-inline-field ${column === 0 ? 'learnset-level-field' : column === 1 ? 'learnset-inline-metadata' : 'learnset-move-field'}` : '';
    return <div className={`path-field editable-field-control ${inlineClass} ${locked(row) ? 'editable-field-disabled' : ''}`} key={field.key}>
      <FieldLabel htmlFor={id} label={`${fieldLabel(field)} ${rowIndex + 1}`} />
      {renderInput(field.choicesByMethod ? { ...field, choices: field.choicesByMethod[row[0]] ?? null } : field,
        row[column] ?? '', value => onChange(rows.map((item, i) => i === rowIndex ? item.map((cell, j) => j === column ? value : cell) : item)),
        !enabled || locked(row), id)}
    </div>;
  });
  const removeButton = <button className="secondary-button danger-icon-button" type="button" disabled={!enabled || !selected || locked(selected)}
    onClick={() => onChange(rows.filter((_, i) => i !== index))}>
    <Trash2 size={16} aria-hidden="true" />{t('pla.remove')}
  </button>;
  return <div className="learnset-editor">
    <h4>{t(`pla.kind.${kind}`)}</h4>
    <ul className="learnset-list">
      {rows.map((row, rowIndex) => <li className="learnset-list-item" key={rowIndex}>
        {!isEvolution && index === rowIndex && enabled ? <div className="learnset-row learnset-inline-row pla-learnset-inline-row learnset-row-selected">
          <button type="button" className="secondary-button compact-button learnset-slot-cell" aria-label={`${translateLiteral('Slot')} ${rowIndex + 1}`} aria-pressed="true">#{rowIndex + 1}</button>
          {renderFields(row, rowIndex, true)}<div className="learnset-inline-actions">{removeButton}</div>
        </div> :
        <button type="button" className={`learnset-row pla-pokemon-detail-row ${isEvolution ? 'evolution-row' : ''} ${index === rowIndex ? 'learnset-row-selected' : ''}`}
          aria-pressed={index === rowIndex} aria-label={`${translateLiteral('Slot')} ${rowIndex + 1}`} onClick={() => setRequestedIndex(rowIndex)}>
          <span>#{rowIndex + 1}</span>
          <strong data-localization-ignore="true">{choiceLabel(isEvolution ? 'species' : 'move', row[2])}</strong>
          <span>{isEvolution ? (editableEvolutionMethods.has(Number(row[0])) ? t(`pla.choice.method.${row[0]}`) : row[0]) : `${t('pla.field.level')}: ${row[0]}`}</span>
          <span>{isEvolution ? `${t('pla.field.form')}: ${row[3]}` : `${t('pla.field.mastery')}: ${row[1]}`}</span>
          {locked(row) ? <span>{translateLiteral('Read-only')}</span> : null}
        </button>}
      </li>)}
    </ul>
    {selected && (isEvolution || !enabled) ? <div className={`learnset-edit-grid ${isEvolution ? 'evolution-edit-grid' : ''}`}>
      {renderFields(selected, index, false)}
      <div className="learnset-button-row">
        {locked(selected) ? <span className="field-note">{translateLiteral('Read-only')}</span> : null}
        {removeButton}
      </div>
    </div> : !selected ? <p className="empty-copy">{t('pla.empty')}</p> : null}
    <button className="secondary-button learnset-add-button" type="button" disabled={!enabled} onClick={() => { setRequestedIndex(rows.length); onAdd(); }}>
      <Plus size={16} aria-hidden="true" />{t('pla.add')}
    </button>
  </div>;
}
