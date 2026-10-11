/* SPDX-License-Identifier: GPL-3.0-only */

import { useEffect, useState } from 'react';
import type { OutputSafetyScope } from '../../bridge/outputSafetyContracts';
import type { PreviewInGameSettingsPackageRequest } from '../../bridge/inGameSettingsPackageContracts';
import type { ProjectBridge } from '../../bridge/projectBridge';
import { usePublishCommonEditorDiagnostics } from '../../components/CommonEditorDiagnostics';
import { inGameSettingsPackageErrorCodes } from '../../errorCodes';
import { useLocalization } from '../../localization';
import type { GameplaySettingsDestinations } from '../gameplay-settings/InGameSettingsPackagePanel';
import type { ApiDiagnostic } from '../../bridge/contracts';
import { toProjectBridgeDiagnostics } from '../../uiErrorDiagnostics';
import './CameraControlSection.css';

export type CameraHookStatus = {
  scopeKey: string;
  ready: boolean;
  requirement: PreviewInGameSettingsPackageRequest['requiredCameraHook'];
};

type Props = GameplaySettingsDestinations & {
  scope: OutputSafetyScope;
  bridge: Pick<ProjectBridge, 'inspectInGameSettingsPackage'>;
  onStatus: (status: CameraHookStatus) => void;
  onOpenCameraHook?: () => void;
  domain?: string;
};

export function CameraHookRequirement({ scope, bridge, destinations, onStatus, onOpenCameraHook,
  domain = 'workflow.cameraControl' }: Props) {
  const { t } = useLocalization();
  const [consoleOutput, setConsoleOutput] = useState(false);
  const [revision, setRevision] = useState(0);
  const [phase, setPhase] = useState<'checking' | 'ready' | 'missing'>('checking');
  const [diagnostics, setDiagnostics] = useState<ApiDiagnostic[]>([]);
  const [hookPath, setHookPath] = useState('');
  const scopeKey = JSON.stringify(scope);
  const destinationKey = JSON.stringify(destinations ?? []);
  useEffect(() => {
    let active = true;
    setDiagnostics([]);
    onStatus({ scopeKey, ready: consoleOutput, requirement: null });
    if (consoleOutput) return;
    setPhase('checking');
    const candidates = JSON.parse(destinationKey) as NonNullable<GameplaySettingsDestinations['destinations']>;
    void Promise.allSettled(candidates.map(async candidate => ({ candidate,
      response: await bridge.inspectInGameSettingsPackage({ scope, installationTarget: candidate.target,
        installationRootPath: candidate.destinationPath, useSharedOutput: true })
    }))).then(results => {
      if (!active) return;
      const installed = results.find(result => result.status === 'fulfilled' && result.value.response.snapshot.state === 'installed');
      if (installed?.status === 'fulfilled') {
        const { candidate } = installed.value;
        setPhase('ready'); setHookPath(candidate.destinationPath);
        onStatus({ scopeKey, ready: true, requirement: { installationTarget: candidate.target, installationRootPath: candidate.destinationPath } });
      } else {
        setPhase('missing'); setHookPath('');
        const failure = results.find(result => result.status === 'rejected');
        if (failure?.status === 'rejected') setDiagnostics(toProjectBridgeDiagnostics(failure.reason, t('cameraHook.checkFailed')));
      }
    });
    return () => { active = false; };
  }, [bridge, scopeKey, destinationKey, revision, consoleOutput, onStatus, t]);
  usePublishCommonEditorDiagnostics(consoleOutput ? [] : diagnostics.length ? diagnostics : phase === 'missing'
    ? [{ code: inGameSettingsPackageErrorCodes.unavailable, domain, field: 'inGamePackage',
      message: t('cameraHook.required'), severity: 'error' }] : []);
  return <div className="camera-hook-requirement">
    <label className="camera-hook-requirement__console">
      <input type="checkbox" checked={consoleOutput} onChange={event => setConsoleOutput(event.target.checked)} />
      <span>{t('cameraHook.consoleOutput')}</span>
    </label>
    {consoleOutput ? <p>{t('cameraHook.consoleHelp')}</p> : <>
      <p role="status">{t(`cameraHook.${phase}`)}</p>
      {phase === 'ready' ? <p><code>{hookPath}</code></p> : null}
      <div className="camera-hook-requirement__actions">
        <button className="secondary-button" type="button" onClick={onOpenCameraHook}>{t('cameraHook.open')}</button>
        <button className="secondary-button" type="button" disabled={phase === 'checking'} onClick={() => setRevision(value => value + 1)}>{t('cameraHook.check')}</button>
      </div>
    </>}
  </div>;
}
