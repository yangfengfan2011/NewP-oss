import { toolbarMenuAction, toolbarActionDisabled } from './toolbarTestActions';
import { workspaceTreeRoots, workspaceTreePath, workspaceTreeChildren, workspaceTreePosition } from '../test-support/workspaceTreeFixtures';
// @vitest-environment jsdom
import { act, StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, beforeEach, expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry, type ActionDataAccessRequirement, type FrontendBehaviorBindings, type StandardCrudResourceRegistration } from '@icp-platform/sdk';
import clipboardContract from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceClipboardDataAccess.contract.json';
import { bindActionDataAccess } from '../metadata/runtimeMetadataLoader';
import { loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import folder from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceFolderRuntime.contract.json';
import { RuntimeMetadataProvider } from './formalRuntimeMetadataProvider';
import { PageEntry } from '../composition/PageOutlet';
import { AuthProvider, useAuth } from '../contexts/AuthContext';
import '../composition/StandardTreePage';
import '../pages/StandardCrudListPageHost';
import '../pages/StandardCrudListOverlayRuntime';
const { default: workspace } = await loadComponentSource<{ default: FrontendBehaviorBindings }>('Workspace', 'index.ts');
await loadComponentSource('Workspace', 'workspaceRootResource.ts');
await loadComponentSource('Workspace', 'WorkspaceFolderCopyInput.tsx');
await loadComponentSource('Workspace', 'WorkspaceObjectCopyInput.tsx');
const content = readComponentJson('Workspace', 'metadata/models/WorkspaceFolderContent.model.json');
const tree = readComponentJson('Workspace', 'metadata/views/WorkspaceStructure.view.json');
const list = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderList.view.json');
const detail = readComponentJson('Workspace', 'metadata/views/WorkspaceFolderDetail.view.json');
const dictionary = readComponentJson('Workspace', 'metadata/dictionaries/Workspace.dic.json');
const partDictionary = readComponentJson('Part', 'metadata/dictionaries/Part.dic.json');
const documentDictionary = readComponentJson('Document', 'metadata/dictionaries/Document.dic.json');
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

async function mount(options: { actionSets?: Record<string, { column: string[]; contextMenu: string[] }>; columnMode?: 'default' | 'menu'; folderRow?: boolean; validate?: (body: Record<string, unknown>) => Promise<Response>; paste?: (body: Record<string, unknown>) => Promise<Response>; failRefresh?: boolean;
  folderCopy?: (body: Record<string, unknown>) => Promise<Response>; objectCopy?: (body: Record<string, unknown>) => Promise<Response>; grants?: readonly string[]; remove?: (body: Record<string, unknown>) => Promise<Response>; formalDeclarations?: boolean; documentBatch?: boolean;
  menuLoad?: (load: () => Promise<{ default: StandardCrudResourceRegistration }>, call: number) => Promise<{ default: StandardCrudResourceRegistration }> } = {}) {
  const lifetime = new AbortController();
  const waitFor = (check: () => void | Promise<void>) => waitForActive(check, lifetime.signal);
  const pendingResponses = new Set<() => void>();
  const controlledResponse = (operation: () => Promise<Response>) => new Promise<Response>((resolve, reject) => {
    const finish = () => resolve(json({ code: 'Workspace.ClipboardOutcomeUnknown' }, 503)); pendingResponses.add(finish);
    operation().then(value => { pendingResponses.delete(finish); resolve(value); }, failure => { pendingResponses.delete(finish); reject(failure); });
  });
  const writes: Array<{ path: string; body: Record<string, unknown> }> = [];
  const declarations = clipboardContract.declarations as ActionDataAccessRequirement[];
  const entryPosition = options.formalDeclarations ? clipboardContract.features.find(point => point.kind === 'navigation' && point.key === 'workspace')!.position : 'entry';
  let actor = 'actor'; let failedRead = false;
  const user = () => ({ id: actor, ecode: actor, ename: actor, revision: 1, mustChangePassword: false, isLocalAdministrator: false,
    timeZoneId: 'Asia/Taipei', featurePositions: [entryPosition, ...actions.map(action => action.position).filter(position => !options.grants || options.grants.includes(position))] });
  const request = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(input instanceof Request ? input.url : String(input), 'http://localhost');
    if (url.pathname.includes('antiforgery-token')) return json({ requestToken: 'token' });
    if (url.pathname.endsWith('/current-user')) return json(user());
    if (url.pathname === '/api/workspace/ownership/eligibility') return json({ code: 'FORBIDDEN' }, 403);
    if (url.pathname === '/api/workspace/ownership/transfer/eligibility') return json({ code: 'FORBIDDEN' }, 403);
    if (url.pathname.startsWith('/api/workspace/content/') && url.pathname.endsWith('/permissions/eligibility')) return json({ code: 'FORBIDDEN' }, 403);
    if (url.pathname.endsWith('/personal/ensure')) return json({ succeeded: true });
    if (url.pathname === '/api/trees/workspace.structure/roots') return json(workspaceTreeRoots([
      { spaceId: 'public', spaceKind: 'Public', rootFolderId: 'root', name: '公共空间', hasChildren: true },
      { spaceId: 'mine', spaceKind: 'Personal', rootFolderId: 'personal-root', name: '个人空间' },
    ]));
    const nodeKey = url.searchParams.get('nodeKey') ?? ''; const { linkIds: links } = workspaceTreePosition(nodeKey);
    if (url.pathname.endsWith('/children')) return json(workspaceTreeChildren(nodeKey, links.length ? (options.folderRow && links.length === 1 ? [{ linkId: 'WorkspaceFolder-lid', associatedObject: { id: 'WorkspaceFolder' }, fields: { Name: 'WorkspaceFolder' }, hasChildren: false }] : []) : ['A', 'B', 'C'].map(name => ({ linkId: `${name}-link`, associatedObject: { id: name }, fields: { Name: name }, hasChildren: false }))));
    if (url.pathname.endsWith('/path')) return json(workspaceTreePath(nodeKey, ['root', ...links.map(id => id.split('-')[0])], ['公共空间', ...links.map(id => id.split('-')[0])]));
    if (url.pathname === '/api/links/workspace.content') {
      if (failedRead) { failedRead = false; return json({ code: 'HTTP_503' }, 503); }
      const source = url.searchParams.get('anchorId') === 'A';
      const items = source ? [...(options.folderRow ? ['WorkspaceFolder'] : []), 'Document', 'Part'].map(modelKey => ({ linkId: `${modelKey}-lid`, associatedObject: { modelKey, id: `${modelKey}-eid` },
        fields: { Name: `${modelKey} source`, ObjectType: modelKey, UpdatedAt: '2026-10-14T00:00:00Z' } })) : [];
      if (source && options.documentBatch) items.push({ linkId: 'Document-second-lid', associatedObject: { modelKey: 'Document', id: 'Document-second-eid' },
        fields: { Name: 'SecondDocument source', ObjectType: 'Document', UpdatedAt: '2026-10-14T00:00:00Z' } });
      return json({ items, total: items.length, page: 1, pageSize: 20 });
    }
    if (url.pathname.startsWith('/api/workspace/folders/')) return json({ fields: { ENAME: 'A' } });
    if (url.pathname.endsWith('/objects/copy/prepare')) {
      const body = input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body));
      writes.push({ path: url.pathname, body });
      if (body.linkIds.length > 1) {
        const items = body.linkIds.map((sourceLinkId: string) => ({ sourceLinkId,
          values: { ECODE: '', ENAME: sourceLinkId === 'Document-lid' ? 'Document source' : 'SecondDocument source', Description: '' },
          sourcePath: ['公共空间', 'A', sourceLinkId === 'Document-lid' ? 'Document source' : 'SecondDocument source'], fileCount: 2, totalBytes: 8 }));
        return json({ operationId: 'actual-operation', modelKey: 'Document', items, values: { items: items.map(({ sourceLinkId, values }: { sourceLinkId: string; values: Record<string, unknown> }) => ({ sourceLinkId, values })) },
          sourcePath: ['公共空间', 'A'], targetPath: ['公共空间', 'B'], fileCount: 4, totalBytes: 16 });
      }
      return json({ operationId: 'actual-operation', modelKey: body.linkIds[0].startsWith('Part') ? 'Part' : 'Document',
        values: { ECODE: '', ENAME: '确认的来源', Description: '' }, sourcePath: ['公共空间', 'A'], targetPath: ['公共空间', 'B'] });
    }
    if (url.pathname.endsWith('/objects/copy')) {
      const body = input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body));
      writes.push({ path: url.pathname, body });
      return options.objectCopy ? controlledResponse(() => options.objectCopy!(body)) : json({ succeeded: true, state: 'committed',
        ...(Array.isArray(body.values?.items) ? { items: body.values.items.map((item: { sourceLinkId: string }) => ({ sourceLinkId: item.sourceLinkId, succeeded: true, state: 'committed' })) } : {}) });
    }
    if (['/folders/copy/prepare', '/folders/copy-structure/prepare'].some(path => url.pathname.endsWith(path))) return json({ sourceName: 'WorkspaceFolder source', sourcePath: ['公共空间', 'A', 'WorkspaceFolder source'], targetPath: ['公共空间', 'B'] });
    if (['/folders/copy', '/folders/copy-structure'].some(path => url.pathname.endsWith(path))) {
      const body = input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body));
      writes.push({ path: url.pathname, body });
      if (options.folderCopy) return controlledResponse(() => options.folderCopy!(body));
      failedRead = options.failRefresh === true; return json({ succeeded: true });
    }
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
      return json(url.pathname.endsWith('/paste') ? { succeeded: true } : { mode: body.mode, sourcePosition: body.sourcePosition, items: body.linkIds.map((linkId: string) => ({ linkId,
        modelKey: linkId.startsWith('Document') ? 'Document' : linkId.startsWith('Part') ? 'Part' : 'WorkspaceFolder' })) });
    }
    throw new Error(`Unexpected request ${url.pathname}`);
  });
  const previousFetch = globalThis.fetch;
  vi.stubGlobal('fetch', request);
  let registrationLoads = 0;
  const bindings = { ...workspace, standardCrudCapabilities: workspace.standardCrudCapabilities?.map(binding => binding.targetKey === list.view.key
    ? { ...binding, load: () => { ++registrationLoads; return registrationLoads > 1 && options.menuLoad ? options.menuLoad(binding.load, registrationLoads) : binding.load(); } } : binding) };
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Workspace', displayName: 'Workspace', version: '1' }, ...bindings }], 'clipboard-test');
  const catalog = Object.fromEntries([
    ...Object.entries(dictionary.Workspace).map(([key, value]) => [`Workspace.${key}`, value.zhs]),
    ...Object.entries(partDictionary.Part).map(([key, value]) => [`Part.${key}`, value.zhs]),
    ...Object.entries(documentDictionary.Document).map(([key, value]) => [`Document.${key}`, value.zhs]),
  ]);
  const actions = options.formalDeclarations ? clipboardContract.features.filter(point => point.kind === 'action') : [...list.view.pageActions.map((action: { key: string }) => ({ key: action.key, position: `page-${action.key}`, kind: 'action', parentPosition: 'entry', staticScope: 'folder', actionScope: 'list.pageActions' })),
    ...list.view.rowActions.map((action: { key: string }) => ({ key: action.key, position: `row-${action.key}`, kind: 'action', parentPosition: 'entry', staticScope: 'folder', actionScope: 'list.rowActions' })),
    ...tree.view.nodeTypes[0].contextMenu.map((action: { key: string }) => ({ key: action.key, position: `tree-${action.key}`, kind: 'action', parentPosition: 'entry', staticScope: 'folder', actionScope: 'contextMenu' }))];
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  function SwitchActor() { const auth = useAuth(); return <button data-current-actor={auth.user?.id} onClick={() => { actor = 'next'; void auth.retrySessionValidation(); }}>Switch actor</button>; }
  const router = createMemoryRouter([{ path: '/workspace', element:
    <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: catalog }}>
      <AuthProvider initialUser={user()}><AuthorizationProvider featurePositions={user().featurePositions}><PlatformQueryProvider scopeKey="clipboard-test">
        <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Workspace', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [],
          features: options.formalDeclarations ? clipboardContract.features : [{ key: 'workspace', position: 'entry', kind: 'navigation' }, ...actions],
          ...(options.formalDeclarations ? { dataAccess: declarations } : {}) }] }, navDocuments: [], modelDocuments: [folder, content], viewDocuments: [tree,
          options.formalDeclarations ? bindActionDataAccess({ ...list, type: 'view' }, declarations)
            : { ...list, view: { ...list.view, rowActionSets: options.actionSets ?? list.view.rowActionSets, rowActionColumn: options.columnMode ?? list.view.rowActionColumn } }, detail] }}>
          <SwitchActor /><PageEntry page={{ pageKey: 'platform.tree', parameters: { viewKey: tree.view.key, featurePosition: entryPosition }, inputResolverKey: 'workspace.tree.current' }} />
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
  const select = async (name: string) => { await act(async () => [...container.querySelectorAll<HTMLElement>('.ant-tree-title')].find(item => item.textContent === name)!.click()); await waitFor(() => expect(container.querySelector('.np-tree-context-header [aria-current="page"]')?.textContent).toBe(name)); };
  const toolbar = () => container.querySelector('.np-tree-content-body')!;
  const button = (name: string) => [...toolbar().querySelectorAll<HTMLButtonElement>('button')].find(item => item.textContent?.replace(/\s/g, '') === name)!;
  const actionGroup = (name: string) => ['移除', '删除'].includes(name) ? '移除与删除' : '粘贴';
  const action = (name: string) => toolbarMenuAction(toolbar(), actionGroup(name), name);
  const disabled = async (name: string) => toolbarActionDisabled(await action(name));
  const click = async (name: string) => { const item = await action(name); expect(item).toBeDefined(); await act(async () => item!.click()); };
  const hasAction = async (name: string) => Boolean(await action(name));
  const copyRows = async (types: string[], action = '复制') => {
    await select('A'); await waitFor(() => expect(container.querySelector('tbody')?.textContent).toContain('Document source'));
    await act(async () => { for (const type of types) [...container.querySelectorAll<HTMLTableRowElement>('tbody tr')].find(row => row.textContent?.includes(`${type} source`))!.querySelector<HTMLInputElement>('input[type=checkbox]')!.click(); });
    if (action === '移除') { await waitFor(async () => expect(await disabled(action)).toBe(false)); await click(action); }
    else { await waitFor(() => expect(button(action).disabled).toBe(false)); await act(async () => button(action).click()); }
  };
  const openTreeMenu = async (name: string) => {
    await select(name);
    const title = [...container.querySelectorAll<HTMLElement>('.ant-tree-title')].find(item => item.textContent === name)!;
    await act(async () => title.firstElementChild!.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, clientX: 60, clientY: 80 })));
    await waitFor(() => expect(document.querySelector('[role=menuitem]')).not.toBeNull());
  };
  return { router, container, writes, request, select, button, disabled, click, hasAction, copyRows, cleanup, openTreeMenu, waitFor };
}

