// @vitest-environment jsdom
import { act, StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry, type FrontendBehaviorBindings, type StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import folder from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceFolderRuntime.contract.json';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { PageEntry } from '../composition/PageOutlet';
import { AuthProvider, useAuth } from '../contexts/AuthContext';
import '../composition/StandardTreePage';
import '../pages/StandardCrudListPageHost';
import '../pages/StandardCrudListOverlayRuntime';
const { default: workspace } = await loadComponentSource<{ default: FrontendBehaviorBindings }>('Workspace', 'index.ts');
await loadComponentSource('Workspace', 'workspaceRootResource.ts');
const content = readComponentJson('Workspace', 'metadata/models/WorkspaceFolderContent.model.json');
const tree = readComponentJson('Workspace', 'metadata/views/WorkspaceStructure.view.json');
const list = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderList.view.json');
const detail = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderDetail.view.json');
const dictionary = readComponentJson('Workspace', 'metadata/dictionaries/Workspace.dic.json');
vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
const mountedPages = new Set<() => Promise<void>>();
const pollingActs = new Set<Promise<void>>();
let testLifetime: AbortController;
beforeEach(() => { testLifetime = new AbortController(); });
afterEach(async () => {
  testLifetime.abort(new Error('Clipboard test disposed'));
  for (const cleanup of [...mountedPages]) await cleanup();
  vi.unstubAllGlobals(); ModuleRegistry.replaceAll([], 'clipboard-clean');
});
async function waitForActive(check: () => void | Promise<void>, signal: AbortSignal): Promise<void> {
  let ended!: () => void;
  const disposed = new Promise<never>((_, reject) => { ended = () => reject(signal.reason); signal.addEventListener('abort', ended, { once: true }); if (signal.aborted) ended(); });
  try { await Promise.race([disposed, vi.waitFor(async () => {
    signal.throwIfAborted(); const flush = Promise.resolve(act(async () => undefined)); pollingActs.add(flush);
    try { await flush; } finally { pollingActs.delete(flush); }
    signal.throwIfAborted(); await check();
  })]); }
  finally { signal.removeEventListener('abort', ended); }
}
const waitFor = (check: () => void | Promise<void>) => waitForActive(check, testLifetime.signal);

async function mount(options: { validate?: (body: Record<string, unknown>) => Promise<Response>; paste?: (body: Record<string, unknown>) => Promise<Response>; failRefresh?: boolean;
  grants?: readonly string[]; remove?: (body: Record<string, unknown>) => Promise<Response>;
  menuLoad?: (load: () => Promise<{ default: StandardCrudResourceRegistration }>, call: number) => Promise<{ default: StandardCrudResourceRegistration }> } = {}) {
  const lifetime = new AbortController();
  const waitFor = (check: () => void | Promise<void>) => waitForActive(check, lifetime.signal);
  const pendingResponses = new Set<() => void>();
  const controlledResponse = (operation: () => Promise<Response>) => new Promise<Response>((resolve, reject) => {
    const finish = () => resolve(json({ code: 'Workspace.ClipboardOutcomeUnknown' }, 503)); pendingResponses.add(finish);
    operation().then(value => { pendingResponses.delete(finish); resolve(value); }, failure => { pendingResponses.delete(finish); reject(failure); });
  });
  const writes: Array<{ path: string; body: Record<string, unknown> }> = [];
  let actor = 'actor'; let failedRead = false;
  const user = () => ({ id: actor, ecode: actor, ename: actor, revision: 1, mustChangePassword: false, isLocalAdministrator: false,
    timeZoneId: 'Asia/Taipei', featurePositions: ['entry', ...actions.map(action => action.position).filter(position => !options.grants || options.grants.includes(position))] });
  const request = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(input instanceof Request ? input.url : String(input), 'http://localhost');
    if (url.pathname.includes('antiforgery-token')) return json({ requestToken: 'token' });
    if (url.pathname.endsWith('/current-user')) return json(user());
    if (url.pathname.endsWith('/personal/ensure')) return json({ succeeded: true });
    if (url.pathname === '/api/workspace/spaces') return json([
      { spaceId: 'public', spaceKind: 'Public', rootFolderId: 'root', name: '公共空间', hasChildren: true },
      { spaceId: 'mine', spaceKind: 'Personal', rootFolderId: 'personal-root', name: '个人空间' },
    ]);
    const links = JSON.parse(url.searchParams.get('linkIds') ?? '[]') as string[];
    if (url.pathname.endsWith('/children')) return json({ items: links.length ? [] : ['A', 'B', 'C'].map(name => ({ linkId: `${name}-link`, associatedObject: { id: name }, fields: { Name: name }, hasChildren: false })) });
    if (url.pathname.endsWith('/path')) return json({ folderIds: ['root', ...links.map(id => id.split('-')[0])], names: ['公共空间', ...links.map(id => id.split('-')[0])] });
    if (url.pathname === '/api/links/workspace.content') {
      if (failedRead) { failedRead = false; return json({ code: 'HTTP_503' }, 503); }
      const source = url.searchParams.get('anchorId') === 'A';
      const items = source ? ['Document', 'Part'].map(modelKey => ({ linkId: `${modelKey}-lid`, associatedObject: { modelKey, id: `${modelKey}-eid` },
        fields: { Name: `${modelKey} source`, ObjectType: modelKey, UpdatedAt: '2026-10-14T00:00:00Z' } })) : [];
      return json({ items, total: items.length, page: 1, pageSize: 20 });
    }
    if (url.pathname.startsWith('/api/workspace/folders/')) return json({ fields: { ENAME: 'A' } });
    if (url.pathname.endsWith('/references/remove')) {
      const body = input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body));
      writes.push({ path: url.pathname, body });
      if (options.remove) return controlledResponse(() => options.remove!(body));
      failedRead = options.failRefresh === true; return json({ succeeded: true });
    }
    if (url.pathname.endsWith('/clipboard/validate') || url.pathname.endsWith('/clipboard/cut/validate') || url.pathname.endsWith('/paste')) {
      const body = input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body));
      writes.push({ path: url.pathname, body });
      if (url.pathname.endsWith('/paste')) {
        if (options.paste) return controlledResponse(() => options.paste!(body));
        failedRead = options.failRefresh === true; return json({ succeeded: true });
      }
      if (options.validate) return controlledResponse(() => options.validate!(body));
      return json(url.pathname.endsWith('/paste') ? { succeeded: true } : { mode: body.mode, sourcePosition: body.sourcePosition, items: body.linkIds.map((linkId: string) => ({ linkId })) });
    }
    throw new Error(`Unexpected request ${url.pathname}`);
  });
  const previousFetch = globalThis.fetch;
  vi.stubGlobal('fetch', request);
  let registrationLoads = 0;
  const bindings = { ...workspace, standardCrudCapabilities: workspace.standardCrudCapabilities?.map(binding => binding.targetKey === list.view.key
    ? { ...binding, load: () => { ++registrationLoads; return registrationLoads > 1 && options.menuLoad ? options.menuLoad(binding.load, registrationLoads) : binding.load(); } } : binding) };
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Workspace', displayName: 'Workspace', version: '1' }, ...bindings }], 'clipboard-test');
  const catalog = Object.fromEntries(Object.entries(dictionary.Workspace).map(([key, value]) => [`Workspace.${key}`, value.zhs]));
  const actions = [...list.view.pageActions.map((action: { key: string }) => ({ key: action.key, position: `page-${action.key}`, kind: 'action', parentPosition: 'entry', staticScope: 'folder', actionScope: 'list.pageActions' })),
    ...list.view.rowActions.map((action: { key: string }) => ({ key: action.key, position: `row-${action.key}`, kind: 'action', parentPosition: 'entry', staticScope: 'folder', actionScope: 'list.rowActions' })),
    ...tree.view.nodeTypes[0].contextMenu.map((action: { key: string }) => ({ key: action.key, position: `tree-${action.key}`, kind: 'action', parentPosition: 'entry', staticScope: 'folder', actionScope: 'contextMenu' }))];
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  function SwitchActor() { const auth = useAuth(); return <button data-current-actor={auth.user?.id} onClick={() => { actor = 'next'; void auth.retrySessionValidation(); }}>Switch actor</button>; }
  const router = createMemoryRouter([{ path: '/workspace', element:
    <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: catalog }}>
      <AuthProvider initialUser={user()}><AuthorizationProvider featurePositions={user().featurePositions}><PlatformQueryProvider scopeKey="clipboard-test">
        <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Workspace', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [],
          features: [{ key: 'workspace', position: 'entry', kind: 'navigation' }, ...actions] }] }, navDocuments: [], modelDocuments: [folder, content], viewDocuments: [tree, list, detail] }}>
          <SwitchActor /><PageEntry page={{ pageKey: 'platform.tree', parameters: { viewKey: tree.view.key, featurePosition: 'entry' }, inputResolverKey: 'workspace.tree.current' }} />
        </RuntimeMetadataProvider>
      </PlatformQueryProvider></AuthorizationProvider></AuthProvider>
    </LocalizationProvider> }], { initialEntries: ['/workspace'] });
  let finished = false;
  const cleanup = async () => {
    if (finished) return; finished = true; lifetime.abort(new Error('Clipboard page disposed'));
    await Promise.allSettled([...pollingActs]);
    await act(async () => { root.unmount(); for (const finish of pendingResponses) finish(); pendingResponses.clear(); });
    router.dispose(); container.remove(); if (globalThis.fetch === request) vi.stubGlobal('fetch', previousFetch); mountedPages.delete(cleanup);
  };
  mountedPages.add(cleanup);
  onTestFinished(cleanup);
  await act(async () => root.render(<StrictMode><RouterProvider router={router} /></StrictMode>));
  await waitFor(async () => {
    const results = await Promise.allSettled(request.mock.results.map(result => result.value as Promise<Response>));
    expect(container.querySelector('.ant-tree-switcher:not(.ant-tree-switcher-noop)'), `${container.textContent}; requests=${request.mock.calls.map(([input]) => String(input instanceof Request ? input.url : input)).join(',')}; results=${results.map(result => result.status === 'fulfilled' ? result.value?.status : String(result.reason)).join(',')}`).not.toBeNull();
  });
  await act(async () => container.querySelector<HTMLElement>('.ant-tree-switcher:not(.ant-tree-switcher-noop)')!.click());
  await waitFor(() => expect(container.querySelector('.ant-tree')?.textContent).toContain('A'));
  const select = async (name: string) => { await act(async () => [...container.querySelectorAll<HTMLElement>('.ant-tree-title')].find(item => item.textContent === name)!.click()); await waitFor(() => expect(container.querySelector('.np-tree-context-header h2')?.textContent).toBe(name)); };
  const toolbar = () => container.querySelector('.np-tree-content-body')!;
  const button = (name: string) => [...toolbar().querySelectorAll<HTMLButtonElement>('button')].find(item => item.textContent?.replace(/\s/g, '') === name)!;
  const copyRows = async (types: string[], action = '复制') => {
    await select('A'); await waitFor(() => expect(container.querySelector('tbody')?.textContent).toContain('Document source'));
    await act(async () => { for (const type of types) [...container.querySelectorAll<HTMLTableRowElement>('tbody tr')].find(row => row.textContent?.includes(`${type} source`))!.querySelector<HTMLInputElement>('input[type=checkbox]')!.click(); });
    await waitFor(() => expect(button(action).disabled).toBe(false)); await act(async () => button(action).click());
  };
  const openTreeMenu = async (name: string) => {
    await select(name);
    const title = [...container.querySelectorAll<HTMLElement>('.ant-tree-title')].find(item => item.textContent === name)!;
    await act(async () => title.firstElementChild!.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, clientX: 60, clientY: 80 })));
    await waitFor(() => expect(document.querySelector('[role=menuitem]')).not.toBeNull());
  };
  return { container, writes, request, select, button, copyRows, cleanup, openTreeMenu, waitFor };
}

