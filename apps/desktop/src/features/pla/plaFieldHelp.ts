/* SPDX-License-Identifier: GPL-3.0-only */
import type { PlaField, PlaKind } from '../../bridge/plaProjectBridge';
import { resolveFieldHelp, type FieldHelpDomain, type FieldHelpTranslator } from '../../fieldHelpCatalog';

export function plaFieldHelp(t: FieldHelpTranslator, kind: PlaKind, field: PlaField, label: string): string {
  const detail = field.key === 'mastery' ? 'pla.help.mastery' : field.key.startsWith('effort') ? 'pla.help.effort'
    : /^mastered[1-4]$/u.test(field.key) ? 'pla.help.mastered' : null;
  if (detail) return t(detail);
  const domain: FieldHelpDomain | undefined = ['personal', 'learnsets', 'evolutions', 'compatibility', 'tutors'].includes(kind) ? 'pokemon'
    : kind === 'static' ? 'staticEncounters' : ['trainers', 'gifts', 'moves', 'items', 'shops', 'encounters'].includes(kind) ? kind as FieldHelpDomain : undefined;
  const fieldId = kind === 'learnsets' ? 'learnset' + field.key : kind === 'evolutions'
    ? 'evolution' + (field.key === 'species' || field.key === 'form' ? 'target' : '') + field.key : field.key === 'battleLevel' ? 'level' : field.key;
  return resolveFieldHelp(t, { domain, fieldId, label, minimum: field.minimum, maximum: field.maximum,
    optionCount: field.choices?.length, optionValues: field.choices?.map(choice => choice.value) });
}
