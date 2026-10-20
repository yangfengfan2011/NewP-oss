import { formalDataAccessFor } from './formalRuntimeMetadataTestSupport';
// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, platformFetch } from '@icp-platform/core';
import { ModuleRegistry, type FrontendBehaviorBindings } from '@icp-platform/sdk';
import { loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import { validateCompositionCandidate } from '../composition/compositionRegistry';
import { loadActiveModuleRuntimeWith } from '../active-module-runtime-loader';
import { buildPageEntriesFromNavDocuments } from '../navigation/pageEntryAdapter';
import { EntryNavigationProvider } from '../navigation/EntryNavigationContext';
import { PageEntry, PageOutlet } from '../composition/PageOutlet';
import { PageComposition } from '../composition/PageCompositionContext';
import { RuntimeMetadataProvider } from './formalRuntimeMetadataProvider';
import { authoring, expectFixedReadHistoryRequests, installHistoryFeatures, mountIndependentHistory } from './independentHistoryHarness';
import { AuthProvider, useAuth } from '../contexts/AuthContext';
import { CurrentPermissionScope } from '../components/CurrentPermissionScope';

vi.unmock('@icp-platform/core');
const workspace = (await loadComponentSource<{ default: FrontendBehaviorBindings }>('Workspace', 'index.ts')).default;
const part = (await loadComponentSource<{ default: FrontendBehaviorBindings }>('Part', 'index.ts')).default;
const documentModule = (await loadComponentSource<{ default: FrontendBehaviorBindings }>('Document', 'index.ts')).default;
await import('../pages/StandardCrudListPageHost');
await import('../pages/StandardCrudListOverlayRuntime');
await import('@icp-platform/ui/crud-form');
const position = { spaceId: 'space', rootFolderId: 'root', linkIds: ['container'] };
const sourceParameters = { objectId: 'part-object', modelKey: 'Part', sourceLinkId: 'part-occurrence',
  spaceId: position.spaceId, rootFolderId: position.rootFolderId, linkIds: JSON.stringify(position.linkIds), name: 'Source Part' };
const item = { id: 'event-1', eventType: 'dataScope.object.changed', occurredAt: '2026-10-18T00:00:00Z', objectType: 'Part',
  actor: { name: '列表操作者', status: 'available' }, target: { name: null, code: null, nameSource: 'unrecorded' },
  description: { key: 'ObjectHistory.DescriptionUnavailable', parameters: {} }, ipAddress: null };
const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });

