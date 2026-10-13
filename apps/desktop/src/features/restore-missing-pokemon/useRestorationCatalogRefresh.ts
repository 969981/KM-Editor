/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef } from 'react';
import type { EditSession, ProjectPaths } from '../../bridge/contracts';
import type { ProjectBridge } from '../../bridge/projectBridge';
import { restorationRevision } from '../../bridge/restorationRequestContext';
import { useWorkbenchStore } from '../../workbenchStore';

type LookupWorkflow = { editableFields?: unknown; fields?: unknown; speciesOptions?: unknown; pokemonOptions?: unknown; stats?: unknown;
  presentSpeciesIds?: unknown; evolutionMethodOptions?: unknown; pokemon?: unknown; safeNormalSpeciesOptions?: unknown };

/** Refresh only lookup data. Existing records, selections, and local drafts stay in place. */
export function mergeRestorationLookups<T extends LookupWorkflow>(current: T, fresh: T, pokemonData = false): T {
  const result = { ...current };
  for (const key of ['editableFields', 'fields', 'speciesOptions', 'pokemonOptions', 'presentSpeciesIds', 'evolutionMethodOptions', 'safeNormalSpeciesOptions'] as const)
    if (key in fresh) result[key] = fresh[key];
  if (pokemonData && Array.isArray(current.pokemon) && Array.isArray(fresh.pokemon)) {
    if ('stats' in fresh) result.stats = fresh.stats;
    type Row = { personalId: number; personal?: { isPresentInGame?: boolean }; dexPresence?: { isPresentInGame?: boolean } };
    const previous = new Map((current.pokemon as Row[]).map(row => [row.personalId, row]));
    result.pokemon = (fresh.pokemon as Row[]).map(row => {
      const old = previous.get(row.personalId);
      if (!old || old.personal?.isPresentInGame !== row.personal?.isPresentInGame) return row;
      return old;
    });
  } else if ('pokemon' in fresh && !pokemonData) result.pokemon = fresh.pokemon;
  return result;
}

export function useRestorationCatalogRefresh(bridge: ProjectBridge, paths: ProjectPaths, session: EditSession | null, installedRevision?: string) {
  const revision = restorationRevision(session);
  const pathKey = JSON.stringify(paths);
  const previous = useRef<string | null>(null);
  useEffect(() => {
    const key = `${pathKey}|${revision}|${installedRevision ?? ''}`;
    if (previous.current === null) { previous.current = key; return; }
    if (previous.current === key) return;
    previous.current = key;
    if (paths.selectedGame !== 'sword' && paths.selectedGame !== 'shield') return;
    let active = true;
    const work = [
      ['pokemonWorkflow', () => bridge.loadPokemonWorkflow({ paths })],
      ['trainersWorkflow', () => bridge.loadTrainersWorkflow({ paths })],
      ['encountersWorkflow', () => bridge.loadEncountersWorkflow({ paths })],
      ['giftPokemonWorkflow', () => bridge.loadGiftPokemonWorkflow({ paths })],
      ['tradePokemonWorkflow', () => bridge.loadTradePokemonWorkflow({ paths })],
      ['rentalPokemonWorkflow', () => bridge.loadRentalPokemonWorkflow({ paths })],
      ['staticEncountersWorkflow', () => bridge.loadStaticEncountersWorkflow({ paths })],
      ['raidBattlesWorkflow', () => bridge.loadRaidBattlesWorkflow({ paths })],
      ['dynamaxAdventuresWorkflow', () => bridge.loadDynamaxAdventuresWorkflow({ paths })],
      ['shakingTreesWorkflow', () => bridge.loadShakingTrees({ paths })],
      ['behaviorWorkflow', () => bridge.loadBehaviorWorkflow({ paths })],
      ['heldItemChanceWorkflow', () => bridge.loadHeldItemChance({ paths })],
      ['placementWorkflow', async () => {
        const { catalog } = await bridge.openSwShPlacementCatalog({ paths });
        return { workflow: { editableFields: catalog.editableFields } };
      }]
    ] as const;
    void (async () => {
      for (const [storeKey, load] of work) {
        if (!active) return;
        if (!useWorkbenchStore.getState()[storeKey]) continue;
        try {
          const { workflow } = await load();
          if (!active) return;
          useWorkbenchStore.setState(state => {
            const current = state[storeKey];
            return current ? { [storeKey]: mergeRestorationLookups(current as LookupWorkflow, workflow as LookupWorkflow, storeKey === 'pokemonWorkflow') } : {};
          });
        } catch {
          // Existing drafts stay visible. Stage and Review revalidate against
          // the current catalog and report unavailable sources explicitly.
        }
      }
    })();
    return () => { active = false; };
    // The serialized scope avoids restarting this bounded refresh on each render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bridge, pathKey, revision, installedRevision]);
}
