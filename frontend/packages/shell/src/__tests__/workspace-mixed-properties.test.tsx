import { workspaceTreeRoots, workspaceTreePath, workspaceTreeChildren } from '../test-support/workspaceTreeFixtures';
// @vitest-environment jsdom
import { loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import type { FrontendBehaviorBindings, StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider, useParams } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import * as core from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
const { default: workspace } = await loadComponentSource<{ default: FrontendBehaviorBindings }>('Workspace', 'index.ts');
const { default: part } = await loadComponentSource<{ default: FrontendBehaviorBindings }>('Part', 'index.ts');
const { default: documentModule } = await loadComponentSource<{ default: FrontendBehaviorBindings }>('Document', 'index.ts');
const { partResource } = await loadComponentSource<{ partResource: StandardCrudResourceRegistration }>('Part', 'partResource.ts');
const { documentResource } = await loadComponentSource<{ documentResource: StandardCrudResourceRegistration }>('Document', 'documentResource.ts');
import folder from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceFolderRuntime.contract.json';
import partModel from '../../../../../tests/Kernel.Tests/Fixtures/PartRuntime.contract.json';
import documentModel from '../../../../../tests/Kernel.Tests/Fixtures/DocumentRuntime.contract.json';
import workspaceWrites from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceClipboardDataAccess.contract.json';
import workspaceHistoryFeatures from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceHistoryFeatures.contract.json';
import partHistoryFeatures from '../../../../../tests/Kernel.Tests/Fixtures/PartHistoryFeatures.contract.json';
import documentHistoryFeatures from '../../../../../tests/Kernel.Tests/Fixtures/DocumentHistoryFeatures.contract.json';
import notificationFeatures from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceTransferNotificationFeatures.contract.json';
const workspaceEntry = workspaceWrites.features.find(point => point.kind === 'navigation' && point.key === 'workspace')!;
const notifications = readComponentJson('Workspace', 'metadata/views/WorkspaceTransferNotificationsList.view.json');
const historyFeatures = { Workspace: workspaceHistoryFeatures, Part: partHistoryFeatures, Document: documentHistoryFeatures };
const historyViews = ['Workspace', 'Part', 'Document'].map(owner => readComponentJson(owner, `metadata/views/${owner === 'Workspace' ? 'WorkspaceObjectHistoryList' : owner + 'ObjectHistoryList'}.view.json`));
const historyDictionaries = [['Audit', 'ObjectHistory'], ['Part', 'PartHistory'], ['Document', 'DocumentHistory']].map(([owner, name]) => [readComponentJson(owner, `metadata/dictionaries/${name}.dic.json`)[name], name] as const);
const content = readComponentJson('Workspace', 'metadata/models/WorkspaceFolderContent.model.json');
const workspaceNavigation = readComponentJson('Workspace', 'metadata/navigation/Workspace.nav.json');
const tree = readComponentJson('Workspace', 'metadata/views/WorkspaceStructure.view.json');
const list = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderList.view.json');
const references = readComponentJson('Workspace', 'metadata/views/WorkspaceReferencesList.view.json');
const detail = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderDetail.view.json');
const partList = readComponentJson('Part', 'metadata/views/PartList.view.json');
const partDetail = readComponentJson('Part', 'metadata/views/PartDetail.view.json');
const documentList = readComponentJson('Document', 'metadata/views/DocumentList.view.json');
const documentDetail = readComponentJson('Document', 'metadata/views/DocumentDetail.view.json');
const partNavigation = readComponentJson('Part', 'metadata/navigation/Part.nav.json');
const documentNavigation = readComponentJson('Document', 'metadata/navigation/Document.nav.json');
const dictionary = readComponentJson('Workspace', 'metadata/dictionaries/Workspace.dic.json');
const partDictionary = readComponentJson('Part', 'metadata/dictionaries/Part.dic.json');
const documentDictionary = readComponentJson('Document', 'metadata/dictionaries/Document.dic.json');
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';
import { PageEntry } from '../composition/PageOutlet';
import { RuntimeMetadataProvider } from './formalRuntimeMetadataProvider';
import { formalDataAccessFor } from './formalRuntimeMetadataTestSupport';
import { AuthProvider } from '../contexts/AuthContext';
import { FeaturePlacementContext } from '../authorization/FeaturePlacementContext';
import { compileStandardCrudRouteProjection } from '../navigation/standardCrudRouteAdapter';
import { buildMenusFromNavDocuments } from '../metadata/navigationAdapter';

// 收集期装载真实模块，避免冷转换耗时进入生命周期 hook 的交互预算。
import '../composition/StandardTreePage';
import '../pages/StandardCrudListPageHost';
import '../pages/StandardCrudListDrawerRuntime';
import '../pages/StandardCrudListOverlayRuntime';
import '@ant-design/pro-descriptions';
await loadComponentSource('Workspace', 'WorkspaceObjectEditInput.tsx');
await loadComponentSource('Workspace', 'WorkspaceDocumentCreateInput.tsx');

vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;

async function renderMixedProperties(type: 'part' | 'document') {
  const deletePoints = workspaceWrites.features.filter(point => point.key === 'delete' && point.actionScope === 'list.rowActions'
    || point.key === 'deleteObjects' && point.actionScope === 'list.pageActions');
  const present = new Set(['part', 'document']);
  const deletes: { path: string; body: unknown; button: string | null; feature: string | null }[] = [];
  const user = { id: 'actor', ecode: 'actor', ename: '账号', revision: 1, mustChangePassword: false, isLocalAdministrator: false,
    timeZoneId: 'Asia/Taipei', featurePositions: [workspaceEntry.position, 'workspace-edit', 'workspace-createPart', 'workspace-createDocument', 'part-entry', 'document-entry', ...deletePoints.map(point => point.position)] };
  const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
  const writes: unknown[] = [];
  const edits: unknown[] = [];
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(input instanceof Request ? input.url : String(input), 'http://localhost');
    if (url.pathname === '/api/links/workspace.content') {
      const source = url.searchParams.get('direction') === 'reverse' ? 'workspace.references.list' : 'workspace.folder.list';
      expect(new Headers(input instanceof Request ? input.headers : init?.headers).get('X-NewP-Data-Declaration'))
        .toBe(formalDataAccessFor(source, 'read').id);
    }
    if (url.pathname.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (url.pathname.endsWith('current-user')) return json(user);
    if (url.pathname.endsWith('/personal/ensure')) return json({ initialized: true });
    if (url.pathname === '/api/trees/workspace.structure/roots') return json(workspaceTreeRoots([
      { spaceId: 'public', spaceKind: 'Public', rootFolderId: 'root', name: '公共空间', hasChildren: true },
      { spaceId: 'personal', spaceKind: 'Personal', rootFolderId: 'personal-root', name: '个人空间' },
    ]));
    if (url.pathname.endsWith('/path')) return json(workspaceTreePath(url.searchParams.get('nodeKey')!, ['root', 'shared'], ['公共空间', '当前共享文件夹']));
    if (url.pathname.endsWith('/children')) return json(workspaceTreeChildren(url.searchParams.get('nodeKey')!, [{ linkId: 'shared-link', associatedObject: { id: 'shared' }, fields: { Name: '当前共享文件夹' }, hasChildren: false }]));
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
      expect(url.searchParams.get('anchorModelKey')).toBe(type === 'part' ? 'Part' : 'Document');
      expect(url.searchParams.get('anchorId')).toBe('same-id');
      expect(url.searchParams.has('queryScope')).toBe(false);
      expect(new Headers(input instanceof Request ? input.headers : init?.headers).get('X-NewP-Feature')).toBe(workspaceEntry.position);
      return json({ items: [{ linkId: 'incoming-relation', associatedObject: { modelKey: 'WorkspaceFolder', id: 'parent-folder' },
        fields: { LID: 'incoming-relation', Name: '直接父文件夹', UpdatedBy: 'actor', UpdatedBy_DisplayValue: '更新账号', UpdatedAt: '2026-10-08T00:00:00Z', UpdatedAt_DisplayValue: '2026-10-08 08:00' } }], total: 1, page: 1, pageSize: 20 });
    }
    if (url.pathname === '/api/workspace/folders/parent-folder') {
      expect(new Headers(input instanceof Request ? input.headers : init?.headers).get('X-NewP-Data-Declaration'))
        .toBe(formalDataAccessFor('workspace.folder.detail', 'read').id);
    }
    if (url.pathname === '/api/workspace/folders/parent-folder') return json({ fields: { EID: 'parent-folder', ENAME: '父文件夹属性', Description: '只开对象不定位路径',
      CreatedBy: 'actor', CreatedBy_DisplayValue: '创建账号', UpdatedBy: 'actor', UpdatedBy_DisplayValue: '更新账号',
      CreatedAt: '2026-10-08T00:00:00Z', CreatedAt_DisplayValue: '2026-10-08 08:00', UpdatedAt: '2026-10-08T00:00:00Z', UpdatedAt_DisplayValue: '2026-10-08 08:00' } });
    if (url.pathname === '/api/links/workspace.content') return json({ items: ['part', 'document'].filter(owner => present.has(owner)).map(owner => ({ linkId: owner + '-relation',
      associatedObject: { modelKey: owner === 'part' ? 'Part' : 'Document', id: 'same-id' },
      fields: { LID: owner + '-relation', ObjectType: owner === 'part' ? 'Part' : 'Document',
        ObjectType_DisplayValue: owner === 'part' ? '零组件' : '图文档', Name: owner + '-name', Code: 'SAME-CODE' } })), total: present.size, page: 1, pageSize: 20 });
    if (url.pathname === `/api/${type}/${type === 'part' ? 'parts' : 'documents'}/same-id`) {
      const headers = new Headers(input instanceof Request ? input.headers : init?.headers);
      expect(headers.get('X-NewP-Feature')).toBe(type + '-entry');
      return json({ fields: { EID: 'same-id', ECODE: 'SAME-CODE', ENAME: type + '-owned-properties', Description: '所属对象说明',
        CreatedBy: 'actor', CreatedBy_DisplayValue: '创建账号', UpdatedBy: 'actor', UpdatedBy_DisplayValue: '更新账号',
        CreatedAt: '2026-10-08T00:00:00Z', CreatedAt_DisplayValue: '2026-10-08 08:00', UpdatedAt: '2026-10-08T00:00:00Z', UpdatedAt_DisplayValue: '2026-10-08 08:00' }, summaries: {} });
    }
    const method = input instanceof Request ? input.method : init?.method ?? 'GET';
    if (method === 'DELETE' && url.pathname === '/api/workspace/content/part-relation' || method === 'POST' && url.pathname === '/api/workspace/content/delete') {
      const body = input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body));
      const headers = new Headers(input instanceof Request ? input.headers : init?.headers);
      expect(headers.has('X-NewP-Data-Declaration')).toBe(false);
      deletes.push({ path: url.pathname, body, button: headers.get('X-NewP-Button'), feature: headers.get('X-NewP-Feature') });
      for (const id of method === 'DELETE' ? ['part-relation'] : body.linkIds as string[]) present.delete(id.split('-')[0]);
      return json({ succeeded: true });
    }
    if (url.pathname === `/api/workspace/content/${type}-relation/edit-values` || url.pathname === `/api/workspace/content/${type}-relation` && method === 'PUT') {
      const headers = new Headers(input instanceof Request ? input.headers : init?.headers);
      expect(headers.get('X-NewP-Button')).toBe(formalDataAccessFor('workspace.folder.list', 'rowActions', 'edit').id);
      expect(headers.has('X-NewP-Data-Declaration')).toBe(false);
    }
    if (url.pathname === `/api/workspace/content/${type}-relation/edit-values`) return json({ fields: { ECODE: 'SAME-CODE', ENAME: 'editable-name', Description: '原说明' } });
    if (url.pathname === `/api/workspace/content/${type}-relation` && (input instanceof Request ? input.method : init?.method) === 'PUT') {
      edits.push(input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body)));
      return json({ succeeded: true });
    }
    throw new Error(`Unexpected read ${url.pathname}`);
  });
  vi.stubGlobal('fetch', fetcher);
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Workspace', displayName: '工作空间', version: '1' }, ...workspace },
    { metadata: { moduleId: 'Part', displayName: '零组件', version: '1' }, ...part },
    { metadata: { moduleId: 'Document', displayName: '图文档', version: '1' }, ...documentModule }], 'workspace-mixed');
  const navs = [partNavigation, documentNavigation].map((navigation, index) => ({ ...navigation, navigation: { items: navigation.navigation.items.map(item => ({ ...item, featurePosition: item.key === (index ? 'document.history' : 'part.history') ? historyFeatures[index ? 'Document' : 'Part'].historyPosition : index ? 'document-entry' : 'part-entry' })) } }));
  const views = [tree, list, detail, references, partList, partDetail, documentList, documentDetail, ...historyViews, notifications];
  const workspaceNav = { ...workspaceNavigation, navigation: { items: workspaceNavigation.navigation.items.map(item => ({ ...item, featurePosition: item.key === 'workspace.history' ? historyFeatures.Workspace.historyPosition : item.key === 'workspace.transfers' ? notificationFeatures.notificationsPosition : workspaceEntry.position })) } };
  expect(buildMenusFromNavDocuments([workspaceNav, ...navs], (title, fallback) => title ?? fallback ?? '').map(menu => menu.path)).toEqual(['/workspace']);
  expect(compileStandardCrudRouteProjection(ModuleRegistry.getAllModules(), [workspaceNavigation, ...navs], views, []).detailEntries).toHaveLength(2);
  const catalogs = { zhs: Object.fromEntries([[dictionary.Workspace, 'Workspace'], [partDictionary.Part, 'Part'], [documentDictionary.Document, 'Document'], ...historyDictionaries].flatMap(([dict, key]) =>
    Object.entries(dict).map(([name, value]) => [`${key}.${name}`, (value as { zhs: string }).zhs]))) };
  function OwnedPage() { const { id } = useParams(); return <FeaturePlacementContext.Provider value={{ entryPosition: type + '-entry' }}>
    <StandardCrudDetailPageHost viewKey={type === 'part' ? partDetail.view.key : documentDetail.view.key} id={id ?? null} registration={type === 'part' ? partResource : documentResource} />
  </FeaturePlacementContext.Provider>; }
  const wrap = (page: React.ReactNode) => <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={catalogs}>
    <AuthProvider initialUser={user}><AuthorizationProvider featurePositions={user.featurePositions}><PlatformQueryProvider scopeKey="workspace-mixed">
      <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Workspace', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: [
        { key: 'workspace', position: workspaceEntry.position, kind: 'navigation' },
        // 新目录采用实际编译事实，原交互位置及actor grants不替换、不扩history资格。
        { key: 'workspace', position: historyFeatures.Workspace.readPosition, kind: 'navigation' },
        { key: 'workspace.history', position: historyFeatures.Workspace.historyPosition, kind: 'navigation', parentPosition: historyFeatures.Workspace.readPosition },
        { key: 'workspace.transfers', position: notificationFeatures.notificationsPosition, kind: 'navigation', parentPosition: notificationFeatures.readPosition },
        ...deletePoints, { key: 'edit', position: 'workspace-edit', kind: 'action', parentPosition: workspaceEntry.position, staticScope: 'folder', actionScope: 'list.rowActions' }, ...['createPart', 'createDocument'].map(key => ({ key, position: 'workspace-' + key, kind: 'action', parentPosition: workspaceEntry.position, staticScope: 'folder', actionScope: 'list.pageActions' })),
      ] }, ...['Part', 'Document'].map(moduleId => ({ moduleId, metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: [{ key: moduleId === 'Part' ? 'part.parts' : 'document.documents', kind: 'navigation', position: moduleId.toLowerCase() + '-entry' },
        { key: moduleId === 'Part' ? 'part.parts' : 'document.documents', kind: 'navigation', position: historyFeatures[moduleId as 'Part' | 'Document'].readPosition },
        { key: moduleId === 'Part' ? 'part.history' : 'document.history', kind: 'navigation', position: historyFeatures[moduleId as 'Part' | 'Document'].historyPosition, parentPosition: historyFeatures[moduleId as 'Part' | 'Document'].readPosition }] }))] },
      navDocuments: [workspaceNav, ...navs], modelDocuments: [folder, content, partModel, documentModel], viewDocuments: views }}>
        {page}
      </RuntimeMetadataProvider>
    </PlatformQueryProvider></AuthorizationProvider></AuthProvider>
  </LocalizationProvider>;
  const router = createMemoryRouter([{ path: '/workspace', element: wrap(<PageEntry page={{ pageKey: 'platform.tree', parameters: { viewKey: tree.view.key, featurePosition: workspaceEntry.position }, inputResolverKey: 'workspace.tree.current' }} />) },
    { path: type === 'part' ? '/parts/:id' : '/documents/:id', element: wrap(<OwnedPage />) }], { initialEntries: ['/workspace?node=public%3Aroot%3Ashared-link&other=keep'] });
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); vi.unstubAllGlobals(); ModuleRegistry.replaceAll([], 'workspace-mixed-cleanup'); });
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(container.querySelector(`tr[data-row-key="${type}-relation"]`)).not.toBeNull());

  return { container, router, fetcher, writes, edits, deletes, deletePoints };
}

