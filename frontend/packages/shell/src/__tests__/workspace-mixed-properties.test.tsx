// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider, useParams } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import workspace from '../../../../modules/workspace/src';
import part from '../../../../modules/part/src';
import documentModule from '../../../../modules/document/src';
import { partResource } from '../../../../modules/part/src/partResource';
import { documentResource } from '../../../../modules/document/src/documentResource';
import folder from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceFolderRuntime.contract.json';
import partModel from '../../../../../tests/Kernel.Tests/Fixtures/PartRuntime.contract.json';
import documentModel from '../../../../../tests/Kernel.Tests/Fixtures/DocumentRuntime.contract.json';
import content from '../../../../../modules/Workspace/metadata/models/WorkspaceFolderContent.model.json';
import workspaceNavigation from '../../../../../modules/Workspace/metadata/navigation/Workspace.nav.json';
import tree from '../../../../../modules/Workspace/metadata/views/WorkspaceStructure.view.json';
import list from '../../../../../modules/Workspace/metadata/views/WorkspaceFolderList.view.json';
import references from '../../../../../modules/Workspace/metadata/views/WorkspaceReferencesList.view.json';
import detail from '../../../../../modules/Workspace/metadata/views/WorkspaceFolderDetail.view.json';
import partList from '../../../../../modules/Part/metadata/views/PartList.view.json';
import partDetail from '../../../../../modules/Part/metadata/views/PartDetail.view.json';
import documentList from '../../../../../modules/Document/metadata/views/DocumentList.view.json';
import documentDetail from '../../../../../modules/Document/metadata/views/DocumentDetail.view.json';
import partNavigation from '../../../../../modules/Part/metadata/navigation/Part.nav.json';
import documentNavigation from '../../../../../modules/Document/metadata/navigation/Document.nav.json';
import dictionary from '../../../../../modules/Workspace/metadata/dictionaries/Workspace.dic.json';
import partDictionary from '../../../../../modules/Part/metadata/dictionaries/Part.dic.json';
import documentDictionary from '../../../../../modules/Document/metadata/dictionaries/Document.dic.json';
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';
import { PageEntry } from '../composition/PageOutlet';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { AuthProvider } from '../contexts/AuthContext';
import { FeaturePlacementContext } from '../authorization/FeaturePlacementContext';
import { compileStandardCrudRouteProjection } from '../navigation/standardCrudRouteAdapter';
import { buildMenusFromNavDocuments } from '../metadata/navigationAdapter';

// 收集期装载真实模块，避免冷转换耗时进入生命周期 hook 的交互预算。
import '../composition/StandardTreePage';
import '../pages/StandardCrudListPageHost';
import '../pages/StandardCrudListDrawerRuntime';
import '@ant-design/pro-descriptions';

vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;

