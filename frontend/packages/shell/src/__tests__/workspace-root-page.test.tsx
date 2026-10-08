// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { beforeAll, expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import workspace from '../../../../modules/workspace/src';
import folder from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceFolderRuntime.contract.json';
import content from '../../../../../modules/Workspace/metadata/models/WorkspaceFolderContent.model.json';
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
  await Promise.all([import('../composition/StandardTreePage'), import('../pages/StandardCrudListPageHost'), import('../pages/StandardCrudDetailPageHost'), import('../../../../modules/workspace/src/workspaceRootResource'), import('@ant-design/pro-descriptions')]);
});

it.each(['receipt', 'success'] as const)('mounts the real shared content list after %s creation and discards another account’s pending content', async scenario => {
  let account = 'a';
  const writes: unknown[] = [];
  let completeOld!: (response: Response) => void;
  const user = () => ({ id: account, ecode: account, ename: account, revision: 1, mustChangePassword: false,
    isLocalAdministrator: false, timeZoneId: 'Asia/Taipei', featurePositions: ['workspace-entry', 'workspace-create'] });
  const spaces = () => [
    { spaceId: 'public', spaceKind: 'Public', rootFolderId: 'public-root', name: '公共空间', hasChildren: scenario === 'success' && writes.length > 0 },
    { spaceId: `${account}-space`, spaceKind: 'Personal', rootFolderId: `${account}-root`, name: `个人空间-${account}` },
  ];
  const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
  const rootDetail = (id: string, name: string, description: string) => ({ id, fields: { EID: id, ENAME: name, Description: description,
    CreatedBy: 'actor', CreatedBy_DisplayValue: '创建账号', UpdatedBy: 'actor', UpdatedBy_DisplayValue: '更新账号',
    CreatedAt: '2026-10-08T00:00:00Z', CreatedAt_DisplayValue: '2026-10-08 08:00', UpdatedAt: '2026-10-08T00:00:00Z', UpdatedAt_DisplayValue: '2026-10-08 08:00' } });
  const contents = (id: string, name: string) => ({ items: [{ linkId: `${id}-link`, associatedObject: { modelKey: 'Workspace/WorkspaceFolder', id },
    fields: { LID: `${id}-link`, Name: name, Code: null, ObjectType: 'Workspace/WorkspaceFolder', UpdatedBy: 'actor', UpdatedBy_DisplayValue: '更新账号', UpdatedAt: '2026-10-08T00:00:00Z', UpdatedAt_DisplayValue: '2026-10-08 08:00' } }], total: 1, page: 1, pageSize: 20 });
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = input instanceof Request ? new URL(input.url).pathname : String(input);
    const method = input instanceof Request ? input.method : init?.method;
    if (path.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (path.endsWith('current-user')) return json(user());
    if (path === '/api/workspace/content/folders' && method === 'POST') {
      writes.push(input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body)));
      return json(scenario === 'success' ? rootDetail('created-folder', '创建后可读', '') : { succeeded: true });
    }
    if (method === 'POST') return json(spaces()[1]);
    if (path === '/api/workspace/spaces') return json(spaces());
    if (path.startsWith('/api/workspace/content/path?')) {
      const root = new URL(path, 'http://localhost').searchParams.get('rootFolderId')!;
      return json({ folderId: root, folderIds: [root], names: [root === 'public-root' ? '公共空间' : '个人空间'] });
    }
    if (path.startsWith('/api/workspace/content/children?')) return json({ items: [], total: 0, page: 1, pageSize: 0 });
    if (path.startsWith('/api/links/workspace.content?')) {
      const id = new URL(path, 'http://localhost').searchParams.get('anchorId');
      if (id === 'a-root') return new Promise<Response>(resolve => { completeOld = resolve; });
      if (id === 'public-root' && scenario === 'success') return json(writes.length
        ? contents('created-folder', '创建后可读') : { items: [], total: 0, page: 1, pageSize: 20 });
      return json(contents(`${id}-folder`, id === 'b-root' ? 'B内容' : '公共根内容'));
    }
    if (path.endsWith('/folders/public-root-folder')) return json(rootDetail('public-root-folder', '公共对象属性', '实际说明'));
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
      <AuthProvider initialUser={user()}><AuthorizationProvider featurePositions={['workspace-entry', 'workspace-create']}><PlatformQueryProvider scopeKey="workspace-test">
        <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Workspace', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: [
          { key: 'workspace', position: 'workspace-entry', kind: 'navigation' },
          { key: 'create', position: 'workspace-create', kind: 'action', parentPosition: 'workspace-entry', staticScope: 'folder', actionScope: 'list.pageActions' },
        ] }] }, navDocuments: [], modelDocuments: [folder, content], viewDocuments: [tree, list, detail] }}>
          <SessionSwitch /><PageEntry page={{ pageKey: 'platform.tree', parameters: { viewKey: tree.view.key, featurePosition: 'workspace-entry' }, inputResolverKey: 'workspace.tree.current' }} />
        </RuntimeMetadataProvider>
      </PlatformQueryProvider></AuthorizationProvider></AuthProvider>
    </LocalizationProvider>,
  }], { initialEntries: ['/workspace'] });
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); vi.unstubAllGlobals(); ModuleRegistry.replaceAll([], 'workspace-test-cleanup'); });
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(container.textContent).toContain(scenario === 'success' ? '当前数据集为空' : '公共根内容'));
  expect(container.querySelector('.ant-tree input[placeholder]')).toBeNull();
  expect(container.querySelector('.ant-table')).not.toBeNull();
  if (scenario === 'receipt') {
    await act(async () => [...container.querySelectorAll<HTMLElement>('.ant-table *')].find(element => element.children.length === 0 && element.textContent === '更多')!.click());
    await vi.waitFor(() => expect(document.body.textContent).toContain('打开属性'));
    await act(async () => [...document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(element => element.textContent === '打开属性')!.click());
    await vi.waitFor(() => expect(document.body.textContent).toContain('公共对象属性'));
    expect(fetcher.mock.calls.some(([input]) => String(input instanceof Request ? input.url : input).endsWith('/folders/public-root-folder'))).toBe(true);
    await act(async () => document.querySelector<HTMLButtonElement>('.ant-drawer-close')!.click());
  }
  const publicListReads = () => fetcher.mock.calls.filter(([input]) => {
    const url = new URL(input instanceof Request ? input.url : String(input), 'http://localhost');
    return url.pathname === '/api/links/workspace.content' && url.searchParams.get('anchorId') === 'public-root';
  }).length;
  const readsBeforeCreate = publicListReads();
  const createButton = [...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.replace(/\s/g, '') === '新建文件夹');
  expect(createButton).toBeDefined(); await act(async () => createButton!.click());
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer form')).not.toBeNull());
  const nameLabel = [...document.querySelectorAll<HTMLLabelElement>('.ant-drawer label')].find(label => label.textContent?.includes('名称'))!;
  const nameInput = document.getElementById(nameLabel.htmlFor) as HTMLInputElement;
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(nameInput, scenario === 'success' ? '创建后可读' : '创建后不可读');
    nameInput.dispatchEvent(new Event('input', { bubbles: true }));
  });
  await act(async () => document.querySelector<HTMLFormElement>('.ant-drawer form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  await vi.waitFor(() => expect(writes).toHaveLength(1));
  expect(writes[0]).toEqual({ position: { spaceId: 'public', rootFolderId: 'public-root', linkIds: [] }, values: { ENAME: scenario === 'success' ? '创建后可读' : '创建后不可读', Description: null } });
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer-open')).toBeNull());
  await vi.waitFor(() => expect(container.textContent).not.toContain('已保存，刷新失败'));
  expect(container.querySelector('.ant-table')?.textContent).not.toContain('创建后不可读');
  if (scenario === 'success') await vi.waitFor(() => expect(container.querySelector('.ant-table')?.textContent).toContain('创建后可读'));
  await vi.waitFor(() => expect(publicListReads()).toBe(readsBeforeCreate + 1));
  if (scenario === 'success') expect(container.querySelector('tr[data-row-key="created-folder-link"]')).not.toBeNull();
  await act(async () => [...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.replace(/\s/g, '') === '刷新')!.click());
  expect(writes).toHaveLength(1);
  expect([...container.querySelectorAll('.ant-tree-title')].map(node => node.textContent)).toEqual(['公共空间', '个人空间']);
  const select = async (text: string) => {
    const item = [...container.querySelectorAll<HTMLElement>('.ant-tree-node-content-wrapper')].find(node => node.textContent?.includes(text));
    expect(item).toBeDefined(); await act(async () => item!.click());
  };
  await select('个人空间');
  await vi.waitFor(() => expect(completeOld).toBeDefined());
  await act(async () => [...container.querySelectorAll('button')].find(button => button.textContent === '切换账号')!.click());
  await vi.waitFor(() => expect(fetcher.mock.calls.filter(([input, init]) => (input instanceof Request ? input.method : init?.method) === 'POST'
    && String(input instanceof Request ? input.url : input).includes('/personal/ensure'))).toHaveLength(2));
  await act(async () => undefined);
  await select('个人空间');
  await vi.waitFor(async () => { await act(async () => undefined); expect(container.textContent).toContain('B内容'); });
  await act(async () => completeOld(json(contents('a-folder', 'A内容'))));
  expect(container.textContent).not.toContain('A内容');
  expect(container.textContent).toContain('B内容');
  expect(fetcher.mock.calls.some(([input]) => String(input instanceof Request ? input.url : input).includes('/search'))).toBe(false);
});
