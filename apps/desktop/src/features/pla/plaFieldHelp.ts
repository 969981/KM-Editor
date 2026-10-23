/* SPDX-License-Identifier: GPL-3.0-only */
import type { PlaField, PlaKind } from '../../bridge/plaProjectBridge';
import { resolveFieldHelp, type FieldHelpDomain, type FieldHelpTranslator } from '../../fieldHelpCatalog';

export function plaFieldHelp(t: FieldHelpTranslator, kind: PlaKind, field: PlaField, label: string): string {
  const description = plaFieldDescription(t, kind, field, label);
  if (field.kind === 'boolean' || field.minimum === null || field.maximum === null || field.choices) return description;
  const range = t(field.kind === 'decimal' || field.kind === 'factor' ? 'pla.rules.factorRange' : 'pla.rules.range',
    { min: field.minimum, max: field.maximum });
  return description + ' ' + range;
}

function plaFieldDescription(t: FieldHelpTranslator, kind: PlaKind, field: PlaField, label: string): string {
  const wild = ['encounters', 'outbreakEncounters', 'distortionEncounters'].includes(kind);
  const detail = field.key === 'mastery' ? 'pla.help.mastery' : field.key.startsWith('effort') ? wild ? 'pla.wild.traitHelp' : 'pla.help.effort'
    : /^mastered[1-4]$/u.test(field.key) ? 'pla.help.mastered' : null;
  if (detail) return t(detail);
  if (/^move[1-4]$/u.test(field.key)) return t('pla.help.battleMove');
  if (wild) {
    if (field.kind === 'decimal') return t('pla.wild.multiplierHelp');
    if (field.key === 'wildMin' || field.key === 'wildMax') return t('pla.help.wildLevel');
    if (field.key === 'wildWeight') return t('pla.help.weight');
    if (field.key === 'perfectIvs') return t('pla.help.perfectIvs');
  }
  if (kind === 'spawns' || kind === 'distortionSpawns') return t('pla.wild.spawnHelp');
  if (kind === 'weather') return t('pla.notice.weather');
  if (kind === 'evolutions') return t('pla.notice.evolutions');
  if (kind === 'compatibility' || kind === 'tutors') return t('pla.notice.' + kind);
  if (field.key.startsWith('actionSpeed')) return t('pla.help.actionDelay');
  if (field.key === 'canStyle') return t('pla.help.styles');
  if (['agilePower', 'strongPower', 'strongAccuracy'].includes(field.key)) return t('pla.help.styleValue');
  if (field.key === 'meritPrice') return t('pla.help.meritPrice');
  if (kind === 'research') return t(field.key.startsWith('threshold') ? 'pla.help.researchThreshold' : 'pla.help.researchPoints');
  if (field.key === 'selectionWeight') return t('pla.help.weight');
  if (['outbreaks', 'massiveOutbreaks'].includes(kind)) return t(field.key === 'shinyRolls' ? 'pla.help.shinyRolls'
    : field.key.endsWith('Chance') ? 'pla.help.eventChance' : 'pla.help.eventCount');
  if (['capture', 'friendship', 'shiny', 'bagPrices', 'charmPrices', 'crafting', 'ginkgo', 'shops', 'drops', 'ranks', 'outbreakDurations', 'distortionItems'].includes(kind))
    return t('pla.notice.' + kind);
  const domain: FieldHelpDomain | undefined = ['personal', 'learnsets', 'evolutions', 'compatibility', 'tutors'].includes(kind) ? 'pokemon'
    : kind === 'static' ? 'staticEncounters' : ['trainers', 'gifts', 'moves', 'items', 'shops', 'encounters'].includes(kind) ? kind as FieldHelpDomain : undefined;
  const fieldId = kind === 'learnsets' ? 'learnset' + field.key : field.key === 'battleLevel' ? 'level' : field.key;
  return resolveFieldHelp(t, { domain, fieldId, label, minimum: field.minimum, maximum: field.maximum,
    optionCount: field.choices?.length, optionValues: field.choices?.map(choice => choice.value) });
}
