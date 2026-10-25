/* SPDX-License-Identifier: GPL-3.0-only */
import { ChevronDown, Dna, MapPin, Package, ShieldCheck, Users } from 'lucide-react';
import { useState } from 'react';
import type { RandomizerOptions, TrainerRandomizerOptions } from '../../bridge/contracts';
import { useLocalization } from '../../localization/LocalizationProvider';
import { usePublishCommonEditorError } from '../../components/CommonEditorDiagnostics';
import { SearchableOptionInput } from '../../components/SearchableOptionInput';
import { defaultTrainerOptions, randomizerCategoryDefinitions, randomizerEncounterOptions,
  randomizerTypeChartOptions, type RandomizerOptionKey } from './randomizerOptions';

type Props = {
  options: RandomizerOptions;
  locked: boolean;
  expandedCategoryIds: Set<string>;
  onToggleCategory: (id: string) => void;
  onToggle: (key: RandomizerOptionKey) => void;
  onTrainerChange: (patch: Partial<TrainerRandomizerOptions>) => void;
  levelDraft: string;
  levelValid: boolean;
  onLevelChange: (value: string) => void;
};
const groups = [
  { id: 'pokemon', icon: Dna }, { id: 'trainers', icon: Users },
  { id: 'encounters', icon: MapPin }, { id: 'items', icon: Package },
  { id: 'typeChart', icon: ShieldCheck }
] as const;
type Group = typeof groups[number]['id'];
const trainerChecks = ['species', 'allowLegendaries', 'fullBossTeams', 'abilities', 'moves', 'heldItems', 'natures', 'genders'] as const;

