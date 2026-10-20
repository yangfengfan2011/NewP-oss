// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import * as core from '@icp-platform/core';
import { ModuleRegistry, type FrontendBehaviorBindings } from '@icp-platform/sdk';
import { loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import { workspaceTreeRoots, workspaceTreePath, workspaceTreeChildren, workspaceTreePosition } from '../test-support/workspaceTreeFixtures';
import folder from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceFolderRuntime.contract.json';
import compiled from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceClipboardDataAccess.contract.json';
import { RuntimeMetadataProvider } from './formalRuntimeMetadataProvider';
import { formalDataAccessFor } from './formalRuntimeMetadataTestSupport';
import { PageEntry } from '../composition/PageOutlet';
import '../composition/StandardTreePage';
import '../pages/StandardCrudListPageHost';
import '../pages/StandardCrudListDrawerRuntime';
const { default: workspace } = await loadComponentSource<{ default: FrontendBehaviorBindings }>('Workspace', 'index.ts');
await loadComponentSource('Workspace', 'WorkspaceObjectCreateInput.tsx');
await loadComponentSource('Workspace', 'WorkspaceDocumentCreateInput.tsx');
const content = readComponentJson('Workspace', 'metadata/models/WorkspaceFolderContent.model.json');
const tree = readComponentJson('Workspace', 'metadata/views/WorkspaceStructure.view.json');
const list = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderList.view.json');
const detail = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderDetail.view.json');
const dictionary = readComponentJson('Workspace', 'metadata/dictionaries/Workspace.dic.json');

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

it.each(['Part', 'Document'] as const)('carries the compiled %s creation source from the actual Workspace PageEntry and drawer', async type => {
  const actionKey = type === 'Part' ? 'createPart' : 'createDocument';
  const entry = compiled.features.find(point => point.kind === 'navigation' && point.key === 'workspace')!;
  const create = compiled.features.find(point => point.key === actionKey && point.actionScope === 'list.pageActions')!;
  const declaration = formalDataAccessFor('workspace.folder.list', 'pageActions', actionKey);
  expect(declaration.featurePositions).toContain(create.position);
  const writes: { body: unknown; declaration: string | null; position: string | null }[] = [];
  const json = (body: unknown) => new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } });
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(input instanceof Request ? input.url : String(input), 'http://localhost');
    const method = input instanceof Request ? input.method : init?.method ?? 'GET';
    if (url.pathname.includes('antiforgery-token')) return json({ requestToken: 'test' });
    if (url.pathname.endsWith('/personal/ensure')) return json({ initialized: true });
    if (url.pathname.endsWith('/roots')) return json(workspaceTreeRoots([
      { spaceId: 'public', spaceKind: 'Public', rootFolderId: 'root', name: '公共空间', hasChildren: true },
      { spaceId: 'mine', spaceKind: 'Personal', rootFolderId: 'mine-root', name: '个人空间', hasChildren: false },
    ]));
    if (url.pathname.endsWith('/children')) return json(workspaceTreeChildren(url.searchParams.get('nodeKey')!,
      workspaceTreePosition(url.searchParams.get('nodeKey')!).linkIds.length ? []
        : [{ linkId: 'parent-link', associatedObject: { id: 'parent-eid' }, fields: { Name: '组合父目录' }, hasChildren: false }]));
    if (url.pathname.endsWith('/path')) {
      const key = url.searchParams.get('nodeKey')!; const nested = workspaceTreePosition(key).linkIds.length > 0;
      return json(workspaceTreePath(key, nested ? ['root', 'parent-eid'] : ['root'], nested ? ['公共空间', '组合父目录'] : ['公共空间']));
    }
    if (url.pathname === '/api/links/workspace.content') return json({ items: [], total: 0, page: 1, pageSize: 20 });
    if (url.pathname === '/api/workspace/content/' + (type === 'Part' ? 'parts' : 'documents') && method === 'POST') {
      const headers = new Headers(input instanceof Request ? input.headers : init?.headers);
      writes.push({ body: input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body)),
        declaration: headers.get(type === 'Part' ? 'X-NewP-Button' : 'X-NewP-Data-Declaration'), position: headers.get('X-NewP-Feature') });
      return json({ succeeded: true });
    }
    throw new Error(`Unexpected request ${method} ${url.pathname}`);
  });
  const uploadId = 'FileUploadLease_11111111111111111111111111111111';
  const upload = vi.spyOn(core, 'platformUpload').mockImplementation(async (_path, body, options) => {
    const headers = new Headers(options?.headers);
    expect(headers.get('X-NewP-Data-Declaration')).toBe(declaration.id);
    expect(headers.get('X-NewP-Feature')).toBe(create.position);
    expect(JSON.parse(String((body as FormData).get('position')))).toEqual({ spaceId: 'public', rootFolderId: 'root', linkIds: ['parent-link'] });
    return json({ uploadId, fileId: 'FileRecord_private', fileName: 'main.pdf', length: 4, contentType: 'application/pdf', expiresAt: '2099-01-01T00:00:00Z' });
  });
  vi.stubGlobal('fetch', fetcher);
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Workspace', displayName: 'Workspace', version: '1' }, ...workspace }], 'object-create315');
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const router = createMemoryRouter([{ path: '/', element:
    <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]}
      catalogs={{ zhs: Object.fromEntries(Object.entries(dictionary.Workspace).map(([key, value]) => [`Workspace.${key}`, value.zhs])) }}>
      <AuthorizationProvider featurePositions={[entry.position, create.position]}><PlatformQueryProvider scopeKey="object-create315">
        <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Workspace', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: [entry, create] }] },
          navDocuments: [], modelDocuments: [folder, content], viewDocuments: [tree, list, detail] }}>
          <PageEntry page={{ pageKey: 'platform.tree', parameters: { viewKey: tree.view.key, featurePosition: entry.position }, inputResolverKey: 'workspace.tree.current' }} />
        </RuntimeMetadataProvider>
      </PlatformQueryProvider></AuthorizationProvider>
    </LocalizationProvider> }], { initialEntries: ['/'] });
  try {
    await act(async () => root.render(<RouterProvider router={router} />));
    await vi.waitFor(() => expect(container.querySelector('.ant-tree-switcher:not(.ant-tree-switcher-noop)')).not.toBeNull());
    await act(async () => container.querySelector<HTMLElement>('.ant-tree-switcher:not(.ant-tree-switcher-noop)')!.click());
    await vi.waitFor(() => expect([...container.querySelectorAll('.ant-tree-title')].some(item => item.textContent === '组合父目录')).toBe(true));
    await act(async () => [...container.querySelectorAll<HTMLElement>('.ant-tree-title')].find(item => item.textContent === '组合父目录')!.click());
    await vi.waitFor(() => expect(container.querySelector('.np-tree-context-header [aria-current="page"]')?.textContent).toBe('组合父目录'));
    await act(async () => [...container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.replace(/\s/g, '') === '新建')!.click());
    await vi.waitFor(() => expect(document.querySelector('[role="menuitem"]')).not.toBeNull());
    await act(async () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(item => item.textContent === (type === 'Part' ? '新建零组件' : '新建图文档'))!.click());
    await vi.waitFor(() => expect(document.querySelector('.ant-drawer form')).not.toBeNull());
    for (const [label, value] of [['编码', 'PAGE315-' + type.toUpperCase()], ['名称', '真实' + type + '弹层']] as const) {
      const field = [...document.querySelectorAll<HTMLLabelElement>('.ant-drawer label')].find(item => item.textContent?.includes(label))!;
      const input = document.getElementById(field.htmlFor) as HTMLInputElement;
      await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value); input.dispatchEvent(new Event('input', { bubbles: true })); });
    }
    if (type === 'Document') {
      const fileInput = document.querySelector<HTMLInputElement>('.ant-drawer input[type="file"]')!;
      Object.defineProperty(fileInput, 'files', { configurable: true, value: [new File(['main'], 'main.pdf', { type: 'application/pdf' })] });
      await act(async () => fileInput.dispatchEvent(new Event('change', { bubbles: true })));
      await vi.waitFor(() => expect(upload).toHaveBeenCalledOnce());
    }
    await vi.waitFor(() => expect(document.querySelector<HTMLButtonElement>('.ant-drawer-footer .ant-btn-primary')?.disabled).toBe(false));
    await act(async () => document.querySelector<HTMLButtonElement>('.ant-drawer-footer .ant-btn-primary')!.click());
    await vi.waitFor(() => expect(writes).toHaveLength(1));
    expect(writes[0]).toEqual({ declaration: declaration.id, position: create.position,
      body: { position: { spaceId: 'public', rootFolderId: 'root', linkIds: ['parent-link'] }, values: { ECODE: 'PAGE315-' + type.toUpperCase(), ENAME: '真实' + type + '弹层' }, ...(type === 'Document' ? { uploads: { mainUploadId: uploadId } } : {}) } });
    await vi.waitFor(() => expect(document.querySelector('.ant-drawer')).toBeNull());
  } finally {
    await act(async () => root.unmount()); upload.mockRestore(); router.dispose(); container.remove(); vi.unstubAllGlobals(); ModuleRegistry.replaceAll([], 'object-create315-clean');
  }
});
