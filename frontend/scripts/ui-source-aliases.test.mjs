import assert from 'node:assert/strict';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { createUiSourceAliases } from './ui-source-aliases.mjs';

test('published UI entry points resolve independently to their existing source owners', () => {
  const aliases = createUiSourceAliases();
  const expected = {
    '@icp-platform/ui': '../packages/ui/src/index.ts',
    '@icp-platform/ui/core': '../packages/ui/src/core.ts',
    '@icp-platform/ui/locale': '../packages/ui/src/locale.tsx',
    '@icp-platform/ui/button': '../packages/ui/src/components/Button.tsx',
    '@icp-platform/ui/crud': '../packages/ui/src/crud.ts',
    '@icp-platform/ui/crud-detail': '../packages/ui/src/crud-detail.ts',
    '@icp-platform/ui/crud-form': '../packages/ui/src/crud-form.ts',
    '@icp-platform/ui/crud-list': '../packages/ui/src/crud-list.ts',
    '@icp-platform/ui/styles': '../packages/ui/src/styles/index.css',
  };
  for (const [specifier, source] of Object.entries(expected)) {
    const matches = aliases.filter(alias => alias.find.test(specifier));
    assert.equal(matches.length, 1, specifier);
    assert.equal(matches[0].replacement, fileURLToPath(new URL(source, import.meta.url)), specifier);
  }
  assert.equal(aliases.length, Object.keys(expected).length);
});

test('the UI root cannot consume an unpublished or nested subpath through prefix matching', () => {
  const aliases = createUiSourceAliases();
  for (const specifier of ['@icp-platform/ui/unknown', '@icp-platform/ui/crud-form/unknown', '@icp-platform/ui-extra']) {
    assert.equal(aliases.some(alias => alias.find.test(specifier)), false, specifier);
  }
});
