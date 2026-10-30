/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { apiDiagnosticSchema, editSessionSchema, projectGameSchema, projectPathsSchema, type EditSession } from './contracts';

export const yCommWorkflowSchema = z.strictObject({
  canEdit: z.boolean(), disabled: z.boolean().nullable(), needsRepair: z.boolean(), sourceLayer: z.enum(['base', 'layered', 'missing']),
  detectedGame: projectGameSchema.nullable(), diagnostics: z.array(apiDiagnosticSchema)
}).refine(workflow => !workflow.canEdit || workflow.disabled !== null && workflow.sourceLayer !== 'missing'
  && (workflow.detectedGame === 'sword' || workflow.detectedGame === 'shield'));
export const loadYCommRequestSchema = z.strictObject({ paths: projectPathsSchema });
export const stageYCommRequestSchema = z.strictObject({ paths: projectPathsSchema,
  disabled: z.boolean(), session: editSessionSchema.nullable() });
export const loadYCommResponseSchema = z.strictObject({ workflow: yCommWorkflowSchema });
export const stageYCommResponseSchema = z.strictObject({ workflow: yCommWorkflowSchema,
  session: editSessionSchema, diagnostics: z.array(apiDiagnosticSchema) });
export type YCommWorkflow = z.infer<typeof yCommWorkflowSchema>;
export type LoadYCommRequest = z.infer<typeof loadYCommRequestSchema>;
export type LoadYCommResponse = z.infer<typeof loadYCommResponseSchema>;
export type StageYCommRequest = z.infer<typeof stageYCommRequestSchema>;
export type StageYCommResponse = z.infer<typeof stageYCommResponseSchema>;

export function getYCommPendingDisabled(session: EditSession | null): boolean | null {
  const edits = session?.pendingEdits.filter(edit => edit.domain === 'workflow.yComm') ?? [];
  if (edits.length !== 1 || !session?.hasPendingChanges || !session.sessionId.trim()) return null;
  const edit = edits[0];
  if (edit.newValue !== 'true' && edit.newValue !== 'false') return null;
  const disabled = edit.newValue === 'true';
  if (edit.recordId !== 'y-comm' || edit.field !== 'disabled'
    || edit.summary !== (disabled ? 'Disable Y Comm.' : 'Enable Y Comm.')
    || edit.sources.length !== 1 || edit.sources[0].layer !== 'pending'
    || edit.sources[0].relativePath !== `pending/y-comm/disabled/${edit.newValue}`) return null;
  return disabled;
}
