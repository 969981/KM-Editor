// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import ts from 'typescript';

const read = relative => readFileSync(new URL(relative, import.meta.url), 'utf8');
const declarations = [...read('../../../src/KM.Api/Bridge/KmCommandNames.cs')
  .matchAll(/public const string (\w+)\s*=\s*"([^"]+)";/g)];
assert.ok(declarations.length > 0, 'The managed command catalog must use explicit stable IDs.');
const names = new Map(declarations.map(match => [match[1], match[2]]));
const ids = declarations.map(match => match[2]).sort();
assert.equal(names.size, declarations.length, 'Managed command names must be unique.');
assert.equal(new Set(ids).size, ids.length, 'Managed command IDs must be unique.');

const dispatcher = read('../../../src/KM.Tools/Bridge/ProjectBridgeDispatcher.cs');
const routes = [...dispatcher.matchAll(/KmCommandNames\.(\w+)\s*=>/g)].map(match => {
  assert.ok(names.has(match[1]), `Dispatcher command ${match[1]} must be declared.`);
  return names.get(match[1]);
});
assert.deepEqual([...new Set(routes)].sort(), ids, 'Every declared command must have a managed route.');

const frontend = ts.createSourceFile('contracts.ts', read('../src/bridge/contracts.ts'), ts.ScriptTarget.Latest, true);
const declaration = frontend.statements.filter(ts.isVariableStatement)
  .flatMap(statement => statement.declarationList.declarations)
  .find(entry => entry.name.getText(frontend) === 'kmCommandNames');
let initializer = declaration?.initializer;
while (initializer && (ts.isAsExpression(initializer) || ts.isSatisfiesExpression(initializer)
  || ts.isParenthesizedExpression(initializer))) initializer = initializer.expression;
assert.ok(initializer && ts.isObjectLiteralExpression(initializer), 'Frontend commands must use an explicit catalog.');
const frontendIds = initializer.properties.map(property => {
  assert.ok(ts.isPropertyAssignment(property) && ts.isStringLiteral(property.initializer),
    'Frontend commands must use explicit stable IDs.');
  return property.initializer.text;
});
assert.deepEqual(frontendIds.sort(), ids, 'Frontend and managed commands must agree exactly.');

const native = read('../src-tauri/src/lib.rs');
const catalog = /const ROUTED_PROJECT_BRIDGE_COMMANDS:\s*&\[&str\]\s*=\s*&\[([\s\S]*?)\];/.exec(native);
assert.ok(catalog, 'The native command catalog must remain explicit.');
const nativeIds = [...catalog[1].matchAll(/"([^"]+)"/g)].map(match => match[1]);
assert.deepEqual(nativeIds, ids,
  'Native commands must match every declared command exactly once in strict binary-search order.');

const policies = ['is_project_bridge_workflow_load_command', 'is_replay_safe_edit_session_command',
  'project_bridge_request_policy'].map(name => {
  const body = new RegExp(`fn ${name}\\([\\s\\S]*?\\n\\}`).exec(native);
  assert.ok(body, `Native command policy ${name} must remain explicit.`);
  return body[0];
}).join('\n');
const classified = new Set([...policies.matchAll(/"([^"]+)"/g)].map(match => match[1]));
// Clipboard staging consumes a one-use authorization and intentionally keeps the non-replay fallback.
const nonReplayStageCommands = new Set(['rowClipboard.paste.stage']);
for (const id of ids.filter(id => id.endsWith('.load') || id.endsWith('.stage'))) {
  assert.ok(classified.has(id) || nonReplayStageCommands.has(id),
    `Editor command ${id} requires an explicit native load or staging policy.`);
}
console.log(`Editor command contract passed: ${ids.length} commands across all bridge layers.`);
