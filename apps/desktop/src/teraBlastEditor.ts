/* SPDX-License-Identifier: GPL-3.0-only */

import type { EditSession, MoveEditableField } from './bridge/contracts';

export function hasTeraBlastEdits(session: EditSession | null) {
  return session?.pendingEdits.some(edit =>
    edit.domain === 'workflow.moves' && edit.field?.startsWith('teraBlast.')) ?? false;
}

export const teraBlastDefaultFields: Readonly<Record<string, number>> = {
  'teraBlast.typeBehavior': 0,
  'teraBlast.categoryBehavior': 0,
  'teraBlast.stellarPower': 100,
  'teraBlast.stellarEffectiveness': 8,
  'teraBlast.normalEffectiveness': 7,
  'teraBlast.attackDrop': 1,
  'teraBlast.specialAttackDrop': 1,
};

export function localizeTeraBlastField(field: MoveEditableField, t: (key: string) => string) {
  if (!field.field.startsWith('teraBlast.')) return field;
  const member = field.field.slice('teraBlast.'.length);
  return {
    ...field,
    label: t(`moves.teraBlast.${member}`),
    options: field.options.map(option => ({
      ...option,
      label: member === 'typeBehavior' || member === 'categoryBehavior'
        ? t(`moves.teraBlast.${member}.${option.value}`) : option.label,
    })),
  };
}
