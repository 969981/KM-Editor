/* SPDX-License-Identifier: GPL-3.0-only */

import { Activity, Check, CircleAlert, Clock3 } from 'lucide-react';
import { LoadingProgress } from '../../components/LoadingProgress';
import { useLocalization } from '../../localization';
import {
  analysisSystemIds,
  analysisPreloadOrder,
  type AnalysisLoadingMode,
  type AnalysisPreparationSnapshot,
  type AnalysisSystemId
} from './analysisPreparation';

export function AnalysisPreparationPanel({
  mode,
  snapshot
}: {
  mode: AnalysisLoadingMode;
  snapshot: AnalysisPreparationSnapshot;
}) {
  const { t } = useLocalization();
  const isReady = snapshot.readyCount === snapshot.targetCount && snapshot.errorCount === 0;
  const hasError = snapshot.errorCount > 0;
  const phase = hasError ? 'error' : isReady ? 'ready' : 'preparing';
  const targetedTools = new Set<AnalysisSystemId>([
    'semanticProject',
    ...analysisPreloadOrder(mode)
  ]);

  return (
    <LoadingProgress className="analysis-preparation-panel" label={t('analysisPreparation.title')}
      status={hasError ? 'error' : isReady ? 'complete' : 'running'}
      statusLabel={t(`analysisPreparation.phase.${phase}`)}
      completed={snapshot.completedUnitCount} total={snapshot.totalUnitCount}>
      <ul className="analysis-preparation-systems">
        {analysisSystemIds.map((system) => {
          const isTargeted = targetedTools.has(system);
          const progress = snapshot.progressBySystem[system];
          const state = !isTargeted && snapshot.states[system] === 'waiting'
            ? 'onDemand'
            : snapshot.states[system];
          return (
            <li key={system}>
              {state === 'ready' ? (
                <Check aria-hidden="true" size={15} />
              ) : state === 'error' ? (
                <CircleAlert aria-hidden="true" size={15} />
              ) : state === 'loading' ? (
                <Activity aria-hidden="true" size={15} />
              ) : (
                <Clock3 aria-hidden="true" size={15} />
              )}
              <span>{t(`analysisPreparation.system.${system}`)}</span>
              <small>
                {t(`analysisPreparation.state.${state}`)}
                {isTargeted && state !== 'onDemand'
                  ? ` (${progress.completedUnitCount}/${progress.totalUnitCount})`
                  : null}
              </small>
            </li>
          );
        })}
      </ul>
    </LoadingProgress>
  );
}