async function mount(hasHistory = true, direct = false, editableInput = false, viewer = false, reservedProjection?: string,
  checkHistory?: (request: Request) => Response | Promise<Response>, omitDataAccess = false, invalidPropertiesSource = false) {
  const metadata = authoring();
  const positions = installHistoryFeatures(metadata).Workspace;
  if (invalidPropertiesSource) {
    const tabs = metadata.viewDocuments.find(document => document.view.key === 'workspace.folder.tabs')!.view.tabs as Array<{ key: string; pageRef: { dataAccess?: unknown } }>;
    tabs.find(tab => tab.key === 'information')!.pageRef.dataAccess = { id: '' };
  }
  if (omitDataAccess) {
    const actions = metadata.viewDocuments.find(document => document.view.key === 'workspace.folder.list')!.view.rowActions as Array<Record<string, unknown>>;
    delete actions.find(action => action.key === 'history')!.dataAccess;
  }
  const nav = metadata.navDocuments.flatMap(document => document.navigation.items);
  nav.find(item => item.key === 'workspace')!.featurePosition = positions.readPosition;
  const target = nav.find(item => item.key === 'workspace.history')!;
  target.featurePosition = positions.historyPosition;
  const pageEntries = buildPageEntriesFromNavDocuments(metadata.navDocuments);
  const targetEntry = pageEntries.find(entry => entry.path === target.path)!;
  const viewerTarget = nav.find(item => item.key === 'workspace.permissions')!;
  const viewerEntry = pageEntries.find(entry => entry.path === viewerTarget.path)!;
  const grants = [positions.readPosition, ...(hasHistory ? [positions.historyPosition] : [])];
  expect(grants.every(grant => grant.startsWith('Workspace/'))).toBe(true);
  const requests: Request[] = [];
  const user = { id: 'reader', ecode: 'reader', ename: 'Reader', revision: 1, mustChangePassword: false, featurePositions: grants, timeZoneId: 'UTC' };
  let currentUser = user;
  let refreshIdentity!: () => Promise<void>;
  function AuthObservation() { refreshIdentity = useAuth().retrySessionValidation; return null; }
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    requests.push(request); const url = new URL(request.url);
    if (url.pathname.endsWith('/current-user')) return json(currentUser);
    if (url.pathname === '/test/authority-revoked') return new Response(null, { status: 403 });
    if (url.pathname.endsWith('/antiforgery-token')) return json({ requestToken: 'history-token' });
    if (url.pathname.startsWith('/api/workspace/content/part-occurrence/history/check/')) return checkHistory?.(request) ?? new Response(null, { status: 204 });
    if (url.pathname === '/api/workspace/content/part-occurrence/properties') return json({ resourceKey: 'part.part', objectId: 'part-object', detail: { fields: { EID: 'part-object', ECODE: 'P1', ENAME: 'Source Part', Description: '', OwnerId: null, OwnerDepartmentId: null, CreatedBy: 'reader', CreatedAt: '2026-10-18T00:00:00Z', UpdatedBy: 'reader', UpdatedAt: '2026-10-18T00:00:00Z' }, summaries: {} } });
    if (url.pathname === '/api/workspace/content/stale-occurrence/properties') return new Response(null, { status: 404 });
    if (url.pathname === '/api/workspace/content/part-occurrence/history/print' && request.method === 'POST') return json({ items: [item] });
    if (url.pathname === '/api/links/workspace.content') return json({ items: [{ linkId: 'part-occurrence', associatedObject: { modelKey: 'Part', id: 'part-object' },
      fields: { LID: 'part-occurrence', ObjectType: 'Part', Name: 'Source Part', Code: 'P-1' } }], total: 1, page: 1, pageSize: 20 });
    if (url.pathname === '/api/workspace/content/part-occurrence/history') return json({ items: [item], total: 1, pageIndex: 1, pageSize: 20 });
    if (url.pathname === '/api/workspace/content/part-occurrence/history/event-1') return json({ ...item, actor: { name: '详情最新操作者', status: 'available' } });
    throw new Error(`Unexpected history request ${request.method} ${url.pathname}`);
  }));
  const capsules = ['Workspace', 'Part', 'Document'].map(moduleId => readComponentJson(moduleId, 'newp.plugin.json'));
  const loaded = await loadActiveModuleRuntimeWith({
    fetchCatalog: async () => ({ ok: true, status: 200, json: async () => ({ schemaVersion: '1.0', snapshotId: 'object-history', planHash: 'object-history',
      modules: capsules.map(capsule => ({ moduleId: capsule.moduleId, version: capsule.version, displayName: capsule.displayName, dependencies: capsule.dependencies,
        frontend: { moduleId: capsule.moduleId, behaviorKey: capsule.frontend.behaviorKey, bindingMode: 'behavior-bindings', bindings: capsule.frontend.bindings } })) }) }),
    internalModules: { Workspace: async () => ({ default: workspace }), Part: async () => ({ default: part }), Document: async () => ({ default: documentModule }) },
  });
  const editableHandler = vi.fn();
  loaded.modules = loaded.modules.map(module => module.metadata.moduleId !== 'Workspace' ? module : ({ ...module,
    standardCrudDetailReadSources: reservedProjection ? module.standardCrudDetailReadSources?.map(source => ({ ...source,
      navigationContributions: source.navigationContributions?.map(contribution => ({ ...contribution,
        getParameters: (resourceKey, id, context) => ({ ...contribution.getParameters(resourceKey, id, context), [reservedProjection]: 'untrusted-value' }),
      })),
    })) : module.standardCrudDetailReadSources,
    standardCrudCapabilities: module.standardCrudCapabilities?.map(binding => binding.targetKey !== 'workspace.history.list' ? binding : ({ ...binding,
      load: async () => {
        const resource = (await binding.load()).default;
        return { default: { ...resource, actionHandlers: { ...resource.actionHandlers, view: editableHandler },
          actionInputRenderers: editableInput
            ? { ...resource.actionInputRenderers, objectHistoryDetails: Object.assign(() => <textarea aria-label="不可借读取资格编辑" />, { readOnly: false }) }
            : resource.actionInputRenderers } };
      },
    })),
  }));
  expect(() => validateCompositionCandidate(loaded.modules, metadata)).not.toThrow();
  ModuleRegistry.replaceAll(loaded.modules, 'object-history');
  const commonWords = readComponentJson('Audit', 'metadata/dictionaries/ObjectHistory.dic.json').ObjectHistory;
  const words = readComponentJson('Workspace', 'metadata/dictionaries/Workspace.dic.json').Workspace;
  const catalog = Object.fromEntries([
    ...Object.entries(commonWords).map(([key, value]) => [`ObjectHistory.${key}`, (value as { zhs: string }).zhs]),
    ...Object.entries(words).map(([key, value]) => [`Workspace.${key}`, (value as { zhs: string }).zhs]),
  ]);
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const wrap = (content: React.ReactNode) => <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: catalog }}>
    <AuthProvider initialUser={user}><AuthorizationProvider featurePositions={grants}><CurrentPermissionScope><AuthObservation /><RuntimeMetadataProvider value={metadata}>
      <EntryNavigationProvider>{content}</EntryNavigationProvider>
    </RuntimeMetadataProvider></CurrentPermissionScope></AuthorizationProvider></AuthProvider>
  </LocalizationProvider>;
  const router = createMemoryRouter([
    { path: '/workspace', element: wrap(<PageComposition><PageOutlet page={{ pageKey: 'platform.list', parameters: { viewKey: 'workspace.folder.list' }, inputResolverKey: 'workspace.folder.content' }}
      featurePlacement={{ entryPosition: positions.readPosition, staticScope: 'folder' }} context={{ nodeKey: 'container', nodeType: 'folder', label: 'Container', hasChildren: true,
        context: { id: 'container-object', ...position } }} /></PageComposition>) },
    { path: target.path!, element: wrap(<PageEntry page={targetEntry.page!} />) },
    { path: viewerEntry.path!, element: wrap(<PageEntry page={viewerEntry.page!} />) },
  ], { initialEntries: [viewer ? { pathname: viewerEntry.path!, state: { pageParameters: { ...sourceParameters, sourceFeaturePosition: positions.readPosition } } } : direct ? { pathname: target.path!, state: { pageParameters: sourceParameters } } : '/workspace'] });
  onTestFinished(async () => {
    await act(async () => root.unmount()); router.dispose(); container.remove(); ModuleRegistry.replaceAll([], 'object-history-cleanup'); vi.unstubAllGlobals();
  });
  await act(async () => root.render(<RouterProvider router={router} />));
  return { container, router, requests, positions, editableHandler, metadata,
    refreshUser: async (change: Partial<typeof user>) => { currentUser = { ...currentUser, ...change }; await refreshIdentity(); } };
}

