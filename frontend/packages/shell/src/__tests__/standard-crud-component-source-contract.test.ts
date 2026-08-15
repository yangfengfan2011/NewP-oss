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

const standardCrudListHostUrl = new URL('../pages/StandardCrudListPageHost.tsx', import.meta.url);
const standardCrudListFormRuntimeUrl = new URL('../pages/StandardCrudListFormRuntime.ts', import.meta.url);
const standardCrudListOverlayRuntimeUrl = new URL('../pages/StandardCrudListOverlayRuntime.tsx', import.meta.url);
const standardCrudDetailHostUrl = new URL('../pages/StandardCrudDetailPageHost.tsx', import.meta.url);
const standardCrudDetailSectionsUrl = new URL(
  '../standard-crud/StandardCrudResolvedDetailSections.tsx',
  import.meta.url,
);
const standardCrudSchemaResolverUrl = new URL(
  '../metadata/standardCrudSchemaResolver.ts',
  import.meta.url,
);
const standardCrudSdkUrl = new URL('../../../sdk/src/standardCrud.ts', import.meta.url);
const standardCrudDetailFormValuesUrl = new URL(
  '../standard-crud/standardCrudDetailFormValues.ts',
  import.meta.url,
);
const detailPageUrl = new URL('../../../ui/src/pro-adapters/DetailPage.tsx', import.meta.url);

describe('standard CRUD component source contract', () => {
  it('uses Ant Design ecosystem controls instead of native or legacy interactive controls', () => {
    const violations = standardCrudSourceFiles.flatMap((relativePath) => {
      const source = readFileSync(fileURLToPath(new URL(relativePath, import.meta.url)), 'utf8');
      const nativeControls = [...source.matchAll(/<(input|button|select|textarea|table)\b/g)]
        .map((match) => `native <${match[1]?.toLowerCase()}>`);
      const legacyImports = [...source.matchAll(/from ['"]\.\.\/components\/(Button|Input|Select|Textarea|Checkbox|DataTable|Drawer|Modal|Pagination|ConfirmDialog)['"]/g)]
        .map((match) => `legacy ${match[1]}`);
      return [...nativeControls, ...legacyImports]
        .map((violation) => `${relativePath}: ${violation}`);
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

  it('keeps drawer and edit-form implementations behind route-local dynamic loaders', () => {
    const listHost = readFileSync(fileURLToPath(standardCrudListHostUrl), 'utf8');
    const detailHost = readFileSync(fileURLToPath(standardCrudDetailHostUrl), 'utf8');
    const overlayRuntime = readFileSync(fileURLToPath(standardCrudListOverlayRuntimeUrl), 'utf8');

    expect(listHost).toContain("import('./StandardCrudListDrawerRuntime')");
    expect(listHost).toContain("import('./StandardCrudListOverlayRuntime')");
    expect(listHost).not.toMatch(/import\s*\{[^}]*\b(?:CrudFormDrawer|StandardCrudForm)\b[^}]*\}\s*from/);
    expect(listHost).not.toContain("from '../standard-crud/standardCrudMetadataForm'");
    expect(listHost).not.toContain("from '../standard-crud/standardCrudDetailFormValues'");
    expect(listHost).not.toMatch(/import\(['"]antd\/es\/(?:drawer|modal)['"]\)/);
    expect(overlayRuntime).toContain("from 'antd/es/drawer'");
    expect(overlayRuntime).toContain("from 'antd/es/modal'");
    expect(overlayRuntime).toContain('<AntdDrawer');
    expect(overlayRuntime).toContain('<AntdModal');
    expect(detailHost).toContain("import('./StandardCrudDetailEditor')");
    expect(detailHost).not.toMatch(/import\s*\{[^}]*\b(?:FormPage|StandardCrudForm)\b[^}]*\}\s*from/);
  });

  it('keeps list form values separate from detail preview and columns', () => {
    const listHost = readFileSync(fileURLToPath(standardCrudListHostUrl), 'utf8');
    const formRuntime = readFileSync(fileURLToPath(standardCrudListFormRuntimeUrl), 'utf8');
    const formValues = readFileSync(fileURLToPath(standardCrudDetailFormValuesUrl), 'utf8');

    expect(formRuntime).toContain("from '../standard-crud/standardCrudDetailFormValues'");
    expect(listHost).toContain("import('./StandardCrudListFormRuntime')");
    expect(listHost).not.toContain("from '../standard-crud/standardCrudDetailFormValues'");
    expect(listHost).not.toContain("from '../standard-crud/standardCrudDetailView'");
    expect(formValues).not.toMatch(/DetailPage|ProDescriptions|buildStandardCrudDetailColumns/);
  });

  it('keeps detail display and form capabilities out of the list static path', () => {
    const detailPage = readFileSync(fileURLToPath(detailPageUrl), 'utf8');

    expect(detailPage).toContain("lazy(() => import('@ant-design/pro-descriptions'))");
  });

  it('keeps retired detail contracts and form fallbacks out of the standard CRUD runtime', () => {
    const sdk = readFileSync(fileURLToPath(standardCrudSdkUrl), 'utf8');
    const detailHost = readFileSync(fileURLToPath(standardCrudDetailHostUrl), 'utf8');
    const detailSections = readFileSync(fileURLToPath(standardCrudDetailSectionsUrl), 'utf8');
    const resolver = readFileSync(fileURLToPath(standardCrudSchemaResolverUrl), 'utf8');

    expect(sdk).not.toMatch(/\bdetailActions\??\s*:/);
    expect(sdk).not.toMatch(/\brenderDetail(?:HeaderActions)?\??\s*:/);
    expect(detailHost).not.toMatch(/registration\.renderDetail/);
    expect(detailSections).not.toMatch(/\bformFields\b|StandardCrudFieldDefinition/);
    expect(resolver).not.toMatch(/function readDetailSection\b|function buildStandardCrudDetailSchema\b/);
    expect(resolver).not.toMatch(/replace\(\/Id\$\/|endsWith\(['"]Id['"]\)/);
  });

});
