/* SPDX-License-Identifier: GPL-3.0-only */
import { plaFieldHelp } from './plaFieldHelp';
import { useRef, useState, type ReactNode } from 'react';
import { ArrowDown, ArrowUp, GripVertical, Plus, Trash2 } from 'lucide-react';
import type { PlaField } from '../../bridge/plaProjectBridge';
import { FieldLabel } from '../../components/FieldLabel';
import { useLocalization } from '../../localization';
import { usePlaRowClipboard } from './usePlaRowClipboard';
import { isPlaFieldValueValid, resolvePlaField, updatePlaDependentValues, validPlaLearnsetRows } from './plaDraftValidation';
import type { PlaEditorProps } from './PlaEditorSection';

const editableEvolutionMethods = new Set([1, 2, 3, 4, 8, 17, 18, 21, 22, 23, 24]);
type Props = {
  source: Pick<PlaEditorProps, 'paths' | 'session' | 'bridge'>; recordId: string; recordLabel: string; busy: boolean;
  kind: 'learnsets' | 'evolutions'; rows: string[][]; fields: PlaField[]; enabled: boolean;
  onChange: (rows: string[][]) => void; fieldLabel: (field: PlaField) => string;
  renderInput: (field: PlaField, value: string, change: (value: string) => void, disabled: boolean, id: string) => ReactNode;
};

