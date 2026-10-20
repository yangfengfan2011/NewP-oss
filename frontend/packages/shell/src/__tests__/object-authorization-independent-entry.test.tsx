// @vitest-environment jsdom
import { readFileSync, readdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { act, StrictMode, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry, type FrontendBehaviorBindings, type StandardCrudActionInstance } from '@icp-platform/sdk';
import { componentRoot, loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import { validateCompositionCandidate } from '../composition/compositionRegistry';
import { loadActiveModuleRuntimeWith } from '../active-module-runtime-loader';
import { buildPageEntriesFromNavDocuments } from '../navigation/pageEntryAdapter';
import { EntryNavigationProvider } from '../navigation/EntryNavigationContext';
import { renderPageEntry } from '../navigation/pageRouteRenderer';
import { FeaturePlacementContext } from '../authorization/FeaturePlacementContext';
import { platformTestCatalogs } from './localizationTestCatalogs';
import { PageComposition } from '../composition/PageCompositionContext';
import { RuntimeMetadataProvider } from './formalRuntimeMetadataProvider';
import { formalDataAccessFor } from './formalRuntimeMetadataTestSupport';
import type { RuntimeMetadataState } from '../metadata/runtimeMetadataManifest';
import { AuthProvider, useAuth } from '../contexts/AuthContext';
import { CurrentPermissionScope } from '../components/CurrentPermissionScope';
import { ActionInstanceScope } from '../composition/ActionInstanceScope';
import { ActionInstanceContext } from '../composition/pageCompositionRuntime';
import { StandardCrudHostEnvironmentContext } from '../standard-crud/standardCrudHostEnvironment';
import { createEmbeddedHostEnvironment } from './standardCrudHostEnvironmentTestSupport';

vi.unmock('@icp-platform/core');
const owners = ['Workspace', 'Part', 'Document'];
const modules = Object.fromEntries(await Promise.all(owners.map(async owner => [owner,
  (await loadComponentSource<{ default: FrontendBehaviorBindings }>(owner, 'index.ts')).default])));
const fixture = (name: string) => JSON.parse(readFileSync(resolve(__dirname, '../../../../../tests/Kernel.Tests/Fixtures', name + '.contract.json'), 'utf8'));
const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
await import('../composition/StandardTabPage');
await import('../../../ui/src/ObjectAuthorizationPage');

async function mount(settings: { save?: () => Response | Promise<Response>; read?: () => Response | Promise<Response>;
  inherited?: ActionInstanceScope; sourceScope?: boolean; strict?: boolean; readOnly?: boolean; owner?: 'Part' | 'Document' } = {}) {
  const activeOwner = settings.owner ?? 'Part';
  const resource = activeOwner === 'Part' ? 'part.part' : 'document.document';
  const route = activeOwner === 'Part' ? '/parts' : '/documents';
  const endpoint = activeOwner === 'Part' ? '/api/part/parts' : '/api/document/documents';
  const observed = new Map<StandardCrudActionInstance, { disposed: boolean }>();
  const marker = Symbol('test-module-action-state');
  const snapshotId = 'independent-authorization-' + crypto.randomUUID();
  let changeSource!: () => void;
  let refreshIdentity!: () => Promise<void>;
  const documents = (owner: string, folder: string) => {
    const path = resolve(componentRoot(owner), 'metadata', folder);
    return readdirSync(path).filter(file => file.endsWith('.json')).map(file => JSON.parse(readFileSync(resolve(path, file), 'utf8')));
  };
  const metadata: RuntimeMetadataState = { manifest: { modules: [] }, navDocuments: owners.flatMap(owner => documents(owner, 'navigation')),
    viewDocuments: owners.flatMap(owner => documents(owner, 'views')),
    modelDocuments: ['WorkspaceFolder', 'Part', 'Document'].map(model => fixture(model + 'Runtime'))
      .concat(readComponentJson('Workspace', 'metadata/models/WorkspaceFolderContent.model.json')) };
  const nav = metadata.navDocuments.flatMap(document => document.navigation.items);
  metadata.manifest.modules = owners.map(owner => {
    const history = fixture(owner + 'HistoryFeatures');
    const readKey = { Workspace: 'workspace', Part: 'part.parts', Document: 'document.documents' }[owner]!;
    const historyKey = { Workspace: 'workspace.history', Part: 'part.history', Document: 'document.history' }[owner]!;
    nav.filter(item => item.featureKey === readKey || item.key === readKey).forEach(item => { item.featurePosition = history.readPosition; });
    nav.find(item => item.key === historyKey)!.featurePosition = history.historyPosition;
    const features = [{ key: readKey, position: history.readPosition, kind: 'navigation' },
      { key: historyKey, position: history.historyPosition, kind: 'navigation', parentPosition: history.readPosition }];
    return { moduleId: owner, metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: owner === activeOwner ? [...features,
      { key: 'authorize', position: fixture(activeOwner + 'PermissionFeatures').managePosition, kind: 'action', parentPosition: history.readPosition, actionScope: 'list.rowActions' }] : features };
  });
  const entries = buildPageEntriesFromNavDocuments(metadata.navDocuments);
  const permissions = fixture(activeOwner + 'PermissionFeatures');
  const grants = settings.readOnly ? [permissions.readPosition] : [permissions.readPosition, permissions.managePosition];
  const user = { id: 'manager', ecode: 'manager', ename: 'Manager', revision: 1, mustChangePassword: false, featurePositions: grants, timeZoneId: 'UTC' };
  let currentUser = user;
  const requests: Request[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    requests.push(request); const url = new URL(request.url);
    if (url.pathname.endsWith('/current-user')) return json(currentUser);
    if (url.pathname.endsWith('/antiforgery-token')) return json({ requestToken: 'token' });
    if (url.pathname === endpoint) return json({ items: [{ EID: 'part-object', ECODE: 'P-1', ENAME: '独立零组件' }], total: 1, page: 1, pageSize: 20 });
    if (url.pathname === endpoint + '/part-object') return json({ fields: { EID: 'part-object', ECODE: 'P-1', ENAME: '独立零组件' }, summaries: {} });
    if (settings.readOnly && url.pathname.includes('/permissions')) {
      const expected = router.state.location.pathname === route
        ? formalDataAccessFor(resource + '.list', 'rowActions', 'viewAuthorization') : formalDataAccessFor(resource + '.permissions', 'read');
      if (request.headers.get('X-NewP-Data-Declaration') !== expected.id)
        return new Response(JSON.stringify({ code: 'ActionDataAccess.DeclarationInvalid', actualDeclaration: request.headers.get('X-NewP-Data-Declaration') }),
          { status: 400, headers: { 'Content-Type': 'application/json' } });
    }
    if (url.pathname.endsWith('/permissions/eligibility')) return json({ allowed: true });
    if (url.pathname.endsWith('/permissions/catalog')) return json({ actions: [{ key: 'read', title: '读取', enabled: true }], subjectTypes: [{ key: 'all', title: '所有人', requiresId: false }] });
    if (url.pathname.endsWith('/permissions/subjects/user')) return json({ items: [{ id: user.id, name: user.ename }], total: 1,
      page: 1, pageSize: 20, currentValues: [{ id: user.id, name: user.ename }] });
    if (url.pathname.endsWith('/permissions/explanation')) return json({ resourceKey: resource, objectId: 'part-object',
      accountId: user.id, accountName: user.ename, actions: [{ key: 'read', title: '读取', enabled: true, requiresRead: false, allowed: true, sources: [] }] });
    if (url.pathname === endpoint + '/part-object/permissions') return request.method === 'PUT' ? settings.save?.() ?? new Response(null, { status: 204 })
      : settings.read?.() ?? json({ resourceKey: resource, objectId: 'part-object', subjects: [] });
    throw new Error(`Unexpected independent authorization request ${request.method} ${url.pathname}`);
  }));
  const capsules = owners.map(owner => readComponentJson(owner, 'newp.plugin.json'));
  const loaded = await loadActiveModuleRuntimeWith({ fetchCatalog: async () => ({ ok: true, status: 200, json: async () => ({ schemaVersion: '1.0', snapshotId, planHash: snapshotId,
    modules: capsules.map(capsule => ({ moduleId: capsule.moduleId, version: capsule.version, displayName: capsule.displayName, dependencies: capsule.dependencies,
      frontend: { moduleId: capsule.moduleId, behaviorKey: capsule.frontend.behaviorKey, bindingMode: 'behavior-bindings', bindings: capsule.frontend.bindings } })) }) }),
    internalModules: Object.fromEntries(owners.map(owner => [owner, async () => ({ default: owner !== 'Part' ? modules[owner] : {
      ...modules[owner], standardCrudCapabilities: modules[owner].standardCrudCapabilities?.map(binding => binding.targetKey !== 'part.part.list' ? binding : {
        ...binding, load: async () => {
          const loaded = await binding.load();
          const render = loaded.default.actionInputRenderers!.objectAuthorization;
          return { default: { ...loaded.default, actionInputRenderers: { ...loaded.default.actionInputRenderers,
            objectAuthorization: Object.assign((context: Parameters<typeof render>[0]) => {
              if (context.instance) observed.set(context.instance, context.instance.getOrCreate(marker, () => ({ disposed: false }), state => { state.disposed = true; }));
              return render(context);
            }, render) } } };
        },
      }),
    } })])) });
  expect(() => validateCompositionCandidate(loaded.modules, metadata)).not.toThrow();
  ModuleRegistry.replaceAll(loaded.modules, snapshotId);
  const catalog: Record<string, string> = { ...platformTestCatalogs.zhs };
  for (const owner of [...owners, 'Authorization']) for (const dictionary of documents(owner, 'dictionaries'))
    for (const [prefix, values] of Object.entries(dictionary)) if (values && typeof values === 'object')
      for (const [key, value] of Object.entries(values)) if (value && typeof value === 'object' && 'zhs' in value)
        catalog[`${prefix}.${key}`] = (value as { zhs: string }).zhs;
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  function Controls({ children }: { children: React.ReactNode }) {
    refreshIdentity = useAuth().retrySessionValidation;
    const [scope, setScope] = useState('first'); changeSource = () => setScope('second');
    const content = settings.sourceScope ? <StandardCrudHostEnvironmentContext.Provider value={createEmbeddedHostEnvironment({ input: {
      instanceId: 'same-list-page', queryScope: { source: scope },
    } })}>{children}</StandardCrudHostEnvironmentContext.Provider> : children;
    return settings.inherited ? <ActionInstanceContext.Provider value={settings.inherited}>{content}</ActionInstanceContext.Provider> : content;
  }
  const wrap = (content: React.ReactNode) => <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: catalog }}>
    <AuthProvider initialUser={user}><AuthorizationProvider featurePositions={grants}><CurrentPermissionScope><RuntimeMetadataProvider value={metadata}>
      <EntryNavigationProvider><PageComposition><Controls>{content}</Controls></PageComposition></EntryNavigationProvider>
    </RuntimeMetadataProvider></CurrentPermissionScope></AuthorizationProvider></AuthProvider>
  </LocalizationProvider>;
  const entry = entries.find(item => item.path === route)!;
  expect(entry.entryType).toBe('standard-list');
  const router = createMemoryRouter([{ path: entry.path, element: wrap(
    <FeaturePlacementContext.Provider value={{ entryPosition: entry.featurePosition! }}>{renderPageEntry(entry)}</FeaturePlacementContext.Provider>) },
    ...entries.filter(item => item.path === route + '/permissions').map(item => ({ path: item.path, element: wrap(renderPageEntry(item)) }))], { initialEntries: [route] });
  let mounted = true;
  const unmount = async () => { if (mounted) { await act(async () => root.unmount()); mounted = false; } };
  onTestFinished(async () => { await unmount(); router.dispose(); container.remove(); ModuleRegistry.replaceAll([], 'independent-authorization-cleanup'); vi.unstubAllGlobals(); });
  await act(async () => root.render(settings.strict ? <StrictMode><RouterProvider router={router} /></StrictMode> : <RouterProvider router={router} />));
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="part-object"]')).not.toBeNull());
  return { container, requests, observed, unmount, router, changeBoundary: async (boundary: 'account' | 'source' | 'unmount') => {
    if (boundary === 'unmount') await unmount();
    else if (boundary === 'source') changeSource();
    else { currentUser = { ...user, id: 'other-manager', ecode: 'other-manager' }; await refreshIdentity(); }
  } };
}

