// @vitest-environment jsdom
import { loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import type { FrontendBehaviorBindings } from '@icp-platform/sdk';
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import * as core from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
const { default: workspace } = await loadComponentSource<{ default: FrontendBehaviorBindings }>('Workspace', 'index.ts');
const { default: part } = await loadComponentSource<{ default: FrontendBehaviorBindings }>('Part', 'index.ts');
const { default: documentModule } = await loadComponentSource<{ default: FrontendBehaviorBindings }>('Document', 'index.ts');
const partDetail = readComponentJson('Part', 'metadata/views/PartDetail.view.json');
const partList = readComponentJson('Part', 'metadata/views/PartList.view.json');
const documentDetail = readComponentJson('Document', 'metadata/views/DocumentDetail.view.json');
const documentList = readComponentJson('Document', 'metadata/views/DocumentList.view.json');
const partNavigation = readComponentJson('Part', 'metadata/navigation/Part.nav.json');
const documentNavigation = readComponentJson('Document', 'metadata/navigation/Document.nav.json');
import partModel from '../../../../../tests/Kernel.Tests/Fixtures/PartRuntime.contract.json';
import documentModel from '../../../../../tests/Kernel.Tests/Fixtures/DocumentRuntime.contract.json';
import folder from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceFolderRuntime.contract.json';
const content = readComponentJson('Workspace', 'metadata/models/WorkspaceFolderContent.model.json');
const tree = readComponentJson('Workspace', 'metadata/views/WorkspaceStructure.view.json');
const list = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderList.view.json');
const detail = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderDetail.view.json');
const incoming = readComponentJson('Workspace', 'metadata/views/WorkspaceReferencesList.view.json');
const dictionary = readComponentJson('Workspace', 'metadata/dictionaries/Workspace.dic.json');
const documentDictionary = readComponentJson('Document', 'metadata/dictionaries/Document.dic.json');
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { PageEntry } from '../composition/PageOutlet';
import { AuthProvider, useAuth } from '../contexts/AuthContext';
import { compileStandardCrudRouteProjection } from '../navigation/standardCrudRouteAdapter';
const navigation = readComponentJson('Workspace', 'metadata/navigation/Workspace.nav.json');

// 收集期装载真实模块，避免冷转换耗时进入生命周期 hook 的交互预算。
import '../composition/StandardTreePage';
import '../pages/StandardCrudListPageHost';
import '../pages/StandardCrudDetailPageHost';
await loadComponentSource('Workspace', 'workspaceRootResource.ts');
await loadComponentSource('Workspace', 'WorkspaceDocumentCreateInput.tsx');
import '@ant-design/pro-descriptions';

vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;

it.each(['receipt', 'success'] as const)('mounts the real shared content list after %s creation and discards another account’s pending content', async scenario => {
  let account = 'a';
  const writes: unknown[] = [];
  const references: unknown[] = [];
  const documentWrites: unknown[] = [];
  const cancelledUploads: string[] = [];
  let referencePresent = false;
  let failRefresh = false;
  let failInitialRead = scenario === 'receipt';
  let completeOld!: (response: Response) => void;
  const user = () => ({ id: account, ecode: account, ename: account, revision: 1, mustChangePassword: false,
    isLocalAdministrator: false, timeZoneId: 'Asia/Taipei', featurePositions: ['workspace-entry', 'workspace-create', 'workspace-createDocument', 'workspace-remove'] });
  const spaces = () => [
    { spaceId: 'public', spaceKind: 'Public', rootFolderId: 'public-root', name: '公共空间', hasChildren: scenario === 'success' && writes.length > 0 },
    { spaceId: `${account}-space`, spaceKind: 'Personal', rootFolderId: `${account}-root`, name: `个人空间-${account}` },
  ];
  const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
  const rootDetail = (id: string, name: string, description: string) => ({ id, fields: { EID: id, ENAME: name, Description: description,
    CreatedBy: 'actor', CreatedBy_DisplayValue: '创建账号', UpdatedBy: 'actor', UpdatedBy_DisplayValue: '更新账号',
    CreatedAt: '2026-10-08T00:00:00Z', CreatedAt_DisplayValue: '2026-10-08 08:00', UpdatedAt: '2026-10-08T00:00:00Z', UpdatedAt_DisplayValue: '2026-10-08 08:00' } });
  const contents = (id: string, name: string) => ({ items: [{ linkId: `${id}-link`, associatedObject: { modelKey: 'WorkspaceFolder', id },
    fields: { LID: `${id}-link`, Name: name, Code: null, ObjectType: 'WorkspaceFolder', UpdatedBy: 'actor', UpdatedBy_DisplayValue: '更新账号', UpdatedAt: '2026-10-08T00:00:00Z', UpdatedAt_DisplayValue: '2026-10-08 08:00' } }], total: 1, page: 1, pageSize: 20 });
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = input instanceof Request ? new URL(input.url).pathname : String(input);
    const method = input instanceof Request ? input.method : init?.method;
    if (path.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (path.endsWith('current-user')) return json(user());
    if (path.startsWith('/api/workspace/folders/candidates')) return json({ items: [{ id: 'shared-folder', name: '已有共享文件夹' }], total: 1, page: 1, pageSize: 20 });
    if (path.startsWith('/api/workspace/content/document-uploads/') && method === 'DELETE') {
      cancelledUploads.push(path); return new Response(null, { status: 204 });
    }
    if ((path === '/api/workspace/content/documents' && method === 'POST') || (path.startsWith('/api/workspace/content/references') && method === 'DELETE')) {
      const body = input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body));
      if (method === 'POST') {
        documentWrites.push(body);
        if (documentWrites.length === 1) return new Response(JSON.stringify({ statusCode: 409, code: 'Document.CodeDuplicated', reasonKey: 'Document.CodeDuplicated', message: '图文档编码必须唯一。',
          fieldErrors: [{ field: 'ECODE', code: 'Document.CodeDuplicated', message: '图文档编码必须唯一。' }], args: {} }), { status: 409, headers: { 'Content-Type': 'application/json' } });
      }
      references.push({ method, body });
      referencePresent = method === 'POST'; failRefresh = true;
      return json({ succeeded: true });
    }
    if (path === '/api/workspace/content/folders' && method === 'POST') {
      writes.push(input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body)));
      return json(scenario === 'success' ? rootDetail('created-folder', '创建后可读', '') : { succeeded: true });
    }
    if (method === 'POST') return json(spaces()[1]);
    if (path === '/api/workspace/spaces') return json(spaces());
    if (path.startsWith('/api/workspace/content/path?')) {
      const root = new URL(path, 'http://localhost').searchParams.get('rootFolderId')!;
      const chain = JSON.parse(new URL(path, 'http://localhost').searchParams.get('linkIds')!) as string[];
      return json({ folderId: chain.length ? 'a-folder' : root, folderIds: [root, ...chain.map(() => 'a-folder')], names: [root === 'public-root' ? '公共空间' : '个人空间', ...chain.map(() => '父A')] });
    }
    if (path.startsWith('/api/workspace/content/children?')) {
      if (scenario !== 'success' || !writes.length) return json({ items: [], total: 0, page: 1, pageSize: 0 });
      const chain = JSON.parse(new URL(path, 'http://localhost').searchParams.get('linkIds')!) as string[];
      const node = (linkId: string, id: string, name: string, hasChildren = true) => ({ linkId, associatedObject: { id }, fields: { Name: name }, hasChildren });
      return json({ items: chain.length === 0 ? [node('a', 'a-folder', '父A'), node('b', 'b-folder', '父B'), ...(referencePresent ? [node('root-shared', 'shared-folder', '已有共享文件夹')] : [])]
        : chain.length === 1 ? [node(`${chain[0]}-shared`, 'shared-folder', '共享文件夹')]
          : chain.length === 2 ? [node('same-last-link', 'same-descendant', '共享后代', false)] : [] });
    }
    if (path.startsWith('/api/links/workspace.content?')) {
      if (failInitialRead) { failInitialRead = false; return new Response('initial read unavailable', { status: 503 }); }
      const id = new URL(path, 'http://localhost').searchParams.get('anchorId');
      if (id === 'a-root') return new Promise<Response>(resolve => { completeOld = resolve; });
      if ((id === 'public-root' || id === 'a-folder') && scenario === 'success') {
        if (failRefresh) { failRefresh = false; return new Response('read unavailable', { status: 503 }); }
        return json(referencePresent ? contents('shared-folder', '已有共享文件夹') : writes.length
          ? contents('created-folder', '创建后可读') : { items: [], total: 0, page: 1, pageSize: 20 });
      }
      return json(contents(`${id}-folder`, id === 'b-root' ? 'B内容' : '公共根内容'));
    }
    if (path.endsWith('/folders/public-root-folder')) return json(rootDetail('public-root-folder', '公共对象属性', '实际说明'));
    if (path.endsWith('/root')) return json({ id: `${account}-root`, fields: { ENAME: `个人空间-${account}` } });
    throw new Error(`Unexpected request ${method} ${path}`);
  });
  vi.stubGlobal('fetch', fetcher);
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Workspace', displayName: '工作空间', version: '1' }, ...workspace },
    { metadata: { moduleId: 'Part', displayName: '零组件', version: '1' }, ...part },
    { metadata: { moduleId: 'Document', displayName: '图文档', version: '1' }, ...documentModule }], 'workspace-test');
  const views = [tree, list, detail, incoming, partDetail, partList, documentDetail, documentList];
  const navs = [{ ...navigation, navigation: { items: navigation.navigation.items.map(item => ({ ...item, featurePosition: 'workspace-entry' })) } }, partNavigation, documentNavigation];
  // 实际 AppRoutes 先编译所有模块路由，再装载组合；直接挂 PageEntry 不能替代这一步。
  const projection = compileStandardCrudRouteProjection(ModuleRegistry.getAllModules(), navs, views, []);
  expect(projection.detailEntries.map(entry => entry.path)).toEqual(['/parts/:id', '/documents/:id']);
  expect(projection.drawerRedirects).toEqual([{ from: '/parts/new', to: '/parts?drawer=create' }, { from: '/documents/new', to: '/documents?drawer=create' }]);
  expect(projection.fallbackRoutes).toEqual([]);
  function SessionSwitch() {
    const auth = useAuth();
    return <button onClick={() => { account = 'b'; void auth.retrySessionValidation(); }}>切换账号</button>;
  }
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  const catalogs = { zhs: { ...Object.fromEntries(Object.entries(dictionary.Workspace).map(([key, value]) => [`Workspace.${key}`, value.zhs])),
    ...Object.fromEntries(Object.entries(documentDictionary.Document).map(([key, value]) => [`Document.${key}`, value.zhs])) } };
  const router = createMemoryRouter([{ path: '/workspace', element:
    <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={catalogs}>
      <AuthProvider initialUser={user()}><AuthorizationProvider featurePositions={user().featurePositions}><PlatformQueryProvider scopeKey="workspace-test">
        <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Workspace', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: [
          { key: 'workspace', position: 'workspace-entry', kind: 'navigation' },
          { key: 'create', position: 'workspace-create', kind: 'action', parentPosition: 'workspace-entry', staticScope: 'folder', actionScope: 'list.pageActions' },
          { key: 'createDocument', position: 'workspace-createDocument', kind: 'action', parentPosition: 'workspace-entry', staticScope: 'folder', actionScope: 'list.pageActions' },
          { key: 'remove', position: 'workspace-remove', kind: 'action', parentPosition: 'workspace-entry', staticScope: 'folder', actionScope: 'list.rowActions' },
        ] }] }, navDocuments: navs, modelDocuments: [folder, content, partModel, documentModel], viewDocuments: views }}>
          <SessionSwitch /><PageEntry page={{ pageKey: 'platform.tree', parameters: { viewKey: tree.view.key, featurePosition: 'workspace-entry' }, inputResolverKey: 'workspace.tree.current' }} />
        </RuntimeMetadataProvider>
      </PlatformQueryProvider></AuthorizationProvider></AuthProvider>
    </LocalizationProvider>,
  }], { initialEntries: ['/workspace'] });
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); vi.unstubAllGlobals(); ModuleRegistry.replaceAll([], 'workspace-test-cleanup'); });
  await act(async () => root.render(<RouterProvider router={router} />));
  if (scenario === 'receipt') {
    await vi.waitFor(() => expect(container.textContent).toContain('列表加载失败'));
    expect(container.textContent).not.toContain('已保存，刷新失败');
    await act(async () => [...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.replace(/\s/g, '') === '重试')!.click());
  }
  await vi.waitFor(() => expect(container.textContent).toContain(scenario === 'success' ? '当前数据集为空' : '公共根内容'));
  expect(container.querySelector('.ant-tree input[placeholder]')).toBeNull();
  expect(container.querySelector('.ant-table')).not.toBeNull();
  if (scenario === 'receipt') {
    await act(async () => container.querySelector<HTMLButtonElement>('.np-standard-crud-cell-action')!.click());
    await vi.waitFor(() => expect(document.body.textContent).toContain('公共对象属性'));
    expect(fetcher.mock.calls.some(([input]) => String(input instanceof Request ? input.url : input).endsWith('/folders/public-root-folder'))).toBe(true);
    await act(async () => document.querySelector<HTMLButtonElement>('.ant-drawer-close')!.click());
  }
  const publicListReads = (anchorId = 'public-root') => fetcher.mock.calls.filter(([input]) => {
    const url = new URL(input instanceof Request ? input.url : String(input), 'http://localhost');
    return url.pathname === '/api/links/workspace.content' && url.searchParams.get('anchorId') === anchorId;
  }).length;
  const readsBeforeCreate = publicListReads();
  const openNewMenu = async () => {
    await act(async () => [...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.replace(/\s/g, '') === '新建')!.click());
    await vi.waitFor(() => expect(document.querySelector('[role="menuitem"]')).not.toBeNull());
  };
  await openNewMenu();
  const createButton = [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(item => item.textContent === '新建文件夹');
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
  if (scenario === 'success') {
    const expand = async (label: string, index = 0) => {
      const titles = [...container.querySelectorAll<HTMLElement>('.ant-tree-title')].filter(element => element.textContent === label);
      await act(async () => titles[index].closest('.ant-tree-treenode')!.querySelector<HTMLElement>('.ant-tree-switcher')!.click());
    };
    await expand('公共空间');
    await vi.waitFor(() => expect(container.querySelector('.ant-tree')?.textContent).toContain('父A'));
    await expand('父A'); await expand('父B');
    await vi.waitFor(() => expect([...container.querySelectorAll('.ant-tree-title')].filter(element => element.textContent === '共享文件夹')).toHaveLength(2));
    await expand('共享文件夹', 0); await expand('共享文件夹', 1);
    await vi.waitFor(() => expect([...container.querySelectorAll('.ant-tree-title')].filter(element => element.textContent === '共享后代')).toHaveLength(2));
    const loadedReads = () => fetcher.mock.calls.filter(([input]) => String(input instanceof Request ? input.url : input).startsWith('/api/workspace/content/children?')).length;
    const beforeAddReads = loadedReads();
    await act(async () => [...container.querySelectorAll<HTMLElement>('.ant-tree-title')].find(item => item.textContent === '父A')!.click());
    await vi.waitFor(() => expect(container.querySelector('.np-tree-context-header h2')?.textContent).toBe('父A'));
    const folderListReads = () => publicListReads('a-folder');
    await openNewMenu();
    const createObject = [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(item => item.textContent === '新建图文档');
    expect(createObject).toBeDefined(); await act(async () => createObject!.click());
    await vi.waitFor(() => expect(document.querySelector('.ant-drawer input[id="ECODE"]')).not.toBeNull());
    for (const [key, value] of [['ECODE', 'NEW-DOC'], ['ENAME', '新建图文档']]) {
      await act(async () => {
        const input = document.querySelector<HTMLInputElement>(`.ant-drawer input[id="${key}"]`)!;
        Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value);
        input.dispatchEvent(new Event('input', { bubbles: true }));
      });
    }
    const uploadId = 'FileUploadLease_0123456789abcdef0123456789abcdef';
    const attachmentId = 'FileUploadLease_1123456789abcdef0123456789abcdef';
    const upload = vi.spyOn(core, 'platformUpload').mockImplementation(async (_path, body) => {
      const attachment = (body as FormData).get('file') instanceof File && ((body as FormData).get('file') as File).name === 'attachment.pdf';
      return new Response(JSON.stringify({ uploadId: attachment ? attachmentId : uploadId,
        fileId: 'FileRecord_internal', fileName: attachment ? 'attachment.pdf' : 'main.pdf', length: 4, contentType: 'application/pdf', expiresAt: '2026-10-15T00:00:00Z' }));
    });
    onTestFinished(() => upload.mockRestore());
    await act(async () => {
      const input = document.querySelector<HTMLInputElement>('.ant-drawer input[type="file"]')!;
      Object.defineProperty(input, 'files', { configurable: true, value: [new File(['main'], 'main.pdf', { type: 'application/pdf' })] });
      input.dispatchEvent(new Event('change', { bubbles: true }));
    });
    await vi.waitFor(async () => { await act(async () => undefined); expect(document.body.textContent).toContain('主文件已上传'); });
    await act(async () => {
      const input = document.querySelectorAll<HTMLInputElement>('.ant-drawer input[type="file"]')[1];
      Object.defineProperty(input, 'files', { configurable: true, value: [new File(['attachment'], 'attachment.pdf', { type: 'application/pdf' })] });
      input.dispatchEvent(new Event('change', { bubbles: true }));
    });
    await vi.waitFor(async () => {
      await act(async () => undefined);
      expect(document.querySelector<HTMLButtonElement>('.ant-drawer-footer .ant-btn-primary')!.disabled).toBe(false);
    });
    const confirm = document.querySelector<HTMLButtonElement>('.ant-drawer-footer .ant-btn-primary')!;
    const beforeConflictReads = folderListReads();
    await act(async () => { confirm.click(); confirm.click(); });
    await vi.waitFor(() => expect(documentWrites).toHaveLength(1));
    await act(async () => undefined);
    expect(document.querySelector('.ant-drawer input[id="ECODE"]')).not.toBeNull();
    expect(document.body.textContent).toContain('图文档编码必须唯一。');
    expect(document.body.textContent).toContain('主文件已上传');
    expect(document.body.textContent).toContain('附件已上传');
    expect(cancelledUploads).toEqual([]);
    expect(folderListReads()).toBe(beforeConflictReads);
    expect(upload).toHaveBeenCalledTimes(2);
    expect(references).toEqual([]);
    await act(async () => {
      const input = document.querySelector<HTMLInputElement>('.ant-drawer input[id="ECODE"]')!;
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, 'CORRECTED-DOC');
      input.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await vi.waitFor(async () => {
      await act(async () => undefined);
      expect(document.querySelector<HTMLButtonElement>('.ant-drawer-footer .ant-btn-primary')!.disabled).toBe(false);
    });
    await act(async () => document.querySelector<HTMLButtonElement>('.ant-drawer-footer .ant-btn-primary')!.click());
    await vi.waitFor(() => expect(references).toHaveLength(1));
    expect(documentWrites).toEqual([
      { position: { spaceId: 'public', rootFolderId: 'public-root', linkIds: ['a'] }, values: { ECODE: 'NEW-DOC', ENAME: '新建图文档' }, uploads: { mainUploadId: uploadId, attachmentUploadIds: [attachmentId] } },
      { position: { spaceId: 'public', rootFolderId: 'public-root', linkIds: ['a'] }, values: { ECODE: 'CORRECTED-DOC', ENAME: '新建图文档' }, uploads: { mainUploadId: uploadId, attachmentUploadIds: [attachmentId] } },
    ]);
    expect(cancelledUploads).toEqual([]);
    await vi.waitFor(() => expect(folderListReads()).toBe(beforeConflictReads + 1));
    await vi.waitFor(() => expect(loadedReads()).toBeGreaterThanOrEqual(beforeAddReads + 5));
    expect(references[0]).toEqual({ method: 'POST', body: { position: { spaceId: 'public', rootFolderId: 'public-root', linkIds: ['a'] },
      values: { ECODE: 'CORRECTED-DOC', ENAME: '新建图文档' }, uploads: { mainUploadId: uploadId, attachmentUploadIds: [attachmentId] } } });
    expect(upload).toHaveBeenCalledTimes(2);
    await vi.waitFor(() => expect(container.textContent).toContain('已保存，刷新失败'));
    await act(async () => [...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.replace(/\s/g, '') === '刷新')!.click());
    await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="shared-folder-link"]')).not.toBeNull());
    expect(container.textContent).not.toContain('已保存，刷新失败');
    expect(references).toHaveLength(1);
    await act(async () => [...container.querySelectorAll<HTMLElement>('.ant-table *')].find(element => element.children.length === 0 && element.textContent === '更多')!.click());
    await vi.waitFor(() => expect(document.body.textContent).toContain('移除引用'));
    await act(async () => [...document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(element => element.textContent === '移除引用')!.click());
    await vi.waitFor(() => expect(document.body.textContent).toContain('移除当前文件夹中的这条引用？'));
    await act(async () => document.querySelector<HTMLButtonElement>('.ant-modal-footer .ant-btn-primary')!.click());
    await vi.waitFor(() => expect(references).toHaveLength(2));
    expect(references[1]).toEqual({ method: 'DELETE', body: { position: { spaceId: 'public', rootFolderId: 'public-root', linkIds: ['a'] } } });
    await vi.waitFor(() => expect(container.textContent).toContain('已保存，刷新失败'));
    await act(async () => [...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.replace(/\s/g, '') === '刷新')!.click());
    await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="shared-folder-link"]')).toBeNull());
    await vi.waitFor(() => expect(container.textContent).not.toContain('已保存，刷新失败'));
    expect(references).toHaveLength(2);
  }
  await act(async () => [...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.replace(/\s/g, '') === '刷新')!.click());
  expect(writes).toHaveLength(1);
  expect([...container.querySelectorAll('.ant-tree-title')].map(node => node.textContent).filter(label => label === '公共空间' || label === '个人空间')).toEqual(['公共空间', '个人空间']);
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
