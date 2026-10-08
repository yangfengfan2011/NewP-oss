// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
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

// 收集期装载真实模块，避免冷转换耗时进入生命周期 hook 的交互预算。
import '../composition/StandardTreePage';
import '../pages/StandardCrudListPageHost';
import '../pages/StandardCrudListDrawerRuntime';
import '../../../../modules/workspace/src/workspaceRootResource';

vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
const publicA = 'public:root:a:a-shared:same-last';
const publicB = 'public:root:b:b-shared:same-last';
const personal = 'mine:my-root:private-shared:same-last';
const waitFor = (check: () => void) => vi.waitFor(async () => { await act(async () => undefined); check(); });

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
    if (url.pathname === '/api/workspace/spaces') return json([
      { spaceId: 'public', spaceKind: 'Public', rootFolderId: 'root', name: '公共空间', hasChildren: true },
      { spaceId: account === 'actor' ? 'mine' : 'next', spaceKind: 'Personal', rootFolderId: account === 'actor' ? 'my-root' : 'next-root', name: '个人空间', hasChildren: true },
    ]);
    const links = JSON.parse(url.searchParams.get('linkIds') ?? '[]') as string[];
    const own = url.searchParams.get('spaceId') === 'mine';
    if (url.pathname.endsWith('/path')) {
      const names = own ? ['个人空间', '共享文件夹', '共享后代'] : ['公共空间', links[0] === 'a' ? '父A' : '父B', '共享文件夹', '共享后代'];
      const ids = own ? ['my-root', 'shared', 'descendant'] : ['root', `${links[0]}-folder`, 'shared', 'descendant'];
      return json({ folderIds: ids.slice(0, links.length + 1), names: names.slice(0, links.length + 1) });
    }
    if (url.pathname.endsWith('/children')) {
      const item = (linkId: string, id: string, name: string, hasChildren = true) => ({ linkId, associatedObject: { id }, fields: { Name: name }, hasChildren });
      const shared = own ? links.length === 1 : links.length === 2;
      return json({ items: links.length === 0 ? own ? [item('private-shared', 'shared', '共享文件夹')]
        : [item('a', 'a-folder', '父A'), item('b', 'b-folder', '父B')]
        : shared ? [item('same-last', 'descendant', '共享后代', false), ...(writes.length ? [item('new-link', 'new-folder', '新共享内容', false)] : [])]
          : own || links.length > 2 ? [] : [item(`${links[0]}-shared`, 'shared', '共享文件夹')] });
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
  const page = await mountOccurrence(publicA, url => url.pathname.endsWith('/path') && url.searchParams.get('linkIds') === '["a","a-shared","same-last"]'
    ? Promise.resolve(new Response(null, { status: 404 })) : undefined);
  await waitFor(() => expect(page.selected()).toBe('公共空间'));
  expect(page.container.querySelector('[role="status"]')?.textContent).toBe('目标节点已不存在');
  expect(page.breadcrumb()).toBe('公共空间');
  expect(new URLSearchParams(page.router.state.location.search).get('node')).toBe('public:root');
  expect(page.requests.filter(url => url.pathname.endsWith('/path')).map(url => url.searchParams.get('linkIds'))).toEqual(['["a","a-shared","same-last"]']);
  expect(page.requests.some(url => url.pathname.endsWith('/children'))).toBe(false);
  await waitFor(() => expect(page.requests.some(url => url.pathname === '/api/links/workspace.content' && url.searchParams.get('anchorId') === 'root')).toBe(true));
});

it('clears old private occurrence state on account replacement and ignores its late path response', async () => {
  let complete!: (response: Response) => void;
  let reads = 0;
  const page = await mountOccurrence(personal, url => {
    if (!url.pathname.endsWith('/path') || url.searchParams.get('spaceId') !== 'mine') return undefined;
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
  expect(page.requests.filter(url => url.pathname.endsWith('/children') && url.searchParams.get('spaceId') === 'mine')).toHaveLength(0);
});

it('ignores a late path from another space after the user restores a newer occurrence', async () => {
  let complete!: (response: Response) => void;
  const page = await mountOccurrence(publicA, url => url.pathname.endsWith('/path') && url.searchParams.get('spaceId') === 'public'
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
  const page = await mountOccurrence(publicA, undefined, () => new Promise<Response>(resolve => { complete = resolve; }));
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
  await waitFor(() => expect([...page.container.querySelectorAll('button')].some(button => button.textContent?.replace(/\s/g, '') === '新建文件夹')).toBe(true));
  await click('新建文件夹', page.container);
  await waitFor(() => expect(document.querySelector('.ant-drawer form')).not.toBeNull());
  const label = [...document.querySelectorAll<HTMLLabelElement>('.ant-drawer label')].find(label => label.textContent?.includes('名称'))!;
  const input = document.getElementById(label.htmlFor) as HTMLInputElement;
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, '新共享内容');
    input.dispatchEvent(new Event('input', { bubbles: true }));
  });
  const selectPublic = async () => { await act(async () => [...page.container.querySelectorAll<HTMLElement>('.ant-tree-title')].find(node => node.textContent === '公共空间')!.click()); };
  await selectPublic();
  await waitFor(() => expect(document.body.textContent).toContain('放弃未保存的修改？'));
  await click('继续编辑');
  expect(input.value).toBe('新共享内容');
  expect(new URLSearchParams(page.router.state.location.search).get('node')).toBe('mine:my-root:private-shared');
  await act(async () => document.querySelector<HTMLFormElement>('.ant-drawer form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  await waitFor(() => expect(page.writes).toHaveLength(1));
  await selectPublic();
  expect(new URLSearchParams(page.router.state.location.search).get('node')).toBe('mine:my-root:private-shared');
  expect(page.writes[0]).toEqual({ position: { spaceId: 'mine', rootFolderId: 'my-root', linkIds: ['private-shared'] }, values: { ENAME: '新共享内容', Description: null } });
  const before = page.requests.length;
  await act(async () => complete(json({ succeeded: true })));
  await waitFor(() => expect(document.querySelector('.ant-drawer-open')).toBeNull());
  await waitFor(() => expect([...page.container.querySelectorAll('.ant-tree-title')].filter(node => node.textContent === '新共享内容')).toHaveLength(3));
  expect(page.writes).toHaveLength(1);
  const refreshed = page.requests.slice(before).filter(url => url.pathname.endsWith('/children'));
  expect(refreshed.map(url => `${url.searchParams.get('spaceId')}:${url.searchParams.get('linkIds')}`)).toEqual(expect.arrayContaining([
    'public:["a","a-shared"]', 'public:["b","b-shared"]', 'mine:["private-shared"]',
  ]));
  expect(page.requests.slice(before).some(url => url.pathname === '/api/links/workspace.content' && url.searchParams.get('anchorId') === 'shared')).toBe(true);
  expect(page.breadcrumb()).toBe('个人空间/共享文件夹');
});
