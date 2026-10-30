/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { editorClipboard, getEditorClipboardRevision, resolveSystemClipboard } from '../../authoring/rowClipboardSystemClipboard';

export type LearnsetClipboardFamily = 'swsh' | 'sv' | 'za' | 'pla';
const entrySchema = z.strictObject({
  moveId: z.number().int().min(0).max(65535),
  level: z.number().int().min(0).max(65535),
  mastery: z.number().int().min(0).max(255).optional()
});
const envelopeSchema = z.strictObject({
  format: z.literal('km-editor.pokemon-learnset'),
  version: z.literal(1),
  copyId: z.uuid(),
  family: z.enum(['swsh', 'sv', 'za', 'pla']),
  entries: z.array(entrySchema).max(4096)
});
export type LearnsetClipboardEntry = z.infer<typeof entrySchema>;
let lastCopy: { text: string; revision: number } | null = null;

export function validateLearnsetEntries(entries: unknown, family: LearnsetClipboardFamily): LearnsetClipboardEntry[] {
  const rows = z.array(entrySchema).max(family === 'swsh' ? 65 : family === 'pla' ? 256 : 4096).parse(entries);
  const maximumLevel = family === 'swsh' || family === 'pla' ? 100 : family === 'za' ? 255 : 65535;
  if (rows.some(row => row.level > maximumLevel
    || (family === 'swsh' && row.moveId === 0)
    || ((family === 'za' || family === 'pla') ? row.mastery === undefined : row.mastery !== undefined)
    || (family === 'pla' && (row.mastery! > 100 || (row.mastery !== 0 && row.mastery! < row.level))))) throw new Error('invalid-learnset');
  if (family === 'pla' && (new Set(rows.map(row => row.moveId)).size !== rows.length
    || rows.some((row, index) => index > 0 && row.level < rows[index - 1].level))) throw new Error('invalid-learnset');
  return rows;
}

export function parseLearnsetClipboard(text: string, family: LearnsetClipboardFamily): LearnsetClipboardEntry[] {
  if (text.length > 1024 * 1024) throw new Error('invalid-learnset');
  const envelope = envelopeSchema.parse(JSON.parse(text));
  if (envelope.family !== family) throw new Error('incompatible-learnset');
  if (lastCopy?.text === text && lastCopy.revision !== getEditorClipboardRevision()) throw new Error('superseded-learnset');
  return validateLearnsetEntries(envelope.entries, family);
}

export async function copyLearnset(entries: LearnsetClipboardEntry[], family: LearnsetClipboardFamily) {
  const clipboard = resolveSystemClipboard();
  if (!clipboard) throw new Error('clipboard-unavailable');
  const text = JSON.stringify({ format: 'km-editor.pokemon-learnset', version: 1, copyId: crypto.randomUUID(), family, entries: validateLearnsetEntries(entries, family) });
  await clipboard.writeText(text);
  await editorClipboard.writeText(text);
  lastCopy = { text, revision: getEditorClipboardRevision() };
}

export async function pasteLearnset(family: LearnsetClipboardFamily) {
  const clipboard = resolveSystemClipboard();
  if (!clipboard) throw new Error('clipboard-unavailable');
  const revision = getEditorClipboardRevision();
  const text = await clipboard.readText();
  if (revision !== getEditorClipboardRevision()) throw new Error('clipboard-changed');
  return parseLearnsetClipboard(text, family);
}