async function openAuthorization(container: HTMLElement) {
  await act(async () => container.querySelector<HTMLButtonElement>('tr[data-row-key="part-object"] button[aria-label="更多"]')!.click());
  const action = () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(item => item.textContent?.trim() === '对象授权');
  await vi.waitFor(() => expect(action()).toBeDefined());
  await act(async () => action()!.click());
  await vi.waitFor(() => expect(document.querySelector('[role="dialog"]')).not.toBeNull());
}

const confirm = () => document.querySelector<HTMLButtonElement>('[role="dialog"] .ant-modal-footer button.ant-btn-primary');
const button = (name: string) => [...document.querySelectorAll<HTMLButtonElement>('button')].find(value => value.textContent?.replace(/\s/g, '') === name);

it.each(['Part', 'Document'] as const)('passes the actual readonly %s tab source through PageEntry without an authorize grant', async owner => {
  const { container, requests, router } = await mount({ readOnly: true, owner });
  await act(async () => container.querySelector<HTMLButtonElement>('tr[data-row-key="part-object"] button[aria-label="更多"]')!.click());
  const permissions = () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(item => item.textContent?.replace(/\s/g, '') === '权限');
  await vi.waitFor(() => expect(permissions(), JSON.stringify({
    menus: [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].map(item => item.textContent),
    reads: requests.filter(request => new URL(request.url).pathname.includes('/permissions/')).map(request => ({
      path: new URL(request.url).pathname, declaration: request.headers.get('X-NewP-Data-Declaration') })),
  })).toBeDefined());
  await act(async () => permissions()!.click());
  const resource = owner === 'Part' ? 'part.part' : 'document.document';
  const expected = formalDataAccessFor(resource + '.permissions', 'read');
  const actionSource = formalDataAccessFor(resource + '.list', 'rowActions', 'viewAuthorization');
  const earlyReads = requests.filter(request => new URL(request.url).pathname.includes('/permissions/'));
  for (const request of earlyReads) expect(request.headers.get('X-NewP-Data-Declaration'), new URL(request.url).pathname).toBe(actionSource.id);
  await vi.waitFor(() => expect(router.state.location.pathname).toBe(owner === 'Part' ? '/parts/permissions' : '/documents/permissions'));
  const tab = () => [...container.querySelectorAll<HTMLElement>('[role="tab"]')].find(item => item.textContent === '权限');
  await vi.waitFor(() => expect(tab()).toBeDefined());
  await act(async () => tab()!.click());
  await vi.waitFor(() => expect(requests.some(request => new URL(request.url).pathname.endsWith('/permissions/explanation'))).toBe(true));
  await vi.waitFor(() => expect(requests.some(request => new URL(request.url).pathname.endsWith('/permissions/subjects/user'))).toBe(true));
  const reads = requests.filter(request => new URL(request.url).pathname.includes('/permissions/'));
  expect(reads.length).toBeGreaterThanOrEqual(2);
  for (const request of reads) {
    expect([actionSource.id, expected.id]).toContain(request.headers.get('X-NewP-Data-Declaration'));
    expect(request.headers.get('X-NewP-Feature')).toBe(fixture(owner + 'PermissionFeatures').readPosition);
  }
  expect(reads.find(request => new URL(request.url).pathname.endsWith('/permissions/explanation'))!.headers.get('X-NewP-Data-Declaration')).toBe(expected.id);
  const catalogs = reads.filter(request => new URL(request.url).pathname.endsWith('/permissions/catalog'));
  expect(catalogs[0].headers.get('X-NewP-Data-Declaration')).toBe(actionSource.id);
  expect(catalogs[catalogs.length - 1].headers.get('X-NewP-Data-Declaration')).toBe(expected.id);
  expect(requests.some(request => request.method === 'PUT')).toBe(false);
});

