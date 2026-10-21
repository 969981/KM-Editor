/* SPDX-License-Identifier: GPL-3.0-only */
import type { PlaKind } from '../bridge/plaProjectBridge';
import type { WorkbenchSection } from './workbenchSections';
import type { WorkbenchLocation } from './workbenchLocation';

export const plaSectionKinds = {
  pokemon: ['personal', 'learnsets', 'evolutions', 'compatibility', 'tutors'],
  moves: ['moves'], text: ['text'], trainers: ['trainers'], giftPokemon: ['gifts'], staticEncounters: ['static'],
  items: ['items'], crafting: ['crafting'], shops: ['shops', 'ginkgo'], pokemonDrops: ['drops'], research: ['research', 'ranks'],
  encounters: ['encounters', 'spawns'],
  outbreaks: ['outbreaks', 'massiveOutbreaks', 'outbreakGroups', 'outbreakWaves', 'outbreakDurations', 'outbreakEncounters'],
  distortions: ['distortionEncounters', 'distortionSpawns', 'distortionItems'], weather: ['weather'],
  gameplayRules: ['capture', 'friendship', 'shiny', 'bagPrices', 'charmPrices'], typeChart: ['typeChart']
} as const satisfies Partial<Record<WorkbenchSection, readonly PlaKind[]>>;

export type PlaSection = keyof typeof plaSectionKinds;
export type PlaNavigationTarget = { section: PlaSection; kind: PlaKind; id: string };
export const isPlaSection = (section: WorkbenchSection): section is PlaSection => Object.hasOwn(plaSectionKinds, section);
export const plaRecordKind = (kind: PlaKind) => `pla-${kind.replace(/[A-Z]/gu, letter => '-' + letter.toLowerCase())}`;

export function parsePlaLocation(location: WorkbenchLocation): PlaNavigationTarget | null {
  if (location.game !== 'pla' || !isPlaSection(location.section) || !location.entity || location.entity.recordKind.schemaVersion !== 1) return null;
  const kind = (plaSectionKinds[location.section] as readonly PlaKind[]).find(kind => plaRecordKind(kind) === location.entity?.recordKind.key);
  return kind ? { section: location.section, kind, id: location.entity.recordId } : null;
}

export function plaLocationLoadOptions(target: PlaNavigationTarget) {
  if (target.kind !== 'text') return { kind: target.kind, query: target.id };
  const slash = target.id.indexOf('/'), colon = target.id.lastIndexOf(':');
  return { kind: target.kind, query: target.id, messageLanguage: target.id.slice(0, slash), textFile: target.id.slice(slash + 1, colon) };
}
