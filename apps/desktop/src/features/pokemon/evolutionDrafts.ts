/* SPDX-License-Identifier: GPL-3.0-only */
import type { PokemonEvolutionRecord } from '../../bridge/contracts';

export type PokemonEvolutionLocalDraft = {
  argument: string;
  form: string;
  level: string;
  method: string;
  species: string;
  action?: 'add' | 'remove';
  position?: number;
};

export function evolutionDraftFields(row: PokemonEvolutionRecord): PokemonEvolutionLocalDraft {
  return { argument: String(row.argument), form: String(row.form), level: String(row.level),
    method: String(row.method), species: String(row.species) };
}

export function evolutionDraftsEqual(left: PokemonEvolutionLocalDraft, right: PokemonEvolutionLocalDraft) {
  return left.argument === right.argument && left.form === right.form && left.level === right.level &&
    left.method === right.method && left.species === right.species && left.action === right.action &&
    left.position === right.position;
}

export function replaceEvolutionDraftRecord(
  records: Record<string, Record<number, PokemonEvolutionLocalDraft>>,
  record: number,
  drafts: Record<number, PokemonEvolutionLocalDraft>
) {
  const next = { ...records };
  if (Object.keys(drafts).length === 0) delete next[record];
  else next[record] = drafts;
  return next;
}

export function projectEvolutionDraftRows(
  source: readonly PokemonEvolutionRecord[],
  drafts: Record<number, PokemonEvolutionLocalDraft>
): PokemonEvolutionRecord[] {
  return [
    ...source.filter(row => drafts[row.slot]?.action !== 'remove'),
    ...Object.entries(drafts).filter(([, draft]) => draft.action === 'add').map(([slot, draft]) => ({
      slot: Number(slot), method: Number(draft.method), argument: Number(draft.argument),
      species: Number(draft.species), form: Number(draft.form), level: Number(draft.level),
      methodName: '', argumentKind: 'value', argumentLabel: 'Argument', argumentValue: draft.argument
    }))
  ].sort((left, right) => (drafts[left.slot]?.position ?? left.slot) - (drafts[right.slot]?.position ?? right.slot));
}

export function orderEvolutionDraftRows(
  source: readonly PokemonEvolutionRecord[],
  drafts: Record<number, PokemonEvolutionLocalDraft>,
  order: readonly number[]
) {
  const natural = [
    ...source.filter(row => drafts[row.slot]?.action !== 'remove').map(row => row.slot),
    ...Object.entries(drafts).filter(([, draft]) => draft.action === 'add').map(([slot]) => Number(slot))
  ];
  const reordered = natural.some((slot, index) => slot !== order[index]);
  const rows = new Map(source.map(row => [row.slot, row]));
  const next = { ...drafts };
  order.forEach((slot, position) => {
    const { position: previousPosition, ...fields } = next[slot] ?? evolutionDraftFields(rows.get(slot)!);
    const draft = reordered ? { ...fields, position } : fields;
    const original = rows.get(slot);
    if (original && evolutionDraftsEqual(draft, evolutionDraftFields(original))) delete next[slot];
    else next[slot] = draft;
  });
  return next;
}

type EvolutionChange = {
  action: string; slot: number | null; method: number | null; argument: number | null;
  species: number | null; form: number | null; level: number | null;
};

// Keep row identities separate from the storage slots used by each game's writer.
export function planEvolutionDraftStage(
  source: readonly PokemonEvolutionRecord[],
  drafts: Record<number, PokemonEvolutionLocalDraft>,
  fixedSlots: boolean,
  values: readonly EvolutionChange[] = []
) {
  const changes: EvolutionChange[] = [];
  const operation = (action: string, slot: number): EvolutionChange =>
    ({ action, slot, method: null, argument: null, species: null, form: null, level: null });
  const rows = [...source].sort((left, right) => left.slot - right.slot);
  for (const row of [...rows].reverse()) {
    if (drafts[row.slot]?.action === 'remove') changes.push(operation('remove', row.slot));
  }
  const identities = rows.filter(row => drafts[row.slot]?.action !== 'remove').map(row => row.slot);
  const storage = fixedSlots ? [...identities] : identities.map((_, index) => index);
  for (const [slot] of Object.entries(drafts).filter(([, draft]) => draft.action === 'add')) {
    let storageSlot = storage.length;
    if (fixedSlots) {
      storageSlot = 0;
      while (storage.includes(storageSlot)) storageSlot++;
    }
    const index = fixedSlots ? storage.filter(value => value < storageSlot).length : storage.length;
    storage.splice(index, 0, storageSlot);
    identities.splice(index, 0, Number(slot));
    const value = values.find(value => value.action === 'add' && value.slot === Number(slot));
    if (value) changes.push({ ...value, slot: null });
  }
  const desired = projectEvolutionDraftRows(rows, drafts).map(row => row.slot);
  desired.forEach((identity, destination) => {
    let index = identities.indexOf(identity);
    while (index > destination) {
      changes.push(operation('moveUp', storage[index]!));
      [identities[index - 1], identities[index]] = [identities[index]!, identities[index - 1]!];
      index--;
    }
  });
  const slots = new Map(identities.map((identity, index) => [identity, storage[index]!]));
  for (const value of values.filter(value => value.action === 'upsert')) {
    changes.push({ ...value, slot: slots.get(value.slot!)! });
  }
  return { changes, slots };
}

export function settleEvolutionDrafts(
  latest: Record<number, PokemonEvolutionLocalDraft>,
  submitted: Record<number, PokemonEvolutionLocalDraft>,
  source: readonly PokemonEvolutionRecord[],
  fixedSlots: boolean
): Record<number, PokemonEvolutionLocalDraft> {
  const { slots } = planEvolutionDraftStage(source, submitted, fixedSlots);
  const remaining: Record<number, PokemonEvolutionLocalDraft> = {};
  for (const [slotText, draft] of Object.entries(latest)) {
    const slot = Number(slotText);
    const saved = submitted[slot];
    if (saved && evolutionDraftsEqual(draft, saved)) continue;
    if (draft.action === 'add' && saved?.action !== 'add') {
      remaining[slot] = draft;
      continue;
    }
    const nextSlot = slots.get(slot);
    if (nextSlot === undefined) continue;
    const { action, position, ...fields } = draft;
    remaining[nextSlot] = { ...fields,
      ...(saved?.action === 'add' ? {} : action ? { action } : {}),
      ...(position === saved?.position ? {} : position === undefined ? {} : { position }) };
  }
  return remaining;
}
