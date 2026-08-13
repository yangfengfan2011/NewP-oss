// @vitest-environment node

import { readFileSync } from 'node:fs';
import { fileURLToPath, URL } from 'node:url';
import { describe, expect, it } from 'vitest';

const standardCrudSourceFiles = [
  '../pages/StandardCrudListPageHost.tsx',
  '../../../ui/src/patterns/StandardCrudField.tsx',
  '../../../ui/src/patterns/StandardCrudForm.tsx',
  '../../../ui/src/pro-adapters/CrudSearchBar.tsx',
  '../../../ui/src/pro-adapters/CrudTableSection.tsx',
  '../../../ui/src/pro-adapters/CrudWorkspacePage.tsx',
  '../../../ui/src/pro-adapters/SearchTablePage.tsx',
  '../../../ui/src/pro-adapters/CrudFormDrawer.tsx',
];

describe('standard CRUD component source contract', () => {
  it('uses Ant Design ecosystem controls instead of native or legacy interactive controls', () => {
    const violations = standardCrudSourceFiles.flatMap((relativePath) => {
      const source = readFileSync(fileURLToPath(new URL(relativePath, import.meta.url)), 'utf8');
      const nativeControls = [...source.matchAll(/<(input|button|select|textarea|table)\b/g)]
        .map((match) => `native <${match[1]?.toLowerCase()}>`);
      const legacyImports = [...source.matchAll(/from ['"]\.\.\/components\/(Button|Input|Select|Textarea|Checkbox|DataTable|Drawer|Modal|Pagination|ConfirmDialog)['"]/g)]
        .map((match) => `legacy ${match[1]}`);

      return [...nativeControls, ...legacyImports].map((violation) => `${relativePath}: ${violation}`);
    });

    expect(violations).toEqual([]);
  });

  it('gives shared query toolbar actions stable accessible names', () => {
    const searchBar = readFileSync(fileURLToPath(new URL(
      '../../../ui/src/pro-adapters/CrudSearchBar.tsx',
      import.meta.url,
    )), 'utf8');
    const tableSection = readFileSync(fileURLToPath(new URL(
      '../../../ui/src/pro-adapters/CrudTableSection.tsx',
      import.meta.url,
    )), 'utf8');

    expect(searchBar).toContain('aria-label="搜索"');
    expect(searchBar).toContain('aria-label="重置"');
    expect(tableSection).toContain('aria-label="刷新"');
  });
});
