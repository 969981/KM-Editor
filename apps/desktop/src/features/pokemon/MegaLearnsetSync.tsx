/* SPDX-License-Identifier: GPL-3.0-only */

import { useId } from 'react';
import type { PokemonRecord } from '../../bridge/contracts';
import { useLocalization } from '../../localization';
import './MegaLearnsetSync.css';

export function MegaLearnsetSync({ pokemon, records, canSync, draftsByPokemonId, onSync }: {
  pokemon: PokemonRecord;
  records: PokemonRecord[];
  canSync: boolean;
  draftsByPokemonId: Record<string, Record<number, unknown>>;
  onSync: () => Promise<boolean>;
}) {
  const { t } = useLocalization();
  const descriptionId = useId();
  if (pokemon.learnsetSyncBaseForm == null || pokemon.personal.isRegionalForm) return null;

  const base = records.find(record => record.personalId === pokemon.speciesId);
  const sourceId = pokemon.learnsetSyncBaseForm === 0 ? pokemon.speciesId
    : base && base.personal.formStatsIndex > 0 && pokemon.learnsetSyncBaseForm < base.personal.formCount
      ? base.personal.formStatsIndex + pokemon.learnsetSyncBaseForm - 1 : null;
  const bases = records.filter(record => record.speciesId === pokemon.speciesId
    && record.personalId === sourceId && !record.personal.isRegionalForm
    && record.personalId !== pokemon.personalId);
  const source = bases.length === 1 ? bases[0] : null;
  const hasDrafts = [pokemon.personalId, source?.personalId].some(id =>
    id !== undefined && Object.keys(draftsByPokemonId[id] ?? {}).length > 0);
  const disabled = !canSync || !source || hasDrafts;
  const description = !source ? t('pokemon.learnsetSync.unavailable')
    : hasDrafts ? t('pokemon.learnsetSync.drafts')
    : t('pokemon.learnsetSync.description', {
      pokemon: source.name
    });

  return <div className="mega-learnset-sync">
    <button type="button" className="secondary-button" disabled={disabled}
      aria-describedby={descriptionId}
      onClick={async () => { if (!disabled) await onSync(); }}>
      {t('pokemon.learnsetSync.button')}
    </button>
    <p className="field-note" id={descriptionId}>{description}</p>
  </div>;
}
