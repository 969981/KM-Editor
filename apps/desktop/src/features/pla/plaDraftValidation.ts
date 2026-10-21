/* SPDX-License-Identifier: GPL-3.0-only */
import type { PlaField, PlaPage, PlaRecord } from '../../bridge/plaProjectBridge';

export function resolvePlaField(field: PlaField, values: Record<string, string>): PlaField {
  if (field.dependsOn && field.dependentChoices) return { ...field, choices: field.dependentChoices[values[field.dependsOn]] ?? [] };
  if (field.choicesByMethod) return { ...field, choices: field.choicesByMethod[values.method] ?? null };
  return field;
}

export function updatePlaDependentValues(fields: PlaField[], values: Record<string, string>, key: string, value: string): Record<string, string> {
  const updated = { ...values, [key]: value };
  for (const field of fields.filter(field => field.dependsOn === key)) {
    const choices = resolvePlaField(field, updated).choices;
    if (choices?.length && !choices.some(choice => choice.value === updated[field.key])) updated[field.key] = choices[0].value;
  }
  return updated;
}

export function isPlaFieldValueValid(field: PlaField, value: string): boolean {
  if (field.kind === 'text') return true;
  if (field.kind === 'rows') return parsePlaRows(value) !== null;
  const decimal = field.kind === 'decimal' || field.kind === 'factor';
  if (!(decimal ? /^-?(?:\d+(?:\.\d*)?|\.\d+)$/u : /^-?\d+$/u).test(value)) return false;
  const number = Number(value);
  return Number.isFinite(number) && (decimal || Number.isSafeInteger(number))
    && (field.kind !== 'decimal' || number === -1 || number >= 0)
    && (field.minimum === null || number >= field.minimum)
    && (field.maximum === null || number <= field.maximum)
    && (field.kind !== 'boolean' || value === '0' || value === '1')
    && (!field.choices || field.choices.some(choice => choice.value === value));
}

export function parsePlaRows(value: string): string[][] | null {
  try {
    const rows: unknown = JSON.parse(value);
    return Array.isArray(rows) && rows.every(row => Array.isArray(row) && row.every(cell => typeof cell === 'number' || typeof cell === 'string'))
      ? rows.map(row => row.map(String)) : null;
  } catch { return null; }
}

export function validPlaDraft(page: PlaPage, record: PlaRecord, values: Record<string, string>): boolean {
  return Object.entries(values).every(([key, value]) => {
    const field = page.fields.find(candidate => candidate.key === key);
    if (!field || field.readOnly || record.readOnlyFields.includes(key) || !isPlaFieldValueValid(resolvePlaField(field, { ...record.values, ...values }), value)) return false;
    if (field.kind !== 'rows') return true;
    const rows = parsePlaRows(value)!;
    if (page.kind === 'learnsets' && !validPlaLearnsetRows(rows)) return false;
    const originalRows = parsePlaRows(record.values[key]) ?? [];
    return rows.every(row => originalRows.some(original => JSON.stringify(original) === JSON.stringify(row)) || row.length === page.rowFields.length && row.every((cell, index) => {
      const rowField = page.rowFields[index];
      return isPlaFieldValueValid(resolvePlaField(rowField, Object.fromEntries(page.rowFields.map((field, index) => [field.key, row[index]]))), cell);
    }));
  });
}

export function validPlaLearnsetRows(rows: string[][]): boolean {
  return rows.length <= 256 && new Set(rows.map(row => row[2])).size === rows.length
    && rows.every((row, index) => row.length === 3 && row.every(value => /^\d+$/u.test(value))
      && Number(row[0]) <= 100 && Number(row[1]) <= 100 && (row[1] === '0' || Number(row[1]) >= Number(row[0]))
      && (index === 0 || Number(row[0]) >= Number(rows[index - 1][0])));
}
