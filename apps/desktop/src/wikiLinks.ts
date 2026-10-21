/* SPDX-License-Identifier: GPL-3.0-only */

import { type ProjectGame } from './bridge/contracts';
import { type WorkbenchSection } from './workbenchStore';
import { isPokemonLegendsZAGame, isScarletVioletGame } from './workflowGameSupport';

const wikiBaseUrl = 'https://github.com/KotMatrosk1n/KM-Editor/wiki';

const commonWikiSlugs: Partial<Record<WorkbenchSection, string>> = {
  bagHook: 'Bag-Hook',
  cameraHook: 'Camera-Hook',
  behavior: 'Behavior-Editor',
  catchCap: 'Catch-Cap-Editor',
  changes: 'Changes-and-Apply',
  dynamaxAdventures: 'Dynamax-Adventures',
  encounters: 'Wild-Encounters-Editor',
  exefsPatches: 'ExeFS-Patches',
  fairyGymBoosts: 'Fairy-Gym-Boosts',
  fixAiFlags: 'Fix-AI-Flags',
  marnieBoosts: 'Marnie-Wyndon-Boosts',
  fashionUnlock: 'Fashion-Unlock',
  flagworkSave: 'Flagwork-and-Save-Viewers',
  fpsPatch: '60FPS-Patch',
  giftPokemon: 'Gift-Pokemon-Editor',
  gameDump: 'Game-Dump',
  gameOptions: 'Game-Options',
  gymUniformRemoval: 'Gym-Uniform-Removal',
  health: 'Project-Setup',
  heldItemChance: 'Held-Item-Chance',
  history: 'History',
  hyperTraining: 'Hyper-Training',
  items: 'Items-Editor',
  ivScreen: 'IV-Screen',
  modMerger: 'Mod-Merger',
  modelViewer: '3D-Model-Editor',
  soundStudio: 'Sound-Studio',
  gameplaySettings: 'Gameplay-Settings',
  cameraControl: 'Camera-Control',
  battleItems: 'Battle-Items',
  moves: 'Moves-Editor',
  npcItemGift: 'NPC-Item-Gift',
  obedience: 'Obedience-Level',
  placement: 'Placement-Editor',
  pokemon: 'Pokemon-Editor',
  profanityFilter: 'Profanity-Filter',
  raidBattles: 'Raid-Battles-Editor',
  raidDens: 'Raid-Dens',
  trainerWhiteout: 'Trainer-Whiteout',
  trainerDynamax: 'Trainer-Dynamax',
  raidBonusRewards: 'Raid-Bonus-Rewards-Editor',
  raidRewards: 'Raid-Rewards-Editor',
  randomizer: 'Randomizer',
  rentalPokemon: 'Rental-Pokemon-Editor',
  restoreMissingPokemon: 'Restore-Missing-Pokemon',
  royalCandy: 'Royal-Candy',
  settings: 'Settings',
  shinyRate: 'Shiny-Rate',
  shakingTrees: 'Shaking-Trees',
  shops: 'Shops-Editor',
  spreadsheetImport: 'Dump-Importer',
  startingItems: 'Starting-Items',
  staticEncounters: 'Static-Encounters-Editor',
  text: 'Text-Viewer',
  tradePokemon: 'Trade-Pokemon-Editor',
  trainers: 'Trainers-Editor',
  typeChart: 'Type-Chart',
  workflows: 'Editing-Workflow'
};

