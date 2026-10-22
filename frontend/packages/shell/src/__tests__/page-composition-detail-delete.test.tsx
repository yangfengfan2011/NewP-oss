import './authorizedFeatureUiFixture';
// @vitest-environment jsdom
import { act, StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import organizationBindings from '../../../../modules/platform/organization/src';
import { RuntimeMetadataProvider } from './formalRuntimeMetadataProvider';
import { PageEntry } from '../composition/PageOutlet';
import list from '../../../../modules/platform/organization/metadata/views/DepartmentList.view.json';
import detail from '../../../../modules/platform/organization/metadata/views/DepartmentDetail.view.json';
import runtimeModels from '../../../../../tests/Kernel.Tests/Fixtures/OrganizationRuntimeModels.contract.json';
vi.mock('@icp-platform/core', async original => ({ ...await original<typeof import('@icp-platform/core')>(), platformFetch: (...args: Parameters<typeof fetch>) => fetch(...args) }));
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
// 此测试验证删除后的读取收敛；收集阶段预载真实组件，冷启动仍由正式 Gate 覆盖。
await Promise.all([
  import('../pages/StandardCrudDetailPageHost'),
  import('../pages/StandardCrudListPageHost'),
  import('../pages/StandardCrudListDrawerRuntime'),
  import('../pages/StandardCrudListOverlayRuntime'),
  organizationBindings.standardCrudCapabilities!.find(binding => binding.targetKey === 'organization.department.list')!.load(),
]);
it('settles after one confirmed delete with the real embedded detail host and Organization REST adapter', async () => {
  const fields = { EID: 'd', CompanyId: 'c', CompanyId_DisplayValue: 'Company', ParentDepartmentId: null, ParentDepartmentId_DisplayValue: null, ECODE: 'D', ENAME: 'Department', IsEnabled: true, SortOrder: '001', CreatedAt: '2026-09-08T00:00:00Z', CreatedBy: 'system', UpdatedBy: 'system', UpdatedAt: '2026-09-08T00:00:00Z' };
  const requests: string[] = [];
  const fetcher = vi.spyOn(globalThis, 'fetch').mockImplementation(async (url, init) => {
    requests.push(`${init?.method ?? 'GET'} ${String(url)}`);
    if (requests.filter(item => item === 'GET /api/organization/departments/d').length > 8) return new Promise<Response>(() => undefined);
    if (init?.method === 'DELETE') return new Response(null, { status: 204 });
    if (String(url).endsWith('/departments/d')) return new Response(JSON.stringify({ fields, summaries: {} }));
    return new Response(JSON.stringify({ items: [], currentItems: [], page: 1, pageSize: 20, hasMore: false }));
  });
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'test', displayName: 'Test', version: '1' },
    standardCrudCapabilities: organizationBindings.standardCrudCapabilities,
    pages: [{ pageKey: 'content', load: async () => ({ default: () => <p>node content</p> }) }],
    pageInputResolvers: [ { key: 'scope', pageKeys: ['platform.tree'], resolve: parameters => ({ parameters, treeScope: {} }) }, { key: 'node', pageKeys: ['platform.list', 'platform.detail'], resolve: (parameters, context) => ({ parameters, context, recordId: String(context!.context!.id) }) } ],
    treeProviders: [{ key: 'tree', roots: async () => [{ nodeKey: 'd', nodeType: 'department', label: 'Department', hasChildren: false, context: { id: 'd' } }], children: async () => [], path: async () => [], search: vi.fn(), resolveChange: async () => ({ kind: 'unrelated' }) }],
  }], 'test');
  const tree = { type: 'view', view: { key: 'tree', mode: 'tree', dicKey: 'Tree', providerKey: 'tree', nodeTypes: [{ key: 'department', titleKey: 'Department', pageRef: { pageKey: 'platform.detail', parameters: { viewKey: 'organization.department.detail' }, inputResolverKey: 'node' }, contextMenu: [{ key: 'delete', labelKey: 'Delete', standardAction: 'delete', viewKey: 'organization.department.list', inputResolverKey: 'node' }] }] } };
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const router = createMemoryRouter([{ path: '/', element: <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}><AuthorizationProvider permissions={['Organization.Department.View', 'Organization.Department.Delete']}><PlatformQueryProvider scopeKey="test"><RuntimeMetadataProvider value={{ manifest: { modules: [] }, navDocuments: [], modelDocuments: [runtimeModels.department], viewDocuments: [list, detail, tree] }}><PageEntry page={{ pageKey: 'platform.tree', parameters: { viewKey: 'tree' }, inputResolverKey: 'scope' }} /></RuntimeMetadataProvider></PlatformQueryProvider></AuthorizationProvider></LocalizationProvider> }]);
  let finished = false;
  const cleanup = async () => {
    if (finished) return;
    finished = true;
    await act(async () => root.unmount()); router.dispose(); container.remove(); fetcher.mockRestore();
  };
  onTestFinished(cleanup);
  const waitFor = async (condition: () => boolean) => {
    for (let i = 0; i < 100 && !condition(); i++) {
      await new Promise(resolve => setTimeout(resolve, 10));
      if (finished) throw new Error('Detail delete test already disposed');
      await act(async () => undefined);
    }
    expect(condition(), document.body.textContent ?? '').toBe(true);
  };
  try {
    await act(async () => root.render(<StrictMode><RouterProvider router={router} /></StrictMode>));
    await waitFor(() => Boolean(container.querySelector('.ant-tree-title')));
    await act(async () => container.querySelector('.ant-tree-title')!.firstElementChild!.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true })));
    await waitFor(() => Boolean(document.querySelector('[role=menuitem]')));
    await act(async () => (document.querySelector('[role=menuitem]') as HTMLElement).click());
    await waitFor(() => Boolean(document.querySelector('[role=dialog]')));
    expect(requests.filter(item => item.startsWith('DELETE'))).toHaveLength(0);
    const confirm = [...document.querySelectorAll('[role=dialog] button')].find(item => item.textContent?.replace(/\s/g, '') === '确认') as HTMLElement;
    const beforeReads = requests.filter(item => item === 'GET /api/organization/departments/d').length;
    await act(async () => confirm.click());
    expect(requests.filter(item => item.startsWith('DELETE'))).toEqual(['DELETE /api/organization/departments/d']);
    const detailReads = requests.filter(item => item === 'GET /api/organization/departments/d').length;
    // 删除呈现与当前详情共享执行后均可读取；检查写后增量并确保随后静止。
    expect(detailReads - beforeReads).toBeLessThanOrEqual(2);
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 30)); });
    expect(requests.filter(item => item === 'GET /api/organization/departments/d')).toHaveLength(detailReads);
    expect(requests.filter(item => item.startsWith('DELETE'))).toHaveLength(1);
  } finally { await cleanup(); }
});
