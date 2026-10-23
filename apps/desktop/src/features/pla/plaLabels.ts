/* SPDX-License-Identifier: GPL-3.0-only */
import type { FieldHelpTranslator } from '../../fieldHelpCatalog';
import type { PlaField } from '../../bridge/plaProjectBridge';

export function plaPokemonLabel(fields: readonly PlaField[], values: Record<string, string>, fallback: string): string {
  const form = fields.find(field => field.key === 'form')?.dependentChoices?.[values.species]?.find(choice => choice.value === values.form)?.label;
  if (form) return form.replace(/^\d+\s+/u, '');
  const species = fields.find(field => field.key === 'species')?.choices?.find(choice => choice.value === values.species)?.label;
  return species ? species + (values.form === '0' ? '' : ` (${values.form})`) : fallback.replace(/^\d+\s+/u, '');
}

export function plaAreaLabel(t: FieldHelpTranslator, area: string): string {
  return /^ha_area0[0-6]$/u.test(area) ? t('pla.area.' + area) : area;
}

export function plaAreaQuery(t: FieldHelpTranslator, query: string): string {
  const text = query.trim().toLocaleLowerCase();
  if (!text) return query;
  const matches = Array.from({ length: 7 }, (_, index) => 'ha_area0' + index)
    .filter(area => plaAreaLabel(t, area).toLocaleLowerCase().includes(text));
  return matches.length === 1 ? matches[0] : query;
}
