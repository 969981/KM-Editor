/* SPDX-License-Identifier: GPL-3.0-only */
export type RestorationSources = { donor: string; donorZa: string; donorPla: string; support: string };
const storageKey = 'km-editor.restoration-sources.v1';
const sharedStorageKey = 'km-editor.restoration-sources.v2';

function read(): Record<string, RestorationSources> {
  try {
    const text = localStorage.getItem(storageKey);
    if (!text || text.length > 65536) return {};
    const value: unknown = JSON.parse(text);
    if (!value || typeof value !== 'object' || Array.isArray(value)) return {};
    return Object.fromEntries(Object.entries(value).filter(([key, source]) => key.length < 8192
      && typeof source?.donor === 'string' && source.donor.length < 4096
      && typeof source?.support === 'string' && source.support.length < 4096
      && (source.donorZa === undefined || typeof source.donorZa === 'string' && source.donorZa.length < 4096)
      && (source.donorPla === undefined || typeof source.donorPla === 'string' && source.donorPla.length < 4096))
      .slice(-8).map(([key, source]) => [key, { ...source, donorZa: source.donorZa ?? '', donorPla: source.donorPla ?? '' }]));
  } catch { return {}; }
}
export function readRestorationSources(project: string, support: string): RestorationSources {
  try {
    const saved: unknown = JSON.parse(localStorage.getItem(sharedStorageKey) ?? 'null');
    if (saved && typeof saved === 'object' && ['donor', 'donorZa', 'support'].every(field =>
      typeof Reflect.get(saved, field) === 'string' && Reflect.get(saved, field).length < 4096)
      && (Reflect.get(saved, 'donorPla') === undefined || typeof Reflect.get(saved, 'donorPla') === 'string' && Reflect.get(saved, 'donorPla').length < 4096))
      return { ...(saved as RestorationSources), donorPla: Reflect.get(saved, 'donorPla') ?? '' };
  } catch { /* Fall back to the previous preference format. */ }
  const previous = read();
  const sources = previous[project] ?? Object.values(previous).at(-1) ?? { donor: '', donorZa: '', donorPla: '', support };
  rememberRestorationSources(project, sources);
  return sources;
}
export function rememberRestorationSources(project: string, sources: Omit<RestorationSources, 'donorPla'> & { donorPla?: string }) {
  void project;
  try {
    localStorage.setItem(sharedStorageKey, JSON.stringify({ ...sources, donorPla: sources.donorPla ?? '' }));
    return true;
  } catch { return false; }
}