it.each(['Part', 'Document'] as const)('routes the actual %s object-copy drawer through the bound Provider and the explicit model transport', async model => {
  const page = await mount({ formalDeclarations: true });
  const declarations = clipboardContract.declarations.filter(item => item.viewKey === list.view.key && item.surface === 'pageActions' && item.actionKey === 'pasteObjectCopy');
  expect(declarations).toHaveLength(1);
  const declaration = declarations[0];
  expect(declaration.execution).toBe('workspace.objectCopy');
  await page.copyRows([model]);
  await page.select('B');
  await page.waitFor(async () => expect(await page.disabled('粘贴对象副本')).toBe(false));
  await page.click('粘贴对象副本');
  await page.waitFor(() => expect(document.querySelector('.ant-drawer input[id="ECODE"]')).not.toBeNull());
  const code = document.querySelector<HTMLInputElement>('.ant-drawer input[id="ECODE"]')!;
  await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(code, 'PAGE-COPY'); code.dispatchEvent(new Event('input', { bubbles: true })); });
  const save = () => [...document.querySelectorAll<HTMLButtonElement>('.ant-drawer button')].find(button => button.textContent?.replace(/\s/g, '') === '保存并关闭')!;
  await page.waitFor(() => expect(save()?.disabled).toBe(false));
  await act(async () => save().click());
  await page.waitFor(() => expect(page.writes.some(write => write.path.endsWith('/objects/copy')), document.querySelector('.ant-drawer')?.textContent ?? 'drawer closed').toBe(true));
  await page.waitFor(() => expect(document.querySelector('.ant-drawer-open')).toBeNull());
  for (const path of ['/objects/copy/prepare', '/objects/copy']) {
    const calls = page.request.mock.calls.filter(([input]) => new URL(input instanceof Request ? input.url : String(input), 'http://localhost').pathname.endsWith(path));
    expect(calls.length).toBeGreaterThan(0);
    for (const call of calls) {
      const headers = new Headers(call[0] instanceof Request ? call[0].headers : call[1]?.headers);
      expect(headers.get('X-NewP-Data-Declaration')).toBe(declaration.id);
      expect(declaration.featurePositions).toContain(headers.get('X-NewP-Feature'));
    }
  }
  expect(page.writes.filter(write => write.path.endsWith('/objects/copy'))).toHaveLength(1);
});