it.each(['part', 'document'] as const)('opens same EID through the registered %s owning read-only page and target feature', async type => {
  const user = { id: 'actor', ecode: 'actor', ename: '账号', revision: 1, mustChangePassword: false, isLocalAdministrator: false,
    timeZoneId: 'Asia/Taipei', featurePositions: ['workspace-entry', 'workspace-createPart', 'workspace-createDocument', 'part-entry', 'document-entry'] };
  const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
  const writes: unknown[] = [];
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(input instanceof Request ? input.url : String(input), 'http://localhost');
    if (url.pathname.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (url.pathname.endsWith('current-user')) return json(user);
    if (url.pathname.endsWith('/personal/ensure')) return json({ initialized: true });
    if (url.pathname === '/api/workspace/spaces') return json([
      { spaceId: 'public', spaceKind: 'Public', rootFolderId: 'root', name: '公共空间', hasChildren: true },
      { spaceId: 'personal', spaceKind: 'Personal', rootFolderId: 'personal-root', name: '个人空间' },
    ]);
    if (url.pathname.endsWith('/path')) return json({ folderIds: ['root', 'shared'], names: ['公共空间', '当前共享文件夹'] });
    if (url.pathname.endsWith('/children')) return json({ items: [{ linkId: 'shared-link', associatedObject: { id: 'shared' }, fields: { Name: '当前共享文件夹' }, hasChildren: false }] });
    if (url.pathname === `/api/workspace/content/${type === 'part' ? 'parts' : 'documents'}`) {
      writes.push(input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body)));
      return json({ succeeded: true });
    }
    if (url.pathname === '/api/workspace/folders/candidates') return json({ items: [], total: 0, page: 1, pageSize: 20 });
    if (url.pathname === `/api/${type}/${type === 'part' ? 'parts' : 'documents'}`) {
      expect(url.searchParams.get('fields')).toBe('EID,ECODE,ENAME');
      expect(new Headers(input instanceof Request ? input.headers : init?.headers).get('X-NewP-Feature')).toBe(type + '-entry');
      return json({ items: [{ EID: 'same-id', ECODE: 'SAME-CODE', ENAME: '无引用候选' }], total: 1, page: 1, pageSize: 20 });
    }
    if (url.pathname === '/api/links/workspace.content' && url.searchParams.get('direction') === 'reverse') {
      expect(url.searchParams.get('anchorModelKey')).toBe(type === 'part' ? 'Part/Part' : 'Document/Document');
      expect(url.searchParams.get('anchorId')).toBe('same-id');
      expect(url.searchParams.has('queryScope')).toBe(false);
      expect(new Headers(input instanceof Request ? input.headers : init?.headers).get('X-NewP-Feature')).toBe('workspace-entry');
      return json({ items: [{ linkId: 'incoming-relation', associatedObject: { modelKey: 'Workspace/WorkspaceFolder', id: 'parent-folder' },
        fields: { LID: 'incoming-relation', Name: '直接父文件夹', UpdatedBy: 'actor', UpdatedBy_DisplayValue: '更新账号', UpdatedAt: '2026-10-08T00:00:00Z', UpdatedAt_DisplayValue: '2026-10-08 08:00' } }], total: 1, page: 1, pageSize: 20 });
    }
    if (url.pathname === '/api/workspace/folders/parent-folder') return json({ fields: { EID: 'parent-folder', ENAME: '父文件夹属性', Description: '只开对象不定位路径',
      CreatedBy: 'actor', CreatedBy_DisplayValue: '创建账号', UpdatedBy: 'actor', UpdatedBy_DisplayValue: '更新账号',
      CreatedAt: '2026-10-08T00:00:00Z', CreatedAt_DisplayValue: '2026-10-08 08:00', UpdatedAt: '2026-10-08T00:00:00Z', UpdatedAt_DisplayValue: '2026-10-08 08:00' } });
    if (url.pathname === '/api/links/workspace.content') return json({ items: ['part', 'document'].map(owner => ({ linkId: owner + '-relation',
      associatedObject: { modelKey: owner === 'part' ? 'Part/Part' : 'Document/Document', id: 'same-id' },
      fields: { LID: owner + '-relation', ObjectType: owner === 'part' ? 'Part/Part' : 'Document/Document',
        ObjectType_DisplayValue: owner === 'part' ? '零组件' : '图文档', Name: owner + '-name', Code: 'SAME-CODE' } })), total: 2, page: 1, pageSize: 20 });
    if (url.pathname === `/api/${type}/${type === 'part' ? 'parts' : 'documents'}/same-id`) {
      const headers = new Headers(input instanceof Request ? input.headers : init?.headers);
      expect(headers.get('X-NewP-Feature')).toBe(type + '-entry');
      return json({ fields: { EID: 'same-id', ECODE: 'SAME-CODE', ENAME: type + '-owned-properties', Description: '所属对象说明',
        CreatedBy: 'actor', CreatedBy_DisplayValue: '创建账号', UpdatedBy: 'actor', UpdatedBy_DisplayValue: '更新账号',
        CreatedAt: '2026-10-08T00:00:00Z', CreatedAt_DisplayValue: '2026-10-08 08:00', UpdatedAt: '2026-10-08T00:00:00Z', UpdatedAt_DisplayValue: '2026-10-08 08:00' }, summaries: {} });
    }
    throw new Error(`Unexpected read ${url.pathname}`);
  });
  vi.stubGlobal('fetch', fetcher);
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Workspace', displayName: '工作空间', version: '1' }, ...workspace },
    { metadata: { moduleId: 'Part', displayName: '零组件', version: '1' }, ...part },
    { metadata: { moduleId: 'Document', displayName: '图文档', version: '1' }, ...documentModule }], 'workspace-mixed');
  const navs = [partNavigation, documentNavigation].map((navigation, index) => ({ ...navigation, navigation: { items: navigation.navigation.items.map(item => ({ ...item, featurePosition: index ? 'document-entry' : 'part-entry' })) } }));
  const views = [tree, list, detail, references, partList, partDetail, documentList, documentDetail];
  const workspaceNav = { ...workspaceNavigation, navigation: { items: workspaceNavigation.navigation.items.map(item => ({ ...item, featurePosition: 'workspace-entry' })) } };
  expect(buildMenusFromNavDocuments([workspaceNav, ...navs], (title, fallback) => title ?? fallback ?? '').map(menu => menu.path)).toEqual(['/workspace']);
  expect(compileStandardCrudRouteProjection(ModuleRegistry.getAllModules(), [workspaceNavigation, ...navs], views, []).detailEntries).toHaveLength(2);
  const catalogs = { zhs: Object.fromEntries([[dictionary.Workspace, 'Workspace'], [partDictionary.Part, 'Part'], [documentDictionary.Document, 'Document']].flatMap(([dict, key]) =>
    Object.entries(dict).map(([name, value]) => [`${key}.${name}`, (value as { zhs: string }).zhs]))) };
  function OwnedPage() { const { id } = useParams(); return <FeaturePlacementContext.Provider value={{ entryPosition: type + '-entry' }}>
    <StandardCrudDetailPageHost viewKey={type === 'part' ? partDetail.view.key : documentDetail.view.key} id={id ?? null} registration={type === 'part' ? partResource : documentResource} />
  </FeaturePlacementContext.Provider>; }
  const wrap = (page: React.ReactNode) => <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={catalogs}>
    <AuthProvider initialUser={user}><AuthorizationProvider featurePositions={user.featurePositions}><PlatformQueryProvider scopeKey="workspace-mixed">
      <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Workspace', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: [
        { key: 'workspace', position: 'workspace-entry', kind: 'navigation' }, ...['createPart', 'createDocument'].map(key => ({ key, position: 'workspace-' + key, kind: 'action', parentPosition: 'workspace-entry', staticScope: 'folder', actionScope: 'list.pageActions' })),
      ] }, ...['Part', 'Document'].map(moduleId => ({ moduleId, metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: [{ key: moduleId === 'Part' ? 'part.parts' : 'document.documents', kind: 'navigation', position: moduleId.toLowerCase() + '-entry' }] }))] },
      navDocuments: [workspaceNav, ...navs], modelDocuments: [folder, content, partModel, documentModel], viewDocuments: views }}>
        {page}
      </RuntimeMetadataProvider>
    </PlatformQueryProvider></AuthorizationProvider></AuthProvider>
  </LocalizationProvider>;
  const router = createMemoryRouter([{ path: '/workspace', element: wrap(<PageEntry page={{ pageKey: 'platform.tree', parameters: { viewKey: tree.view.key, featurePosition: 'workspace-entry' }, inputResolverKey: 'workspace.tree.current' }} />) },
    { path: type === 'part' ? '/parts/:id' : '/documents/:id', element: wrap(<OwnedPage />) }], { initialEntries: ['/workspace?node=public%3Aroot%3Ashared-link&other=keep'] });
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); vi.unstubAllGlobals(); ModuleRegistry.replaceAll([], 'workspace-mixed-cleanup'); });
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(container.querySelector(`tr[data-row-key="${type}-relation"]`)).not.toBeNull());
  expect(container.textContent).not.toContain('添加已有对象');
  expect([...container.querySelectorAll('button')].some(button => button.textContent === '新建零组件')).toBe(false);
  await act(async () => [...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.replace(/\s/g, '') === '新建')!.click());
  await vi.waitFor(() => expect(document.querySelector('[role="menuitem"]')).not.toBeNull());
  expect([...document.querySelectorAll('[role="menuitem"]')].map(item => item.textContent)).toEqual(['新建零组件', '新建图文档']);
  await act(async () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(item => item.textContent === (type === 'part' ? '新建零组件' : '新建图文档'))!.click());
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer input[id="ECODE"]')).not.toBeNull());
  expect(document.querySelector('.ant-modal')).toBeNull();
  expect(document.querySelector('.ant-drawer-footer')?.textContent).toContain('保存并关闭');
  await vi.waitFor(() => expect(document.querySelector<HTMLButtonElement>('.ant-drawer-footer .ant-btn-primary')!.disabled).toBe(true));
  for (const [key, value] of [['ECODE', 'NEW-CODE'], ['ENAME', '文件夹内新对象']]) {
    await act(async () => {
      const input = document.querySelector<HTMLInputElement>(`.ant-drawer input[id="${key}"]`)!;
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value);
      input.dispatchEvent(new Event('input', { bubbles: true }));
    });
  }
  await act(async () => document.querySelector<HTMLButtonElement>('.ant-drawer-close')!.click());
  await vi.waitFor(() => expect(document.querySelector('.ant-modal')?.textContent).toContain('放弃未保存的草稿？'));
  await act(async () => [...document.querySelectorAll<HTMLButtonElement>('.ant-modal button')].find(button => button.textContent === '继续编辑')!.click());
  await vi.waitFor(() => expect((document.querySelector('.ant-modal-wrap') as HTMLElement)?.style.display).toBe('none'));
  expect(document.querySelector<HTMLInputElement>('.ant-drawer input[id="ECODE"]')?.value).toBe('NEW-CODE');
  await act(async () => document.querySelector<HTMLButtonElement>('.ant-drawer-footer .ant-btn-primary')!.click());
  await vi.waitFor(() => expect(writes).toEqual([{ position: { spaceId: 'public', rootFolderId: 'root', linkIds: ['shared-link'] }, values: { ECODE: 'NEW-CODE', ENAME: '文件夹内新对象' } }]));
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer')).toBeNull());
  const row = container.querySelector(`tr[data-row-key="${type}-relation"]`)!;
  expect(row.textContent).not.toContain('打开属性');
  await act(async () => row.querySelector<HTMLButtonElement>('.np-standard-crud-cell-action')!.click());
  await vi.waitFor(() => expect(router.state.location.pathname).toBe(type === 'part' ? '/parts/same-id' : '/documents/same-id'));
  await vi.waitFor(() => expect(container.textContent).toContain(type + '-owned-properties'));
  expect(container.textContent).toContain('审计信息'); expect(container.textContent).toContain('更新账号');
  expect(container.querySelector('input,textarea')).toBeNull();
  expect(fetcher.mock.calls.some(([input]) => String(input instanceof Request ? input.url : input).includes('-relation'))).toBe(false);
  await act(async () => [...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent === '直接引用文件夹')!.click());
  await vi.waitFor(() => expect(document.querySelector('tr[data-row-key="incoming-relation"]')).not.toBeNull());
  expect(router.state.location.pathname).toBe(type === 'part' ? '/parts/same-id' : '/documents/same-id');
  const incoming = document.querySelector('tr[data-row-key="incoming-relation"]')!;
  await act(async () => [...incoming.querySelectorAll<HTMLElement>('*')].find(node => node.children.length === 0 && node.textContent === '更多')!.click());
  await vi.waitFor(() => expect(document.querySelector('[role="menuitem"]')).not.toBeNull());
  await act(async () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(node => node.textContent === '打开属性')!.click());
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer-body')?.textContent).toContain('父文件夹属性'));
  expect(document.querySelector('.ant-drawer-body')?.textContent).toContain('直接引用文件夹');
  await act(async () => document.querySelector<HTMLButtonElement>('.ant-drawer-close')!.click());
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer')).toBeNull());
  await act(async () => document.querySelector<HTMLButtonElement>('.ant-modal-close')!.click());
  await vi.waitFor(() => expect(document.querySelector('.ant-modal')).toBeNull());
  expect(container.textContent).toContain(type + '-owned-properties');
  await act(async () => router.navigate(-1));
  await vi.waitFor(() => expect(container.querySelector('.np-tree-context-header .ant-breadcrumb')?.textContent).toBe('公共空间/当前共享文件夹'));
  await vi.waitFor(() => expect(container.querySelector(`tr[data-row-key="${type}-relation"]`)).not.toBeNull());
  expect(new URLSearchParams(router.state.location.search).get('node')).toBe('public:root:shared-link');
  expect(new URLSearchParams(router.state.location.search).get('other')).toBe('keep');
});
