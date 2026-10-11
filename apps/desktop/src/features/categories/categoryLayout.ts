/* SPDX-License-Identifier: GPL-3.0-only */

import type { WorkbenchSection } from '../../workbench/workbenchSections';
import { workflowNavigationGroups, type WorkflowNavigationGroup } from '../../workflowGameSupport';

export type CategoryPreference = { id: string; name?: string; editorIds: string[] };
export type CategoryLayout = { version: 1; categories: CategoryPreference[] };
export type EditorCategory = {
  id: string;
  label: string;
  labelKey?: string;
  customName?: string;
  custom: boolean;
  sectionIds: WorkbenchSection[];
};
export type CategoryAction =
  | { type: 'create'; id: string; name: string }
  | { type: 'rename'; id: string; name: string }
  | { type: 'delete'; id: string }
  | { type: 'moveCategory'; id: string; beforeId: string | null }
  | { type: 'moveEditor'; id: string; categoryId: string; beforeId: string | null };
export type CategoryIssue = 'load' | 'invalid' | 'version' | 'save' | 'name' | 'locked' | 'stale';
export const emptyCategoryLayout: CategoryLayout = { version: 1, categories: [] };
export const isCustomCategoryId = (id: string) => /^custom:[a-zA-Z0-9-]+$/.test(id);

export function parseCategoryLayout(raw: string | null): { layout: CategoryLayout; issue: CategoryIssue | null } {
  if (raw === null) return { layout: emptyCategoryLayout, issue: null };
  try {
    const value = JSON.parse(raw);
    if (!value || typeof value !== 'object') throw new Error();
    if (typeof value.version === 'number' && value.version > 1) {
      return { layout: emptyCategoryLayout, issue: 'version' };
    }
    if (value.version !== 1 || !Array.isArray(value.categories)) throw new Error();
    const ids = new Set<string>();
    const categories: CategoryPreference[] = value.categories.map((entry: CategoryPreference) => {
      if (!entry || typeof entry.id !== 'string' || !entry.id || ids.has(entry.id)
          || (entry.name !== undefined && (typeof entry.name !== 'string' || !entry.name.trim()))
          || (isCustomCategoryId(entry.id) && !entry.name)
          || !Array.isArray(entry.editorIds) || entry.editorIds.some(id => typeof id !== 'string')) throw new Error();
      ids.add(entry.id);
      return { id: entry.id, name: entry.name, editorIds: [...new Set(entry.editorIds)] };
    });
    return { layout: { version: 1, categories }, issue: null };
  } catch {
    return { layout: emptyCategoryLayout, issue: 'invalid' };
  }
}

// Defaults remain the authority for capabilities and newly introduced editors.
// Personal preferences contain only presentation identities, never workflow state.
export function resolveEditorCategories(
  layout: CategoryLayout,
  defaults: readonly WorkflowNavigationGroup[] = workflowNavigationGroups
): EditorCategory[] {
  const definitions = new Map(defaults.map(group => [group.id as string, group]));
  const beta = definitions.get('betaEditors');
  const lockedEditors = new Set(beta?.sectionIds ?? []);
  const knownEditors = new Set(defaults.flatMap(group => group.sectionIds));
  const assigned = new Set<WorkbenchSection>();
  const groups: EditorCategory[] = [];
  for (const saved of layout.categories) {
    if (saved.id === 'betaEditors') continue;
    const definition = definitions.get(saved.id);
    if (!definition && !isCustomCategoryId(saved.id)) continue;
    const sectionIds = saved.editorIds.filter((id): id is WorkbenchSection => {
      const section = id as WorkbenchSection;
      if (!knownEditors.has(section) || lockedEditors.has(section) || assigned.has(section)) return false;
      assigned.add(section);
      return true;
    });
    groups.push({ id: saved.id, label: definition?.label ?? '', labelKey: definition?.labelKey,
      customName: saved.name, custom: !definition, sectionIds });
  }
  for (const definition of defaults) {
    if (definition.id === 'betaEditors') continue;
    let group = groups.find(candidate => candidate.id === definition.id);
    if (!group) {
      group = { ...definition, custom: false, sectionIds: [] };
      groups.push(group);
    }
    for (const id of definition.sectionIds) {
      if (!assigned.has(id) && !lockedEditors.has(id)) {
        group.sectionIds.push(id);
        assigned.add(id);
      }
    }
  }
  if (beta) groups.push({ ...beta, custom: false, sectionIds: [...beta.sectionIds] });
  return groups;
}

export function changeCategoryLayout(layout: CategoryLayout, action: CategoryAction): CategoryLayout | CategoryIssue {
  const groups = resolveEditorCategories(layout);
  const group = groups.find(candidate => candidate.id === action.id);
  if (action.type === 'create' || action.type === 'rename') {
    if (!action.name.trim()) return 'name';
  }
  if (action.type !== 'moveEditor' && action.type !== 'create' && !group) return 'stale';
  if (action.type !== 'moveEditor' && action.id === 'betaEditors') return 'locked';

  if (action.type === 'create') {
    if (!isCustomCategoryId(action.id) || groups.some(candidate => candidate.id === action.id)) return 'stale';
    groups.splice(groups.length - 1, 0, { id: action.id, label: '', customName: action.name.trim(), custom: true, sectionIds: [] });
  } else if (action.type === 'rename') {
    group!.customName = action.name.trim();
  } else if (action.type === 'delete') {
    if (!group!.custom) return 'locked';
    groups.splice(groups.indexOf(group!), 1);
    // Unassigned editors return to their registered defaults on the next resolution.
  } else if (action.type === 'moveCategory') {
    const target = action.beforeId === null ? groups.find(candidate => candidate.id === 'betaEditors')
      : groups.find(candidate => candidate.id === action.beforeId);
    if (!target) return 'stale';
    if (target === group) return layout;
    groups.splice(groups.indexOf(group!), 1);
    groups.splice(groups.indexOf(target), 0, group!);
  } else {
    const source = groups.find(candidate => candidate.sectionIds.includes(action.id as WorkbenchSection));
    const target = groups.find(candidate => candidate.id === action.categoryId);
    if (!source || !target) return 'stale';
    if (source.id === 'betaEditors' || target.id === 'betaEditors') return 'locked';
    if (action.beforeId === action.id) return layout;
    if (action.beforeId !== null && !target.sectionIds.includes(action.beforeId as WorkbenchSection)) return 'stale';
    source.sectionIds.splice(source.sectionIds.indexOf(action.id as WorkbenchSection), 1);
    const index = action.beforeId === null ? target.sectionIds.length : target.sectionIds.indexOf(action.beforeId as WorkbenchSection);
    target.sectionIds.splice(index, 0, action.id as WorkbenchSection);
  }
  const knownEditors = new Set(workflowNavigationGroups.flatMap(candidate => candidate.sectionIds));
  const categories: CategoryPreference[] = groups.filter(candidate => candidate.id !== 'betaEditors').map(candidate => ({
    id: candidate.id, name: candidate.customName,
    editorIds: [...candidate.sectionIds, ...(layout.categories.find(saved => saved.id === candidate.id)?.editorIds
      .filter(id => !knownEditors.has(id as WorkbenchSection)) ?? [])]
  }));
  // Retain dormant identities across upgrades or temporarily older app versions.
  categories.push(...layout.categories.filter(saved => saved.id !== 'betaEditors'
    && !groups.some(candidate => candidate.id === saved.id) && !(action.type === 'delete' && saved.id === action.id)));
  return { version: 1, categories };
}
