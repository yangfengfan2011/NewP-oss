// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { beforeAll, expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import workspace from '../../../../modules/workspace/src';
import folder from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceFolderRuntime.contract.json';
import tree from '../../../../../modules/Workspace/metadata/views/WorkspaceStructure.view.json';
import list from '../../../../../modules/Workspace/metadata/views/WorkspaceFolderList.view.json';
import detail from '../../../../../modules/Workspace/metadata/views/WorkspaceFolderDetail.view.json';
import dictionary from '../../../../../modules/Workspace/metadata/dictionaries/Workspace.dic.json';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { PageEntry } from '../composition/PageOutlet';
import { AuthProvider, useAuth } from '../contexts/AuthContext';
import { compileStandardCrudRouteProjection } from '../navigation/standardCrudRouteAdapter';
import navigation from '../../../../../modules/Workspace/metadata/navigation/Workspace.nav.json';

vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
beforeAll(async () => {
  await Promise.all([import('../composition/StandardTreePage'), import('../pages/StandardCrudDetailPageHost'), import('../../../../modules/workspace/src/workspaceRootResource'), import('@ant-design/pro-descriptions')]);
});

it('mounts the real shared root detail, disables search and discards another account’s pending root context', async () => {
  let account = 'a';
  let completeOld!: (response: Response) => void;
  const user = () => ({ id: account, ecode: account, ename: account, revision: 1, mustChangePassword: false,
    isLocalAdministrator: false, timeZoneId: 'Asia/Taipei', featurePositions: ['workspace-entry'] });
  const spaces = () => [
    { spaceId: 'public', spaceKind: 'Public', rootFolderId: 'public-root', name: '公共空间' },
    { spaceId: `${account}-space`, spaceKind: 'Personal', rootFolderId: `${account}-root`, name: `个人空间-${account}` },
  ];
  const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
  const rootDetail = (id: string, name: string, description: string) => ({ id, fields: { EID: id, ENAME: name, Description: description,
    CreatedBy: 'actor', CreatedBy_DisplayValue: '创建账号', UpdatedBy: 'actor', UpdatedBy_DisplayValue: '更新账号',
    CreatedAt: '2026-10-08T00:00:00Z', CreatedAt_DisplayValue: '2026-10-08 08:00', UpdatedAt: '2026-10-08T00:00:00Z', UpdatedAt_DisplayValue: '2026-10-08 08:00' } });
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = input instanceof Request ? new URL(input.url).pathname : String(input);
    const method = input instanceof Request ? input.method : init?.method;
    if (path.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (path.endsWith('current-user')) return json(user());
    if (method === 'POST') return json(spaces()[1]);
    if (path === '/api/workspace/spaces') return json(spaces());
    if (path.endsWith('/roots/a-root')) return new Promise<Response>(resolve => { completeOld = resolve; });
    if (path.endsWith('/roots/b-root')) return json(rootDetail('b-root', 'B的根详情', 'B内容'));
    if (path.endsWith('/roots/public-root')) return json(rootDetail('public-root', '公共根详情', '实际公共根'));
    if (path.endsWith('/root')) return json({ id: `${account}-root`, fields: { ENAME: `个人空间-${account}` } });
    throw new Error(`Unexpected request ${method} ${path}`);
  });
  vi.stubGlobal('fetch', fetcher);
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Workspace', displayName: '工作空间', version: '1' }, ...workspace }], 'workspace-test');
  // 实际 AppRoutes 先编译所有模块路由，再装载组合；直接挂 PageEntry 不能替代这一步。
  expect(compileStandardCrudRouteProjection(ModuleRegistry.getAllModules(), [navigation], [tree, list, detail], [])).toEqual({
    detailEntries: [], drawerRedirects: [], fallbackRoutes: [],
  });
  function SessionSwitch() {
    const auth = useAuth();
    return <button onClick={() => { account = 'b'; void auth.retrySessionValidation(); }}>切换账号</button>;
  }
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  const catalogs = { zhs: Object.fromEntries(Object.entries(dictionary.Workspace).map(([key, value]) => [`Workspace.${key}`, value.zhs])) };
  const router = createMemoryRouter([{ path: '/workspace', element:
    <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={catalogs}>
      <AuthProvider initialUser={user()}><AuthorizationProvider featurePositions={['workspace-entry']}><PlatformQueryProvider scopeKey="workspace-test">
        <RuntimeMetadataProvider value={{ manifest: { modules: [] }, navDocuments: [], modelDocuments: [folder], viewDocuments: [tree, list, detail] }}>
          <SessionSwitch /><PageEntry page={{ pageKey: 'platform.tree', parameters: { viewKey: tree.view.key, featurePosition: 'workspace-entry' }, inputResolverKey: 'workspace.tree.current' }} />
        </RuntimeMetadataProvider>
      </PlatformQueryProvider></AuthorizationProvider></AuthProvider>
    </LocalizationProvider>,
  }], { initialEntries: ['/workspace'] });
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); vi.unstubAllGlobals(); ModuleRegistry.replaceAll([], 'workspace-test-cleanup'); });
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(container.textContent).toContain('公共根详情'));
  expect(container.querySelector('input[placeholder]')).toBeNull();
  expect(container.querySelector('.ant-table')).toBeNull();
  expect([...container.querySelectorAll('.ant-tree-title')].map(node => node.textContent)).toEqual(['公共空间', '个人空间']);
  const select = async (text: string) => {
    const item = [...container.querySelectorAll<HTMLElement>('.ant-tree-node-content-wrapper')].find(node => node.textContent?.includes(text));
    expect(item).toBeDefined(); await act(async () => item!.click());
  };
  await select('个人空间');
  await vi.waitFor(() => expect(completeOld).toBeDefined());
  await act(async () => [...container.querySelectorAll('button')].find(button => button.textContent === '切换账号')!.click());
  await vi.waitFor(() => expect(fetcher.mock.calls.filter(([input, init]) => (input instanceof Request ? input.method : init?.method) === 'POST')).toHaveLength(2));
  await act(async () => undefined);
  await select('个人空间');
  await vi.waitFor(async () => { await act(async () => undefined); expect(container.textContent).toContain('B的根详情'); });
  await act(async () => completeOld(json(rootDetail('a-root', 'A的私有根', 'A内容'))));
  expect(container.textContent).not.toContain('A的私有根');
  expect(container.textContent).not.toContain('A内容');
  expect(container.textContent).toContain('B内容');
  expect(fetcher.mock.calls.some(([input]) => String(input instanceof Request ? input.url : input).includes('/search'))).toBe(false);
});
