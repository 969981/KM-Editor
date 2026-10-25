/* SPDX-License-Identifier: GPL-3.0-only */
import { Activity, ArrowUp, CheckCircle, Dna, ListChecks, Package, Settings as SettingsIcon, Zap, type LucideIcon } from 'lucide-react';
import type { RandomizerOptions, TrainerRandomizerOptions } from '../../bridge/contracts';

export const defaultTrainerOptions: TrainerRandomizerOptions = {
  species: true, allowLegendaries: false, levelPercent: 100, fullBossTeams: false,
  abilities: false, moves: false, heldItems: false, natures: false, genders: false,
  ivs: 'unchanged', evs: 'unchanged'
};

export type RandomizerOptionKey = { [K in keyof RandomizerOptions]: RandomizerOptions[K] extends boolean ? K : never }[keyof RandomizerOptions];

export const defaultRandomizerOptions: RandomizerOptions = {
  randomizeTrainers: false,
  trainers: null,
  randomizeShops: false,
  randomizeWorldItems: false,
  ability1: true,
  ability2: true,
  allowSameType: false,
  compatibilityMachines: true,
  compatibilityRecords: true,
  compatibilityTutors: true,
  hiddenAbility: true,
  learnsetBanFixedDamageMoves: true,
  learnsetExpandTo25: false,
  learnsetRequireDamagingMove: true,
  learnsetStabFirst: true,
  randomizeGiftEncounters: false,
  randomizePokemonAbilities: false,
  randomizePokemonCatchRates: false,
  randomizePokemonCompatibility: false,
  randomizePokemonEvolutions: false,
  randomizePokemonHeldItems: false,
  randomizePokemonLearnsets: false,
  randomizePokemonStats: false,
  randomizePokemonTypes: false,
  randomizeWildEncounters: false,
  randomizeRaidBonusRewards: false,
  randomizeRaidRewards: false,
  randomizeStaticEncounters: false,
  randomizeTypeChart: false,
  shufflePokemonStats: true,
  statAttack: true,
  statDefense: true,
  statHp: true,
  statSpecialAttack: true,
  statSpecialDefense: true,
  statSpeed: true,
  typeChartNoImmunities: false,
  typeChartOneImmunityPerType: false,
  typePrimary: true,
  typeSecondary: true
};