it.each(['part', 'document'] as const)('creates %s with draft confirmation and one formal save', async type => {
  const uploadId = 'FileUploadLease_0123456789abcdef0123456789abcdef';
  const upload = type === 'document' ? vi.spyOn(core, 'platformUpload').mockImplementation(async (path, body, options) => {
    expect(path).toBe('/api/workspace/content/document-uploads');
    expect(JSON.parse(String(body.get('position')))).toEqual({ spaceId: 'public', rootFolderId: 'root', linkIds: ['shared-link'] });
    expect(JSON.parse(String(body.get('values')))).toEqual({ ECODE: 'NEW-CODE', ENAME: '文件夹内新对象' });
    expect(new Headers(options?.headers).get('X-NewP-Feature')).toBe('workspace-createDocument');
    expect(body.get('file')).toBeInstanceOf(File);
    return new Response(JSON.stringify({ uploadId, fileId: 'FileRecord_internal', fileName: 'main.pdf', length: 4,
      contentType: 'application/pdf', expiresAt: '2026-10-14T00:00:00Z' }));
  }) : undefined;
  onTestFinished(() => upload?.mockRestore());
  const { container, writes } = await renderMixedProperties(type);
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
  if (type === 'document') {
    await act(async () => {
      const input = document.querySelector<HTMLInputElement>('.ant-drawer input[type="file"]')!;
      Object.defineProperty(input, 'files', { configurable: true, value: [new File(['main'], 'main.pdf', { type: 'application/pdf' })] });
      input.dispatchEvent(new Event('change', { bubbles: true }));
    });
    await vi.waitFor(async () => {
      await act(async () => undefined);
      expect(document.querySelector<HTMLButtonElement>('.ant-drawer-footer .ant-btn-primary')!.disabled).toBe(false);
    });
  }
  await act(async () => document.querySelector<HTMLButtonElement>('.ant-drawer-footer .ant-btn-primary')!.click());
  await vi.waitFor(() => expect(writes).toEqual([{ position: { spaceId: 'public', rootFolderId: 'root', linkIds: ['shared-link'] },
    values: { ECODE: 'NEW-CODE', ENAME: '文件夹内新对象' }, ...(type === 'document' ? { uploads: { mainUploadId: uploadId } } : {}) }]));
  if (upload) expect(upload).toHaveBeenCalledTimes(1);
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer')).toBeNull());
});