it('routes every Document batch row through the actual PageEntry Provider and the same formal copy declaration', async () => {
  const page = await mount({ formalDeclarations: true, documentBatch: true });
  const declarations = clipboardContract.declarations.filter(item => item.viewKey === list.view.key && item.surface === 'pageActions' && item.actionKey === 'pasteObjectCopy');
  expect(declarations).toHaveLength(1);
  const declaration = declarations[0];
  await page.copyRows(['Document', 'SecondDocument']);
  await page.select('B');
  await page.waitFor(async () => expect(await page.disabled('粘贴对象副本')).toBe(false));
  await page.click('粘贴对象副本');
  await page.waitFor(() => expect(document.querySelectorAll('.ant-drawer section[aria-label]')).toHaveLength(2));
  const codes = ['BATCH-FIRST', 'BATCH-SECOND'];
  for (let index = 0; index < codes.length; index++) {
    const code = document.querySelector<HTMLInputElement>(`.ant-drawer input[id="object-copy-${index}-ECODE"]`)!;
    expect(code).not.toBeNull();
    await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(code, codes[index]); code.dispatchEvent(new Event('input', { bubbles: true })); });
  }
  const save = () => [...document.querySelectorAll<HTMLButtonElement>('.ant-drawer button')].find(button => button.textContent?.replace(/\s/g, '') === '保存并关闭')!;
  await page.waitFor(() => expect(save()?.disabled).toBe(false));
  await act(async () => save().click());
  await page.waitFor(() => expect(page.writes.some(write => write.path.endsWith('/objects/copy'))).toBe(true));
  const submission = page.writes.filter(write => write.path.endsWith('/objects/copy'));
  expect(submission).toHaveLength(1);
  expect(submission[0].body.values).toEqual({ items: ['Document-lid', 'Document-second-lid'].map((sourceLinkId, index) => ({ sourceLinkId,
    values: { ECODE: codes[index], ENAME: index === 0 ? 'Document source' : 'SecondDocument source', Description: '' } })) });
  await page.waitFor(() => expect(document.querySelector('.ant-drawer-open')).toBeNull());
  await page.waitFor(() => expect(document.querySelector('.ant-modal')?.textContent).toContain('对象副本结果'));
  const close = [...document.querySelectorAll<HTMLButtonElement>('.ant-modal button')].find(button => button.textContent?.replace(/\s/g, '') === '关闭')!;
  await act(async () => close.click());
  for (const call of page.request.mock.calls.filter(([input]) => /\/objects\/copy(?:\/prepare)?$/.test(new URL(input instanceof Request ? input.url : String(input), 'http://localhost').pathname))) {
    const headers = new Headers(call[0] instanceof Request ? call[0].headers : call[1]?.headers);
    expect(headers.get('X-NewP-Data-Declaration')).toBe(declaration.id);
    expect(declaration.featurePositions).toContain(headers.get('X-NewP-Feature'));
  }
});