const scarletVioletWikiSlugs: Partial<Record<WorkbenchSection, string>> = {
  summaryStats: 'Scarlet-and-Violet-IV-And-EV-Screen',
  bbqRewards: 'BBQ-Rewards',
  supportBoard: 'Support-Board',
  snacksworth: 'Snacksworth',
  encounters: 'Scarlet-and-Violet-Wild-Encounters-Editor',
  fashionUnlock: 'Scarlet-and-Violet-Fashion-Unlock',
  hyperspaceBypass: 'Hyperspace-Bypass',
  items: 'Scarlet-and-Violet-Items-Editor',
  modMerger: 'Scarlet-and-Violet-Mod-Merger',
  starmobiles: 'Starmobiles-Editor',
  moves: 'Scarlet-and-Violet-Moves-Editor',
  placement: 'Scarlet-and-Violet-Placement-Editor',
  pokemon: 'Scarlet-and-Violet-Pokemon-Editor',
  shops: 'Scarlet-and-Violet-Shops-Editor',
  staticEncounters: 'Scarlet-and-Violet-Static-Encounters-Editor',
  teraRaids: 'Scarlet-and-Violet-Tera-Raids-Editor',
  titanSwapper: 'Titan-Swapper',
  trainers: 'Scarlet-and-Violet-Trainers-Editor',
  typeChart: 'Scarlet-and-Violet-Type-Chart'
};

const pokemonLegendsZAWikiSlugs: Partial<Record<WorkbenchSection, string>> = {
  angeFight: 'Legends-Z-A-Ange-Fight',
  behavior: 'Legends-Z-A-Behavior-Editor',
  dexLayout: 'Legends-Z-A-Dex-Layout',
  encounters: 'Legends-Z-A-Wild-Encounters-Editor',
  gameDump: 'Legends-Z-A-Game-Dump',
  giftPokemon: 'Legends-Z-A-Gift-Pokemon-Editor',
  items: 'Legends-Z-A-Items-Editor',
  modMerger: 'Legends-Z-A-Mod-Merger',
  moves: 'Legends-Z-A-Moves-Editor',
  placement: 'Legends-Z-A-Placement-Editor',
  pokemon: 'Legends-Z-A-Pokemon-Editor',
  shops: 'Legends-Z-A-Shops-Editor',
  spreadsheetImport: 'Legends-Z-A-Dump-Importer',
  staticEncounters: 'Legends-Z-A-Static-Encounters-Editor',
  tradePokemon: 'Legends-Z-A-Trade-Pokemon-Editor',
  trainers: 'Legends-Z-A-Trainers-Editor',
  typeChart: 'Legends-Z-A-Type-Chart'
};

const pokemonLegendsArceusWikiSlugs: Partial<Record<WorkbenchSection, string>> = {
  health: 'Legends-Arceus-Overview#project-setup',
  pokemon: 'Legends-Arceus-Pokemon-Editor',
  moves: 'Legends-Arceus-Moves-Editor',
  text: 'Legends-Arceus-Text-Editor',
  trainers: 'Legends-Arceus-Trainers-Editor',
  giftPokemon: 'Legends-Arceus-Trainers-Editor#gift-pokemon',
  staticEncounters: 'Legends-Arceus-Trainers-Editor#static-encounters',
  items: 'Legends-Arceus-Economy-And-Research#items',
  crafting: 'Legends-Arceus-Economy-And-Research#crafting',
  shops: 'Legends-Arceus-Economy-And-Research#shops-and-ginkgo',
  pokemonDrops: 'Legends-Arceus-Economy-And-Research#pokemon-drops',
  research: 'Legends-Arceus-Economy-And-Research#research-and-ranks',
  encounters: 'Legends-Arceus-Wild-Encounters',
  outbreaks: 'Legends-Arceus-Outbreaks',
  distortions: 'Legends-Arceus-Space-Time-Distortions',
  weather: 'Legends-Arceus-Weather-And-Gameplay-Rules#weather',
  gameplayRules: 'Legends-Arceus-Weather-And-Gameplay-Rules#gameplay-rules',
  typeChart: 'Legends-Arceus-Type-Chart'
};

export function getSectionWikiUrl(section: WorkbenchSection, selectedGame: ProjectGame | null) {
  const gameWikiSlugs = isScarletVioletGame(selectedGame)
    ? scarletVioletWikiSlugs
    : selectedGame === 'pla'
      ? pokemonLegendsArceusWikiSlugs
      : isPokemonLegendsZAGame(selectedGame)
        ? pokemonLegendsZAWikiSlugs
        : null;
  const slug = gameWikiSlugs?.[section] ?? commonWikiSlugs[section];

  return slug ? `${wikiBaseUrl}/${slug}` : null;
}
