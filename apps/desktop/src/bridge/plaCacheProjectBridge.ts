/* SPDX-License-Identifier: GPL-3.0-only */

import { kmCommandNames } from './contracts';
import { sendProjectBridgeRequest, type ProjectBridgeTransport } from './projectBridgeRequest';
import {
  type ClearPlaCacheRequest,
  type GetPlaCacheStatusRequest,
  type PlaCacheStatusResponse,
  type UpdatePlaCacheSettingsRequest,
  type WarmupPlaCacheStepRequest,
  plaCacheStatusResponseSchema
} from './plaCacheContracts';

export type PlaCacheProjectBridgeApi = {
  clearPlaCache: (request: ClearPlaCacheRequest) => Promise<PlaCacheStatusResponse>;
  getPlaCacheStatus: (
    request: GetPlaCacheStatusRequest
  ) => Promise<PlaCacheStatusResponse>;
  updatePlaCacheSettings: (
    request: UpdatePlaCacheSettingsRequest
  ) => Promise<PlaCacheStatusResponse>;
  warmupPlaCacheStep: (
    request: WarmupPlaCacheStepRequest
  ) => Promise<PlaCacheStatusResponse>;
};

export function createPlaCacheProjectBridgeApi(
  transport: ProjectBridgeTransport
): PlaCacheProjectBridgeApi {
  return {
    clearPlaCache: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.clearPlaCache,
        request,
        plaCacheStatusResponseSchema
      ),
    getPlaCacheStatus: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.getPlaCacheStatus,
        request,
        plaCacheStatusResponseSchema
      ),
    updatePlaCacheSettings: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.updatePlaCacheSettings,
        request,
        plaCacheStatusResponseSchema
      ),
    warmupPlaCacheStep: (request) =>
      sendProjectBridgeRequest(
        transport,
        kmCommandNames.warmupPlaCacheStep,
        request,
        plaCacheStatusResponseSchema
      )
  };
}
