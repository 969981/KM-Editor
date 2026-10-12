/* SPDX-License-Identifier: GPL-3.0-only */
import { treeFieldKey, type ShakingTreesWorkflow, type TreeSettings } from '../../bridge/shakingTreesContracts';

export function parseTreeNumber(value: string | undefined): number | null {
  return value !== undefined && /^\d{1,5}$/.test(value) ? Number(value) : null;
}
export function readTreeSettings(values: Record<string, string>): TreeSettings | null {
  const fields = ['initialRiskMin', 'initialRiskMax', 'riskStepMin', 'riskStepMax', 'loseAllRolls', 'dropMin', 'dropMax'] as const;
  const parsed = fields.map(field => parseTreeNumber(values[treeFieldKey('settings', field)]));
  if (parsed.some(value => value === null)) return null;
  const [initialRiskMin, initialRiskMax, riskStepMin, riskStepMax, loseAllRolls, dropMin, dropMax] = parsed as number[];
  if (initialRiskMin < 0 || initialRiskMax > 99 || initialRiskMin > initialRiskMax || riskStepMin < 1 || riskStepMax > 99 || riskStepMin > riskStepMax
    || loseAllRolls > 101 || dropMin < 1 || dropMax > 3 || dropMin > dropMax || Math.floor((99 - initialRiskMin) / riskStepMin) * dropMax > 255) return null;
  return { initialRiskMin, initialRiskMax, riskStepMin, riskStepMax, loseAllRolls, dropMin, dropMax };
}

export function validateTreeDraft(workflow: ShakingTreesWorkflow | null, values: Record<string, string>): boolean {
  if (!workflow || !readTreeSettings(values)) return false;
  const get = (id: string, field: string) => parseTreeNumber(values[treeFieldKey(id, field)]);
  const percent = (value: number | null) => value !== null && value <= 100;
  for (const tree of workflow.trees) {
    const percentages = tree.drops.map((_, slot) => get(tree.id, `percent.${slot}`));
    if (!percentages.every(percent) || percentages.reduce<number>((sum, value) => sum + (value ?? 0), 0) !== 100
      || tree.drops.some((_, slot) => !workflow.items.some(item => item.value === get(tree.id, `item.${slot}`)))) return false;
  }
  for (const area of workflow.areas) {
    const min = get(area.id, 'levelMin'); const max = get(area.id, 'levelMax');
    const percentages = area.slots.map((_, slot) => get(area.id, `percent.${slot}`));
    if (min === null || max === null || min < 1 || max > 100 || min > max || !percentages.every(percent)
      || percentages.reduce<number>((sum, value) => sum + (value ?? 0), 0) !== 100) return false;
    if (area.slots.some((_, slot) => (get(area.id, `percent.${slot}`) ?? 0) > 0 && !workflow.pokemon.some(pokemon =>
      pokemon.species === get(area.id, `species.${slot}`) && pokemon.form === get(area.id, `form.${slot}`)))) return false;
  }
  return true;
}

/** Conditional next-shake odds, given only that every earlier shake was safe. */
export function treeShakeOdds(settings: TreeSettings) {
  let distribution = Array<number>(100).fill(0);
  for (let risk = settings.initialRiskMin; risk <= settings.initialRiskMax; risk++) distribution[risk] = 1 / (settings.initialRiskMax - settings.initialRiskMin + 1);
  let survival = 1;
  const rows: { shake: number; encounter: number; survival: number }[] = [];
  const width = settings.riskStepMax - settings.riskStepMin + 1;
  for (let shake = 1; shake <= 100; shake++) {
    const next = Array<number>(100).fill(0);
    for (let risk = 0; risk < 100; risk++) for (let step = settings.riskStepMin; step <= settings.riskStepMax; step++)
      if (risk + step < 100) next[risk + step] += distribution[risk] / width;
    const safe = next.reduce((sum, value) => sum + value, 0);
    survival *= safe;
    rows.push({ shake, encounter: Math.max(0, Math.min(1, 1 - safe)), survival });
    if (safe === 0) break;
    distribution = next.map(value => value / safe);
  }
  return rows;
}