it('ends an outstanding fixture wait when its mounted page is disposed', async () => {
  const diagnostic = vi.spyOn(console, 'error'); onTestFinished(() => diagnostic.mockRestore());
  const page = await mount();
  const waiting = page.waitFor(() => { throw new Error('still waiting'); });
  const outcome = expect(waiting).rejects.toThrow('Clipboard page disposed');
  await page.cleanup(); await outcome;
  expect(diagnostic.mock.calls.some(args => String(args[0]).includes('overlapping act()'))).toBe(false);
});

it('shows pending cut through existing granted actions across folders and clears on a confirmed same-parent success', async () => {
  const page = await mount({ grants: ['tree-cut', 'tree-paste', 'page-paste'], paste: async () => json({ succeeded: true, noChange: true }) });
  await page.openTreeMenu('A');
  const menu = (label: string) => [...document.querySelectorAll<HTMLElement>('[role=menuitem]')].find(item => item.textContent === label)!;
  await waitFor(() => expect(menu('剪切')?.classList.contains('ant-dropdown-menu-item-disabled')).toBe(false));
  await act(async () => menu('剪切').click());
  await waitFor(() => expect(page.writes).toHaveLength(1));
  expect(page.writes[0]).toEqual({ path: '/api/workspace/content/clipboard/cut/validate', body: {
    mode: 'cut', sourcePosition: { spaceId: 'public', rootFolderId: 'root', linkIds: [] }, linkIds: ['A-link'],
  } });
  await page.select('B');
  await waitFor(() => expect(page.button('粘贴引用（待移动）')?.disabled).toBe(false));
  expect(page.button('剪切（待移动）')).toBeUndefined();
  await page.openTreeMenu('B');
  await waitFor(() => expect(menu('剪切（待移动）')?.classList.contains('ant-dropdown-menu-item-disabled')).toBe(false));
  await act(async () => menu('粘贴引用（待移动）').click());
  await waitFor(() => expect(page.button('粘贴引用')?.disabled).toBe(true));
  expect(page.writes).toHaveLength(2);
  expect(document.body.textContent).toContain('位置未改变');
});

