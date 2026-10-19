/* SPDX-License-Identifier: GPL-3.0-only */

import { useCallback, useRef, useState } from 'react';
import { useLocalization } from '../localization';
import { LoadingProgress } from './LoadingProgress';

type Operation = 'start' | 'stage' | 'remove' | 'validate' | 'review';
type Phase = 'starting' | 'staging' | 'removing' | 'capturing' | 'refreshing' | 'validating' | 'planning';
export type EditorProgressDetails = {
  operation: Operation;
  phase: Phase;
  phases?: readonly Phase[];
  subject?: string;
  completed?: number;
  total?: number;
};
type EditorProgress = EditorProgressDetails & { owner: object; scope: number; key: number };

export function useEditorOperationProgress(scope: number) {
  const [progress, setProgress] = useState<EditorProgress | null>(null);
  const sequence = useRef(0);
  const begin = useCallback((owner: object, details: EditorProgressDetails) => {
    setProgress({ ...details, owner, scope, key: ++sequence.current });
  }, [scope]);
  const update = useCallback((owner: object, details: Partial<EditorProgressDetails>) => {
    setProgress(current => current?.owner === owner && current.scope === scope
      ? { ...current, ...details } : current);
  }, [scope]);
  const finish = useCallback((owner: object | null) => {
    setProgress(current => current?.owner === owner ? null : current);
  }, []);
  const clear = useCallback(() => setProgress(null), []);
  return { progress: progress?.scope === scope ? progress : null, begin, update, finish, clear };
}

export function EditorOperationProgress({ progress }: { progress: EditorProgress }) {
  const { t } = useLocalization();
  const phases = progress.phases ?? [progress.phase];
  const active = phases.indexOf(progress.phase);
  return <LoadingProgress className="is-compact wide-panel workspace-operation-progress"
    label={t(`editorProgress.operation.${progress.operation}`)}
    detail={t(`editorProgress.activity.${progress.phase}`)} operationKey={progress.key}
    completed={progress.completed} total={progress.total}
    countLabel={progress.total === undefined ? undefined : t('editorProgress.selections', {
      completed: progress.completed ?? 0, total: progress.total
    })}
    steps={phases.map((phase, index) => ({ label: t(`editorProgress.phase.${phase}`),
      state: index < active ? 'complete' : index === active ? 'active' : 'pending' }))}>
    {progress.subject ? <p data-localization-ignore="true">{progress.subject}</p> : null}
  </LoadingProgress>;
}