export const randomizerCategoryDefinitions: Array<{
  enabledKey: RandomizerOptionKey;
  enabledLabel: string;
  fields: Array<{ help: string; key: RandomizerOptionKey; label: string }>;
  help: string;
  icon: LucideIcon;
  id: string;
  label: string;
}> = [
  {
    enabledKey: 'randomizePokemonStats',
    enabledLabel: 'Randomize Base Stats',
    fields: [
      {
        help: 'Shuffle the selected base stat values within each Pokemon instead of generating a fresh stat spread.',
        key: 'shufflePokemonStats',
        label: 'Shuffle stat values'
      },
      { help: 'Include base HP when base stats are randomized.', key: 'statHp', label: 'HP' },
      {
        help: 'Include base Attack when base stats are randomized.',
        key: 'statAttack',
        label: 'Attack'
      },
      {
        help: 'Include base Defense when base stats are randomized.',
        key: 'statDefense',
        label: 'Defense'
      },
      {
        help: 'Include base Special Attack when base stats are randomized.',
        key: 'statSpecialAttack',
        label: 'Sp. Attack'
      },
      {
        help: 'Include base Special Defense when base stats are randomized.',
        key: 'statSpecialDefense',
        label: 'Sp. Defense'
      },
      { help: 'Include base Speed when base stats are randomized.', key: 'statSpeed', label: 'Speed' }
    ],
    help: 'Randomizes Pokemon base stat values while keeping values within safe personal-data limits.',
    icon: Activity,
    id: 'stats',
    label: 'Stats'
  },
  {
    enabledKey: 'randomizePokemonTypes',
    enabledLabel: 'Randomize Types',
    fields: [
      {
        help: 'Randomize each Pokemon primary type. When both type slots are selected, KM can also change whether a Pokemon is mono-type or dual-type.',
        key: 'typePrimary',
        label: 'Primary type'
      },
      {
        help: 'Randomize each Pokemon secondary type. When both type slots are selected, KM can also change whether a Pokemon is mono-type or dual-type.',
        key: 'typeSecondary',
        label: 'Secondary type'
      },
      {
        help: 'For single-slot type rerolls, allow the selected type slot to match the other slot. When both type slots are selected, KM rolls mono-type versus dual-type shape separately.',
        key: 'allowSameType',
        label: 'Allow duplicate types'
      }
    ],
    help: 'Randomizes Pokemon personal-data types and can change Pokemon between mono-type and dual-type when both type slots are selected.',
    icon: Dna,
    id: 'types',
    label: 'Types'
  },
  {
    enabledKey: 'randomizePokemonAbilities',
    enabledLabel: 'Randomize Abilities',
    fields: [
      { help: 'Randomize ability slot 1 for each Pokemon.', key: 'ability1', label: 'Ability 1' },
      { help: 'Randomize ability slot 2 for each Pokemon.', key: 'ability2', label: 'Ability 2' },
      {
        help: 'Randomize the hidden ability slot for each Pokemon.',
        key: 'hiddenAbility',
        label: 'Hidden ability'
      }
    ],
    help: 'Randomizes Pokemon personal-data ability slots using valid loaded ability IDs.',
    icon: CheckCircle,
    id: 'abilities',
    label: 'Abilities'
  },
  {
    enabledKey: 'randomizePokemonHeldItems',
    enabledLabel: 'Randomize Held Items',
    fields: [],
    help: 'Randomizes each Pokemon held item slots 1, 2, and 3 using safe item candidates.',
    icon: Package,
    id: 'heldItems',
    label: 'Held Items'
  },
  {
    enabledKey: 'randomizePokemonCatchRates',
    enabledLabel: 'Randomize Catch Rates',
    fields: [],
    help: 'Randomizes Pokemon catch rates between 1 and 255 in personal data.',
    icon: SettingsIcon,
    id: 'misc',
    label: 'Catch Rates'
  },
  {
    enabledKey: 'randomizePokemonLearnsets',
    enabledLabel: 'Randomize Learnsets',
    fields: [
      {
        help: 'Prefer a same-type attack bonus move in the first learnset slot when one is available.',
        key: 'learnsetStabFirst',
        label: 'STAB first move'
      },
      {
        help: 'Expand randomized learnsets with fewer than 25 moves to 25 move slots spread from Lv. 1 to Lv. 75.',
        key: 'learnsetExpandTo25',
        label: 'Expand learnsets to 25 moves'
      },
      {
        help: 'Exclude fixed-damage moves such as Sonic Boom and Dragon Rage from randomized learnsets.',
        key: 'learnsetBanFixedDamageMoves',
        label: 'Ban fixed-damage moves'
      },
      {
        help: 'Make sure each randomized learnset contains at least one damaging move when possible.',
        key: 'learnsetRequireDamagingMove',
        label: 'Require damaging move'
      }
    ],
    help: 'Randomizes Pokemon level-up learnset moves using legal, usable moves from the loaded move table.',
    icon: Zap,
    id: 'learnsets',
    label: 'Learnsets'
  },
  {
    enabledKey: 'randomizePokemonCompatibility',
    enabledLabel: 'Randomize Move Compatibility',
    fields: [
      { help: 'Randomize TM learn compatibility for each Pokemon.', key: 'compatibilityMachines', label: 'TMs' },
      { help: 'Randomize TR learn compatibility for each Pokemon.', key: 'compatibilityRecords', label: 'TRs' },
      {
        help: 'Randomize move tutor compatibility, including type tutors and Armor tutor moves.',
        key: 'compatibilityTutors',
        label: 'Tutors'
      }
    ],
    help: 'Randomizes whether each Pokemon can learn TM, TR, and tutor moves.',
    icon: ListChecks,
    id: 'compatibility',
    label: 'Moves'
  },
  {
    enabledKey: 'randomizePokemonEvolutions',
    enabledLabel: 'Randomize Evolutions',
    fields: [],
    help: 'Randomizes evolution target species/forms while keeping existing evolution methods, arguments, and levels.',
    icon: ArrowUp,
    id: 'evolutions',
    label: 'Evolutions'
  }
];

