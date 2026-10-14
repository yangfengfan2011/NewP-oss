// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry, type FrontendBehaviorBindings } from '@icp-platform/sdk';
import { loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import folder from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceFolderRuntime.contract.json';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { PageEntry } from '../composition/PageOutlet';
import '../composition/StandardTreePage';
import '../pages/StandardCrudListPageHost';

const { default: workspace } = await loadComponentSource<{ default: FrontendBehaviorBindings }>('Workspace', 'index.ts');
await loadComponentSource('Workspace', 'WorkspaceObjectEditInput.tsx');
const content = readComponentJson('Workspace', 'metadata/models/WorkspaceFolderContent.model.json');
const tree = readComponentJson('Workspace', 'metadata/views/WorkspaceStructure.view.json');
const list = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderList.view.json');
const detail = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderDetail.view.json');
const dictionary = readComponentJson('Workspace', 'metadata/dictionaries/Workspace.dic.json');

it.each(['edit', 'remove'] as const)('executes the real Workspace tree %s action against its incoming LID and parent occurrence', async action => {
  let name = '原目录'; let present = true;
  const writes: { path: string; method: string; body: unknown }[] = [];
  const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
  const request = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(input instanceof Request ? input.url : String(input), 'http://localhost');
    const method = input instanceof Request ? input.method : init?.method ?? 'GET';
    if (url.pathname.includes('antiforgery-token')) return json({ requestToken: 'test' });
    if (url.pathname.endsWith('/personal/ensure')) return json({ initialized: true });
    if (url.pathname === '/api/workspace/spaces') return json([
      { spaceId: 'public', spaceKind: 'Public', rootFolderId: 'root', name: '公共空间', hasChildren: present },
      { spaceId: 'mine', spaceKind: 'Personal', rootFolderId: 'personal-root', name: '个人空间' },
    ]);
    if (url.pathname.endsWith('/children')) return json({ items: present && url.searchParams.get('linkIds') === '[]'
      ? [{ linkId: 'incoming-link', associatedObject: { id: 'folder-eid' }, fields: { Name: name }, hasChildren: false }] : [] });
    if (url.pathname.endsWith('/path')) {
      const links = JSON.parse(url.searchParams.get('linkIds') ?? '[]') as string[];
      if (links.length && !present) return json({}, 404);
      return json({ folderIds: links.length ? ['root', 'folder-eid'] : ['root'], names: links.length ? ['公共空间', name] : ['公共空间'] });
    }
    if (url.pathname === '/api/links/workspace.content') return json({ items: [], total: 0, page: 1, pageSize: 20 });
    if (url.pathname === '/api/workspace/folders/folder-eid') return json({ fields: { ENAME: name, Description: '原说明' } });
    if (url.pathname === '/api/workspace/content/incoming-link/edit-values') return json({ fields: { ENAME: name, Description: '原说明' } });
    if (method === 'PUT' || method === 'DELETE') {
      const body = input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body));
      writes.push({ path: url.pathname, method, body });
      if (method === 'PUT') name = body.values.ENAME;
      else present = false;
      return json({ succeeded: true });
    }
    throw new Error(`Unexpected request ${method} ${url.pathname}`);
  });
  vi.stubGlobal('fetch', request);
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Workspace', displayName: 'Workspace', version: '1' }, ...workspace }], 'node-menu');
  const catalog = Object.fromEntries(Object.entries(dictionary.Workspace).map(([key, value]) => [`Workspace.${key}`, value.zhs]));
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const router = createMemoryRouter([{ path: '/workspace', element:
    <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: catalog }}>
      <AuthorizationProvider featurePositions={['entry', 'tree-edit', 'tree-remove']}><PlatformQueryProvider scopeKey="node-menu">
        <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Workspace', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [],
          features: [{ key: 'workspace', position: 'entry', kind: 'navigation' }, ...['edit', 'remove'].map(key => ({
            key, position: `tree-${key}`, kind: 'action', parentPosition: 'entry', staticScope: 'folder', actionScope: 'contextMenu',
          }))] }] }, navDocuments: [], modelDocuments: [folder, content], viewDocuments: [tree, list, detail] }}>
          <PageEntry page={{ pageKey: 'platform.tree', parameters: { viewKey: tree.view.key, featurePosition: 'entry' }, inputResolverKey: 'workspace.tree.current' }} />
        </RuntimeMetadataProvider>
      </PlatformQueryProvider></AuthorizationProvider>
    </LocalizationProvider> }], { initialEntries: ['/workspace'] });
  try {
    await act(async () => root.render(<RouterProvider router={router} />));
    await vi.waitFor(() => expect(container.querySelector('.ant-tree-switcher:not(.ant-tree-switcher-noop)')).not.toBeNull());
    const rootLabel = [...container.querySelectorAll('.ant-tree-title')].find(item => item.textContent === '公共空间')!;
    const native = new MouseEvent('contextmenu', { bubbles: true, cancelable: true });
    await act(async () => rootLabel.firstElementChild!.dispatchEvent(native));
    expect(native.defaultPrevented).toBe(false);
    expect(document.querySelector('[role="menu"]')).toBeNull();
    await act(async () => container.querySelector<HTMLElement>('.ant-tree-switcher:not(.ant-tree-switcher-noop)')!.click());
    await vi.waitFor(() => expect([...container.querySelectorAll('.ant-tree-title')].some(item => item.textContent === '原目录')).toBe(true));
    const target = [...container.querySelectorAll('.ant-tree-title')].find(item => item.textContent === '原目录')!;
    await act(async () => target.firstElementChild!.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, clientX: 40, clientY: 80 })));
    await vi.waitFor(() => expect(document.querySelector('[role="menuitem"]')).not.toBeNull());
    await act(async () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(item => item.textContent === (action === 'edit' ? '编辑' : '移除引用'))!.click());
    if (action === 'edit') {
      await vi.waitFor(() => expect(document.querySelector('.ant-drawer textarea')).not.toBeNull());
      expect(document.querySelector('.ant-drawer-title')?.textContent).toBe('编辑');
      const label = [...document.querySelectorAll<HTMLLabelElement>('.ant-drawer label')].find(item => item.textContent?.includes('名称'))!;
      const input = document.getElementById(label.htmlFor) as HTMLInputElement;
      await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, '已编辑目录'); input.dispatchEvent(new Event('input', { bubbles: true })); });
      await act(async () => document.querySelector<HTMLButtonElement>('.ant-drawer-footer .ant-btn-primary')!.click());
    } else {
      await vi.waitFor(() => expect(document.querySelector('.ant-modal-title')?.textContent).toContain('移除当前文件夹中的这条引用？'));
      expect(writes).toHaveLength(0);
      await act(async () => document.querySelector<HTMLButtonElement>('.ant-modal-footer .ant-btn-primary')!.click());
    }
    await vi.waitFor(() => expect(writes).toHaveLength(1));
    expect(writes[0]).toEqual(action === 'edit'
      ? { path: '/api/workspace/content/incoming-link', method: 'PUT', body: { position: { spaceId: 'public', rootFolderId: 'root', linkIds: [] }, values: { ENAME: '已编辑目录', Description: '原说明' } } }
      : { path: '/api/workspace/content/references/incoming-link', method: 'DELETE', body: { position: { spaceId: 'public', rootFolderId: 'root', linkIds: [] } } });
    await vi.waitFor(() => expect(container.querySelector('.ant-tree')?.textContent).toContain(action === 'edit' ? '已编辑目录' : '公共空间'));
    if (action === 'remove') await vi.waitFor(() => expect(container.querySelector('.ant-tree')?.textContent).not.toContain('原目录'));
    expect(request.mock.calls.some(([input]) => String(input instanceof Request ? input.url : input).includes('/folders/incoming-link'))).toBe(false);
  } finally {
    await act(async () => root.unmount()); router.dispose(); container.remove(); vi.unstubAllGlobals(); ModuleRegistry.replaceAll([], 'node-menu-clean');
  }
});
