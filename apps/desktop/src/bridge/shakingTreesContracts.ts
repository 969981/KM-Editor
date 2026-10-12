/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { apiDiagnosticSchema, editSessionSchema, projectPathsSchema, type EditSession } from './contracts';
import { calculatePendingPayloadSha256 } from '../utils/pendingPayloadHash';

const integer = z.number().int();
export const treeSettingsSchema = z.strictObject({ initialRiskMin: integer, initialRiskMax: integer,
  riskStepMin: integer, riskStepMax: integer, loseAllRolls: integer, dropMin: integer, dropMax: integer });
export const shakingTreesWorkflowSchema = z.strictObject({ canEdit: z.boolean(),
  trees: z.array(z.strictObject({ id: z.string(), label: z.string(), areaId: z.string(), area: z.string(),
    drops: z.array(z.strictObject({ itemId: integer, percent: integer })).min(1).max(10) })).max(512),
  areas: z.array(z.strictObject({ id: z.string(), name: z.string(), levelMin: integer, levelMax: integer,
    slots: z.array(z.strictObject({ species: integer, form: integer, percent: integer })).min(1).max(20) })),
  settings: treeSettingsSchema.nullable(), items: z.array(z.strictObject({ value: integer, label: z.string() })),
  pokemon: z.array(z.strictObject({ species: integer, form: integer, label: z.string() })), diagnostics: z.array(apiDiagnosticSchema)
});
export const treeUpdateSchema = z.strictObject({ recordId: z.string().min(1).max(256), field: z.string().min(1).max(64), value: integer.min(0).max(65535) });
export const loadShakingTreesRequestSchema = z.strictObject({ paths: projectPathsSchema });
export const stageShakingTreesRequestSchema = z.strictObject({ paths: projectPathsSchema, session: editSessionSchema.nullable(), updates: z.array(treeUpdateSchema).min(1).max(20_000) });
export const loadShakingTreesResponseSchema = z.strictObject({ workflow: shakingTreesWorkflowSchema });
export const stageShakingTreesResponseSchema = z.strictObject({ workflow: shakingTreesWorkflowSchema, session: editSessionSchema, diagnostics: z.array(apiDiagnosticSchema) });
export type ShakingTreesWorkflow = z.infer<typeof shakingTreesWorkflowSchema>;
export type TreeSettings = z.infer<typeof treeSettingsSchema>;
export type TreeUpdate = z.infer<typeof treeUpdateSchema>;
export type LoadShakingTreesRequest = z.infer<typeof loadShakingTreesRequestSchema>;
export type LoadShakingTreesResponse = z.infer<typeof loadShakingTreesResponseSchema>;
export type StageShakingTreesRequest = z.infer<typeof stageShakingTreesRequestSchema>;
export type StageShakingTreesResponse = z.infer<typeof stageShakingTreesResponseSchema>;
export const treeFieldKey = (recordId: string, field: string) => `${recordId}|${field}`;

export function treeValues(workflow: ShakingTreesWorkflow | null, session: EditSession | null) {
  const values: Record<string, string> = {};
  const add = (id: string, field: string, value: number) => { values[treeFieldKey(id, field)] = String(value); };
  for (const tree of workflow?.trees ?? []) tree.drops.forEach((row, slot) => {
    add(tree.id, `item.${slot}`, row.itemId); add(tree.id, `percent.${slot}`, row.percent);
  });
  for (const area of workflow?.areas ?? []) {
    add(area.id, 'levelMin', area.levelMin); add(area.id, 'levelMax', area.levelMax);
    area.slots.forEach((row, slot) => { add(area.id, `species.${slot}`, row.species); add(area.id, `form.${slot}`, row.form); add(area.id, `percent.${slot}`, row.percent); });
  }
  for (const [field, value] of Object.entries(workflow?.settings ?? {})) add('settings', field, value);
  let invalidPending = false;
  const seen = new Set<string>();
  for (const edit of session?.pendingEdits.filter(edit => edit.domain === 'workflow.shakingTrees') ?? []) {
    const key = treeFieldKey(edit.recordId ?? '', edit.field ?? '');
    const valid = key in values && !seen.has(key) && /^\d+$/.test(edit.newValue ?? '')
      && edit.summary === 'Stage tree field.' && edit.sources.length === 1 && edit.sources[0].layer === 'pending'
      && edit.sources[0].relativePath === `pending/shaking-trees/${calculatePendingPayloadSha256(`${edit.recordId}|${edit.field}|${edit.newValue}`)}`;
    seen.add(key);
    if (!valid) invalidPending = true; else values[key] = edit.newValue!;
  }
  return { values, invalidPending };
}
