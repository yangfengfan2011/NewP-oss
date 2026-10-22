// @vitest-environment node

import { readFileSync } from 'node:fs';
import { fileURLToPath, URL } from 'node:url';
import { describe, expect, it } from 'vitest';
import * as core from '@icp-platform/ui/core';
import * as crud from '@icp-platform/ui/crud';

describe('@icp-platform/ui physical entrypoints', () => {
  it('keeps the core interface free of CRUD and Pro component exports', () => {
    expect(core).toHaveProperty('Button');
    expect(core).toHaveProperty('ErrorState');
    expect(core).toHaveProperty('StandardListPage');
    expect(core).not.toHaveProperty('StandardCrudForm');
    expect(core).not.toHaveProperty('CrudWorkspacePage');
    expect(core).not.toHaveProperty('SearchTablePage');
  });

  it('exposes the CRUD workspace through a separate package export', () => {
    expect(crud).toHaveProperty('StandardCrudForm');
    expect(crud).toHaveProperty('CrudWorkspacePage');
    expect(crud).toHaveProperty('SearchTablePage');

    const packageJson = JSON.parse(readFileSync(
      fileURLToPath(new URL('../../../ui/package.json', import.meta.url)),
      'utf8',
    ));
    expect(packageJson.exports).toMatchObject({
      './core': {
        types: './dist/core.d.ts',
        import: './dist/core.js',
      },
      './crud': {
        types: './dist/crud.d.ts',
        import: './dist/crud.js',
      },
    });
  });
});