it('cuts the clicked row instead of another checked row and retains pending state on unknown outcome until explicit clear', async () => {
  const page = await mount({ paste: async () => json({ code: 'Persistence.CommitOutcomeUnknown' }, 409) });
  await page.select('A'); await waitFor(() => expect(page.container.querySelector('tbody')?.textContent).toContain('Document source'));
  const rows = [...page.container.querySelectorAll<HTMLTableRowElement>('tbody tr')];
  await act(async () => rows.find(row => row.textContent?.includes('Part source'))!.querySelector<HTMLInputElement>('input[type=checkbox]')!.click());
  await act(async () => rows.find(row => row.textContent?.includes('Document source'))!.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, clientX: 60, clientY: 80 })));
  const menu = (label: string) => [...document.querySelectorAll<HTMLElement>('[role=menuitem]')].find(item => item.textContent === label)!;
  await waitFor(() => expect(menu('剪切')?.classList.contains('ant-dropdown-menu-item-disabled')).toBe(false));
  await act(async () => menu('剪切').click());
  await waitFor(() => expect(page.writes).toHaveLength(1));
  expect(page.writes[0].body.linkIds).toEqual(['Document-lid']);
  await page.select('B'); await waitFor(() => expect(page.button('粘贴引用（待移动）')?.disabled).toBe(false));
  await act(async () => page.button('粘贴引用（待移动）').click());
  await waitFor(() => expect(page.button('粘贴引用（待移动）')?.disabled).toBe(true));
  await page.openTreeMenu('B');
  await waitFor(() => expect(menu('粘贴引用（待移动）')?.classList.contains('ant-dropdown-menu-item-disabled')).toBe(true));
  expect(menu('粘贴引用（待移动）').querySelector('[title]')?.getAttribute('title')).toContain('核对源和目标');
  await act(async () => menu('粘贴引用（待移动）').click()); expect(page.writes).toHaveLength(2);
  await act(async () => menu('清空剪贴板').click());
  await waitFor(() => expect(page.button('粘贴引用')?.disabled).toBe(true)); expect(page.writes).toHaveLength(2);
});