export function RandomizerSettings(props: Props) {
  const { t, translateLiteral } = useLocalization();
  const [group, setGroup] = useState<Group>('pokemon');
  const { options, locked, onToggle } = props;
  const trainers = options.trainers ?? defaultTrainerOptions;
  const counts: Record<Group, number> = {
    pokemon: randomizerCategoryDefinitions.filter(row => options[row.enabledKey]).length,
    trainers: Number(options.randomizeTrainers),
    encounters: Number(options.randomizeWildEncounters) + Number(options.randomizeStaticEncounters) + Number(options.randomizeGiftEncounters),
    items: Number(options.randomizeShops) + Number(options.randomizeWorldItems) + Number(options.randomizeRaidRewards) + Number(options.randomizeRaidBonusRewards),
    typeChart: Number(options.randomizeTypeChart)
  };
  usePublishCommonEditorError({ domain: 'workflow.randomizer', field: 'trainers.levelPercent',
    message: options.randomizeTrainers && !props.levelValid ? t('randomizer.trainers.levelError') : null });

  const check = (id: string, label: string, help: string, checked: boolean, change: () => void, disabled = locked) => (
    <div className={`randomizer-setting ${checked ? 'randomizer-setting-enabled' : ''}`} key={id}>
      <input aria-describedby={`${id}-help`} aria-label={label} checked={checked} className="km-choice-control"
        disabled={disabled} id={id} onChange={change} type="checkbox" />
      <div><label htmlFor={id}>{label}</label><p id={`${id}-help`}>{help}</p></div>
    </div>
  );
  const option = (row: { key: RandomizerOptionKey; label: string; help: string }, disabled = locked) =>
    check(`randomizer-option-${row.key}`, translateLiteral(row.label), translateLiteral(row.help),
      options[row.key], () => onToggle(row.key), disabled);

  return (
    <section className="randomizer-settings wide-panel" aria-label={t('randomizer.settings.title')}>
      <nav className="randomizer-settings-nav" aria-label={t('randomizer.settings.title')}>
        {groups.map(row => {
          const Icon = row.icon;
          return <button aria-controls="randomizer-settings-content" aria-pressed={group === row.id}
            className={`randomizer-group-button ${group === row.id ? 'randomizer-group-active' : ''}`}
            key={row.id} onClick={() => setGroup(row.id)} type="button">
            <Icon aria-hidden="true" size={18} /><span>{t(`randomizer.groups.${row.id}`)}</span>
            <span className="randomizer-group-count" aria-label={t('randomizer.settings.selected', { count: counts[row.id] })}>{counts[row.id]}</span>
          </button>;
        })}
      </nav>
      <div className="panel randomizer-settings-content" id="randomizer-settings-content">
        <div className="randomizer-settings-heading"><h2>{t(`randomizer.groups.${group}`)}</h2>
          <p>{t(`randomizer.groups.${group}Help`)}</p></div>

        {group === 'pokemon' && <div className="randomizer-accordion-list">{randomizerCategoryDefinitions.map(category => (
          <details className="randomizer-category-card" key={category.id} open={props.expandedCategoryIds.has(category.id)}>
            <summary onClick={event => { event.preventDefault(); props.onToggleCategory(category.id); }}>
              <category.icon aria-hidden="true" size={18} /><span>{translateLiteral(category.label)}</span>
              <span className="randomizer-category-state">{t(options[category.enabledKey] ? 'randomizer.settings.on' : 'randomizer.settings.off')}</span>
              <ChevronDown aria-hidden="true" size={16} />
            </summary>
            <div className="randomizer-category-fields">
              {check(`randomizer-${category.id}-enabled`, translateLiteral(category.enabledLabel), translateLiteral(category.help),
                options[category.enabledKey], () => onToggle(category.enabledKey))}
              {category.fields.length > 0 && <fieldset disabled={!options[category.enabledKey] || locked} className="randomizer-child-fields">
                <legend className="sr-only">{translateLiteral(category.label)}</legend>
                {category.fields.map(field => check(`randomizer-${category.id}-${field.key}`, translateLiteral(field.label),
                  translateLiteral(field.help), options[field.key], () => onToggle(field.key), !options[category.enabledKey] || locked))}
              </fieldset>}
            </div>
          </details>
        ))}</div>}

        {group === 'trainers' && <>
          {check('randomizer-trainers-enabled', t('randomizer.trainers.enable'), t('randomizer.trainers.enableHelp'),
            options.randomizeTrainers, () => onToggle('randomizeTrainers'))}
          <fieldset className="randomizer-trainer-fields" disabled={!options.randomizeTrainers || locked}>
            <legend className="sr-only">{t('randomizer.groups.trainers')}</legend>
            <div className="randomizer-settings-grid randomizer-trainer-main">
              {trainerChecks.slice(0, 3).map(key => check(`randomizer-trainer-${key}`, t(`randomizer.trainers.${key}`),
                t(`randomizer.trainers.${key}Help`), trainers[key], () => props.onTrainerChange({ [key]: !trainers[key] }),
                locked || !options.randomizeTrainers))}
              <div className="randomizer-number-setting">
                <label htmlFor="randomizer-trainer-level">{t('randomizer.trainers.level')}</label>
                <div className="randomizer-percent-input"><input aria-describedby="randomizer-trainer-level-help" aria-invalid={!props.levelValid || undefined}
                  id="randomizer-trainer-level" inputMode="numeric" maxLength={4} onChange={event => props.onLevelChange(event.currentTarget.value)}
                  type="text" value={props.levelDraft} /><span>%</span></div>
                <p id="randomizer-trainer-level-help">{t(props.levelValid ? 'randomizer.trainers.levelHelp' : 'randomizer.trainers.levelError')}</p>
              </div>
            </div>
            <h3>{t('randomizer.trainers.details')}</h3>
            <div className="randomizer-settings-grid">{trainerChecks.slice(3).map(key => check(`randomizer-trainer-${key}`,
              t(`randomizer.trainers.${key}`), t(`randomizer.trainers.${key}Help`), trainers[key],
              () => props.onTrainerChange({ [key]: !trainers[key] }), locked || !options.randomizeTrainers))}</div>
            <div className="randomizer-settings-grid">{(['ivs', 'evs'] as const).map(key => (
              <div className="randomizer-number-setting" key={key}>
                <label htmlFor={`randomizer-trainer-${key}`}>{t(`randomizer.trainers.${key}`)}</label>
                <SearchableOptionInput id={`randomizer-trainer-${key}`} value={trainers[key]}
                  ariaLabel={t(`randomizer.trainers.${key}`)} ariaDescribedBy={`randomizer-trainer-${key}-help`}
                  disabled={!options.randomizeTrainers || locked} localizeOptions={false} isFiniteCatalog
                  options={(['unchanged', 'random', 'maximum'] as const).map(mode => ({ value: mode, label: t(`randomizer.statMode.${mode}`) }))}
                  onChange={value => props.onTrainerChange({ [key]: value as TrainerRandomizerOptions[typeof key] })} />
                <p id={`randomizer-trainer-${key}-help`}>{t(`randomizer.trainers.${key}Help`)}</p>
              </div>
            ))}</div>
          </fieldset>
        </>}

        {group === 'encounters' && <div className="randomizer-settings-grid">
          {randomizerEncounterOptions.filter(row => !row.key.startsWith('randomizeRaid')).map(row => option(row))}
        </div>}
        {group === 'items' && <div className="randomizer-settings-grid">
          {check('randomizer-shops', t('randomizer.items.shops'), t('randomizer.items.shopsHelp'), options.randomizeShops, () => onToggle('randomizeShops'))}
          {check('randomizer-world-items', t('randomizer.items.world'), t('randomizer.items.worldHelp'), options.randomizeWorldItems, () => onToggle('randomizeWorldItems'))}
          {randomizerEncounterOptions.filter(row => row.key.startsWith('randomizeRaid')).map(row => option(row))}
        </div>}
        {group === 'typeChart' && <>
          {check('randomizer-type-chart-enabled', translateLiteral('Randomize Type Chart'), t('randomizer.groups.typeChartHelp'),
            options.randomizeTypeChart, () => onToggle('randomizeTypeChart'))}
          <fieldset className="randomizer-child-fields" disabled={!options.randomizeTypeChart || locked}>
            <legend className="sr-only">{t('randomizer.groups.typeChart')}</legend>
            {randomizerTypeChartOptions.map(row => option(row, !options.randomizeTypeChart || locked))}
          </fieldset>
        </>}
      </div>
    </section>
  );
}
