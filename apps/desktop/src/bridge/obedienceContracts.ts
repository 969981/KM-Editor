/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { apiDiagnosticSchema, editSessionSchema, projectGameSchema, projectPathsSchema, type EditSession } from './contracts';
import { calculatePendingPayloadSha256 } from '../utils/pendingPayloadHash';

export const defaultObedienceSelections = [20, 30, 40, 50, 60, 70, 80, 90, 100];
export const encodeObedienceSelections = (values: readonly number[]) => values.join(',');
export const obedienceSelectionsSchema = z.array(z.number().int().min(1).max(100)).length(9);
export const obedienceWorkflowSchema = z.strictObject({
  canEdit: z.boolean(), detectedGame: projectGameSchema.nullable(), selections: z.array(z.number().int()),
  sourceLayer: z.enum(['base', 'layered', 'missing']), diagnostics: z.array(apiDiagnosticSchema)
}).refine(workflow => (workflow.selections.length === 0 && !workflow.canEdit || obedienceSelectionsSchema.safeParse(workflow.selections).success)
  && (!workflow.canEdit || (workflow.detectedGame === 'sword' || workflow.detectedGame === 'shield') && workflow.sourceLayer !== 'missing'));
export const loadObedienceRequestSchema = z.strictObject({ paths: projectPathsSchema });
export const stageObedienceRequestSchema = z.strictObject({ paths: projectPathsSchema,
  selections: obedienceSelectionsSchema, session: editSessionSchema.nullable() });
export const loadObedienceResponseSchema = z.strictObject({ workflow: obedienceWorkflowSchema });
export const stageObedienceResponseSchema = z.strictObject({ workflow: obedienceWorkflowSchema,
  session: editSessionSchema, diagnostics: z.array(apiDiagnosticSchema) });
export type ObedienceWorkflow = z.infer<typeof obedienceWorkflowSchema>;
export type LoadObedienceRequest = z.infer<typeof loadObedienceRequestSchema>;
export type LoadObedienceResponse = z.infer<typeof loadObedienceResponseSchema>;
export type StageObedienceRequest = z.infer<typeof stageObedienceRequestSchema>;
export type StageObedienceResponse = z.infer<typeof stageObedienceResponseSchema>;

export function getObediencePendingSelections(session: EditSession | null): number[] | null {
  const edits = session?.pendingEdits.filter(edit => edit.domain === 'workflow.obedience') ?? [];
  if (edits.length !== 1 || !session?.hasPendingChanges || !session.sessionId.trim()) return null;
  const edit = edits[0];
  const values = edit.newValue?.split(',').map(value => /^\d{1,3}$/.test(value) ? Number(value) : NaN) ?? [];
  if (!obedienceSelectionsSchema.safeParse(values).success || edit.newValue !== encodeObedienceSelections(values)
    || edit.recordId !== 'obedience' || edit.field !== 'selections' || edit.summary !== 'Stage obedience level limits by badge count.'
    || edit.sources.length !== 1 || edit.sources[0].layer !== 'pending'
    || edit.sources[0].relativePath !== `pending/obedience/selections/${calculatePendingPayloadSha256(edit.newValue)}`) return null;
  return values;
}
