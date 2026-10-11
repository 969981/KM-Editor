/* SPDX-License-Identifier: GPL-3.0-only */

import { Layers, Plus } from 'lucide-react';
import { useState } from 'react';
import type { AppSidebarProps } from '../../components/AppSidebar';
import { useLocalization } from '../../localization';
import { getWorkbenchCapabilityRegistration, getWorkbenchSectionLabelKey } from '../../workbench/capabilityRegistry';
import { canAccessWorkflowSectionForHealth, isWorkflowNavigationVisibleForGame } from '../../workflowGameSupport';
import { useBetaEditorsEnabled } from '../settings/BetaEditorsSettings';
import { CategoryContextMenu, type CategoryMenuState } from './CategoryContextMenu';
import { CategoryDiagnostics, CategoryDialog, type CategoryDialogAction } from './CategoryDialogs';
import { useEditorCategories } from './categoryPreferences';
import { useCategoryDragging } from './useCategoryDragging';

export function SidebarCategories(props: AppSidebarProps) {
  const { activeSection, availableWorkflowSectionIds, canShowEditableWorkflowNavigation,
    canShowGameplaySettingsNavigation, canShowWorkflowNavigation, expandedWorkflowGroups,
    hasCriticalWriteOperation, isEditSessionOperationBusy, isSidebarCompact, onNavigate,
    onToggleWorkflowGroup, selectedGame, suppressedActiveWorkflowGroup } = props;
  const { t, translateLiteral } = useLocalization();
  const { groups, blocked } = useEditorCategories();
  const showBeta = useBetaEditorsEnabled();
  const [menu, setMenu] = useState<CategoryMenuState | null>(null);
  const [dialog, setDialog] = useState<CategoryDialogAction | null>(null);
  const navigationDisabled = isEditSessionOperationBusy || hasCriticalWriteOperation;
  const visibleGroups = groups.filter(group => group.id !== 'betaEditors' || (canShowWorkflowNavigation && showBeta))
    .map(group => ({ ...group, sectionIds: group.sectionIds.filter(sectionId =>
      (sectionId === 'modMerger' ? true : sectionId === 'gameplaySettings' || sectionId === 'cameraControl' || sectionId === 'battleItems' || sectionId === 'cameraHook'
        ? canShowGameplaySettingsNavigation
        : canShowWorkflowNavigation && canAccessWorkflowSectionForHealth(sectionId,
          canShowWorkflowNavigation, canShowEditableWorkflowNavigation))
      && isWorkflowNavigationVisibleForGame(sectionId, selectedGame, availableWorkflowSectionIds)) }))
    .filter(group => group.custom || group.sectionIds.length > 0 || canShowWorkflowNavigation);
  const expand = (id: string) => {
    const active = groups.find(group => group.id === id)?.sectionIds.includes(activeSection);
    if ((!expandedWorkflowGroups.has(id) && !active) || suppressedActiveWorkflowGroup === id) onToggleWorkflowGroup(id);
  };
  const drag = useCategoryDragging(visibleGroups, navigationDisabled || blocked, expand);
  return <>
    <CategoryDiagnostics />
    <span className="sr-only" id="category-drag-help">{t('categories.dragHelp')}</span>
    {visibleGroups.map(group => {
      const visibleSectionIds = group.sectionIds;
      // Keep empty categories available as drop destinations, including after their last editor moves out.
      const label = group.customName ?? (group.labelKey ? t(group.labelKey) : translateLiteral(group.label));
      const hasActiveSection = visibleSectionIds.includes(activeSection);
      const expanded = expandedWorkflowGroups.has(group.id) || (hasActiveSection && suppressedActiveWorkflowGroup !== group.id);
      const locked = group.id === 'betaEditors';
      return <div className={`nav-workflow-group${hasActiveSection ? ' nav-workflow-group-active' : ''}`} key={group.id}>
        <button {...drag.bindings(group)} aria-describedby={locked ? undefined : 'category-drag-help'}
          aria-expanded={expanded} aria-label={label} className={`nav-group-button${drag.className(group.id)}`}
          data-localization-ignore={group.customName !== undefined ? 'true' : undefined}
          onClick={() => onToggleWorkflowGroup(group.id)} onContextMenu={event => {
            if (locked) return;
            event.preventDefault();
            if (navigationDisabled || blocked) return;
            const bounds = event.currentTarget.getBoundingClientRect();
            setMenu({ category: group, name: label, x: event.clientX || bounds.left,
              y: event.clientY || bounds.bottom, trigger: event.currentTarget });
          }} title={isSidebarCompact ? label : undefined} type="button">
          <Layers aria-hidden="true" size={16} />
          <span data-localization-ignore={group.customName !== undefined ? 'true' : undefined}>{label}</span>
        </button>
        {expanded ? <div className="nav-group-items"
          onDragOver={!visibleSectionIds.length ? drag.bindings(group).onDragOver : undefined}
          onDragLeave={!visibleSectionIds.length ? drag.bindings(group).onDragLeave : undefined}
          onDrop={!visibleSectionIds.length ? drag.bindings(group).onDrop : undefined}>
          {!visibleSectionIds.length ? <p className="category-empty">{t('categories.empty')}</p> : null}
          {visibleSectionIds.map(sectionId => {
            const section = getWorkbenchCapabilityRegistration(sectionId);
            const Icon = section.icon;
            const label = section.id === 'gameplaySettings' || section.id === 'modelViewer' || section.id === 'soundStudio'
              ? t(getWorkbenchSectionLabelKey(section.id)) : translateLiteral(section.label);
            return <button {...drag.bindings(group, sectionId)} aria-current={activeSection === sectionId ? 'page' : undefined}
              aria-describedby={locked ? undefined : 'category-drag-help'} aria-label={label}
              className={`nav-button nav-child-button${drag.className(sectionId)}`} disabled={navigationDisabled}
              key={sectionId} onClick={() => onNavigate(sectionId)} title={isSidebarCompact ? label : undefined} type="button">
              <Icon aria-hidden="true" size={16} /><span>{label}</span>
            </button>;
          })}
        </div> : null}
      </div>;
    })}
    <button aria-label={t('categories.create')} className="nav-button nav-create-category" disabled={navigationDisabled || blocked}
      onClick={() => setDialog({ type: 'create' })} title={isSidebarCompact ? t('categories.create') : undefined} type="button">
      <Plus aria-hidden="true" size={18} /><span>{t('categories.create')}</span>
    </button>
    {menu ? <CategoryContextMenu menu={menu} onClose={() => setMenu(null)} onDialog={setDialog} /> : null}
    {dialog ? <CategoryDialog action={dialog} onClose={() => setDialog(null)} /> : null}
  </>;
}
