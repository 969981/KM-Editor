/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { apiDiagnosticSchema, editSessionSchema, projectGameSchema, projectPathsSchema, type EditSession } from './contracts';

export const pokeJobsWorkflowSchema = z.strictObject({
  canEdit: z.boolean(), disabled: z.boolean().nullable(), sourceLayer: z.enum(['base', 'layered', 'missing']),
  detectedGame: projectGameSchema.nullable(), diagnostics: z.array(apiDiagnosticSchema)
}).refine(workflow => !workflow.canEdit || workflow.disabled !== null && workflow.sourceLayer !== 'missing'
  && (workflow.detectedGame === 'sword' || workflow.detectedGame === 'shield'));
export const loadPokeJobsRequestSchema = z.strictObject({ paths: projectPathsSchema });
export const stagePokeJobsRequestSchema = z.strictObject({ paths: projectPathsSchema,
  disabled: z.boolean(), session: editSessionSchema.nullable() });
export const loadPokeJobsResponseSchema = z.strictObject({ workflow: pokeJobsWorkflowSchema });
export const stagePokeJobsResponseSchema = z.strictObject({ workflow: pokeJobsWorkflowSchema,
  session: editSessionSchema, diagnostics: z.array(apiDiagnosticSchema) });
export type PokeJobsWorkflow = z.infer<typeof pokeJobsWorkflowSchema>;
export type LoadPokeJobsRequest = z.infer<typeof loadPokeJobsRequestSchema>;
export type LoadPokeJobsResponse = z.infer<typeof loadPokeJobsResponseSchema>;
export type StagePokeJobsRequest = z.infer<typeof stagePokeJobsRequestSchema>;
export type StagePokeJobsResponse = z.infer<typeof stagePokeJobsResponseSchema>;

export function getPokeJobsPendingDisabled(session: EditSession | null): boolean | null {
  const edits = session?.pendingEdits.filter(edit => edit.domain === 'workflow.pokeJobs') ?? [];
  if (edits.length !== 1 || !session?.hasPendingChanges || !session.sessionId.trim()) return null;
  const edit = edits[0];
  if (edit.newValue !== 'true' && edit.newValue !== 'false') return null;
  const disabled = edit.newValue === 'true';
  if (edit.recordId !== 'poke-jobs' || edit.field !== 'disabled'
    || edit.summary !== (disabled ? 'Disable Poke Jobs.' : 'Enable Poke Jobs.')
    || edit.sources.length !== 1 || edit.sources[0].layer !== 'pending'
    || edit.sources[0].relativePath !== `pending/poke-jobs/disabled/${edit.newValue}`) return null;
  return disabled;
}
