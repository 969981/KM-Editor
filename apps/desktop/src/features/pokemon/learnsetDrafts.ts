/* SPDX-License-Identifier: GPL-3.0-only */
import type { PokemonLearnsetMove } from '../../bridge/contracts';

export type PokemonLearnsetLocalDraft = {
  moveId: string;
  level: string;
  rawLevel?: number;
  action?: 'add' | 'remove';
};

export function learnsetDraftsEqual(left: PokemonLearnsetLocalDraft, right: PokemonLearnsetLocalDraft) {
  return left.level === right.level && left.moveId === right.moveId && left.action === right.action && left.rawLevel === right.rawLevel;
}

export function replaceLearnsetDraftRecord(
  records: Record<string, Record<number, PokemonLearnsetLocalDraft>>,
  record: number,
  drafts: Record<number, PokemonLearnsetLocalDraft>
) {
  const next = { ...records };
  if (Object.keys(drafts).length === 0) delete next[record];
  else next[record] = drafts;
  return next;
}

export function projectLearnsetDraftRows(
  source: readonly PokemonLearnsetMove[],
  drafts: Record<number, PokemonLearnsetLocalDraft>,
  moveNames: readonly { value: number; label: string }[]
): PokemonLearnsetMove[] {
  return [
    ...source.filter(row => drafts[row.slot]?.action !== 'remove'),
    ...Object.entries(drafts).filter(([, draft]) => draft.action === 'add')
      .map(([slot, draft]) => {
        const level = /^\d+$/u.test(draft.level) ? Number(draft.level) : 1;
        const moveId = /^\d+$/u.test(draft.moveId) ? Number(draft.moveId) : 0;
        return { slot: Number(slot), moveId, level, rawLevel: draft.rawLevel === undefined ? level : (draft.rawLevel & 0xff00) | level,
          moveName: moveNames.find(option => option.value === moveId)?.label ?? moveId.toString() };
      })
  ];
}

export function replaceLearnsetDrafts(source: readonly PokemonLearnsetMove[], entries: readonly { moveId: number; level: number; mastery?: number }[]): Record<number, PokemonLearnsetLocalDraft> {
  const drafts: Record<number, PokemonLearnsetLocalDraft> = {};
  for (const row of source) drafts[row.slot] = { action: 'remove', moveId: String(row.moveId), level: String(row.level) };
  let slot = Math.max(-1, ...source.map(row => row.slot)) + 1;
  for (const row of entries) drafts[slot++] = { action: 'add', moveId: String(row.moveId), level: String(row.level),
    ...(row.mastery === undefined ? {} : { rawLevel: row.mastery * 256 + row.level }) };
  return drafts;
}

export function stagedLearnsetSlotMap(
  source: readonly Pick<PokemonLearnsetMove, 'slot'>[],
  submitted: Record<number, PokemonLearnsetLocalDraft>
) {
  const slots = new Map<number, number>();
  for (const row of source) {
    if (submitted[row.slot]?.action !== 'remove') slots.set(row.slot, slots.size);
  }
  for (const [slot] of Object.entries(submitted).filter(([, draft]) => draft.action === 'add')) {
    slots.set(Number(slot), slots.size);
  }
  return slots;
}

export function settleLearnsetDrafts(
  latest: Record<number, PokemonLearnsetLocalDraft>,
  submitted: Record<number, PokemonLearnsetLocalDraft>,
  source: readonly Pick<PokemonLearnsetMove, 'slot'>[]
): Record<number, PokemonLearnsetLocalDraft> {
  const slots = stagedLearnsetSlotMap(source, submitted);
  const remaining: Record<number, PokemonLearnsetLocalDraft> = {};
  for (const [slotText, draft] of Object.entries(latest)) {
    const slot = Number(slotText);
    const saved = submitted[slot];
    if (saved && learnsetDraftsEqual(draft, saved)) continue;
    if (draft.action === 'add' && saved?.action !== 'add') {
      remaining[slot] = draft;
      continue;
    }
    const nextSlot = slots.get(slot);
    if (nextSlot === undefined) continue;
    const { action, ...fields } = draft;
    remaining[nextSlot] = saved?.action === 'add' ? fields : draft;
  }
  return remaining;
}
