// @vitest-environment jsdom
import { loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import type { FrontendBehaviorBindings } from '@icp-platform/sdk';
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
const { default: workspace } = await loadComponentSource<{ default: FrontendBehaviorBindings }>('Workspace', 'index.ts');
import folder from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceFolderRuntime.contract.json';
const content = readComponentJson('Workspace', 'metadata/models/WorkspaceFolderContent.model.json');
const tree = readComponentJson('Workspace', 'metadata/views/WorkspaceStructure.view.json');
const list = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderList.view.json');
const detail = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderDetail.view.json');
const dictionary = readComponentJson('Workspace', 'metadata/dictionaries/Workspace.dic.json');
import { RuntimeMetadataProvider } from './formalRuntimeMetadataProvider';
import { PageEntry } from '../composition/PageOutlet';
import { AuthProvider, useAuth } from '../contexts/AuthContext';

// 收集期装载真实模块，避免冷转换耗时进入生命周期 hook 的交互预算。
import '../composition/StandardTreePage';
import '../pages/StandardCrudListPageHost';
import '../pages/StandardCrudListDrawerRuntime';
await loadComponentSource('Workspace', 'workspaceRootResource.ts');

vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
const publicA = 'public:root:a:a-shared:same-last';
const publicB = 'public:root:b:b-shared:same-last';
const personal = 'mine:my-root:private-shared:same-last';
const waitFor = (check: () => void) => vi.waitFor(async () => { await act(async () => undefined); check(); });

it('locates the chosen search occurrence through the real Workspace Provider and rejects its removed chain', async () => {
  let removed = false;
  const page = await mountOccurrence('public:root', url => {
    if (url.pathname.endsWith('/search')) return Promise.resolve(json({ items: ['a', 'b'].map(prefix => ({
      node: { nodeKey: `public:root:${prefix}:${prefix}-shared:same-last`, nodeType: 'folder', label: '共享后代', hasChildren: false,
        context: { spaceId: 'public', rootFolderId: 'root', id: 'descendant', linkIds: [prefix, `${prefix}-shared`, 'same-last'] } },
      path: ['公共空间', prefix === 'a' ? '父A' : '父B', '共享文件夹', '共享后代'].map((label, index) => ({
        nodeKey: ['public', 'root', ...[prefix, `${prefix}-shared`, 'same-last'].slice(0, index)].join(':'), label })),
    })), total: 2, page: 1, pageSize: 20 }));
    if (removed && url.pathname.endsWith('/path') && url.searchParams.get('nodeKey')?.includes('b-shared'))
      return Promise.resolve(new Response('', { status: 404 }));
  });
  await waitFor(() => expect(page.selected()).toBe('公共空间'));
  const input = async () => act(async () => {
    const element = page.container.querySelector<HTMLInputElement>('[aria-label="搜索节点"]')!;
    element.dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(element, '共享后代');
    element.dispatchEvent(new Event('input', { bubbles: true }));
  });
  const chooseB = async () => {
    await waitFor(() => expect(page.container.querySelectorAll('[aria-label="定位节点 共享后代"]')).toHaveLength(2));
    const button = [...page.container.querySelectorAll<HTMLButtonElement>('[aria-label="定位节点 共享后代"]')]
      .find(button => button.textContent?.includes('公共空间 / 父B / 共享文件夹 / 共享后代'))!;
    await act(async () => button.click());
  };
  await input(); await chooseB();
  await waitFor(() => expect(page.breadcrumb()).toBe('公共空间/父B/共享文件夹/共享后代'));
  expect(new URLSearchParams(page.router.state.location.search).get('node')).toBe(publicB);
  await waitFor(() => expect(page.requests.some(url => url.pathname === '/api/links/workspace.content'
    && JSON.parse(url.searchParams.get('queryScope') ?? '{}').linkIds === '["b","b-shared","same-last"]')).toBe(true));
  expect(page.container.querySelectorAll('.ant-tree-treenode-switcher-open').length).toBeGreaterThanOrEqual(3);
  await page.navigate('public:root');
  removed = true;
  await input(); await chooseB();
  await waitFor(() => expect(page.container.textContent).toContain('目标节点已不存在'));
  expect(page.selected()).toBe('公共空间');
  expect(new URLSearchParams(page.router.state.location.search).get('node')).toBe('public:root');
  expect(page.requests.some(url => url.pathname.endsWith('/path') && url.searchParams.get('nodeKey') === publicA)).toBe(false);
});

async function mountOccurrence(initial: string, intercept?: (url: URL) => Promise<Response> | undefined, create?: () => Promise<Response>) {
  let account = 'actor';
  const user = () => ({ id: account, ecode: account, ename: '账号', revision: 1, mustChangePassword: false,
    isLocalAdministrator: false, timeZoneId: 'Asia/Taipei', featurePositions: ['workspace-entry', 'workspace-create'] });
  const writes: unknown[] = [];
  const requests: URL[] = [];
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(input instanceof Request ? input.url : String(input), 'http://localhost');
    requests.push(url);
    const intercepted = intercept?.(url);
    if (intercepted) return intercepted;
    if (url.pathname.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (url.pathname.endsWith('current-user')) return json(user());
    if (url.pathname.endsWith('/personal/ensure')) return json({ succeeded: true });
    const rootNode = (spaceId: string, rootFolderId: string, kind: string) => ({ nodeKey: `${spaceId}:${rootFolderId}`, nodeType: 'folder',
      label: kind === 'Public' ? '公共空间' : '个人空间', labelKey: `Workspace.${kind}`, hasChildren: true, contextMenuKeys: ['paste', 'clearClipboard'],
      context: { spaceId, rootFolderId, id: rootFolderId, linkIds: [] } });
    if (url.pathname === '/api/trees/workspace.structure/roots') return json([
      rootNode('public', 'root', 'Public'), rootNode(account === 'actor' ? 'mine' : 'next', account === 'actor' ? 'my-root' : 'next-root', 'Personal'),
    ]);
    const [spaceId, rootFolderId, ...links] = (url.searchParams.get('nodeKey') ?? '').split(':').map(decodeURIComponent);
    const own = spaceId === 'mine';
    if (url.pathname.endsWith('/path')) {
      const names = own ? ['个人空间', '共享文件夹', '共享后代'] : ['公共空间', links[0] === 'a' ? '父A' : '父B', '共享文件夹', '共享后代'];
      const ids = own ? ['my-root', 'shared', 'descendant'] : ['root', `${links[0]}-folder`, 'shared', 'descendant'];
      return json(names.slice(0, links.length + 1).map((label, index) => ({ nodeKey: [spaceId, rootFolderId, ...links.slice(0, index)].join(':'), label,
        nodeType: 'folder', hasChildren: index < links.length, context: { spaceId, rootFolderId, id: ids[index], linkIds: links.slice(0, index) } })));
    }
    if (url.pathname.endsWith('/children')) {
      const item = (linkId: string, id: string, label: string, hasChildren = true) => ({ nodeKey: `${url.searchParams.get('nodeKey')}:${linkId}`, nodeType: 'folder', label, hasChildren,
        context: { spaceId, rootFolderId, id, linkIds: [...links, linkId] } });
      const shared = own ? links.length === 1 : links.length === 2;
      return json(links.length === 0 ? own ? [item('private-shared', 'shared', '共享文件夹')]
        : [item('a', 'a-folder', '父A'), item('b', 'b-folder', '父B')]
        : shared ? [item('same-last', 'descendant', '共享后代', false), ...(writes.length ? [item('new-link', 'new-folder', '新共享内容', false)] : [])]
          : own || links.length > 2 ? [] : [item(`${links[0]}-shared`, 'shared', '共享文件夹')]);
    }
    if (url.pathname === '/api/workspace/content/folders') {
      writes.push(input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body)));
      return create ? create() : json({ succeeded: true });
    }
    if (url.pathname === '/api/links/workspace.content') return json({ items: [], total: 0, page: 1, pageSize: 20 });
    throw new Error(`Unexpected request ${url.pathname}`);
  });
  vi.stubGlobal('fetch', fetcher);
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Workspace', displayName: '工作空间', version: '1' }, ...workspace }], 'workspace-occurrence');
  function SessionSwitch() {
    const auth = useAuth();
    return <button onClick={() => { account = 'next'; void auth.retrySessionValidation(); }}>切换账号</button>;
  }
  const catalogs = { zhs: Object.fromEntries(Object.entries(dictionary.Workspace).map(([key, value]) => [`Workspace.${key}`, value.zhs])) };
  const router = createMemoryRouter([{ path: '/workspace', element:
    <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={catalogs}>
      <AuthProvider initialUser={user()}><AuthorizationProvider featurePositions={user().featurePositions}><PlatformQueryProvider scopeKey="workspace-occurrence">
        <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Workspace', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: [
          { key: 'workspace', position: 'workspace-entry', kind: 'navigation' },
          { key: 'create', position: 'workspace-create', kind: 'action', parentPosition: 'workspace-entry', staticScope: 'folder', actionScope: 'list.pageActions' },
        ] }] }, navDocuments: [], modelDocuments: [folder, content], viewDocuments: [tree, list, detail] }}>
          <SessionSwitch /><PageEntry page={{ pageKey: 'platform.tree', parameters: { viewKey: tree.view.key, featurePosition: 'workspace-entry' }, inputResolverKey: 'workspace.tree.current' }} />
        </RuntimeMetadataProvider>
      </PlatformQueryProvider></AuthorizationProvider></AuthProvider>
    </LocalizationProvider>,
  }], { initialEntries: [`/workspace?node=${encodeURIComponent(initial)}&other=keep`] });
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); vi.unstubAllGlobals(); ModuleRegistry.replaceAll([], 'workspace-occurrence-cleanup'); });
  await act(async () => root.render(<RouterProvider router={router} />));
  const selected = () => container.querySelector('.ant-tree-node-selected .ant-tree-title')?.textContent;
  const breadcrumb = () => container.querySelector('.np-tree-context-header .ant-breadcrumb')?.textContent;
  const navigate = async (key: string) => { await act(async () => router.navigate(`/workspace?node=${encodeURIComponent(key)}&other=keep`)); };
  return { container, router, selected, breadcrumb, navigate, requests, writes };
}

