/* SPDX-License-Identifier: GPL-3.0-only */
import { z } from 'zod';
import { kmCommandNames } from './contracts';
import * as contracts from './restorationContracts';
import { sendProjectBridgeRequest, type ProjectBridgeTransport } from './projectBridgeRequest';

export function createRestorationProjectBridge(transport: ProjectBridgeTransport) {
  return {
    loadRestoration: (request: z.infer<typeof contracts.loadRestorationRequestSchema>) => sendProjectBridgeRequest(transport,
      kmCommandNames.loadRestoration, contracts.loadRestorationRequestSchema.parse(request), contracts.loadRestorationResponseSchema),
    prepareRestoration: (request: z.infer<typeof contracts.prepareRestorationRequestSchema>) => sendProjectBridgeRequest(transport,
      kmCommandNames.prepareRestoration, contracts.prepareRestorationRequestSchema.parse(request), contracts.restorationPreparationResponseSchema),
    getRestorationPreparation: (request: z.infer<typeof contracts.restorationPreparationRequestSchema>) => sendProjectBridgeRequest(transport,
      kmCommandNames.getRestorationPreparation, contracts.restorationPreparationRequestSchema.parse(request), contracts.restorationPreparationResponseSchema),
    cancelRestorationPreparation: (request: z.infer<typeof contracts.restorationPreparationRequestSchema>) => sendProjectBridgeRequest(transport,
      kmCommandNames.cancelRestorationPreparation, contracts.restorationPreparationRequestSchema.parse(request), contracts.restorationPreparationResponseSchema),
    stageRestoration: (request: z.infer<typeof contracts.stageRestorationRequestSchema>) => sendProjectBridgeRequest(transport,
      kmCommandNames.stageRestoration, contracts.stageRestorationRequestSchema.parse(request), contracts.stageRestorationResponseSchema)
  };
}
export type RestorationProjectBridgeApi = ReturnType<typeof createRestorationProjectBridge>;