it('keeps toolbar cut selection through validation and clears pending after successful move even when the next read fails', async () => {
  const page = await mount({ failRefresh: true });
  await page.copyRows(['Document', 'Part'], '剪切');
  await waitFor(() => expect(page.writes).toHaveLength(1));
  expect(page.writes[0].body.linkIds).toEqual(['Document-lid', 'Part-lid']);
  await page.select('B'); await waitFor(() => expect(page.button('粘贴引用（待移动）')?.disabled).toBe(false));
  await act(async () => page.button('粘贴引用（待移动）').click());
  await waitFor(() => expect(page.button('粘贴引用')?.disabled).toBe(true));
  expect(page.button('粘贴引用（待移动）')).toBeUndefined(); expect(page.writes).toHaveLength(2);
  await page.openTreeMenu('B');
  await waitFor(() => expect([...document.querySelectorAll<HTMLElement>('[role=menuitem]')].some(item => item.textContent === '剪切')).toBe(true));
  expect(page.writes).toHaveLength(2);
});

it('confirms a fixed current-page batch removal and clears selection after success without replaying a failed refresh', async () => {
  const page = await mount({ failRefresh: true });
  await page.copyRows(['Document', 'Part'], '批量移除引用');
  await waitFor(() => expect(document.querySelector('[role=dialog]')?.textContent).toContain('保留对象及文件'));
  expect(page.writes).toEqual([]);
  const confirm = () => [...document.querySelectorAll<HTMLButtonElement>('[role=dialog] button')].find(item => item.textContent?.replace(/\s/g, '') === '确认')!;
  await act(async () => confirm().click());
  await waitFor(() => expect(page.writes).toHaveLength(1));
  expect(page.writes[0]).toEqual({ path: '/api/workspace/content/references/remove', body: {
    position: { spaceId: 'public', rootFolderId: 'root', linkIds: ['A-link'] }, linkIds: ['Document-lid', 'Part-lid'],
  } });
  await waitFor(() => expect(document.querySelector('[role=dialog]')).toBeNull());
  await waitFor(() => expect(page.button('批量移除引用')?.disabled).toBe(true)); expect(page.writes).toHaveLength(1);
});

