/* SPDX-License-Identifier: GPL-3.0-only */
import type { EditSession, ProjectPaths } from './contracts';

type Context = { paths: ProjectPaths; session: EditSession | null };
let readContext: (() => Context) | null = null;
export function configureRestorationRequestContext(provider: () => Context) {
  readContext = provider;
  return () => { if (readContext === provider) readContext = null; };
}
export function restorationRevision(session: EditSession | null) {
  return JSON.stringify(session?.pendingEdits.filter(e => e.domain === 'workflow.restoreMissingPokemon') ?? []);
}
export function readRestorationRequestContext(payload: unknown) {
  const context = readContext?.();
  if (!context || typeof payload !== 'object' || payload === null) return null;
  const scope = 'scope' in payload && typeof payload.scope === 'object' && payload.scope !== null ? payload.scope : null;
  const paths = ('paths' in payload ? payload.paths : scope && 'paths' in scope ? scope.paths : null) as ProjectPaths | null;
  if (paths?.selectedGame !== context.paths.selectedGame || paths?.baseRomFsPath !== context.paths.baseRomFsPath
    || paths?.outputRootPath !== context.paths.outputRootPath || !['sword', 'shield'].includes(paths?.selectedGame ?? '')) return null;
  const revision = restorationRevision(context.session);
  const pathScope = JSON.stringify(context.paths);
  return { session: revision === '[]' ? null : context.session, revision,
    isCurrent: () => {
      const current = readContext?.();
      return current != null && pathScope === JSON.stringify(current.paths) && revision === restorationRevision(current.session);
    } };
}
export class StaleSpeciesCatalogError extends Error {
  constructor() { super('The Pokemon catalog changed while this editor was loading. Reload the editor.'); this.name = 'StaleSpeciesCatalogError'; }
}
