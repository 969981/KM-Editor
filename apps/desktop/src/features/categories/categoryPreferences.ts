/* SPDX-License-Identifier: GPL-3.0-only */

import { useSyncExternalStore } from 'react';
import { changeCategoryLayout, emptyCategoryLayout, parseCategoryLayout, resolveEditorCategories,
  type CategoryAction, type CategoryIssue, type CategoryLayout } from './categoryLayout';

export const categoryLayoutStorageKey = 'km-editor.navigation-categories.v1';
type Snapshot = { layout: CategoryLayout; groups: ReturnType<typeof resolveEditorCategories>;
  issue: CategoryIssue | null; blocked: boolean };
let snapshot: Snapshot | undefined;
const listeners = new Set<() => void>();

function read(): Snapshot {
  let result: { layout: CategoryLayout; issue: CategoryIssue | null };
  try { result = parseCategoryLayout(localStorage.getItem(categoryLayoutStorageKey)); }
  catch { result = { layout: emptyCategoryLayout, issue: 'load' }; }
  return { ...result, groups: resolveEditorCategories(result.layout), blocked: result.issue !== null };
}

function getSnapshot() { return snapshot ??= read(); }
function notify() { listeners.forEach(listener => listener()); }
function handleStorage(event: StorageEvent) {
  if (event.key !== categoryLayoutStorageKey && event.key !== null) return;
  snapshot = read();
  notify();
}
function subscribe(listener: () => void) {
  if (listeners.size === 0) {
    window.addEventListener('storage', handleStorage);
    snapshot = read();
  }
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
    if (listeners.size === 0) window.removeEventListener('storage', handleStorage);
  };
}
export function useEditorCategories() { return useSyncExternalStore(subscribe, getSnapshot, getSnapshot); }

export function reloadCategoryPreferences() {
  snapshot = read();
  notify();
}

export function reportCategoryIssue(issue: CategoryIssue) {
  snapshot = { ...getSnapshot(), issue };
  notify();
}

export function updateEditorCategories(action: CategoryAction | { type: 'restore' }): boolean {
  // Re-read before writing so another window's completed customization is preserved.
  const current = read();
  if (current.blocked && action.type !== 'restore') {
    snapshot = current;
    notify();
    return false;
  }
  const next = action.type === 'restore' ? emptyCategoryLayout : changeCategoryLayout(current.layout, action);
  if (typeof next === 'string') { reportCategoryIssue(next); return false; }
  try {
    localStorage.setItem(categoryLayoutStorageKey, JSON.stringify(next));
    snapshot = { layout: next, groups: resolveEditorCategories(next), issue: null, blocked: false };
    notify();
    return true;
  } catch {
    reportCategoryIssue('save');
    return false;
  }
}