it.each(['copy', 'cut'] as const)('uses the compiled %s button through the actual tree Provider and current paste occurrence', async mode => {
  const page = await mount({ formalDeclarations: true });
  const menu = (label: string) => [...document.querySelectorAll<HTMLElement>('.ant-dropdown:not(.ant-dropdown-hidden):not(.np-crud-header-menu) [role=menuitem]')]
    .find(item => !item.closest('.np-crud-header-menu') && item.textContent === label)!;
  await page.openTreeMenu('A');
  await page.waitFor(() => expect(menu(mode === 'copy' ? '复制' : '剪切')?.classList.contains('ant-dropdown-menu-item-disabled')).toBe(false));
  await act(async () => menu(mode === 'copy' ? '复制' : '剪切').click());
  await page.waitFor(() => expect(page.writes).toHaveLength(1));
  await page.openTreeMenu('B');
  const pasteLabel = mode === 'cut' ? '粘贴引用（待移动）' : '粘贴引用';
  await page.waitFor(() => expect(menu(pasteLabel)?.classList.contains('ant-dropdown-menu-item-disabled')).toBe(false));
  await act(async () => menu(pasteLabel).click());
  await page.waitFor(() => expect(page.writes).toHaveLength(2));
  for (const [path, surface, key] of [
    [mode === 'copy' ? '/clipboard/validate' : '/clipboard/cut/validate', 'rowActions', mode], ['/paste', 'pageActions', 'paste'],
  ]) {
    const call = page.request.mock.calls.find(([input]) => String(input instanceof Request ? input.url : input).endsWith(path))!;
    const headers = new Headers(call[0] instanceof Request ? call[0].headers : call[1]?.headers);
    const declaration = clipboardContract.declarations.find(item => item.surface === surface && item.actionKey === key)!;
    expect(headers.get('X-NewP-Button')).toBe(declaration.id);
    expect(headers.has('X-NewP-Data-Declaration')).toBe(false);
    expect(declaration.featurePositions).toContain(headers.get('X-NewP-Feature'));
    expect(headers.get('X-NewP-Feature')).toContain('/surface:contextMenu/');
  }
  if (mode === 'copy') {
    await page.openTreeMenu('B');
    await page.waitFor(() => expect(menu('清空剪贴板')?.classList.contains('ant-dropdown-menu-item-disabled')).toBe(false));
    await act(async () => menu('清空剪贴板').click());
    expect(page.writes).toHaveLength(2);
  }
});

it('uses compiled batch buttons through selection and the real removal review Provider', async () => {
  const page = await mount({ formalDeclarations: true });
  await page.copyRows(['Document', 'Part']);
  await page.waitFor(() => expect(page.writes).toHaveLength(1));
  await page.click('移除');
  await page.waitFor(() => expect(document.querySelector('[role=dialog]')?.textContent).toContain('保留对象及文件'));
  await act(async () => [...document.querySelectorAll<HTMLButtonElement>('[role=dialog] button')]
    .find(item => item.textContent?.replace(/\s/g, '') === '确认')!.click());
  await page.waitFor(() => expect(page.writes).toHaveLength(2));
  for (const [path, key] of [['/clipboard/validate', 'copySelected'], ['/references/remove', 'removeSelected']]) {
    const call = page.request.mock.calls.find(([input]) => String(input instanceof Request ? input.url : input).endsWith(path))!;
    const headers = new Headers(call[0] instanceof Request ? call[0].headers : call[1]?.headers);
    const declaration = clipboardContract.declarations.find(item => item.surface === 'pageActions' && item.actionKey === key)!;
    expect(headers.get('X-NewP-Button')).toBe(declaration.id);
    expect(headers.has('X-NewP-Data-Declaration')).toBe(false);
    expect(declaration.featurePositions).toContain(headers.get('X-NewP-Feature'));
  }
});

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
  const menu = (label: string) => [...document.querySelectorAll<HTMLElement>('.ant-dropdown:not(.ant-dropdown-hidden):not(.np-crud-header-menu) [role=menuitem]')].find(item => !item.closest('.np-crud-header-menu') && item.textContent === label)!;
  await waitFor(() => expect(menu('剪切')?.classList.contains('ant-dropdown-menu-item-disabled')).toBe(false));
  await act(async () => menu('剪切').click());
  await waitFor(() => expect(page.writes).toHaveLength(1));
  expect(page.writes[0]).toEqual({ path: '/api/workspace/content/clipboard/cut/validate', body: {
    mode: 'cut', sourcePosition: { spaceId: 'public', rootFolderId: 'root', linkIds: [] }, linkIds: ['A-link'],
  } });
  await page.select('B');
  await waitFor(async () => expect(await page.disabled('粘贴引用（待移动）')).toBe(false));
  expect(page.button('剪切（待移动）')).toBeUndefined();
  await page.openTreeMenu('B');
  await waitFor(() => expect(menu('剪切（待移动）')?.classList.contains('ant-dropdown-menu-item-disabled')).toBe(false));
  await act(async () => menu('粘贴引用（待移动）').click());
  await waitFor(async () => expect(await page.disabled('粘贴引用')).toBe(true));
  expect(page.writes).toHaveLength(2);
  expect(document.body.textContent).toContain('位置未改变');
});

