import './authorizedFeatureUiFixture';
// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import organizationBindings from '../../../../modules/platform/organization/src';
import { organizationPageInputs } from '../../../../modules/platform/organization/src/organizationTree';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { PageEntry } from '../composition/PageOutlet';
import list from '../../../../modules/platform/organization/metadata/views/DepartmentList.view.json';
import detail from '../../../../modules/platform/organization/metadata/views/DepartmentDetail.view.json';
import runtimeModels from '../../../../../tests/Kernel.Tests/Fixtures/OrganizationRuntimeModels.contract.json';
vi.mock('@icp-platform/core', async original => ({ ...await original<typeof import('@icp-platform/core')>(), platformFetch: (...args: Parameters<typeof fetch>) => fetch(...args) }));
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
// 本例验证定位落地时的表单保护；收集阶段预载真实模块，不把冷编译计入交互断言。
await Promise.all([
  import('../pages/StandardCrudListPageHost'),
  import('../pages/StandardCrudListDrawerRuntime'),
  import('../pages/StandardCrudListOverlayRuntime'),
  organizationBindings.standardCrudCapabilities!.find(binding => binding.targetKey === 'organization.department.list')!.load(),
]);

it.each(['cancel', 'submitting'])('checks shared List lifecycle again when a pending tree path lands: %s retains the original form', async scenario => {
  const nodes = ['c', 'other'].map(id => ({ nodeKey: id, nodeType: 'company', label: id, hasChildren: false, context: { id, companyId: id } }));
  let finishPath: ((path: typeof nodes) => void) | undefined;
  let finishWrite: ((response: Response) => void) | undefined;
  const fetcher = vi.spyOn(globalThis, 'fetch').mockImplementation(async (_url, init) => init?.method === 'POST' ? new Promise(resolve => { finishWrite = resolve; }) : new Response(JSON.stringify({ items: [], currentItems: [], total: 0, page: 1, pageSize: 20, hasMore: false })));
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'test', displayName: 'Test', version: '1' }, standardCrudCapabilities: organizationBindings.standardCrudCapabilities,
    pageInputResolvers: [...organizationPageInputs, { key: 'scope', pageKeys: ['platform.tree'], resolve: parameters => ({ parameters, treeScope: {} }) }],
    treeProviders: [{ key: 'tree', roots: async () => nodes, children: async () => [], path: async () => new Promise(resolve => { finishPath = resolve; }), search: async () => ({ items: [{ node: nodes[1], path: [nodes[1]] }], total: 1, page: 1, pageSize: 20 }), resolveChange: async () => ({ kind: 'unrelated' }) }],
  }], 'test');
  const tree = { type: 'view', view: { key: 'tree', mode: 'tree', dicKey: 'Tree', providerKey: 'tree', nodeTypes: [{ key: 'company', titleKey: 'Company', pageRef: { pageKey: 'platform.tabs', parameters: { viewKey: 'inner' } } }] } };
  const inner = { type: 'view', view: { key: 'inner', mode: 'tab', dicKey: 'Tree', defaultTabKey: 'children', tabs: [{ key: 'children', titleKey: 'Children', pageRef: { pageKey: 'platform.list', parameters: { viewKey: 'organization.department.list' }, inputResolverKey: 'organization.company.direct' } }] } };
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const router = createMemoryRouter([{ path: '/', element: <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: { 'DepartmentListView.Action_Create': '新增部门' } }}><AuthorizationProvider permissions={['Organization.Department.View', 'Organization.Department.Create']}><PlatformQueryProvider scopeKey="dirty-location"><RuntimeMetadataProvider value={{ manifest: { modules: [] }, navDocuments: [], modelDocuments: [runtimeModels.department], viewDocuments: [list, detail, tree, inner] }}><PageEntry page={{ pageKey: 'platform.tree', parameters: { viewKey: 'tree' }, inputResolverKey: 'scope' }} /></RuntimeMetadataProvider></PlatformQueryProvider></AuthorizationProvider></LocalizationProvider> }]);
  let finished = false;
  let cleanupTask: Promise<void> | undefined;
  const cleanup = () => cleanupTask ??= (async () => {
    finished = true;
    try {
      await act(async () => {
        root.unmount();
        finishPath?.([]);
        finishWrite?.(new Response(JSON.stringify({ title: 'validation', errors: { ENAME: ['test rejected'] } }), { status: 400 }));
      });
    } finally { router.dispose(); container.remove(); fetcher.mockRestore(); }
  })();
  onTestFinished(cleanup);
  const waitFor = async (condition: () => boolean) => {
    const deadline = Date.now() + 4000;
    while (!finished) {
      if (condition()) return;
      if (Date.now() >= deadline) { expect(condition(), document.body.textContent ?? '').toBe(true); return; }
      await new Promise(resolve => setTimeout(resolve, 25));
      if (finished) throw new Error('Tree location test already disposed');
      await act(async () => undefined);
    }
    throw new Error('Tree location test already disposed');
  };
  const button = (text: string) => [...document.querySelectorAll<HTMLButtonElement>('button')].find(item => item.textContent?.replace(/\s/g, '') === text)!;
  const fill = async (input: HTMLInputElement, value: string) => act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value); input.dispatchEvent(new Event('input', { bubbles: true })); });
  try {
    await act(async () => root.render(<RouterProvider router={router} />));
    await waitFor(() => !!button('新增部门'));
    await fill(container.querySelector<HTMLInputElement>('[aria-label="搜索节点"]')!, 'other');
    await act(async () => container.querySelector<HTMLButtonElement>('[aria-label="定位节点 other"]')!.click());
    await act(async () => button('新增部门').click());
    await waitFor(() => !!document.querySelector('#ENAME'));
    await fill(document.querySelector<HTMLInputElement>('#ENAME')!, '未保存名称');
    if (scenario === 'submitting') {
      await fill(document.querySelector<HTMLInputElement>('#ECODE')!, 'CODE');
      await fill(document.querySelector<HTMLInputElement>('#SortOrder')!, '001');
      await act(async () => (button('保存并关闭') ?? button('保存')).click());
      await waitFor(() => !!finishWrite);
    }
    await act(async () => finishPath!([nodes[1]]));
    expect(container.querySelector('.ant-tree-node-selected')?.textContent).toBe('c');
    expect(document.querySelector<HTMLInputElement>('#ENAME')?.value).toBe('未保存名称');
    if (scenario === 'cancel') {
      await waitFor(() => !!button('继续编辑'));
      await act(async () => button('继续编辑').click());
    } else expect(button('继续编辑')).toBeUndefined();
    expect(container.querySelector('.ant-tree-node-selected')?.textContent).toBe('c');
    expect(document.querySelector<HTMLInputElement>('#ENAME')?.value).toBe('未保存名称');
    expect(new URLSearchParams(router.state.location.search).get('node')).toBeNull();
  } finally { await cleanup(); }
});
