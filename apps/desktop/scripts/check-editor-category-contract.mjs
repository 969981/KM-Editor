// SPDX-License-Identifier: GPL-3.0-only

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import ts from 'typescript';

function readSource(relative) {
  return ts.createSourceFile(relative, readFileSync(new URL(relative, import.meta.url), 'utf8'),
    ts.ScriptTarget.Latest, true);
}

function unwrap(expression) {
  while (ts.isAsExpression(expression) || ts.isSatisfiesExpression(expression)
    || ts.isParenthesizedExpression(expression)) expression = expression.expression;
  return expression;
}

function list(source, name) {
  const declaration = source.statements.filter(ts.isVariableStatement)
    .flatMap(statement => statement.declarationList.declarations)
    .find(entry => entry.name.getText(source) === name);
  assert.ok(declaration?.initializer, `${name} must be declared.`);
  const value = unwrap(declaration.initializer);
  assert.ok(ts.isArrayLiteralExpression(value), `${name} must be an explicit registry.`);
  return value.elements;
}

function fields(expression) {
  assert.ok(ts.isObjectLiteralExpression(expression), 'Registry entries must declare their fields.');
  return new Map(expression.properties.filter(ts.isPropertyAssignment)
    .map(property => [property.name.getText(), property.initializer]));
}

function literal(value, label) {
  assert.ok(value && ts.isStringLiteral(value), `${label} must use a stable literal ID.`);
  return value.text;
}

const capabilities = readSource('../src/workbench/capabilityRegistry.ts');
const visibleEditors = new Set();
const registeredIds = new Set();
for (const entry of list(capabilities, 'workbenchCapabilityRegistry')) {
  const expression = unwrap(entry);
  const isWorkflow = ts.isCallExpression(expression) && expression.expression.getText() === 'workflow';
  const values = fields(isWorkflow ? expression.arguments[0] : expression);
  const id = literal(values.get('id'), 'Capability');
  assert.ok(!registeredIds.has(id), `Capability ID ${id} is registered more than once.`);
  registeredIds.add(id);
  const kind = values.has('navigationKind') ? literal(values.get('navigationKind'), 'Navigation kind')
    : isWorkflow ? 'workflow' : null;
  if (kind === 'workflow') visibleEditors.add(id);
}

const navigation = readSource('../src/workflowGameSupport.ts');
const categories = new Set();
const assigned = new Map();
const groups = list(navigation, 'workflowNavigationGroups');
for (const [index, entry] of groups.entries()) {
  const values = fields(unwrap(entry));
  const id = literal(values.get('id'), 'Category');
  assert.ok(!categories.has(id), `Category ${id} is registered more than once.`);
  assert.ok(!id.startsWith('custom:'), 'Default categories cannot use personal preference IDs.');
  categories.add(id);
  if (id === 'betaEditors') assert.equal(index, groups.length - 1, 'Beta Editors must remain last.');
  const sections = values.get('sectionIds');
  assert.ok(sections && ts.isArrayLiteralExpression(sections), `Category ${id} must declare its editor IDs.`);
  for (const section of sections.elements) {
    const editor = literal(section, 'Category member');
    assert.ok(visibleEditors.has(editor), `Category ${id} contains an unregistered or hidden editor: ${editor}.`);
    assert.ok(!assigned.has(editor), `Editor ${editor} is assigned to both ${assigned.get(editor)} and ${id}.`);
    assigned.set(editor, id);
  }
}
assert.ok(categories.has('betaEditors'), 'The locked Beta Editors category must remain registered.');
assert.deepEqual([...assigned.keys()].sort(), [...visibleEditors].sort(),
  'Every visible editor needs exactly one default category, including newly registered editors.');
console.log(`Editor category contract passed: ${assigned.size} editors in ${categories.size} default categories.`);
