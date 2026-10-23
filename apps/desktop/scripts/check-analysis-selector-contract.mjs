// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const desktopRoot = new URL('../', import.meta.url);

function read(relativePath) {
  return readFileSync(new URL(relativePath, desktopRoot), 'utf8')
    .replace(/\r\n?/gu, '\n');
}

const apiContract = read('../../src/KM.Api/GuidedDesign/GuidedDesignContracts.cs');
const provider = read('../../src/KM.Tools/Application/GuidedDesignProviders.cs');
const application = read('../../src/KM.Tools/Application/GuidedDesignApplicationService.cs');
const bridgeContract = read('src/bridge/guidedDesignContracts.ts');
const semanticContract = read('src/bridge/semanticMergeContracts.ts');

assert.match(
  apiContract,
  /GuidedDesignFieldCatalogDto\([\s\S]*?SelectionMode[\s\S]*?MinimumSelections[\s\S]*?FieldKeys/u,
  'Guided Design must expose a typed field-selection catalog.'
);
assert.match(
  application,
  /GuidedDesignProviders\.FieldCatalogs\([\s\S]*?capability\.State != SemanticCoverageStateDto\.Unavailable/u,
  'Guided Design must publish fields only for current-project available proposals.'
);
for (const catalog of [
  '["level"]',
  '["levelMin", "levelMax"]',
  '["probability"]',
  '["weight"]',
  '["price"]',
  '["buyPrice"]',
  '[.. EvFields]',
  '[.. StatFields]'
]) {
  assert.ok(provider.includes(catalog), `Missing verified Guided Design catalog ${catalog}.`);
}
assert.match(
  provider,
  /BuildEvolutionClamp\([\s\S]*?return Selection\([\s\S]*?\["level"\][\s\S]*?NormalizeInput\([\s\S]*?\["level"\],[\s\S]*?allowEvolutionPinChildren: true/u,
  'Evolution clamping must normalize the same fixed level field exposed by its catalog.'
);
assert.match(
  bridgeContract,
  /fieldCatalogs: z\.array\(guidedDesignFieldCatalogSchema\)[\s\S]*?exactly cover available proposals/u,
  'The desktop bridge must reject incomplete or extra Guided Design field catalogs.'
);
assert.match(
  bridgeContract,
  /trainerLevelAdjustment[\s\S]*?family === 'swordShield' \? null[\s\S]*?encounterWeightScale[\s\S]*?family === 'legendsZA' \? \['weight'\] : null[\s\S]*?evolutionLevelClamp[\s\S]*?family === 'legendsZA' \? \['level'\] : null[\s\S]*?trainerEvArchetype[\s\S]*?family === 'swordShield' \? null/u,
  'The desktop bridge must reject unsupported game/proposal field catalogs.'
);
assert.match(
  semanticContract,
  /kmRecipeCompatibilityStateValues = \[[\s\S]*?'compatible'[\s\S]*?'alreadyApplied'[\s\S]*?'conflict'[\s\S]*?'unsupported'/u,
  'Recipe compatibility filters must retain every contract-valid state.'
);

console.log('Analysis selector contract passed.');