it('requires the actual separate history grant and opens the authorized Part source through the standard guarded navigation', async () => {
  const { container, router, requests, positions } = await mount();
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="part-occurrence"]')).not.toBeNull());
  await act(async () => container.querySelector<HTMLButtonElement>('tr[data-row-key="part-occurrence"] button[aria-label="更多"]')!.click());
  const history = () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(element => element.textContent?.trim() === readComponentJson('Workspace', 'metadata/dictionaries/Workspace.dic.json').Workspace.History.zhs);
  await vi.waitFor(() => expect(history()).toBeDefined());
  act(() => history()!.click());
  await vi.waitFor(() => expect(router.state.location.pathname).toBe('/workspace/history'));
  await vi.waitFor(() => expect(container.textContent).toContain('列表操作者'));
  expect(document.body.textContent).not.toContain('放弃未保存修改');
  expect(router.state.location.state?.pageParameters.sourceFeaturePosition).toBe(positions.readPosition);
  const request = requests.find(request => new URL(request.url).pathname.endsWith('/history'))!;
  expect(request.headers.get('X-NewP-Feature')).toBe(positions.historyPosition);
  expect(JSON.parse(new URL(request.url).searchParams.get('queryScope')!)).toEqual(position);
  expect(requests.some(request => new URL(request.url).pathname.startsWith('/api/part/'))).toBe(false);
});

