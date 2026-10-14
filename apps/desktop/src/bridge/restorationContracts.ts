/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { apiDiagnosticSchema, editSessionSchema, projectPathsSchema } from './contracts';

export const restorationWorkflowSchema = z.strictObject({ canEdit: z.boolean(), revision: z.string(),
  entries: z.array(z.strictObject({ speciesId: z.number().int(), name: z.string(), definitionId: z.string().nullable(),
    support: z.enum(['NotSupported', 'Testing', 'Ready']), installation: z.enum(['Disabled', 'Enabled', 'NeedsRepair', 'External']),
    donor: z.enum(['ScarletViolet', 'LegendsZA']).nullable(), family: z.array(z.number().int()), preparedHash: z.string().nullable(), selected: z.boolean(), staged: z.boolean() })).max(898),
  diagnostics: z.array(apiDiagnosticSchema) });
export const restorationPreparationSchema = z.strictObject({ id: z.string(), definitionId: z.string(),
  status: z.enum(['preparing', 'prepared', 'canceled', 'failed']), phase: z.string(), completedSpecies: z.number().int(),
  totalSpecies: z.number().int(), preparedHash: z.string().nullable(), unavailableSourceMoves: z.record(z.string(), z.array(z.number().int())),
  diagnostics: z.array(apiDiagnosticSchema) });
export const loadRestorationRequestSchema = z.strictObject({ paths: projectPathsSchema, session: editSessionSchema.nullable().optional() });
export const loadRestorationResponseSchema = z.strictObject({ workflow: restorationWorkflowSchema });
export const prepareRestorationRequestSchema = z.strictObject({ paths: projectPathsSchema, definitionId: z.string(),
  donorRomFsPath: z.string().min(1), compressionSupportFolderPath: z.string().nullable() });
export const restorationPreparationRequestSchema = z.strictObject({ paths: projectPathsSchema, preparationId: z.string() });
export const restorationPreparationResponseSchema = z.strictObject({ preparation: restorationPreparationSchema });
export const stageRestorationRequestSchema = z.strictObject({ paths: projectPathsSchema, definitionId: z.string(), preparedHash: z.string(),
  enabled: z.boolean(), session: editSessionSchema.nullable() });
export const stageRestorationResponseSchema = z.strictObject({ workflow: restorationWorkflowSchema, session: editSessionSchema,
  diagnostics: z.array(apiDiagnosticSchema) });
export type RestorationWorkflow = z.infer<typeof restorationWorkflowSchema>;
export type RestorationPreparation = z.infer<typeof restorationPreparationSchema>;
export type RestorationSelection = { definitionId: string; preparedHash: string; enabled: boolean };