it('cuts the clicked row instead of another checked row and retains pending state on unknown outcome until explicit clear', async () => {
  const page = await mount({ paste: async () => json({ code: 'Persistence.CommitOutcomeUnknown' }, 409) });
  await page.select('A'); await waitFor(() => expect(page.container.querySelector('tbody')?.textContent).toContain('Document source'));
  const rows = [...page.container.querySelectorAll<HTMLTableRowElement>('tbody tr')];
  await act(async () => rows.find(row => row.textContent?.includes('Part source'))!.querySelector<HTMLInputElement>('input[type=checkbox]')!.click());
  await act(async () => rows.find(row => row.textContent?.includes('Document source'))!.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, clientX: 60, clientY: 80 })));
  const menu = (label: string) => [...document.querySelectorAll<HTMLElement>('.ant-dropdown:not(.ant-dropdown-hidden):not(.np-crud-header-menu) [role=menuitem]')].find(item => !item.closest('.np-crud-header-menu') && item.textContent === label)!;
  await waitFor(() => expect(menu('剪切')?.classList.contains('ant-dropdown-menu-item-disabled')).toBe(false));
  await act(async () => menu('剪切').click());
  await waitFor(() => expect(page.writes).toHaveLength(1));
  expect(page.writes[0].body.linkIds).toEqual(['Document-lid']);
  await page.select('B'); await waitFor(async () => expect(await page.disabled('粘贴引用（待移动）')).toBe(false));
  await page.click('粘贴引用（待移动）');
  await waitFor(async () => expect(await page.disabled('粘贴引用（待移动）')).toBe(true));
  await page.openTreeMenu('B');
  await waitFor(() => expect(menu('粘贴引用（待移动）')?.classList.contains('ant-dropdown-menu-item-disabled')).toBe(true));
  expect(menu('粘贴引用（待移动）').querySelector('[title]')?.getAttribute('title')).toContain('核对源和目标');
  await act(async () => menu('粘贴引用（待移动）').click()); expect(page.writes).toHaveLength(2);
  await act(async () => menu('清空剪贴板').click());
  await waitFor(async () => expect(await page.disabled('粘贴引用')).toBe(true)); expect(page.writes).toHaveLength(2);
});

it('keeps toolbar cut selection through validation and clears pending after successful move even when the next read fails', async () => {
  const page = await mount({ failRefresh: true });
  await page.copyRows(['Document', 'Part'], '剪切');
  await waitFor(() => expect(page.writes).toHaveLength(1));
  expect(page.writes[0].body.linkIds).toEqual(['Document-lid', 'Part-lid']);
  await page.select('B'); await waitFor(async () => expect(await page.disabled('粘贴引用（待移动）')).toBe(false));
  await page.click('粘贴引用（待移动）');
  await waitFor(async () => expect(await page.disabled('粘贴引用')).toBe(true));
  expect(await page.hasAction('粘贴引用（待移动）')).toBe(false); expect(page.writes).toHaveLength(2);
  await page.openTreeMenu('B');
  await waitFor(() => expect([...document.querySelectorAll<HTMLElement>('[role=menuitem]')].some(item => item.textContent === '剪切')).toBe(true));
  expect(page.writes).toHaveLength(2);
});

it('confirms a fixed current-page batch removal and clears selection after success without replaying a failed refresh', async () => {
  const page = await mount({ failRefresh: true });
  await page.copyRows(['Document', 'Part'], '移除');
  await waitFor(() => expect(document.querySelector('[role=dialog]')?.textContent).toContain('保留对象及文件'));
  expect(page.writes).toEqual([]);
  const confirm = () => [...document.querySelectorAll<HTMLButtonElement>('[role=dialog] button')].find(item => item.textContent?.replace(/\s/g, '') === '确认')!;
  await act(async () => confirm().click());
  await waitFor(() => expect(page.writes).toHaveLength(1));
  expect(page.writes[0]).toEqual({ path: '/api/workspace/content/references/remove', body: {
    position: { spaceId: 'public', rootFolderId: 'root', linkIds: ['A-link'] }, linkIds: ['Document-lid', 'Part-lid'],
  } });
  await waitFor(() => expect(document.querySelector('[role=dialog]')).toBeNull());
  await waitFor(async () => expect(await page.disabled('移除')).toBe(true)); expect(page.writes).toHaveLength(1);
});

it('replaces a cut with a new copy and restores the existing paste and cut labels', async () => {
  const page = await mount(); await page.openTreeMenu('A');
  const menu = () => [...document.querySelectorAll<HTMLElement>('.ant-dropdown:not(.ant-dropdown-hidden):not(.np-crud-header-menu) [role=menuitem]')].find(item => !item.closest('.np-crud-header-menu') && item.textContent === '剪切')!;
  await waitFor(() => expect(menu()?.classList.contains('ant-dropdown-menu-item-disabled')).toBe(false));
  await act(async () => menu().click());
  await waitFor(async () => expect(await page.disabled('粘贴引用（待移动）')).toBe(false));
  await page.copyRows(['Part']); await waitFor(() => expect(page.writes).toHaveLength(2));
  await waitFor(async () => expect(await page.disabled('粘贴引用')).toBe(false));
  expect(page.button('剪切（待移动）')).toBeUndefined(); expect(page.button('剪切')).toBeDefined();
  await page.select('B'); await page.click('粘贴引用');
  await waitFor(() => expect(page.writes).toHaveLength(3)); expect(page.writes[2].body.mode).toBe('copy');
});