it.each(['part', 'document'] as const)('opens the owning %s properties and direct references then restores its occurrence', async type => {
  const { container, router, fetcher } = await renderMixedProperties(type);
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

it.each(['part', 'document'] as const)('edits %s through the current occurrence and preserves the owning input', async type => {
  const { container, edits } = await renderMixedProperties(type);
  const editableRow = container.querySelector(`tr[data-row-key="${type}-relation"]`)!;
  await act(async () => [...editableRow.querySelectorAll<HTMLButtonElement>('button')].find(button => button.getAttribute('aria-label') === '更多')!.click());
  await act(async () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(item => item.textContent === '编辑')!.click());
  await vi.waitFor(() => expect(document.querySelector<HTMLInputElement>('.ant-drawer input[id="ENAME"]')?.value).toBe('editable-name'));
  expect(document.querySelector<HTMLTextAreaElement>('.ant-drawer textarea')?.value).toBe('原说明');
  await act(async () => {
    const input = document.querySelector<HTMLInputElement>('.ant-drawer input[id="ENAME"]')!;
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, '编辑后的名称');
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
  await act(async () => document.querySelector<HTMLButtonElement>('.ant-drawer-footer .ant-btn-primary')!.click());
  await vi.waitFor(() => expect(edits).toEqual([{ position: { spaceId: 'public', rootFolderId: 'root', linkIds: ['shared-link'] },
    values: { ECODE: 'SAME-CODE', ENAME: '编辑后的名称', Description: '原说明' } }]));
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer')).toBeNull());
});


it.each(['single', 'mixed'] as const)('deletes the actual %s selection through its own compiled button and one confirmation', async mode => {
  const { container, deletes, deletePoints } = await renderMixedProperties('part');
  const row = container.querySelector('tr[data-row-key="part-relation"]')!;
  if (mode === 'single') {
    await act(async () => row.querySelector<HTMLButtonElement>('button[aria-label="更多"]')!.click());
  } else {
    for (const key of ['part', 'document']) await act(async () => container.querySelector<HTMLInputElement>(`tr[data-row-key="${key}-relation"] input[type="checkbox"]`)!.click());
    await act(async () => [...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.replace(/\s/g, '') === '移除与删除')!.click());
  }
  await vi.waitFor(() => expect([...document.querySelectorAll('[role="menuitem"]')].some(item => item.textContent === '删除')).toBe(true));
  await act(async () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(item => item.textContent === '删除')!.click());
  await vi.waitFor(() => expect(document.querySelector('[role="dialog"]')?.textContent).toContain('所有文件夹中的引用'));
  expect(deletes).toHaveLength(0);
  await act(async () => [...document.querySelectorAll<HTMLButtonElement>('[role="dialog"] button')].find(button => button.textContent?.replace(/\s/g, '') === '确认')!.click());
  await vi.waitFor(() => expect(deletes).toHaveLength(1));
  const key = mode === 'single' ? 'delete' : 'deleteObjects';
  const declaration = formalDataAccessFor('workspace.folder.list', mode === 'single' ? 'rowActions' : 'pageActions', key);
  expect(deletes[0]).toEqual({ path: mode === 'single' ? '/api/workspace/content/part-relation' : '/api/workspace/content/delete',
    body: { position: { spaceId: 'public', rootFolderId: 'root', linkIds: ['shared-link'] }, ...(mode === 'mixed' ? { linkIds: ['part-relation', 'document-relation'] } : {}) },
    button: declaration.id, feature: deletePoints.find(point => point.key === key)!.position });
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="part-relation"]')).toBeNull());
  expect(container.querySelector('tr[data-row-key="document-relation"]') === null).toBe(mode === 'mixed');
});