export const randomizerEncounterOptions: Array<{
  help: string;
  key: RandomizerOptionKey;
  label: string;
}> = [
  {
    help: 'Randomize wild encounter Pokemon/form slots and encounter percentages for symbol, hidden, fishing, shaking tree, and weather tables where they exist.',
    key: 'randomizeWildEncounters',
    label: 'Randomize Wild Encounters'
  },
  {
    help: 'Randomize species/forms for fixed overworld or scripted static encounters.',
    key: 'randomizeStaticEncounters',
    label: 'Randomize Static Encounters'
  },
  {
    help: 'Randomize species/forms for Pokemon received as gifts from scripts or event data.',
    key: 'randomizeGiftEncounters',
    label: 'Randomize Gift Encounters'
  },
  {
    help: 'Randomize raid drop rewards with safe item candidates; Royal Candy item 1128 is excluded when Royal Candy is installed.',
    key: 'randomizeRaidRewards',
    label: 'Randomize Raid Rewards'
  },
  {
    help: 'Randomize raid bonus rewards with safe item candidates; Royal Candy item 1128 is excluded when Royal Candy is installed.',
    key: 'randomizeRaidBonusRewards',
    label: 'Randomize Raid Bonus Rewards'
  }
];

export const randomizerTypeChartOptions: Array<{
  help: string;
  key: RandomizerOptionKey;
  label: string;
}> = [
  {
    help: 'Prevent randomized Type Chart values from creating immunities and replace vanilla immunities with non-immune effectiveness values.',
    key: 'typeChartNoImmunities',
    label: 'No immunities'
  },
  {
    help: 'Allow immunities, but limit each attacking type to at most one defending type that is immune to it.',
    key: 'typeChartOneImmunityPerType',
    label: 'No more than one immunity per type'
  }
];

const randomizerChildKeysByParent: Partial<Record<RandomizerOptionKey, RandomizerOptionKey[]>> = {
  randomizePokemonAbilities: ['ability1', 'ability2', 'hiddenAbility'],
  randomizePokemonCompatibility: [
    'compatibilityMachines',
    'compatibilityRecords',
    'compatibilityTutors'
  ],
  randomizePokemonLearnsets: [
    'learnsetStabFirst',
    'learnsetExpandTo25',
    'learnsetBanFixedDamageMoves',
    'learnsetRequireDamagingMove'
  ],
  randomizePokemonStats: [
    'shufflePokemonStats',
    'statHp',
    'statAttack',
    'statDefense',
    'statSpecialAttack',
    'statSpecialDefense',
    'statSpeed'
  ],
  randomizePokemonTypes: ['typePrimary', 'typeSecondary', 'allowSameType'],
  randomizeTypeChart: ['typeChartNoImmunities', 'typeChartOneImmunityPerType']
};

export function createEffectiveRandomizerOptions(options: RandomizerOptions): RandomizerOptions {
  const effective = { ...options, trainers: options.randomizeTrainers ? options.trainers ?? defaultTrainerOptions : null };

  for (const [parentKey, childKeys] of Object.entries(randomizerChildKeysByParent) as Array<
    [RandomizerOptionKey, RandomizerOptionKey[]]
  >) {
    if (!effective[parentKey]) {
      for (const childKey of childKeys) {
        effective[childKey] = false;
      }
    }
  }

  if (effective.typeChartNoImmunities && effective.typeChartOneImmunityPerType) {
    effective.typeChartOneImmunityPerType = false;
  }

  return effective;
}