it('projects empty and ready clipboard states into tree menus without preparing an action or writing during preview', async () => {
  const page = await mount();
  const menu = (label: string) => [...document.querySelectorAll<HTMLElement>('.ant-dropdown:not(.ant-dropdown-hidden):not(.np-crud-header-menu) [role=menuitem]')].find(item => !item.closest('.np-crud-header-menu') && item.textContent === label)!;
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

it('locks toolbar menus during paste and keeps tree clear available after an unknown receipt', async () => {
  let finish!: (value: Response) => void;
  const page = await mount({ paste: () => new Promise(resolve => { finish = resolve; }) });
  const menu = (label: string) => [...document.querySelectorAll<HTMLElement>('.ant-dropdown:not(.ant-dropdown-hidden):not(.np-crud-header-menu) [role=menuitem]')].find(item => !item.closest('.np-crud-header-menu') && item.textContent === label)!;
  await page.copyRows(['Document']); await waitFor(() => expect(page.writes).toHaveLength(1)); await page.openTreeMenu('B');
  await waitFor(() => expect(menu('粘贴引用').classList.contains('ant-dropdown-menu-item-disabled')).toBe(false));
  await page.click('粘贴引用'); await waitFor(() => expect(finish).toBeTypeOf('function'));
  await waitFor(async () => expect(await page.disabled('粘贴引用')).toBe(true));
  expect(page.button('复制').disabled).toBe(true);
  await page.click('粘贴引用'); expect(page.writes).toHaveLength(2);
  await act(async () => finish(json({ code: 'Persistence.CommitOutcomeUnknown' }, 409)));
  await waitFor(async () => expect(await page.disabled('粘贴引用')).toBe(true));
  await page.openTreeMenu('B');
  await waitFor(() => expect(menu('粘贴引用').classList.contains('ant-dropdown-menu-item-disabled')).toBe(true));
  expect(menu('粘贴引用').querySelector('[title]')?.getAttribute('title')).toContain('结果未知');
  expect(menu('清空剪贴板').classList.contains('ant-dropdown-menu-item-disabled')).toBe(false);
  await act(async () => menu('清空剪贴板').click()); await waitFor(async () => expect(await page.disabled('清空剪贴板')).toBe(true));
  await page.openTreeMenu('B');
  await waitFor(() => expect(menu('粘贴引用').classList.contains('ant-dropdown-menu-item-disabled')).toBe(true));
  expect(page.writes).toHaveLength(2);
});

it('settles an unfinished fixture response on disposal before another page mounts and ignores its later result', async () => {
  let finish!: (value: Response) => void;
  const first = await mount({ paste: () => new Promise(resolve => { finish = resolve; }) });
  await first.copyRows(['Document']); await waitFor(() => expect(first.writes).toHaveLength(1)); await first.select('B');
  await waitFor(async () => expect(await first.disabled('粘贴引用')).toBe(false)); await first.click('粘贴引用');
  await waitFor(() => expect(finish).toBeTypeOf('function'));
  let settled = false;
  void (first.request.mock.results.at(-1)!.value as Promise<Response>).then(() => { settled = true; });
  await first.cleanup(); await waitFor(() => expect(settled).toBe(true));
  const next = await mount(); await next.select('B');
  await act(async () => finish(json({ succeeded: true })));
  expect(await next.disabled('粘贴引用')).toBe(true); expect(next.writes).toEqual([]); expect(first.writes).toHaveLength(2);
});

it('keeps failed menu registration unavailable and ignores a late registration from the previous actor', async () => {
  const diagnostic = vi.spyOn(console, 'error').mockImplementation(() => undefined); onTestFinished(() => diagnostic.mockRestore());
  const failed = await mount({ menuLoad: async () => { throw new Error('menu-binding-failure'); } });
  await failed.openTreeMenu('B');
  await waitFor(() => expect(diagnostic).toHaveBeenCalledWith('Tree action registration unavailable.', expect.any(Error)));
  const copy = [...document.querySelectorAll<HTMLElement>('[role=menuitem]')].find(item => item.textContent === '复制')!;
  expect(copy.classList.contains('ant-dropdown-menu-item-disabled')).toBe(true);
  await act(async () => copy.click()); expect(failed.writes).toEqual([]);
  expect(failed.container.querySelector('.np-tree-context-header [aria-current="page"]')?.textContent).toBe('B');
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
  await waitFor(async () => expect(await page.disabled('粘贴引用')).toBe(false));
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
    await page.select(name); await waitFor(async () => expect(await page.disabled('粘贴引用')).toBe(false));
    await page.click('粘贴引用');
    await waitFor(() => expect(page.writes.filter(item => item.path.endsWith('/paste'))).toHaveLength(name === 'B' ? 1 : 2));
  }
  expect(page.writes.slice(1).map(item => item.body)).toEqual(['B', 'C'].map(name => ({ mode: 'copy', sourcePosition: { spaceId: 'public', rootFolderId: 'root', linkIds: ['A-link'] }, linkIds: ['Document-lid'], targetPosition: { spaceId: 'public', rootFolderId: 'root', linkIds: [`${name}-link`] } })));
});

it('uses current-page toolbar selection and shares tree action state with the right list while isolating another tree instance', async () => {
  const first = await mount(); const second = await mount();
  // 两个真实页面共享浏览器transport；后挂的拦截器收集两者请求，实例状态仍须隔离。
  await first.copyRows(['Document', 'Part']); await waitFor(() => expect(second.writes).toHaveLength(1));
  expect(second.writes[0].body.linkIds).toEqual(['Document-lid', 'Part-lid']);
  await first.select('B'); await waitFor(async () => expect(await first.disabled('粘贴引用')).toBe(false));
  await second.select('B'); await waitFor(async () => expect(await second.disabled('粘贴引用')).toBe(true));
  await first.click('清空剪贴板');
  await waitFor(async () => expect(await first.disabled('粘贴引用')).toBe(true));
  const title = [...first.container.querySelectorAll<HTMLElement>('.ant-tree-title')].find(item => item.textContent === 'A')!;
  await act(async () => title.firstElementChild!.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, clientX: 60, clientY: 80 })));
  await waitFor(() => expect([...document.querySelectorAll('[role=menuitem]')].some(item => item.textContent === '复制')).toBe(true));
  await act(async () => [...document.querySelectorAll<HTMLElement>('[role=menuitem]')].find(item => item.textContent === '复制')!.click());
  await waitFor(() => expect(second.writes).toHaveLength(2));
  expect(second.writes[1].body).toEqual({ mode: 'copy', sourcePosition: { spaceId: 'public', rootFolderId: 'root', linkIds: [] }, linkIds: ['A-link'] });
  expect(document.querySelector('[role=dialog]')).toBeNull();
  await first.select('B'); await waitFor(async () => expect(await first.disabled('粘贴引用')).toBe(false));
  expect(await second.disabled('粘贴引用')).toBe(true);
});