it('replaces a cut with a new copy and restores the existing paste and cut labels', async () => {
  const page = await mount(); await page.openTreeMenu('A');
  const menu = () => [...document.querySelectorAll<HTMLElement>('[role=menuitem]')].find(item => item.textContent === '剪切')!;
  await waitFor(() => expect(menu()?.classList.contains('ant-dropdown-menu-item-disabled')).toBe(false));
  await act(async () => menu().click());
  await waitFor(() => expect(page.button('粘贴引用（待移动）')?.disabled).toBe(false));
  await page.copyRows(['Part']); await waitFor(() => expect(page.writes).toHaveLength(2));
  await waitFor(() => expect(page.button('粘贴引用')?.disabled).toBe(false));
  expect(page.button('剪切（待移动）')).toBeUndefined(); expect(page.button('剪切')).toBeDefined();
  await page.select('B'); await act(async () => page.button('粘贴引用').click());
  await waitFor(() => expect(page.writes).toHaveLength(3)); expect(page.writes[2].body.mode).toBe('copy');
});

it('projects empty and ready clipboard states into tree menus without preparing an action or writing during preview', async () => {
  const page = await mount();
  const menu = (label: string) => [...document.querySelectorAll<HTMLElement>('[role=menuitem]')].find(item => item.textContent === label)!;
  await page.openTreeMenu('B');
  await waitFor(() => expect(menu('粘贴引用').classList.contains('ant-dropdown-menu-item-disabled')).toBe(true));
  expect(menu('清空剪贴板').classList.contains('ant-dropdown-menu-item-disabled')).toBe(true);
  expect(menu('粘贴引用').querySelector('[title]')?.getAttribute('title')).toContain('请先');
  await act(async () => menu('粘贴引用').click());
  expect(page.writes).toEqual([]);
  expect(document.querySelector('.ant-drawer input')).toBeNull();
  expect(page.request.mock.calls.some(([input]) => String(input instanceof Request ? input.url : input).includes('edit-values'))).toBe(false);
  await act(async () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })));
  await page.copyRows(['Document']); await waitFor(() => expect(page.writes).toHaveLength(1));
  await page.openTreeMenu('B');
  await waitFor(() => expect(menu('粘贴引用').classList.contains('ant-dropdown-menu-item-disabled')).toBe(false));
  expect(page.writes).toHaveLength(1);
});

