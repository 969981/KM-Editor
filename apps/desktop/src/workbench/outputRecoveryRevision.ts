/* SPDX-License-Identifier: GPL-3.0-only */

export type ObservedOutputRecoveryRevision = {
  revision: string | null;
  scopeKey: string | null;
};

export type OutputRecoveryRevisionObservation = {
  next: ObservedOutputRecoveryRevision;
  shouldInvalidateQueries: boolean;
};

export function observeOutputRecoveryRevision(
  current: ObservedOutputRecoveryRevision,
  scopeKey: string | null,
  revision: string | null
): OutputRecoveryRevisionObservation {
  if (scopeKey === null) {
    return {
      next: { revision: null, scopeKey: null },
      shouldInvalidateQueries: false
    };
  }
  if (current.scopeKey !== scopeKey) {
    return {
      next: { revision, scopeKey },
      shouldInvalidateQueries: false
    };
  }
  if (revision === null || current.revision === revision) {
    return { next: current, shouldInvalidateQueries: false };
  }
  if (current.revision === null) {
    return {
      next: { revision, scopeKey },
      shouldInvalidateQueries: false
    };
  }
  return {
    next: { revision, scopeKey },
    shouldInvalidateQueries: true
  };
}