it('keeps the functional history button and stays at the source when the click data check refuses the real Part occurrence', async () => {
  const { container, router, requests } = await mount(true, false, false, false, undefined, () => new Response(null, { status: 404 }));
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="part-occurrence"]')).not.toBeNull());
  await act(async () => container.querySelector<HTMLButtonElement>('tr[data-row-key="part-occurrence"] button[aria-label="更多"]')!.click());
  const history = () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(element => element.textContent?.trim() === '履历');
  await vi.waitFor(() => expect(history()).toBeDefined());
  act(() => history()!.click());
  await vi.waitFor(() => expect(requests.some(request => new URL(request.url).pathname.endsWith('/history/check/enter'))).toBe(true));
  expect(router.state.location.pathname).toBe('/workspace');
  expect(requests.some(request => new URL(request.url).pathname.endsWith('/history'))).toBe(false);
  await vi.waitFor(() => expect(document.body.textContent).toContain('对象不存在或不可访问'));
});

it('fails before navigation when an opted-in history button loses its compiled declaration', async () => {
  const { container, router } = await mount(true, false, false, false, undefined, undefined, true);
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="part-occurrence"]')).not.toBeNull());
  await act(async () => container.querySelector<HTMLButtonElement>('tr[data-row-key="part-occurrence"] button[aria-label="更多"]')!.click());
  const history = () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(element => element.textContent?.trim() === '履历');
  await vi.waitFor(() => expect(history()).toBeDefined());
  act(() => history()!.click());
  expect(router.state.location.pathname).toBe('/workspace');
  await vi.waitFor(() => expect(container.querySelector('[role="alert"]')).not.toBeNull());
});

it.each(['cancel', 'subject', 'permissions', 'epoch', 'source', 'target', 'page'] as const)('ignores a late allow after %s changes and prevents duplicate click checks', async change => {
  let finish!: (response: Response) => void;
  const { container, router, requests, positions, refreshUser } = await mount(true, false, false, true, undefined,
    () => new Promise<Response>(resolve => { finish = resolve; }));
  await vi.waitFor(() => expect(container.textContent).toContain('Source Part'));
  const history = () => [...container.querySelectorAll('button')].find(button => button.textContent?.replace(/\s/g, '') === '履历');
  await vi.waitFor(() => expect(history()).toBeDefined()); act(() => history()!.click());
  await vi.waitFor(() => expect(requests.filter(request => new URL(request.url).pathname.endsWith('/history/check/enter'))).toHaveLength(1));
  expect(router.state.location.pathname).toBe('/workspace/permissions');
  expect(history()!.disabled).toBe(true);
  act(() => history()!.click());
  expect(requests.filter(request => new URL(request.url).pathname.endsWith('/history/check/enter'))).toHaveLength(1);
  const check = requests.find(request => new URL(request.url).pathname.endsWith('/history/check/enter'))!;
  await act(async () => {
    if (change === 'cancel') [...container.querySelectorAll('button')].find(button => button.textContent?.replace(/\s/g, '') === '取消')!.click();
    if (change === 'subject') await refreshUser({ id: 'new-reader' });
    if (change === 'permissions') await refreshUser({ featurePositions: [positions.readPosition] });
    if (change === 'epoch') await platformFetch('/test/authority-revoked', { method: 'PUT' });
    if (change === 'source') await router.navigate('/workspace/permissions', { state: { pageParameters: { ...sourceParameters,
      sourceLinkId: 'stale-occurrence', sourceFeaturePosition: positions.readPosition } } });
    if (change === 'target') await router.navigate('/workspace/permissions', { state: { pageParameters: { ...sourceParameters,
      objectId: 'other-object', sourceFeaturePosition: positions.readPosition } } });
    if (change === 'page') await router.navigate('/workspace');
  });
  await vi.waitFor(() => expect(check.signal.aborted).toBe(true));
  await act(async () => finish(new Response(null, { status: 204 })));
  expect(router.state.location.pathname).toBe(change === 'page' ? '/workspace' : '/workspace/permissions');
  expect(requests.some(request => new URL(request.url).pathname.endsWith('/history'))).toBe(false);
});

it('opens the model-less history detail in the existing readonly input and fetches the current safe item', async () => {
  const { container, requests, positions, editableHandler } = await mount(true, true);
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="event-1"]')).not.toBeNull());
  const row = container.querySelector('tr[data-row-key="event-1"]')!;
  await act(async () => row.querySelector<HTMLButtonElement>('button[aria-label="更多"]')!.click());
  const details = () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(element => element.textContent?.trim() === '详情');
  await vi.waitFor(() => expect(details()).toBeDefined()); act(() => details()!.click());
  await vi.waitFor(() => expect(document.body.textContent).toContain('详情最新操作者'));
  const request = requests.find(request => new URL(request.url).pathname.endsWith('/history/event-1'))!;
  expect(request.headers.get('X-NewP-Feature')).toBe(positions.historyPosition);
  expect(JSON.parse(new URL(request.url).searchParams.get('queryScope')!)).toEqual(position);
  expect(requests.every(request => request.method === 'GET')).toBe(true);
  const close = document.querySelector<HTMLButtonElement>('.ant-drawer-close')!;
  expect(close).not.toBeNull(); await act(async () => close.click());
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer-open')).toBeNull());
  expect(editableHandler).not.toHaveBeenCalled();
  expect(requests.filter(request => new URL(request.url).pathname === '/api/workspace/content/part-occurrence/history')).toHaveLength(1);
});