it('clears on actor change and ignores a late validation from the old tree instead of reviving it', async () => {
  let finish!: (value: Response) => void;
  const page = await mount({ validate: () => new Promise(resolve => { finish = resolve; }) });
  await page.copyRows(['Document']); await waitFor(() => expect(finish).toBeTypeOf('function'));
  await act(async () => [...page.container.querySelectorAll<HTMLButtonElement>('button')].find(item => item.textContent === 'Switch actor')!.click());
  await waitFor(() => expect(page.container.querySelector('[data-current-actor]')?.getAttribute('data-current-actor')).toBe('next'));
  await act(async () => finish(json({ mode: 'copy', items: [{ linkId: 'Document-lid' }] })));
  await waitFor(async () => expect(await page.disabled('粘贴引用')).toBe(true));
  expect(page.container.textContent).not.toContain('已复制引用');
});

it('locks unknown paste outcomes without replay and clears the snapshot when the page is destroyed and reloaded', async () => {
  const page = await mount({ paste: async () => json({ code: 'Persistence.CommitOutcomeUnknown' }, 409) });
  await page.copyRows(['Document']); await waitFor(() => expect(page.writes).toHaveLength(1)); await page.select('B');
  await waitFor(async () => expect(await page.disabled('粘贴引用')).toBe(false)); await page.click('粘贴引用');
  await waitFor(async () => expect(await page.disabled('粘贴引用')).toBe(true));
  // 共享Host通过Ant消息portal呈现执行错误，它在document.body而非页面container内。
  await waitFor(() => expect([...document.querySelectorAll('.ant-message-notice-content')].some(item => item.textContent?.includes('本次粘贴结果未知'))).toBe(true));
  await page.click('粘贴引用'); expect(page.writes.filter(item => item.path.endsWith('/paste'))).toHaveLength(1);
  await page.cleanup(); const reloaded = await mount(); await reloaded.select('B');
  await waitFor(async () => expect(await reloaded.disabled('粘贴引用')).toBe(true));
});

it('keeps a committed clipboard after refresh failure and retries only reading', async () => {
  const page = await mount({ failRefresh: true }); await page.copyRows(['Document']); await waitFor(() => expect(page.writes).toHaveLength(1));
  await page.select('B'); await waitFor(async () => expect(await page.disabled('粘贴引用')).toBe(false)); await page.click('粘贴引用');
  await waitFor(() => expect(page.container.textContent).toContain('已保存，刷新失败'));
  await act(async () => page.button('刷新').click()); await waitFor(() => expect(page.container.textContent).not.toContain('已保存，刷新失败'));
  expect(page.writes.filter(item => item.path.endsWith('/paste'))).toHaveLength(1); expect(await page.disabled('粘贴引用')).toBe(false);
});

it.each(['copy', 'copy-structure'] as const)('submits the real prepared folder copy once and retries only reading after its committed refresh fails', async kind => {
  const page = await mount({ folderRow: true, failRefresh: true, formalDeclarations: true });
  await page.copyRows(['WorkspaceFolder']); await waitFor(() => expect(page.writes).toHaveLength(1));
  await page.select('B'); await waitFor(async () => expect(await page.disabled(kind === 'copy' ? '粘贴文件夹副本' : '粘贴文件夹结构')).toBe(false));
  await page.click(kind === 'copy' ? '粘贴文件夹副本' : '粘贴文件夹结构');
  await waitFor(() => expect(document.querySelector<HTMLInputElement>('#workspace-copy-name')?.value).toBe('WorkspaceFolder source'));
  await act(async () => [...document.querySelectorAll<HTMLButtonElement>('.ant-modal button')].find(button => button.textContent?.replace(/\s/g, '') === '确认')!.click());
  await waitFor(() => expect(page.container.textContent).toContain('已保存，刷新失败'));
  expect(page.writes.filter(item => item.path.endsWith(`/folders/${kind}`))).toHaveLength(1);
  const actionKey = kind === 'copy' ? 'pasteFolderCopy' : 'pasteFolderStructure';
  const declaration = clipboardContract.declarations.find(item => item.viewKey === 'workspace.folder.list' && item.surface === 'pageActions' && item.actionKey === actionKey)!;
  const copyCalls = page.request.mock.calls.filter(([input]) => String(input instanceof Request ? input.url : input).includes(`/folders/${kind}`));
  // StrictMode首prepare被取消后重建同来源，只允许两次prepare和一次用户submit。
  expect(copyCalls).toHaveLength(3);
  const preparations = copyCalls.filter(([input]) => String(input instanceof Request ? input.url : input).endsWith('/prepare'));
  expect(preparations).toHaveLength(2);
  const firstPreparation = preparations[0];
  expect((firstPreparation[0] instanceof Request ? firstPreparation[0].signal : firstPreparation[1]?.signal)?.aborted).toBe(true);
  for (const call of copyCalls) {
    const headers = new Headers(call[0] instanceof Request ? call[0].headers : call[1]?.headers);
    expect(headers.get('X-NewP-Data-Declaration')).toBe(declaration.id);
    expect(declaration.featurePositions).toContain(headers.get('X-NewP-Feature'));
  }
  expect(page.request.mock.calls.some(([input]) => String(input instanceof Request ? input.url : input).includes('/api/workspace/folders/'))).toBe(false);
  expect(page.writes.find(item => item.path.endsWith(`/folders/${kind}`))!.body).toEqual({ mode: 'copy', sourcePosition: { spaceId: 'public', rootFolderId: 'root', linkIds: ['A-link'] },
    linkIds: ['WorkspaceFolder-lid'], targetPosition: { spaceId: 'public', rootFolderId: 'root', linkIds: ['B-link'] }, name: 'WorkspaceFolder source', copyParentAuthorization: true });
  await act(async () => page.button('刷新').click());
  await waitFor(() => expect(page.container.textContent).not.toContain('已保存，刷新失败'));
  expect(page.writes.filter(item => item.path.endsWith(`/folders/${kind}`))).toHaveLength(1);
  expect(await page.disabled(kind === 'copy' ? '粘贴文件夹副本' : '粘贴文件夹结构')).toBe(false);
});

