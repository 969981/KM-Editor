/* SPDX-License-Identifier: GPL-3.0-only */

import { z } from 'zod';
import { projectPathsSchema } from './contracts';

export const plaCacheModeSchema = z.enum(['minimal', 'balanced', 'performance']);
export type PlaCacheMode = z.infer<typeof plaCacheModeSchema>;

export const plaCacheSettingsSchema = z.strictObject({
  maxCacheSizeBytes: z.number(),
  mode: plaCacheModeSchema
});
export type PlaCacheSettings = z.infer<typeof plaCacheSettingsSchema>;

export const plaCacheStatusSchema = z.strictObject({
  cacheSizeBytes: z.number(),
  isActiveProjectPreserved: z.boolean(),
  message: z.string(),
  phase: z.string(),
  progressPercent: z.number(),
  settings: plaCacheSettingsSchema,
  warmupCompleted: z.number(),
  warmupTotal: z.number()
});
export type PlaCacheStatus = z.infer<typeof plaCacheStatusSchema>;

export const getPlaCacheStatusRequestSchema = z.strictObject({
  paths: projectPathsSchema.nullable().optional()
});
export type GetPlaCacheStatusRequest = z.infer<typeof getPlaCacheStatusRequestSchema>;

export const updatePlaCacheSettingsRequestSchema = z.strictObject({
  maxCacheSizeBytes: z.number(),
  mode: plaCacheModeSchema,
  paths: projectPathsSchema.nullable().optional()
});
export type UpdatePlaCacheSettingsRequest = z.infer<
  typeof updatePlaCacheSettingsRequestSchema
>;

export const clearPlaCacheRequestSchema = z.strictObject({
  activePaths: projectPathsSchema.nullable().optional()
});
export type ClearPlaCacheRequest = z.infer<typeof clearPlaCacheRequestSchema>;

export const warmupPlaCacheStepRequestSchema = z.strictObject({
  paths: projectPathsSchema,
  stepIndex: z.number()
});
export type WarmupPlaCacheStepRequest = z.infer<typeof warmupPlaCacheStepRequestSchema>;

export const plaCacheStatusResponseSchema = z.strictObject({
  status: plaCacheStatusSchema
});
export type PlaCacheStatusResponse = z.infer<typeof plaCacheStatusResponseSchema>;
