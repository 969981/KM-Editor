/* SPDX-License-Identifier: GPL-3.0-only */
import type { ReactNode } from 'react';
import { SlidersVertical } from 'lucide-react';
import type { PlaField, PlaPage } from '../../bridge/plaProjectBridge';
import { FieldLabel } from '../../components/FieldLabel';
import { useLocalization } from '../../localization';

export function PlaRulesWorkspace({ kind, page, controls, sessionBar, actions, refresh, loading, fieldLabel, renderInput }: {
  kind: 'capture' | 'friendship' | 'shiny'; page: PlaPage | null; controls: ReactNode; sessionBar: ReactNode;
  actions: ReactNode; refresh: ReactNode; loading: boolean; fieldLabel: (field: PlaField) => string; renderInput: (field: PlaField) => ReactNode;
}) {
  const { t, translateLiteral } = useLocalization();
  const fields = page?.fields ?? [];
  const groups = kind === 'capture' ? [
    { title: t('pla.rules.balls'), fields: fields.filter(field => field.key.startsWith('ball')) },
    { title: t('pla.rules.conditions'), fields: fields.filter(field => !field.key.startsWith('ball')) }
  ] : kind === 'friendship' ? [
    ...['Low', 'Mid', 'High'].map(band => ({ title: t('pla.rules.band' + band), fields: fields.filter(field => field.key.endsWith(band)) })),
    { title: t('pla.rules.experience'), fields: fields.filter(field => field.key === 'friendshipExpChance') }
  ] : [{ title: t('pla.kind.shiny'), fields }];
  return <section className="panel wide-panel pla-editor" aria-labelledby="pla-editor-title">
    <div className="panel-heading"><SlidersVertical size={18} aria-hidden="true" /><h2 id="pla-editor-title">{t('workbench.section.gameplayRules.label')}</h2>{refresh}</div>
    {controls}{sessionBar}{actions}
    <p className="field-note" id="pla-rules-help">{t('pla.notice.' + kind)}</p>
    {loading ? <p role="status">{translateLiteral('Loading')}</p> : null}
    <div className="editable-field-groups" aria-describedby="pla-rules-help">
      {groups.map(group => <fieldset className="editable-field-group" key={group.title}><legend>{group.title}</legend>
        <div className="editable-field-grid">{group.fields.map(field => <div className="path-field editable-field-control" key={field.key}>
          <FieldLabel htmlFor={'pla-field-' + field.key} label={fieldLabel(field)} />{renderInput(field)}
          <small className="field-note">{t(field.kind === 'factor' ? 'pla.rules.factorRange' : 'pla.rules.range', { min: field.minimum ?? 0, max: field.maximum ?? 0 })}</small>
        </div>)}</div>
      </fieldset>)}
    </div>
  </section>;
}