export function PlaPokemonRows({ kind, rows, fields, enabled, onChange, fieldLabel, renderInput, source, recordId, recordLabel, busy }: Props) {
  const { t, translateLiteral } = useLocalization();
  const [requestedIndex, setRequestedIndex] = useState(0);
  const [newRow, setNewRow] = useState<Record<string, string>>(() => Object.fromEntries(fields.map(field => [field.key,
    field.key === 'method' ? '4' : field.key === 'level' || field.key === 'mastery' ? rows.at(-1)?.[0] ?? '1'
      : field.key === 'species' ? field.choices?.find(choice => choice.value !== recordId.split(':')[0])?.value ?? '25'
        : field.key === 'move' ? field.choices?.find(choice => !rows.some(row => row[2] === choice.value))?.value ?? '' : '0'])));
  const drag = useRef<{ index: number; signature: string; token: string } | null>(null);
  const rowKeys = useRef({ scope: `${kind}:${recordId}`, next: 0, keys: [] as number[] });
  if (rowKeys.current.scope !== `${kind}:${recordId}`) {
    rowKeys.current = { scope: `${kind}:${recordId}`, next: 0, keys: [] };
  }
  while (rowKeys.current.keys.length < rows.length) rowKeys.current.keys.push(rowKeys.current.next++);
  rowKeys.current.keys.length = rows.length;
  const index = Math.min(requestedIndex, Math.max(0, rows.length - 1));
  const selected = rows[index];
  const isEvolution = kind === 'evolutions';
  const canEdit = enabled && !busy;
  const rowValues = (row: string[]) => Object.fromEntries(fields.map((field, index) => [field.key, row[index]]));
  const clipboard = usePlaRowClipboard({ source, kind: 'learnsets', identity: recordId, enabled: enabled && !isEvolution, busy,
    getRow: id => rows[Number(id)] ? { label: t('rowClipboard.learnset.target', { pokemon: recordLabel, slot: Number(id) + 1 }), fields, values: rowValues(rows[Number(id)]) } : null,
    onPaste: (id, values) => {
      const next = rows.map((row, index) => index === Number(id) ? fields.map((field, column) => values[field.key] ?? row[column]) : row);
      if (!validPlaLearnsetRows(next)) return false;
      onChange(next); return true;
    } });
  const locked = (row: string[]) => isEvolution && !editableEvolutionMethods.has(Number(row[0]));
  const choiceLabel = (key: string, value: string) => fields.find(item => item.key === key)?.choices?.find(choice => choice.value === value)?.label ?? value;
  const renderFields = (row: string[], rowIndex: number, inline: boolean) => fields.map((field, column) => {
    const id = `pla-${kind}-row-${rowIndex}-${column}`;
    const inlineClass = inline ? `learnset-inline-field ${column === 0 ? 'learnset-level-field' : column === 1 ? 'learnset-inline-metadata' : 'learnset-move-field'}` : '';
    return <div className={`path-field editable-field-control ${inlineClass} ${locked(row) ? 'editable-field-disabled' : ''}`} key={field.key}>
      <FieldLabel htmlFor={id} label={`${isEvolution && field.key === 'level' ? t('pla.evolutionLevel') : fieldLabel(field)} ${rowIndex + 1}`} help={plaFieldHelp(t, kind, field, fieldLabel(field))} />
      {renderInput(resolvePlaField(field, rowValues(row)), row[column] ?? '', value => {
        const updated = updatePlaDependentValues(fields, rowValues(row), field.key, value);
        const next = rows.map((item, i) => i === rowIndex ? fields.map(field => updated[field.key]) : item);
        if (!isEvolution && field.key === 'level' && /^\d+$/u.test(value) && Number(value) <= 100) {
          const selectedRow = next[rowIndex];
          const ordered = next.map((row, index) => ({ row, key: rowKeys.current.keys[index] }))
            .sort((left, right) => Number(left.row[0]) - Number(right.row[0]));
          next.splice(0, next.length, ...ordered.map(({ row }) => row));
          rowKeys.current.keys = ordered.map(({ key }) => key);
          setRequestedIndex(next.indexOf(selectedRow));
        }
        onChange(next);
      }, !canEdit || locked(row), id)}
    </div>;
  });
  const moveRow = (from: number, to: number) => {
    if (!canEdit || isEvolution || from < 0 || from >= rows.length || to < 0 || to >= rows.length || from === to) return;
    // As in the other Pokemon editors, reorder moves while preserving each slot's level metadata.
    const moves = rows.map(row => row[2]); moves.splice(to, 0, moves.splice(from, 1)[0]);
    const next = rows.map((row, index) => [row[0], row[1], moves[index]]);
    if (!validPlaLearnsetRows(next)) return;
    onChange(next); setRequestedIndex(to);
  };
  const removeButton = <button className="secondary-button danger-icon-button" type="button" disabled={!canEdit || !selected || locked(selected)}
    onClick={() => { rowKeys.current.keys.splice(index, 1); onChange(rows.filter((_, i) => i !== index)); }}>
    <Trash2 size={16} aria-hidden="true" />{t('pla.remove')}
  </button>;
  const moveButtons = <>
    <button type="button" className="secondary-button icon-button" aria-label={translateLiteral('Move learnset row up')} disabled={!canEdit || index === 0} onClick={() => moveRow(index, index - 1)}><ArrowUp size={16} aria-hidden="true" /></button>
    <button type="button" className="secondary-button icon-button" aria-label={translateLiteral('Move learnset row down')} disabled={!canEdit || index >= rows.length - 1} onClick={() => moveRow(index, index + 1)}><ArrowDown size={16} aria-hidden="true" /></button>
  </>;
  const pendingRow = fields.map(field => newRow[field.key] ?? '');
  const withNewRow = isEvolution ? [...rows, pendingRow] : [...rows, pendingRow].sort((a, b) => Number(a[0]) - Number(b[0]));
  const canAdd = canEdit && rows.length < (isEvolution ? 64 : 256)
    && fields.every(field => isPlaFieldValueValid(resolvePlaField(field, newRow), newRow[field.key] ?? ''))
    && (isEvolution ? editableEvolutionMethods.has(Number(newRow.method)) : validPlaLearnsetRows(withNewRow));
  return <div className="learnset-editor">
    <h4>{t(`pla.kind.${kind}`)}</h4>
    {!isEvolution ? clipboard.content : null}
    <ul className="learnset-list">
      {rows.map((row, rowIndex) => <li className="learnset-list-item" key={rowKeys.current.keys[rowIndex]} tabIndex={isEvolution ? undefined : 0} {...(!isEvolution ? clipboard.bind(String(rowIndex)) : {})}
        onDragOver={event => { if (canEdit && drag.current) { event.preventDefault(); event.dataTransfer.dropEffect = 'move'; } }}
        onDrop={event => {
          event.preventDefault(); const active = drag.current; drag.current = null;
          if (active && active.signature === JSON.stringify(rows) && event.dataTransfer.getData('application/x-km-pla-learnset') === active.token) moveRow(active.index, rowIndex);
        }}>
        {!isEvolution && index === rowIndex && enabled ? <div className="learnset-row learnset-inline-row pla-learnset-inline-row learnset-row-selected">
          <button type="button" className="secondary-button compact-button learnset-slot-cell" aria-label={`${translateLiteral('Slot')} ${rowIndex + 1}`} aria-pressed="true">#{rowIndex + 1}</button>
          {renderFields(row, rowIndex, true)}<div className="learnset-inline-actions">{moveButtons}{removeButton}</div>
        </div> :
        <button type="button" className={`learnset-row pla-pokemon-detail-row ${isEvolution ? 'evolution-row' : ''} ${index === rowIndex ? 'learnset-row-selected' : ''}`}
          aria-pressed={index === rowIndex} aria-label={`${translateLiteral('Slot')} ${rowIndex + 1}`} onClick={() => setRequestedIndex(rowIndex)}>
          {!isEvolution ? <span className="learnset-drag-cell" aria-hidden="true" draggable={canEdit} onDragStart={event => {
            if (!canEdit) { event.preventDefault(); return; }
            const token = crypto.randomUUID(); drag.current = { index: rowIndex, signature: JSON.stringify(rows), token };
            event.dataTransfer.setData('application/x-km-pla-learnset', token); event.dataTransfer.effectAllowed = 'move';
          }} onDragEnd={() => { drag.current = null; }}><GripVertical size={15} /></span> : null}
          <span className="learnset-slot-cell">#{rowIndex + 1}</span>
          {!isEvolution ? <><span className="learnset-level-cell">{t('pla.field.level')}: {row[0]}</span><span className="learnset-mastery-cell">{t('pla.field.mastery')}: {row[1]}</span><strong className="learnset-move-name" data-localization-ignore="true">{choiceLabel('move', row[2])}</strong><span>{row[2]}</span></>
            : <><strong data-localization-ignore="true">{choiceLabel('species', row[2])}</strong><span>{editableEvolutionMethods.has(Number(row[0])) ? t(`pla.choice.method.${row[0]}`) : t('pla.preservedMethod', { id: row[0] })}</span><span>{row[3] === '65535' ? t('pla.keepForm') : `${t('pla.field.form')}: ${row[3]}`}</span></>}
          {locked(row) ? <span>{translateLiteral('Read-only')}</span> : null}
        </button>}
      </li>)}
    </ul>
    {selected && (isEvolution || !enabled) ? <div className={`learnset-edit-grid ${isEvolution ? 'evolution-edit-grid' : ''}`}>
      {renderFields(selected, index, false)}<div className="learnset-button-row">{locked(selected) ? <span className="field-note">{translateLiteral('Read-only')}</span> : null}{removeButton}</div>
    </div> : !selected ? <p className="empty-copy">{t('pla.empty')}</p> : null}
    <div className={`learnset-add-grid ${isEvolution ? 'evolution-edit-grid' : ''}`}>
      {fields.map(field => <div className="path-field editable-field-control" key={field.key}>
        <FieldLabel htmlFor={`pla-${kind}-new-${field.key}`} label={isEvolution && field.key === 'level' ? t('pla.evolutionLevel') : fieldLabel(field)} help={plaFieldHelp(t, kind, field, fieldLabel(field))} />
        {renderInput(resolvePlaField(field, newRow), newRow[field.key] ?? '', value => setNewRow(current => updatePlaDependentValues(fields, current, field.key, value)), !canEdit, `pla-${kind}-new-${field.key}`)}
      </div>)}
      <div className="learnset-button-row"><button className="secondary-button" type="button" disabled={!canAdd} onClick={() => {
        rowKeys.current.keys = withNewRow.map(row => row === pendingRow ? rowKeys.current.next++ : rowKeys.current.keys[rows.indexOf(row)]);
        onChange(withNewRow); setRequestedIndex(withNewRow.indexOf(pendingRow));
        if (!isEvolution) setNewRow(current => ({ ...current, move: fields.find(field => field.key === 'move')?.choices?.find(choice => !withNewRow.some(row => row[2] === choice.value))?.value ?? '' }));
      }}><Plus size={16} aria-hidden="true" />{t('pla.add')}</button></div>
    </div>
  </div>;
}
