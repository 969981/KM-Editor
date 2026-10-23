// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';

const read = (path) => readFileSync(new URL(path, import.meta.url), 'utf8');
const app = read('../src/App.tsx');
const workbench = read('../src/features/workbench/WorkbenchSection.tsx');

for (const name of [
  'AnalysisLoadingSettings', 'AnalysisPreparationPanel', 'useAnalysisPreparation',
  'BalanceLabRuntime', 'GuidedDesignRuntime', 'SemanticMergeRuntime',
  'GameModulesRuntime', 'ResearchLabRuntime', 'SemanticExploreSection'
]) {
  assert.ok(!app.includes(name), `${name} must not be mounted or prepared by the application.`);
}
for (const name of ['preloadTools', 'mountedTools', 'onOpenTool', 'semanticExplore']) {
  assert.ok(!workbench.includes(name), `Workbench must not retain the removed ${name} integration.`);
}
for (const path of [
  '../src/features/workbench/useAnalysisPreparation.ts',
  '../src/features/workbench/analysisPreparation.ts',
  '../src/features/workbench/AnalysisPreparationPanel.tsx',
  '../src/features/workbench/AnalysisLoadingSettings.tsx'
]) {
  assert.ok(!existsSync(new URL(path, import.meta.url)), `Removed analysis preparation remains at ${path}.`);
}
assert.match(app, /recordLookupRequested = isCommandPaletteOpen \|\| isSemanticInspectorRequested/);
assert.match(app, /useProjectSourceRevision\(\{\s*bridge: recordLookupBridge,\s*enabled: recordLookupRequested/);
assert.match(read('../src/workbench/useProjectSourceRevision.ts'), /options\.enabled === false \|\|/);
assert.match(app, /await outputSafety\.notifyOutputMutation\(\)/);
assert.match(app, /await outputSafety\.notifyOutputFailure\(error\)/);
assert.match(app, /workspaceScopeKey=\{activeProjectId/);
assert.match(app, /workspaceScopeKey=\{activeProjectId\s*\? JSON\.stringify\(\[activeProjectId, selectedGame, draftPaths\.outputRootPath\]\)/);
assert.match(workbench, /scopeKey=\{workspaceScopeKey\}/);

for (const locale of ['en', 'de', 'es', 'fr', 'ru', 'uk', 'zh']) {
  const source = read(`../src/localization/resources/${locale}.json`);
  assert.doesNotMatch(source, /"analysis(?:Loading|Preparation)\./);
  assert.match(source, /"cache.mode.recommended"/);
}
console.log('Analysis removal and retained editor dependencies contract passed.');
