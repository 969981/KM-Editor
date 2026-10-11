/* SPDX-License-Identifier: GPL-3.0-only */

import { PanelLeftClose, PanelLeftOpen } from 'lucide-react';
import type { RefObject } from 'react';
import kmLogoUrl from '../assets/km-logo.png';
import type { ProjectGame } from '../bridge/contracts';
import { useLocalization } from '../localization';
import { SidebarCategories } from '../features/categories/SidebarCategories';
import {
  getWorkbenchCapabilitiesByNavigationKind,
  getWorkbenchSectionLabelKey
} from '../workbench/capabilityRegistry';
import type { WorkbenchSection } from '../workbenchStore';

const primaryNavigationSections = getWorkbenchCapabilitiesByNavigationKind('primary');
const utilityNavigationSections = getWorkbenchCapabilitiesByNavigationKind('utility');

export type AppSidebarProps = {
  activeSection: WorkbenchSection;
  appVersion: string;
  availableWorkflowSectionIds: ReadonlySet<WorkbenchSection>;
  canShowEditableWorkflowNavigation: boolean;
  canShowGameplaySettingsNavigation: boolean;
  canShowWorkflowNavigation: boolean;
  expandedWorkflowGroups: ReadonlySet<string>;
  hasAvailableUpdate: boolean;
  hasCriticalWriteOperation: boolean;
  isEditSessionOperationBusy: boolean;
  isSidebarCompact: boolean;
  onNavigate: (section: WorkbenchSection) => void;
  onToggle: () => void;
  onToggleWorkflowGroup: (groupId: string) => void;
  pendingEditCount: number;
  scrollRef: RefObject<HTMLDivElement | null>;
  selectedGame: ProjectGame;
  suppressedActiveWorkflowGroup: string | null;
  toggleRef: RefObject<HTMLButtonElement | null>;
};

export function AppSidebar(props: AppSidebarProps) {
  const { activeSection, appVersion, hasAvailableUpdate, hasCriticalWriteOperation,
    isEditSessionOperationBusy, isSidebarCompact, onNavigate, onToggle, pendingEditCount,
    scrollRef, toggleRef } = props;
  const { t, translateLiteral } = useLocalization();
  const toggleLabel = translateLiteral(
    isSidebarCompact ? 'Expand sidebar' : 'Collapse sidebar'
  );
  const navigationDisabled = isEditSessionOperationBusy || hasCriticalWriteOperation;

  return (
    <aside
      aria-label="Application navigation"
      className={`sidebar${isSidebarCompact ? ' sidebar-compact' : ''}`}
      id="application-sidebar"
    >
      <div className="brand">
        <img alt="" aria-hidden="true" className="brand-logo" src={kmLogoUrl} />
        <span className="brand-copy">
          <span className="brand-name">
            <span>KM Editor</span>
            <span className="brand-version">v{appVersion}</span>
          </span>
          <span className="brand-credit">Made by Matroskin</span>
        </span>
        <button
          aria-controls="application-sidebar"
          aria-expanded={!isSidebarCompact}
          aria-label={toggleLabel}
          aria-pressed={isSidebarCompact}
          className="sidebar-toggle"
          onClick={onToggle}
          ref={toggleRef}
          title={toggleLabel}
          type="button"
        >
          {isSidebarCompact ? (
            <PanelLeftOpen aria-hidden="true" size={18} />
          ) : (
            <PanelLeftClose aria-hidden="true" size={18} />
          )}
        </button>
      </div>

      <nav aria-label="Workspace" className="section-nav">
        <div className="sidebar-navigation-scroll" ref={scrollRef}>
          {primaryNavigationSections.map((section) => {
            const Icon = section.icon;
            const isActive = activeSection === section.id;
            const sectionLabel = section.id === 'workbench'
              ? t(getWorkbenchSectionLabelKey(section.id))
              : translateLiteral(section.label);

            return (
              <button
                aria-current={isActive ? 'page' : undefined}
                aria-label={sectionLabel}
                className="nav-button"
                disabled={navigationDisabled}
                key={section.id}
                onClick={() => onNavigate(section.id)}
                title={isSidebarCompact ? sectionLabel : undefined}
                type="button"
              >
                <Icon aria-hidden="true" size={18} />
                <span>{sectionLabel}</span>
              </button>
            );
          })}

          <SidebarCategories {...props} />
        </div>

        <div className="sidebar-utility-nav">
          {utilityNavigationSections.map((section) => {
            const Icon = section.icon;
            const isActive = activeSection === section.id;
            const sectionHasAvailableUpdate = section.id === 'settings' && hasAvailableUpdate;
            const navigationLabel = sectionHasAvailableUpdate
              ? `${translateLiteral(section.label)}: ${translateLiteral('Update Available')}`
              : translateLiteral(section.label);

            return (
              <button
                aria-current={isActive ? 'page' : undefined}
                aria-label={navigationLabel}
                className="nav-button"
                disabled={navigationDisabled}
                key={section.id}
                onClick={() => onNavigate(section.id)}
                title={isSidebarCompact ? navigationLabel : undefined}
                type="button"
              >
                <Icon aria-hidden="true" size={18} />
                <span>{section.label}</span>
                {section.id === 'changes' && pendingEditCount > 0 ? (
                  <span className="nav-count" aria-label={`${pendingEditCount} pending changes`}>
                    {pendingEditCount}
                  </span>
                ) : null}
                {sectionHasAvailableUpdate ? (
                  <span aria-hidden="true" className="nav-count">
                    !
                  </span>
                ) : null}
              </button>
            );
          })}
        </div>
      </nav>
    </aside>
  );
}
