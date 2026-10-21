/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectGame } from '../bridge/contracts';

// Analysis requires a qualified semantic provider, independently of editor and cache support.
const analysisGameSupport = {
  sword: true,
  shield: true,
  scarlet: true,
  violet: true,
  za: true,
  pla: false
} satisfies Record<ProjectGame, boolean>;

export function supportsAnalysisTools(game: ProjectGame | null): boolean {
  return game !== null && analysisGameSupport[game];
}