it('disables an open tree menu during paste and keeps clear available after an unknown receipt', async () => {
  let finish!: (value: Response) => void;
  const page = await mount({ paste: () => new Promise(resolve => { finish = resolve; }) });
  const menu = (label: string) => [...document.querySelectorAll<HTMLElement>('[role=menuitem]')].find(item => item.textContent === label)!;
  await page.copyRows(['Document']); await waitFor(() => expect(page.writes).toHaveLength(1)); await page.openTreeMenu('B');
  await waitFor(() => expect(menu('粘贴引用').classList.contains('ant-dropdown-menu-item-disabled')).toBe(false));
  await act(async () => page.button('粘贴引用').click()); await waitFor(() => expect(finish).toBeTypeOf('function'));
  await waitFor(() => expect(menu('复制').classList.contains('ant-dropdown-menu-item-disabled')).toBe(true));
  expect(menu('编辑').classList.contains('ant-dropdown-menu-item-disabled')).toBe(true);
  expect(menu('移除引用').classList.contains('ant-dropdown-menu-item-disabled')).toBe(true);
  await act(async () => menu('复制').click()); expect(page.writes).toHaveLength(2);
  await act(async () => finish(json({ code: 'Persistence.CommitOutcomeUnknown' }, 409)));
  await waitFor(() => expect(page.button('粘贴引用').disabled).toBe(true));
  await page.openTreeMenu('B');
  await waitFor(() => expect(menu('粘贴引用').classList.contains('ant-dropdown-menu-item-disabled')).toBe(true));
  expect(menu('粘贴引用').querySelector('[title]')?.getAttribute('title')).toContain('结果未知');
  expect(menu('清空剪贴板').classList.contains('ant-dropdown-menu-item-disabled')).toBe(false);
  await act(async () => menu('清空剪贴板').click()); await waitFor(() => expect(page.button('清空剪贴板').disabled).toBe(true));
  await page.openTreeMenu('B');
  await waitFor(() => expect(menu('粘贴引用').classList.contains('ant-dropdown-menu-item-disabled')).toBe(true));
  expect(page.writes).toHaveLength(2);
});

it('settles an unfinished fixture response on disposal before another page mounts and ignores its later result', async () => {
  let finish!: (value: Response) => void;
  const first = await mount({ paste: () => new Promise(resolve => { finish = resolve; }) });
  await first.copyRows(['Document']); await waitFor(() => expect(first.writes).toHaveLength(1)); await first.select('B');
  await waitFor(() => expect(first.button('粘贴引用').disabled).toBe(false)); await act(async () => first.button('粘贴引用').click());
  await waitFor(() => expect(finish).toBeTypeOf('function'));
  let settled = false;
  void (first.request.mock.results.at(-1)!.value as Promise<Response>).then(() => { settled = true; });
  await first.cleanup(); await waitFor(() => expect(settled).toBe(true));
  const next = await mount(); await next.select('B');
  await act(async () => finish(json({ succeeded: true })));
  expect(next.button('粘贴引用').disabled).toBe(true); expect(next.writes).toEqual([]); expect(first.writes).toHaveLength(2);
});

it('keeps failed menu registration unavailable and ignores a late registration from the previous actor', async () => {
  const diagnostic = vi.spyOn(console, 'error').mockImplementation(() => undefined); onTestFinished(() => diagnostic.mockRestore());
  const failed = await mount({ menuLoad: async () => { throw new Error('menu-binding-failure'); } });
  await failed.openTreeMenu('B');
  await waitFor(() => expect(diagnostic).toHaveBeenCalledWith('Tree action registration unavailable.', expect.any(Error)));
  const copy = [...document.querySelectorAll<HTMLElement>('[role=menuitem]')].find(item => item.textContent === '复制')!;
  expect(copy.classList.contains('ant-dropdown-menu-item-disabled')).toBe(true);
  await act(async () => copy.click()); expect(failed.writes).toEqual([]);
  expect(failed.container.querySelector('.np-tree-context-header h2')?.textContent).toBe('B');
  await failed.cleanup();
  let finish!: () => Promise<void>;
  const page = await mount({ menuLoad: (load, call) => call !== 2 ? load() : new Promise(resolve => { finish = async () => resolve(await load()); }) });
  await page.openTreeMenu('B'); await waitFor(() => expect(finish).toBeTypeOf('function'));
  await act(async () => [...page.container.querySelectorAll<HTMLButtonElement>('button')].find(item => item.textContent === 'Switch actor')!.click());
  await waitFor(() => expect(page.container.querySelector('[data-current-actor]')?.getAttribute('data-current-actor')).toBe('next'));
  await act(async () => finish()); await page.openTreeMenu('B');
  await waitFor(() => expect([...document.querySelectorAll<HTMLElement>('[role=menuitem]')].find(item => item.textContent === '粘贴引用')?.classList.contains('ant-dropdown-menu-item-disabled')).toBe(true));
  expect(page.writes).toEqual([]);
});

