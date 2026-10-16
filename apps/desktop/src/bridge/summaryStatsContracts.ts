/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { apiDiagnosticSchema, editSessionSchema, projectPathsSchema, workflowSummarySchema } from './contracts';

export const summaryStatsWorkflowSchema = z.strictObject({
  summary: workflowSummarySchema, sourceRevision: z.string().max(64),
  enabled: z.boolean(), appliedEnabled: z.boolean(), diagnostics: z.array(apiDiagnosticSchema)
}).refine(workflow => workflow.summary.id === 'summaryStats');
export const loadSummaryStatsRequestSchema = z.strictObject({ paths: projectPathsSchema, session: editSessionSchema.nullable() });
export const loadSummaryStatsResponseSchema = z.strictObject({ workflow: summaryStatsWorkflowSchema });
export const stageSummaryStatsRequestSchema = loadSummaryStatsRequestSchema.extend({
  sourceRevision: z.string().regex(/^[A-F0-9]{64}$/u), enabled: z.boolean()
});
export const stageSummaryStatsResponseSchema = z.strictObject({
  workflow: summaryStatsWorkflowSchema, session: editSessionSchema, diagnostics: z.array(apiDiagnosticSchema)
});
export type SummaryStatsWorkflow = z.infer<typeof summaryStatsWorkflowSchema>;
export type LoadSummaryStatsRequest = z.infer<typeof loadSummaryStatsRequestSchema>;
export type LoadSummaryStatsResponse = z.infer<typeof loadSummaryStatsResponseSchema>;
export type StageSummaryStatsRequest = z.infer<typeof stageSummaryStatsRequestSchema>;
export type StageSummaryStatsResponse = z.infer<typeof stageSummaryStatsResponseSchema>;
