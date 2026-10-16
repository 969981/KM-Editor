/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { editSessionSchema, type EditSession, type ProjectPaths } from './contracts';
import { sendProjectBridgeRequest, type ProjectBridgeTransport } from './projectBridgeRequest';

const choice = z.strictObject({ value: z.string(), label: z.string() });
const field = z.strictObject({ key: z.string(), label: z.string(), kind: z.enum(['number', 'boolean', 'text', 'rows']),
  minimum: z.number().nullable(), maximum: z.number().nullable(), choices: z.array(choice).nullable(), readOnly: z.boolean(),
  choicesByMethod: z.record(z.string(), z.array(choice)).nullable() });
const record = z.strictObject({ id: z.string(), label: z.string(), values: z.record(z.string(), z.string()), readOnlyFields: z.array(z.string()),
  metadata: z.record(z.string(), z.string()).default({}), relatedRecords: z.array(z.strictObject({ kind: z.string(), id: z.string(), label: z.string() })).default([]) });
export const plaKindSchema = z.enum(['personal', 'moves', 'learnsets', 'evolutions', 'compatibility', 'tutors', 'text', 'trainers', 'gifts', 'static',
  'items', 'crafting', 'shops', 'ginkgo', 'drops', 'research', 'ranks']);
export const plaPageSchema = z.strictObject({ kind: plaKindSchema, revision: z.string().regex(/^[A-F0-9]{64}$/u),
  fields: z.array(field), rowFields: z.array(field), records: z.array(record).max(100), total: z.number().int().nonnegative(),
  textFiles: z.array(z.string()).max(1024), textFile: z.string().nullable(), notices: z.array(z.string()),
  pokemonSpriteNames: z.record(z.string(), z.string()).default({}) });
export type PlaKind = z.infer<typeof plaKindSchema>;
export type PlaPage = z.infer<typeof plaPageSchema>;
export type PlaField = z.infer<typeof field>;
export type PlaRecord = z.infer<typeof record>;
export type LoadPlaRequest = { paths: ProjectPaths; session: EditSession | null; kind: PlaKind; textFile?: string | null; query?: string; offset?: number; messageLanguage?: string | null };
export type StagePlaRequest = { paths: ProjectPaths; session: EditSession | null; kind: PlaKind; id: string; values: Record<string, string>; revision: string; restoreBase?: boolean };
export type PlaProjectBridgeApi = {
  loadPla: (request: LoadPlaRequest) => Promise<{ page: PlaPage }>;
  stagePla: (request: StagePlaRequest) => Promise<{ session: EditSession }>;
};
export function createPlaProjectBridge(transport: ProjectBridgeTransport): PlaProjectBridgeApi {
  return {
    loadPla: request => sendProjectBridgeRequest(transport, 'pla.load', request, z.strictObject({ page: plaPageSchema })),
    stagePla: request => sendProjectBridgeRequest(transport, 'pla.stage', request, z.strictObject({ session: editSessionSchema }))
  };
}