it('blocks right-list input opening while a tree paste is pending and allows it again after the receipt', async () => {
  let finish!: (value: Response) => void;
  const page = await mount({ paste: () => new Promise(resolve => { finish = resolve; }) });
  await page.copyRows(['Document']); await waitFor(() => expect(page.writes).toHaveLength(1)); await page.openTreeMenu('B');
  const paste = () => [...document.querySelectorAll<HTMLElement>('[role=menuitem]')].find(item => item.textContent === '粘贴引用')!;
  await waitFor(() => expect(paste().classList.contains('ant-dropdown-menu-item-disabled')).toBe(false));
  await act(async () => paste().click()); await waitFor(() => expect(finish).toBeTypeOf('function'));
  await act(async () => page.button('新建').click());
  const create = () => [...document.querySelectorAll<HTMLElement>('[role=menuitem]')].find(item => item.textContent === '新建图文档')!;
  await waitFor(() => expect(create().classList.contains('ant-dropdown-menu-item-disabled')).toBe(true));
  await act(async () => create().click()); expect(document.querySelector('.ant-drawer input')).toBeNull(); expect(page.writes).toHaveLength(2);
  await act(async () => finish(json({ succeeded: true })));
  await waitFor(() => expect(page.button('粘贴引用').disabled).toBe(false));
  await act(async () => page.button('新建').click());
  await waitFor(() => expect(create().classList.contains('ant-dropdown-menu-item-disabled')).toBe(false));
});

it('binds row copy to the clicked LID, keeps the snapshot across directories and preserves it after two successful pastes', async () => {
  const page = await mount(); await page.select('A');
  await waitFor(() => expect(page.container.querySelector('tbody')?.textContent).toContain('Document source'));
  const rows = [...page.container.querySelectorAll<HTMLTableRowElement>('tbody tr')];
  await act(async () => rows.find(row => row.textContent?.includes('Part source'))!.querySelector<HTMLInputElement>('input[type=checkbox]')!.click());
  await act(async () => rows.find(row => row.textContent?.includes('Document source'))!.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, clientX: 60, clientY: 80 })));
  await waitFor(() => expect(document.querySelector('[role=menuitem]')).not.toBeNull());
  const copy = [...document.querySelectorAll<HTMLElement>('[role=menuitem]')].find(item => item.textContent === '复制' || item.textContent === 'Copy')!;
  expect(copy.classList.contains('ant-dropdown-menu-item-disabled')).toBe(false);
  await act(async () => copy.click());
  await waitFor(() => expect(page.writes).toHaveLength(1));
  expect(page.writes[0].body).toEqual({ mode: 'copy', sourcePosition: { spaceId: 'public', rootFolderId: 'root', linkIds: ['A-link'] }, linkIds: ['Document-lid'] });
  for (const name of ['B', 'C']) {
    await page.select(name); await waitFor(() => expect(page.button('粘贴引用')?.disabled).toBe(false));
    await act(async () => page.button('粘贴引用').click());
    await waitFor(() => expect(page.writes.filter(item => item.path.endsWith('/paste'))).toHaveLength(name === 'B' ? 1 : 2));
  }
  expect(page.writes.slice(1).map(item => item.body)).toEqual(['B', 'C'].map(name => ({ mode: 'copy', sourcePosition: { spaceId: 'public', rootFolderId: 'root', linkIds: ['A-link'] }, linkIds: ['Document-lid'], targetPosition: { spaceId: 'public', rootFolderId: 'root', linkIds: [`${name}-link`] } })));
});