it('opens a folder name at its exact tree occurrence without reading a details drawer', async () => {
  const page = await mount({ folderRow: true });
  await page.select('A');
  await page.waitFor(() => expect(page.container.querySelector('tbody')?.textContent).toContain('WorkspaceFolder source'));
  const name = [...page.container.querySelectorAll<HTMLButtonElement>('tbody button')].find(button => button.textContent === 'WorkspaceFolder source')!;
  await act(async () => name.click());
  await page.waitFor(() => expect(page.container.querySelector('.np-tree-context-header [aria-current="page"]')?.textContent).toBe('WorkspaceFolder'));
  expect(new URLSearchParams(page.router.state.location.search).get('node')).toBe('public:root:A-link:WorkspaceFolder-lid');
  expect(document.querySelector('.ant-drawer')).toBeNull();
  expect(page.request.mock.calls.some(([input]) => String(input instanceof Request ? input.url : input).includes('/api/workspace/folders/'))).toBe(false);
});

it('uses a compact accessible button with the same actions as the row context menu', async () => {
  const page = await mount();
  await page.select('A');
  await page.waitFor(() => expect(page.container.querySelector('tbody')?.textContent).toContain('Document source'));
  const row = [...page.container.querySelectorAll<HTMLTableRowElement>('tbody tr')].find(item => item.textContent?.includes('Document source'))!;
  const more = row.querySelector<HTMLButtonElement>('.np-row-actions-more')!;
  expect(more.textContent).toBe('⋯');
  expect(more.getAttribute('aria-label')).toBe('更多');
  const labels = () => [...document.querySelectorAll('.ant-dropdown:not(.ant-dropdown-hidden) [role=menuitem]')].map(item => item.textContent);
  await act(async () => more.click());
  await page.waitFor(() => expect(labels()).toEqual(['复制', '剪切', '编辑', '删除', '移除引用']));
  const buttonActions = labels();
  await act(async () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })));
  await act(async () => row.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, clientX: 100, clientY: 100 })));
  await page.waitFor(() => expect(labels()).toEqual(buttonActions));
});

it.each(['separate', 'context-only', 'column-only', 'default'] as const)('keeps typed action surfaces independent: %s', async mode => {
  const documentActions = { column: mode === 'context-only' ? [] : ['view'], contextMenu: mode === 'column-only' ? [] : ['copy', 'edit'] };
  const page = await mount({ columnMode: mode === 'default' ? 'default' : 'menu', actionSets: {
    WorkspaceFolder: { column: [], contextMenu: [] },
    Document: documentActions, Part: { column: [], contextMenu: ['cut'] },
  } });
  await page.select('A');
  await page.waitFor(() => expect(page.container.querySelector('tbody')?.textContent).toContain('Document source'));
  const row = [...page.container.querySelectorAll<HTMLTableRowElement>('tbody tr')].find(item => item.textContent?.includes('Document source'))!;
  const partRow = [...page.container.querySelectorAll<HTMLTableRowElement>('tbody tr')].find(item => item.textContent?.includes('Part source'))!;
  expect(partRow.querySelector('.np-row-actions-more')).toBeNull();
  const labels = () => [...document.querySelectorAll('.ant-dropdown:not(.ant-dropdown-hidden) [role=menuitem]')].map(item => item.textContent);
  if (mode === 'context-only') {
    expect(page.container.querySelector('.ant-table-thead')?.textContent).not.toContain('操作');
  } else {
    const button = row.querySelector<HTMLButtonElement>('.np-row-actions-more')!;
    expect(button.textContent).toBe(mode === 'default' ? '更多' : '⋯');
    await act(async () => button.click());
    await page.waitFor(() => expect(labels()).toEqual(['打开属性']));
    await act(async () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })));
  }
  await act(async () => row.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, clientX: 90, clientY: 100 })));
  await page.waitFor(() => expect(labels()).toEqual(mode === 'column-only' ? [] : ['复制', '编辑']));
  if (mode !== 'column-only') {
    await act(async () => (document.querySelector('.ant-dropdown:not(.ant-dropdown-hidden) [role=menuitem]') as HTMLElement).click());
    await page.waitFor(() => expect(page.writes).toHaveLength(1));
    expect(page.writes[0].path).toBe('/api/workspace/content/clipboard/validate');
  }
});

it.each(['copy', 'copy-structure'] as const)('keeps the name after a %s collation conflict without replay or automatic suffix', async kind => {
  const page = await mount({ folderRow: true, formalDeclarations: true, folderCopy: async () => json({ code: 'Workspace.SiblingNameConflict', status: 409 }, 409) });
  await page.copyRows(['WorkspaceFolder']); await waitFor(() => expect(page.writes).toHaveLength(1)); await page.select('B');
  const label = kind === 'copy' ? '粘贴文件夹副本' : '粘贴文件夹结构';
  await waitFor(async () => expect(await page.disabled(label)).toBe(false)); await page.click(label);
  await waitFor(() => expect(document.querySelector<HTMLInputElement>('#workspace-copy-name')?.value).toBe('WorkspaceFolder source'));
  await act(async () => [...document.querySelectorAll<HTMLButtonElement>('.ant-modal button')].find(button => button.textContent?.replace(/\s/g, '') === '确认')!.click());
  await waitFor(() => expect(page.writes.filter(item => item.path.endsWith(`/folders/${kind}`))).toHaveLength(1));
  await waitFor(() => expect(document.querySelector<HTMLInputElement>('#workspace-copy-name')?.disabled).toBe(false));
  expect(document.querySelector<HTMLInputElement>('#workspace-copy-name')!.value).toBe('WorkspaceFolder source');
  expect(document.querySelector('[role=dialog]')).not.toBeNull();
  expect(page.writes.filter(item => item.path.endsWith(`/folders/${kind}`))).toHaveLength(1);
});