it('does not offer history to the same object browser without the compiled history grant', async () => {
  const { container, requests } = await mount(false);
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="part-occurrence"]')).not.toBeNull());
  await act(async () => container.querySelector<HTMLButtonElement>('tr[data-row-key="part-occurrence"] button[aria-label="更多"]')!.click());
  await vi.waitFor(() => expect(document.querySelector('[role="menuitem"]')).not.toBeNull());
  expect([...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].some(element => element.textContent?.trim() === readComponentJson('Workspace', 'metadata/dictionaries/Workspace.dic.json').Workspace.History.zhs)).toBe(false);
  expect(requests.some(request => new URL(request.url).pathname.includes('/history'))).toBe(false);
});

it('does not execute or mount an editable input through the standard view read qualification', async () => {
  const { container, requests, editableHandler } = await mount(true, true, true);
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="event-1"]')).not.toBeNull());
  await act(async () => container.querySelector<HTMLButtonElement>('tr[data-row-key="event-1"] button[aria-label="更多"]')!.click());
  const details = () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(element => element.textContent?.trim() === '详情');
  await vi.waitFor(() => expect(details()).toBeDefined()); act(() => details()!.click());
  expect(document.querySelector('textarea[aria-label="不可借读取资格编辑"]')).toBeNull();
  expect(editableHandler).not.toHaveBeenCalled();
  expect(requests.every(request => request.method === 'GET')).toBe(true);
});

it('opens the readonly output preview with no selection and prints all using the same compiled history identity', async () => {
  const { container, requests, positions, editableHandler, metadata } = await mount(true, true);
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="event-1"]')).not.toBeNull());
  const output = [...container.querySelectorAll('button')].find(button => button.textContent?.replace(/\s/g, '') === '输出');
  expect(output).toBeDefined(); act(() => output!.click());
  const printAll = () => [...document.querySelectorAll('button')].find(button => button.textContent?.replace(/\s/g, '') === '打印全部');
  await vi.waitFor(() => expect(printAll()).toBeDefined());
  expect([...document.querySelectorAll('button')].find(button => button.textContent?.replace(/\s/g, '') === '打印选中')?.disabled).toBe(true);
  const outputCandidates = metadata.manifest.modules.flatMap(module => module.dataAccess ?? [])
    .filter(declaration => declaration.surface === 'outputActions' && declaration.actionKey === 'printAll');
  // 输入目录包含其他真实模块的同名输出；履历必须仍选择所属View的唯一正式来源。
  expect(new Set(outputCandidates.map(declaration => declaration.viewKey)).size).toBeGreaterThan(1);
  const clickedPrint = printAll()!;
  const disabledAtClick = clickedPrint.disabled;
  expect(disabledAtClick).toBe(false);
  await act(async () => clickedPrint.click());
  await vi.waitFor(() => expect(document.querySelector('.ant-modal-body table')?.textContent, JSON.stringify({ disabledAtClick, candidateIds: outputCandidates.map(declaration => declaration.id),
    printRequests: requests.filter(request => new URL(request.url).pathname.endsWith('/history/print')).map(request => request.method),
    modal: document.querySelector('.ant-modal-body')?.innerHTML,
  })).toContain('列表操作者'));
  const request = requests.find(request => new URL(request.url).pathname.endsWith('/history/print'))!;
  expect(request.headers.get('X-NewP-Feature')).toBe(positions.historyPosition);
  expectFixedReadHistoryRequests(metadata, requests);
  expect(JSON.parse(new URL(request.url).searchParams.get('queryScope')!)).toEqual(position);
  expect(await request.clone().json()).toEqual({ mode: 'all', filter: { pageIndex: 1, pageSize: 20 } });
  expect(editableHandler).not.toHaveBeenCalled();
  await act(async () => document.querySelector<HTMLButtonElement>('.ant-modal-footer button')!.click());
  await vi.waitFor(() => expect(document.querySelector('.ant-modal-body')).toBeNull());
  expect(requests.filter(request => new URL(request.url).pathname === '/api/workspace/content/part-occurrence/history')).toHaveLength(1);
});

