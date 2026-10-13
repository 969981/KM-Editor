/* SPDX-License-Identifier: GPL-3.0-only */
export type RestorationSources = { donor: string; donorZa: string; support: string };
const storageKey = 'km-editor.restoration-sources.v1';

function read(): Record<string, RestorationSources> {
  try {
    const text = localStorage.getItem(storageKey);
    if (!text || text.length > 65536) return {};
    const value: unknown = JSON.parse(text);
    if (!value || typeof value !== 'object' || Array.isArray(value)) return {};
    return Object.fromEntries(Object.entries(value).filter(([key, source]) => key.length < 8192
      && typeof source?.donor === 'string' && source.donor.length < 4096
      && typeof source?.support === 'string' && source.support.length < 4096
      && (source.donorZa === undefined || typeof source.donorZa === 'string' && source.donorZa.length < 4096))
      .slice(-8).map(([key, source]) => [key, { ...source, donorZa: source.donorZa ?? '' }]));
  } catch { return {}; }
}
export function readRestorationSources(project: string, support: string): RestorationSources {
  return read()[project] ?? { donor: '', donorZa: '', support };
}
export function rememberRestorationSources(project: string, sources: RestorationSources) {
  try {
    const entries = Object.entries(read()).filter(([key]) => key !== project).slice(-7);
    localStorage.setItem(storageKey, JSON.stringify(Object.fromEntries([...entries, [project, sources]])));
  } catch { /* Source selection remains available when local preferences cannot be saved. */ }
}