it('restores separate shared occurrences and keeps selection, breadcrumb, PageRef input and address on the same full chain', async () => {
  const page = await mountOccurrence(publicB);
  await waitFor(() => expect(page.selected()).toBe('共享后代'));
  expect(page.breadcrumb()).toBe('公共空间/父B/共享文件夹/共享后代');
  const assertRead = (spaceId: string, rootFolderId: string, linkIds: string[]) => expect(page.requests.some(url => {
    const scope = JSON.parse(url.searchParams.get('queryScope') ?? '{}') as Record<string, string>;
    return url.pathname === '/api/links/workspace.content' && scope.spaceId === spaceId && scope.rootFolderId === rootFolderId
      && scope.linkIds === JSON.stringify(linkIds) && url.searchParams.get('anchorId') === 'descendant';
  })).toBe(true);
  await waitFor(() => assertRead('public', 'root', ['b', 'b-shared', 'same-last']));
  await page.navigate(publicA);
  await waitFor(() => expect(page.breadcrumb()).toBe('公共空间/父A/共享文件夹/共享后代'));
  await waitFor(() => assertRead('public', 'root', ['a', 'a-shared', 'same-last']));
  await page.navigate(personal);
  await waitFor(() => expect(page.breadcrumb()).toBe('个人空间/共享文件夹/共享后代'));
  await waitFor(() => assertRead('mine', 'my-root', ['private-shared', 'same-last']));
  expect(new URLSearchParams(page.router.state.location.search).get('node')).toBe(personal);
  const ancestor = [...page.container.querySelectorAll<HTMLButtonElement>('.ant-breadcrumb button')].find(button => button.textContent === '共享文件夹')!;
  await act(async () => ancestor.click());
  await waitFor(() => expect(page.selected()).toBe('共享文件夹'));
  expect(new URLSearchParams(page.router.state.location.search).get('node')).toBe('mine:my-root:private-shared');
  expect(new URLSearchParams(page.router.state.location.search).get('other')).toBe('keep');
});