it('shows source viewer history only with the independently compiled history grant and preserves the full Part locator through guarded navigation', async () => {
  const { container, router, requests, positions } = await mount(true, false, false, true);
  await vi.waitFor(() => expect(container.textContent).toContain('Source Part'));
  const history = () => [...container.querySelectorAll('button')].find(button => button.textContent?.replace(/\s/g, '') === '履历');
  expect(history()).toBeDefined(); act(() => history()!.click());
  await vi.waitFor(() => expect(router.state.location.pathname).toBe('/workspace/history'));
  await vi.waitFor(() => expect(container.textContent).toContain('列表操作者'));
  const { name, ...locator } = sourceParameters;
  expect(router.state.location.state?.pageParameters).toEqual(locator);
  expect(router.state.location.state?.pageParameters).not.toHaveProperty('name', name);
  expect(document.body.textContent).not.toContain('放弃未保存修改');
  const request = requests.find(request => new URL(request.url).pathname.endsWith('/history'))!;
  expect(request.headers.get('X-NewP-Feature')).toBe(positions.historyPosition);
  expect(JSON.parse(new URL(request.url).searchParams.get('queryScope')!)).toEqual(position);
  expect(requests.every(request => request.method === 'GET')).toBe(true);
  expect(requests.some(request => new URL(request.url).pathname.startsWith('/api/part/'))).toBe(false);
});

it('does not expose source viewer history to a read-only actor without the independent history grant', async () => {
  const { container, requests } = await mount(false, false, false, true);
  await vi.waitFor(() => expect(container.textContent).toContain('Source Part'));
  expect([...container.querySelectorAll('button')].some(button => button.textContent?.replace(/\s/g, '') === '履历')).toBe(false);
  expect(requests.some(request => new URL(request.url).pathname.includes('/history'))).toBe(false);
});

it.each(['missing', 'stale'] as const)('does not retain viewer history after a %s source replaces the displayed Part', async boundary => {
  const { container, router, requests, positions } = await mount(true, false, false, true);
  await vi.waitFor(() => expect(container.textContent).toContain('Source Part'));
  await vi.waitFor(() => expect([...container.querySelectorAll('button')].some(button => button.textContent?.replace(/\s/g, '') === '履历')).toBe(true));
  const parameters = { ...sourceParameters, sourceFeaturePosition: positions.readPosition,
    sourceLinkId: boundary === 'stale' ? 'stale-occurrence' : '' };
  await act(async () => { await router.navigate('/workspace/permissions', { state: { pageParameters: parameters } }); });
  await vi.waitFor(() => expect([...container.querySelectorAll('button')].some(button => button.textContent?.replace(/\s/g, '') === '履历')).toBe(false));
  expect(requests.some(request => new URL(request.url).pathname.includes('/history'))).toBe(false);
  expect(requests.some(request => new URL(request.url).pathname.startsWith('/api/part/'))).toBe(false);
  if (boundary === 'stale') await vi.waitFor(() => expect(requests.some(request => new URL(request.url).pathname.includes('stale-occurrence/properties'))).toBe(true));
});

it.each(['featurePosition', 'sourceFeaturePosition', 'url'])('refuses a source navigation projection containing reserved %s', async key => {
  const { container, router, requests } = await mount(true, false, false, true, key);
  await vi.waitFor(() => expect(container.textContent).toContain('Source Part'));
  const history = () => [...container.querySelectorAll('button')].find(button => button.textContent?.replace(/\s/g, '') === '履历');
  await vi.waitFor(() => expect(history()).toBeDefined()); act(() => history()!.click());
  await vi.waitFor(() => expect(container.querySelector('.ant-alert-error')).not.toBeNull());
  expect(router.state.location.pathname).toBe('/workspace/permissions');
  expect(requests.some(request => new URL(request.url).pathname.includes('/history'))).toBe(false);
});