it('uses current-page toolbar selection and shares tree action state with the right list while isolating another tree instance', async () => {
  const first = await mount(); const second = await mount();
  // 两个真实页面共享浏览器transport；后挂的拦截器收集两者请求，实例状态仍须隔离。
  await first.copyRows(['Document', 'Part']); await waitFor(() => expect(second.writes).toHaveLength(1));
  expect(second.writes[0].body.linkIds).toEqual(['Document-lid', 'Part-lid']);
  await first.select('B'); await waitFor(() => expect(first.button('粘贴引用').disabled).toBe(false));
  await second.select('B'); await waitFor(() => expect(second.button('粘贴引用').disabled).toBe(true));
  await act(async () => first.button('清空剪贴板').click());
  await waitFor(() => expect(first.button('粘贴引用').disabled).toBe(true));
  const title = [...first.container.querySelectorAll<HTMLElement>('.ant-tree-title')].find(item => item.textContent === 'A')!;
  await act(async () => title.firstElementChild!.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, clientX: 60, clientY: 80 })));
  await waitFor(() => expect([...document.querySelectorAll('[role=menuitem]')].some(item => item.textContent === '复制')).toBe(true));
  await act(async () => [...document.querySelectorAll<HTMLElement>('[role=menuitem]')].find(item => item.textContent === '复制')!.click());
  await waitFor(() => expect(second.writes).toHaveLength(2));
  expect(second.writes[1].body).toEqual({ mode: 'copy', sourcePosition: { spaceId: 'public', rootFolderId: 'root', linkIds: [] }, linkIds: ['A-link'] });
  expect(document.querySelector('[role=dialog]')).toBeNull();
  await first.select('B'); await waitFor(() => expect(first.button('粘贴引用').disabled).toBe(false));
  expect(second.button('粘贴引用').disabled).toBe(true);
});

it('clears on actor change and ignores a late validation from the old tree instead of reviving it', async () => {
  let finish!: (value: Response) => void;
  const page = await mount({ validate: () => new Promise(resolve => { finish = resolve; }) });
  await page.copyRows(['Document']); await waitFor(() => expect(finish).toBeTypeOf('function'));
  await act(async () => [...page.container.querySelectorAll<HTMLButtonElement>('button')].find(item => item.textContent === 'Switch actor')!.click());
  await waitFor(() => expect(page.container.querySelector('[data-current-actor]')?.getAttribute('data-current-actor')).toBe('next'));
  await act(async () => finish(json({ mode: 'copy', items: [{ linkId: 'Document-lid' }] })));
  await waitFor(() => expect(page.button('粘贴引用').disabled).toBe(true));
  expect(page.container.textContent).not.toContain('已复制引用');
});

it('locks unknown paste outcomes without replay and clears the snapshot when the page is destroyed and reloaded', async () => {
  const page = await mount({ paste: async () => json({ code: 'Persistence.CommitOutcomeUnknown' }, 409) });
  await page.copyRows(['Document']); await waitFor(() => expect(page.writes).toHaveLength(1)); await page.select('B');
  await waitFor(() => expect(page.button('粘贴引用').disabled).toBe(false)); await act(async () => page.button('粘贴引用').click());
  await waitFor(() => expect(page.button('粘贴引用').disabled).toBe(true));
  // 共享Host通过Ant消息portal呈现执行错误，它在document.body而非页面container内。
  await waitFor(() => expect([...document.querySelectorAll('.ant-message-notice-content')].some(item => item.textContent?.includes('本次粘贴结果未知'))).toBe(true));
  await act(async () => page.button('粘贴引用').click()); expect(page.writes.filter(item => item.path.endsWith('/paste'))).toHaveLength(1);
  await page.cleanup(); const reloaded = await mount(); await reloaded.select('B');
  await waitFor(() => expect(reloaded.button('粘贴引用').disabled).toBe(true));
});

it('keeps a committed clipboard after refresh failure and retries only reading', async () => {
  const page = await mount({ failRefresh: true }); await page.copyRows(['Document']); await waitFor(() => expect(page.writes).toHaveLength(1));
  await page.select('B'); await waitFor(() => expect(page.button('粘贴引用').disabled).toBe(false)); await act(async () => page.button('粘贴引用').click());
  await waitFor(() => expect(page.container.textContent).toContain('已保存，刷新失败'));
  await act(async () => page.button('刷新').click()); await waitFor(() => expect(page.container.textContent).not.toContain('已保存，刷新失败'));
  expect(page.writes.filter(item => item.path.endsWith('/paste'))).toHaveLength(1); expect(page.button('粘贴引用').disabled).toBe(false);
});