it('ends a removed or unreadable occurrence with a visible notice and safe root without reading another parent path', async () => {
  const page = await mountOccurrence(publicA, url => url.pathname.endsWith('/path') && url.searchParams.get('nodeKey') === publicA
    ? Promise.resolve(new Response(null, { status: 404 })) : undefined);
  await waitFor(() => expect(page.selected()).toBe('公共空间'));
  expect(page.container.querySelector('[role="status"]')?.textContent).toBe('目标节点已不存在');
  expect(page.breadcrumb()).toBe('公共空间');
  expect(new URLSearchParams(page.router.state.location.search).get('node')).toBe('public:root');
  expect(page.requests.filter(url => url.pathname.endsWith('/path')).map(url => url.searchParams.get('nodeKey'))).toEqual([publicA]);
  expect(page.requests.some(url => url.pathname.endsWith('/children'))).toBe(false);
  await waitFor(() => expect(page.requests.some(url => url.pathname === '/api/links/workspace.content' && url.searchParams.get('anchorId') === 'root')).toBe(true));
});

it('clears old private occurrence state on account replacement and ignores its late path response', async () => {
  let complete!: (response: Response) => void;
  let reads = 0;
  const page = await mountOccurrence(personal, url => {
    if (!url.pathname.endsWith('/path') || !url.searchParams.get('nodeKey')?.startsWith('mine:')) return undefined;
    reads += 1;
    return reads === 1 ? new Promise<Response>(resolve => { complete = resolve; }) : Promise.resolve(new Response(null, { status: 404 }));
  });
  await waitFor(() => expect(complete).toBeDefined());
  await act(async () => [...page.container.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent === '切换账号')!.click());
  await waitFor(() => expect(page.selected()).toBe('公共空间'));
  await waitFor(() => expect(reads).toBe(2));
  await act(async () => complete(json({ folderIds: ['my-root', 'shared', 'descendant'], names: ['旧账号私树', '旧共享', '旧后代'] })));
  expect(page.breadcrumb()).toBe('公共空间');
  expect(page.container.textContent).not.toContain('旧账号私树');
  expect(new URLSearchParams(page.router.state.location.search).get('node')).toBe('public:root');
  expect(page.requests.filter(url => url.pathname.endsWith('/children') && url.searchParams.get('nodeKey')?.startsWith('mine:'))).toHaveLength(0);
});