it('saves owner authorization through the real standalone Part route without an injected action instance', async () => {
  const { container, requests } = await mount();
  await openAuthorization(container);
  await vi.waitFor(() => expect(confirm()?.disabled).toBe(false));
  await act(async () => confirm()!.click());
  await vi.waitFor(() => expect(requests.filter(request => request.method === 'PUT')).toHaveLength(1));
  await vi.waitFor(() => expect(document.querySelector('[role="dialog"]')).toBeNull());
  const source = formalDataAccessFor('part.part.list', 'rowActions', 'authorize');
  const rowView = formalDataAccessFor('part.part.list', 'rowActions', 'viewAuthorization');
  for (const request of requests.filter(request => new URL(request.url).pathname.includes('/permissions'))) {
    // 当前行还会独立检查既有查看入口；它保留自己的read来源，不冒充打开的管理输入。
    if (request.headers.get('X-NewP-Data-Declaration') === rowView.id) {
      expect(new URL(request.url).pathname).toBe('/api/part/parts/part-object/permissions/catalog');
      expect(request.method).toBe('GET');
      expect(request.headers.get('X-NewP-Feature')).toBe(fixture('PartPermissionFeatures').readPosition);
      continue;
    }
    expect(request.headers.get('X-NewP-Data-Declaration')).toBe(source.id);
    expect(request.headers.get('X-NewP-Feature')).toBe(fixture('PartPermissionFeatures').managePosition);
  }
});

