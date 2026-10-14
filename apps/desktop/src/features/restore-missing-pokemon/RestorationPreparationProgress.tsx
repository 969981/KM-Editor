/* SPDX-License-Identifier: GPL-3.0-only */
import { Check, Circle, LoaderCircle, XCircle } from 'lucide-react';
import type { RestorationPreparation } from '../../bridge/restorationContracts';
import { useLocalization } from '../../localization';

const stages = ['sources', 'models', 'gameplay', 'voices', 'validation', 'saving'] as const;
export function RestorationPreparationProgress({ preparation: job, name, onCancel }: {
  preparation: RestorationPreparation; name: string; onCancel: () => void;
}) {
  const { t } = useLocalization();
  const running = job.status === 'preparing';
  const complete = job.status === 'prepared';
  const stage = complete ? stages.length : Math.max(0, stages.indexOf(job.phase as typeof stages[number]));
  const total = job.totalUnits;
  const completed = total ? Math.min(job.completedUnits, total) : 0;
  const seconds = Math.floor(job.elapsedMilliseconds / 1000);
  const elapsed = `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
  const activity = job.activity || job.phase;
  return <section className="restoration-preparation" aria-label={t('restoration.progress.title')} aria-busy={running}>
    <div className="restoration-preparation-heading">
      <div className="restoration-preparation-title">
        {complete ? <Check aria-hidden="true" /> : running ? <LoaderCircle className="restoration-spin" aria-hidden="true" /> : <XCircle aria-hidden="true" />}
        <div><strong data-localization-ignore="true">{name}</strong><p role="status" aria-live="polite">
          {t(job.cancellationRequested && running ? 'restoration.progress.canceling' : `restoration.status.${job.status}`)}
        </p></div>
      </div>
      <span className="restoration-elapsed">{t('restoration.progress.elapsed', { time: elapsed })}</span>
      {running ? <button className="secondary-button" type="button" onClick={onCancel} disabled={job.cancellationRequested}>
        {t('restoration.cancelPreparation')}</button> : null}
    </div>
    <ol className="restoration-steps">{stages.map((phase, index) => <li key={phase}
      className={index < stage ? 'done' : index === stage ? 'current' : ''} aria-current={index === stage ? 'step' : undefined}>
      {index < stage ? <Check size={16} aria-hidden="true" /> : <Circle size={14} aria-hidden="true" />}
      {t(`restoration.progress.stage.${phase}`)}
    </li>)}</ol>
    {running ? <div className="restoration-current-work">
      <div className="restoration-progress-caption"><span role="status" aria-live="polite">{t(`restoration.progress.activity.${activity}`)}</span>
        {total ? <span>{t('restoration.progress.units', { completed, total })}</span> : null}</div>
      <progress aria-label={t(`restoration.progress.activity.${activity}`)} max={total ?? 1} value={total ? completed : undefined} />
      <p>{t(job.cacheMode === 'existing' ? 'restoration.progress.existing' : 'restoration.progress.temporary')}</p>
    </div> : null}
    {Object.values(job.unavailableSourceMoves).some(ids => ids.length > 0) ? <p>{t('restoration.excludedMoves', {
      count: Object.values(job.unavailableSourceMoves).reduce((count, ids) => count + ids.length, 0) })}</p> : null}
  </section>;
}
