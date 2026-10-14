/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useState } from 'react';
import { Check, LoaderCircle } from 'lucide-react';
import type { ProjectPaths } from '../../bridge/contracts';
import type { RestorationProjectBridgeApi } from '../../bridge/restorationProjectBridge';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { useLocalization } from '../../localization';

export function RestorationDonorStatus({ donor, path, support, paths, bridge }: {
  donor: 'ScarletViolet' | 'LegendsZA'; path: string; support: string; paths: ProjectPaths; bridge: RestorationProjectBridgeApi;
}) {
  const { t } = useLocalization();
  const key = JSON.stringify([donor, path.trim(), support.trim(), paths]);
  const [result, setResult] = useState<{ key: string; status: string } | null>(null);
  const [retry, setRetry] = useState(0);
  const status = result?.key === key ? result.status : 'checking';
  usePublishCommonEditorError({ domain: 'workflow.restoreMissingPokemon', field: donor,
    message: path.trim() && status === 'failed' ? t('restoration.donorCache.failed') : null });
  useEffect(() => {
    if (!path.trim()) return;
    let canceled = false;
    const timer = window.setTimeout(async () => {
      try {
        const response = await bridge.inspectRestorationDonor({ paths, donor, donorRomFsPath: path.trim(), compressionSupportFolderPath: support.trim() || null });
        if (!canceled) setResult({ key, status: response.mode });
      } catch { if (!canceled) setResult({ key, status: 'failed' }); }
    }, 700);
    return () => { canceled = true; window.clearTimeout(timer); };
    // The complete request identity is encoded in key.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, bridge, retry]);
  if (!path.trim()) return null;
  return <div className="restoration-donor-status" role="status">
    {status === 'checking' ? <LoaderCircle size={14} className="restoration-spin" aria-hidden="true" /> : status !== 'failed' ? <Check size={14} aria-hidden="true" /> : null}
    <span>{t(`restoration.donorCache.${status}`)}</span>
    {status === 'failed' ? <button className="secondary-button" type="button" onClick={() => { setResult(null); setRetry(value => value + 1); }}>{t('restoration.refresh')}</button> : null}
  </div>;
}
