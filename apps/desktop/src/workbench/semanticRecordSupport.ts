/* SPDX-License-Identifier: GPL-3.0-only */

import type { ProjectGame } from '../bridge/contracts';

// Record search and inspection require a qualified semantic provider.
const semanticRecordSupport = {
  sword: true,
  shield: true,
  scarlet: true,
  violet: true,
  za: true,
  pla: false
} satisfies Record<ProjectGame, boolean>;

export function supportsSemanticRecordLookup(game: ProjectGame | null): boolean {
  return game !== null && semanticRecordSupport[game];
}