it('keeps the same standalone owner and unknown outcome across Review close and reopen without replay', async () => {
  const { container, requests, observed } = await mount({ save: async () => { throw new TypeError('response lost'); } });
  await openAuthorization(container);
  await vi.waitFor(() => expect(confirm()?.disabled).toBe(false));
  await act(async () => confirm()!.click());
  await vi.waitFor(() => expect(button('核对结果')).toBeDefined());
  expect(confirm()?.disabled).toBe(true);
  const owner = [...observed.keys()][0];
  await act(async () => button('取消')!.click());
  await vi.waitFor(() => expect(document.querySelector('[role="dialog"]')).toBeNull());
  expect(owner.active).toBe(true);
  expect(observed.get(owner)?.disposed).toBe(false);
  await openAuthorization(container);
  await vi.waitFor(() => expect(button('核对结果')).toBeDefined());
  expect(observed.size).toBe(1);
  expect(confirm()?.disabled).toBe(true);
  await act(async () => confirm()!.click());
  expect(requests.filter(request => request.method === 'PUT')).toHaveLength(1);
});

it.each(['account', 'source', 'unmount'] as const)('ends only the old standalone owner and aborts its late configuration on %s', async boundary => {
  let finish!: (response: Response) => void;
  const { container, requests, observed, changeBoundary } = await mount({ sourceScope: true,
    read: () => new Promise<Response>(resolve => { finish = resolve; }) });
  await openAuthorization(container);
  await vi.waitFor(() => expect(finish).toBeTypeOf('function'));
  const owner = [...observed.keys()][0];
  const request = requests.find(request => request.method === 'GET' && new URL(request.url).pathname === '/api/part/parts/part-object/permissions')!;
  await act(async () => changeBoundary(boundary));
  await vi.waitFor(() => expect(owner.active).toBe(false));
  expect(observed.get(owner)?.disposed).toBe(true);
  await vi.waitFor(() => expect(request.signal.aborted).toBe(true));
  await act(async () => finish(json({ resourceKey: 'part.part', objectId: 'part-object', subjects: [
    { id: 'old-private', type: 'all', subjectId: null, priority: 0, actions: { read: 'deny' }, expiresAt: null },
  ] })));
  expect(document.querySelector('tr[data-row-key="old-private"]')).toBeNull();
  expect(requests.some(request => request.method === 'PUT')).toBe(false);
});

it('inherits an existing outer action owner and never activates or disposes it on list unmount', async () => {
  const inherited = new ActionInstanceScope(); const deactivate = inherited.activate();
  try {
    const { container, observed, unmount } = await mount({ inherited });
    await openAuthorization(container);
    await vi.waitFor(() => expect(confirm()?.disabled).toBe(false));
    expect([...observed.keys()]).toEqual([inherited]);
    await unmount();
    expect(inherited.active).toBe(true);
    expect(observed.get(inherited)?.disposed).toBe(false);
    deactivate();
    expect(observed.get(inherited)?.disposed).toBe(true);
  } finally { deactivate(); }
});

it('keeps one live standalone action owner through StrictMode activation and submits once', async () => {
  const { container, requests, observed } = await mount({ strict: true });
  await openAuthorization(container);
  await vi.waitFor(() => expect(confirm()?.disabled).toBe(false));
  expect(observed.size).toBe(1);
  expect([...observed.keys()].every(owner => owner.active)).toBe(true);
  await act(async () => confirm()!.click());
  await vi.waitFor(() => expect(requests.filter(request => request.method === 'PUT')).toHaveLength(1));
});