it('ignores a late path from another space after the user restores a newer occurrence', async () => {
  let complete!: (response: Response) => void;
  const page = await mountOccurrence(publicA, url => url.pathname.endsWith('/path') && url.searchParams.get('nodeKey')?.startsWith('public:')
    ? new Promise<Response>(resolve => { complete = resolve; }) : undefined);
  await waitFor(() => expect(complete).toBeDefined());
  await page.navigate(personal);
  await waitFor(() => expect(page.breadcrumb()).toBe('个人空间/共享文件夹/共享后代'));
  await act(async () => complete(json({ folderIds: ['root', 'a-folder', 'shared', 'descendant'], names: ['公共空间', '父A', '共享文件夹', '过期后代'] })));
  expect(page.breadcrumb()).toBe('个人空间/共享文件夹/共享后代');
  expect(page.container.textContent).not.toContain('过期后代');
  expect(new URLSearchParams(page.router.state.location.search).get('node')).toBe(personal);
});

it('protects a cross-space draft and submission, then refreshes all loaded shared occurrences after a receipt', async () => {
  let complete!: (response: Response) => void;
  const page = await mountOccurrence(publicA, url => url.pathname.endsWith('/search') ? Promise.resolve(json({ items: [{ node: { nodeKey: publicB, nodeType: 'folder', label: '共享后代', hasChildren: false }, path: [] }], total: 1, page: 1, pageSize: 20 })) : undefined, () => new Promise<Response>(resolve => { complete = resolve; }));
  await waitFor(() => expect(page.selected()).toBe('共享后代'));
  await page.navigate(publicB);
  await waitFor(() => expect(page.breadcrumb()).toBe('公共空间/父B/共享文件夹/共享后代'));
  await page.navigate(personal);
  await waitFor(() => expect(page.breadcrumb()).toBe('个人空间/共享文件夹/共享后代'));
  const ancestor = [...page.container.querySelectorAll<HTMLButtonElement>('.ant-breadcrumb button')].find(button => button.textContent === '共享文件夹')!;
  await act(async () => ancestor.click());
  await waitFor(() => expect(page.selected()).toBe('共享文件夹'));
  const click = async (label: string, scope: ParentNode = document) => {
    const button = [...scope.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.replace(/\s/g, '') === label)!;
    await act(async () => button.click());
  };
  await waitFor(() => expect([...page.container.querySelectorAll('button')].some(button => button.textContent?.replace(/\s/g, '') === '新建')).toBe(true));
  await click('新建', page.container);
  await waitFor(() => expect(document.querySelector('[role="menuitem"]')).not.toBeNull());
  await act(async () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(item => item.textContent === '新建文件夹')!.click());
  await waitFor(() => expect(document.querySelector('.ant-drawer form')).not.toBeNull());
  const label = [...document.querySelectorAll<HTMLLabelElement>('.ant-drawer label')].find(label => label.textContent?.includes('名称'))!;
  const input = document.getElementById(label.htmlFor) as HTMLInputElement;
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, '新共享内容');
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
  await act(async () => {
    const search = page.container.querySelector<HTMLInputElement>('[aria-label="搜索节点"]')!;
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(search, '共享后代');
    search.dispatchEvent(new Event('input', { bubbles: true }));
  });
  await waitFor(() => expect(page.container.querySelector('[aria-label="定位节点 共享后代"]')).not.toBeNull());
  await act(async () => page.container.querySelector<HTMLButtonElement>('[aria-label="定位节点 共享后代"]')!.click());
  await waitFor(() => expect(document.body.textContent).toContain('放弃未保存的修改？'));
  await click('继续编辑');
  expect(input.value).toBe('新共享内容');
  expect(new URLSearchParams(page.router.state.location.search).get('node')).toBe('mine:my-root:private-shared');
  const selectPublic = async () => { await act(async () => [...page.container.querySelectorAll<HTMLElement>('.ant-tree-title')].find(node => node.textContent === '公共空间')!.click()); };
  await selectPublic();
  await waitFor(() => expect(document.body.textContent).toContain('放弃未保存的修改？'));
  await click('继续编辑');
  expect(input.value).toBe('新共享内容');
  expect(new URLSearchParams(page.router.state.location.search).get('node')).toBe('mine:my-root:private-shared');
  const saveButton = [...document.querySelectorAll<HTMLButtonElement>('.ant-drawer .np-crud-form-drawer-footer button')].find(button => button.textContent?.replace(/\s/g, '') === '保存并关闭')!;
  expect(saveButton).toBeDefined();
  await waitFor(() => expect(saveButton.disabled).toBe(false));
  await act(async () => saveButton.click());
  await waitFor(() => expect(page.writes).toHaveLength(1));
  await selectPublic();
  expect(new URLSearchParams(page.router.state.location.search).get('node')).toBe('mine:my-root:private-shared');
  expect(page.writes[0]).toEqual({ position: { spaceId: 'mine', rootFolderId: 'my-root', linkIds: ['private-shared'] }, values: { ENAME: '新共享内容' }, copyParentAuthorization: true });
  const before = page.requests.length;
  await act(async () => complete(json({ succeeded: true })));
  await waitFor(() => expect(document.querySelector('.ant-drawer-open')).toBeNull());
  await waitFor(() => expect([...page.container.querySelectorAll('.ant-tree-title')].filter(node => node.textContent === '新共享内容')).toHaveLength(3));
  expect(page.writes).toHaveLength(1);
  const refreshed = page.requests.slice(before).filter(url => url.pathname.endsWith('/children'));
  expect(refreshed.map(url => url.searchParams.get('nodeKey'))).toEqual(expect.arrayContaining([
    'public:root:a:a-shared', 'public:root:b:b-shared', 'mine:my-root:private-shared',
  ]));
  expect(page.requests.slice(before).some(url => url.pathname === '/api/links/workspace.content' && url.searchParams.get('anchorId') === 'shared')).toBe(true);
  expect(page.breadcrumb()).toBe('个人空间/共享文件夹');
});
