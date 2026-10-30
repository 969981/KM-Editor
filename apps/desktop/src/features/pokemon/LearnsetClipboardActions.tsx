/* SPDX-License-Identifier: GPL-3.0-only */
import { useEffect, useRef, useState } from 'react';
import { Copy, ClipboardPaste } from 'lucide-react';
import { useLocalization } from '../../localization';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { copyLearnset, pasteLearnset, type LearnsetClipboardEntry, type LearnsetClipboardFamily } from './learnsetClipboard';

type Props = {
  family: LearnsetClipboardFamily;
  scope: string;
  revision: string;
  entries: LearnsetClipboardEntry[] | null;
  enabled: boolean;
  busy: boolean;
  onPaste: (entries: LearnsetClipboardEntry[]) => boolean;
};

export function LearnsetClipboardActions(props: Props) {
  const { t } = useLocalization();
  const [running, setRunning] = useState(false);
  const [feedback, setFeedback] = useState<{ error: boolean; key: string } | null>(null);
  const operation = useRef(false), mounted = useRef(true), latest = useRef(props);
  latest.current = props;
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  useEffect(() => setFeedback(null), [props.scope]);
  usePublishCommonEditorError({ domain: 'workflow.pokemon', field: 'clipboard', message: feedback?.error ? t(feedback.key) : null });
  const run = async (paste: boolean) => {
    if (operation.current || props.busy || (paste ? !props.enabled : props.entries === null)) return;
    operation.current = true; setRunning(true); setFeedback(null);
    const captured = props;
    const current = () => mounted.current && latest.current.scope === captured.scope
      && latest.current.revision === captured.revision && !latest.current.busy
      && (!paste || latest.current.enabled);
    try {
      if (paste) {
        const entries = await pasteLearnset(props.family);
        if (!current()) return;
        if (!latest.current.onPaste(entries)) throw new Error('invalid-learnset');
      } else {
        await copyLearnset(props.entries!, props.family);
      }
      if (mounted.current && latest.current.scope === captured.scope) setFeedback({ error: false, key: paste ? 'pokemon.learnsetClipboard.pasted' : 'pokemon.learnsetClipboard.copied' });
    } catch {
      if (mounted.current && latest.current.scope === captured.scope) setFeedback({ error: true, key: paste ? 'pokemon.learnsetClipboard.rejected' : 'rowClipboard.feedback.clipboardWriteFailed' });
    } finally {
      operation.current = false;
      if (mounted.current) setRunning(false);
    }
  };
  return <>
    <div className="draft-action-row learnset-clipboard-actions">
      <button type="button" className="secondary-button compact-button" disabled={props.busy || running || props.entries === null} onClick={() => void run(false)}><Copy size={16} aria-hidden="true" />{t('pokemon.learnsetClipboard.copy')}</button>
      <button type="button" className="secondary-button compact-button" disabled={!props.enabled || props.busy || running} onClick={() => void run(true)}><ClipboardPaste size={16} aria-hidden="true" />{t('pokemon.learnsetClipboard.paste')}</button>
    </div>
    {feedback ? <p className={feedback.error ? 'field-error' : 'field-note'} role={feedback.error ? 'alert' : 'status'}>{t(feedback.key)}</p> : null}
  </>;
}