it.each(['Part', 'Document'] as const)('opens independent %s history and fresh detail using its own compiled history grant and EID', async owner => {
  const { container, router, requests, points, api } = await mountIndependentHistory(owner);
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="entity"]')).not.toBeNull());
  await act(async () => container.querySelector<HTMLButtonElement>('tr[data-row-key="entity"] button[aria-label="更多"]')!.click());
  const history = () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(entry => entry.textContent?.trim() === '履历');
  await vi.waitFor(() => expect(history()).toBeDefined()); act(() => history()!.click());
  await vi.waitFor(() => expect(router.state.location.pathname.endsWith('/history')).toBe(true));
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="event-1"]')).not.toBeNull());
  await act(async () => container.querySelector<HTMLButtonElement>('tr[data-row-key="event-1"] button[aria-label="更多"]')!.click());
  const detail = () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(entry => entry.textContent?.trim() === '详情');
  await vi.waitFor(() => expect(detail()).toBeDefined()); act(() => detail()!.click());
  await vi.waitFor(() => expect(document.body.textContent).toContain('独立详情最新操作者'));
  const historyRequests = requests.filter(request => new URL(request.url).pathname.startsWith(`${api}/entity/history`) && !new URL(request.url).pathname.includes('/check/'));
  expect(historyRequests).toHaveLength(2);
  expect(historyRequests.every(request => request.headers.get('X-NewP-Feature') === points.historyPosition && !new URL(request.url).searchParams.has('queryScope'))).toBe(true);
  expect(requests.some(request => new URL(request.url).pathname.includes('/workspace/'))).toBe(false);
  expect(requests.every(request => request.method === 'GET')).toBe(true);
});

it.each(['Part', 'Document'] as const)('opens independent %s history from the real list properties and qualified detail contribution', async owner => {
  const { requests, points, api } = await mountIndependentHistory(owner, true);
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer')?.textContent).toContain('Independent object'));
  const history = () => [...document.querySelectorAll('.ant-drawer button')].find(button => button.textContent?.replace(/\s/g, '') === '操作履历');
  await vi.waitFor(() => expect(history()).toBeDefined()); act(() => history()!.click());
  await vi.waitFor(() => expect(document.querySelector('.ant-modal-body')?.textContent).toContain('列表操作者'));
  const request = requests.find(request => new URL(request.url).pathname === `${api}/entity/history`)!;
  expect(request.headers.get('X-NewP-Feature')).toBe(points.historyPosition);
  expect(new URL(request.url).searchParams.has('queryScope')).toBe(false);
  expect(requests.some(request => new URL(request.url).pathname.includes('/workspace/'))).toBe(false);
});

it.each(['Part', 'Document'] as const)('does not offer independent %s history without its independent history grant', async owner => {
  const { container, requests } = await mountIndependentHistory(owner, false, false);
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="entity"]')).not.toBeNull());
  await act(async () => container.querySelector<HTMLButtonElement>('tr[data-row-key="entity"] button[aria-label="更多"]')!.click());
  await vi.waitFor(() => expect(document.querySelector('[role="menuitem"]')).not.toBeNull());
  expect([...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].some(entry => entry.textContent?.trim() === '履历')).toBe(false);
  expect(requests.some(request => new URL(request.url).pathname.includes('/history'))).toBe(false);
});

it('uses the actual Workspace properties PageRef declaration for the readonly owning Part adapter', async () => {
  const { container, requests, positions } = await mount(true, false, false, true);
  await vi.waitFor(() => expect(container.textContent).toContain('Source Part'));
  expect(requests.filter(request => new URL(request.url).pathname.endsWith('/part-occurrence/properties'))).toHaveLength(1);
  const request = requests.find(request => new URL(request.url).pathname.endsWith('/part-occurrence/properties'))!;
  expect(request.headers.get('X-NewP-Feature')).toBe(positions.readPosition);
  expect(request.headers.get('X-NewP-Data-Declaration')).toBe(formalDataAccessFor('workspace.content.properties', 'read').id);
  expect(requests.some(request => new URL(request.url).pathname.startsWith('/api/part/'))).toBe(false);
});
it('rejects an unbound properties PageRef instead of borrowing the independent Part detail declaration', async () => {
  const { container, requests } = await mount(true, false, false, true, undefined, undefined, false, true);
  await vi.waitFor(() => expect(container.textContent).toContain('页面配置或上下文无效'));
  expect(requests.some(request => new URL(request.url).pathname.endsWith('/properties'))).toBe(false);
  expect(requests.some(request => new URL(request.url).pathname.startsWith('/api/part/'))).toBe(false);
});