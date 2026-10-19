import { readFileSync, readdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry, type FrontendBehaviorBindings } from '@icp-platform/sdk';
import { componentRoot, loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import { validateCompositionCandidate } from '../composition/compositionRegistry';
import { loadActiveModuleRuntimeWith } from '../active-module-runtime-loader';
import { buildPageEntriesFromNavDocuments } from '../navigation/pageEntryAdapter';
import { EntryNavigationProvider } from '../navigation/EntryNavigationContext';
import { RouteAccess } from '../App';
import { renderPageEntry } from '../navigation/pageRouteRenderer';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { bindFormalRuntimeMetadata } from './formalRuntimeMetadataTestSupport';
import type { RuntimeMetadataState } from '../metadata/runtimeMetadataManifest';
import { AuthProvider } from '../contexts/AuthContext';
import { CurrentPermissionScope } from '../components/CurrentPermissionScope';
import clipboardContract from '../../../../../tests/Kernel.Tests/Fixtures/WorkspaceClipboardDataAccess.contract.json';

await import('../pages/StandardCrudListPageHost');
await import('../pages/StandardCrudListOverlayRuntime');
await import('@icp-platform/ui/crud-form');

export interface IndependentHistoryOptions {
  fullDetail?: boolean;
  historyAllowed?: boolean;
  checkHistory?: (request: Request) => Response | Promise<Response>;
  fetchOverride?: (request: Request) => Response | undefined | Promise<Response | undefined>;
}
const item = { id: 'event-1', eventType: 'dataScope.object.changed', occurredAt: '2026-10-18T00:00:00Z', objectType: 'Part',
  actor: { name: '列表操作者', status: 'available' }, target: { name: null, code: null, nameSource: 'unrecorded' },
  description: { key: 'ObjectHistory.DescriptionUnavailable', parameters: {} }, ipAddress: null };
const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });

export function authoring(): RuntimeMetadataState {
  const documents = (moduleId: string, folder: string) => {
    const root = resolve(componentRoot(moduleId), 'metadata', folder);
    return readdirSync(root).filter(file => file.endsWith('.json')).map(file => JSON.parse(readFileSync(resolve(root, file), 'utf8')));
  };
  return { manifest: { modules: [] }, navDocuments: ['Workspace', 'Part', 'Document'].flatMap(owner => documents(owner, 'navigation')),
    viewDocuments: ['Workspace', 'Part', 'Document'].flatMap(owner => documents(owner, 'views')),
    modelDocuments: ['WorkspaceFolder', 'Part', 'Document'].map(model => JSON.parse(readFileSync(resolve(__dirname, '../../../../../tests/Kernel.Tests/Fixtures', model + 'Runtime.contract.json'), 'utf8')))
      .concat(readComponentJson('Workspace', 'metadata/models/WorkspaceFolderContent.model.json')) };
}

export function installHistoryFeatures(metadata: RuntimeMetadataState) {
  const nav = metadata.navDocuments.flatMap(document => document.navigation.items);
  const positions = Object.fromEntries(['Workspace', 'Part', 'Document'].map(owner => [owner,
    JSON.parse(readFileSync(resolve(__dirname, '../../../../../tests/Kernel.Tests/Fixtures', `${owner}HistoryFeatures.contract.json`), 'utf8')) as { readPosition: string; historyPosition: string }]));
  metadata.manifest.modules = ['Workspace', 'Part', 'Document'].map(owner => {
    const keys = { Workspace: ['workspace', 'workspace.history'], Part: ['part.parts', 'part.history'], Document: ['document.documents', 'document.history'] }[owner]!;
    const points = positions[owner];
    nav.find(entry => entry.key === keys[0])!.featurePosition = points.readPosition;
    nav.find(entry => entry.key === keys[1])!.featurePosition = points.historyPosition;
    const declared = metadata.viewDocuments.filter(document => String(document.view.key).startsWith(owner.toLowerCase() + '.'))
      .some(({ view }) => view.dataAccess !== undefined || ['pageActions', 'rowActions', 'outputActions', 'detailContributions']
        .some(surface => Array.isArray(view[surface]) && (view[surface] as Array<Record<string, unknown>>).some(action => action.dataAccess !== undefined)));
    return { moduleId: owner, metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [],
      dataAccess: declared ? [...JSON.parse(readFileSync(resolve(__dirname, '../../../../../tests/Kernel.Tests/Fixtures', `${owner}HistoryDataAccess.contract.json`), 'utf8')),
        ...(owner === 'Workspace' ? clipboardContract.declarations.filter(item => item.execution.startsWith('workspace.clipboard.')) : [])] : [], features: [
      { key: keys[0], position: points.readPosition, kind: 'navigation' },
      { key: keys[1], position: points.historyPosition, kind: 'navigation', parentPosition: points.readPosition },
    ] };
  });
  const bound = bindFormalRuntimeMetadata(metadata);
  metadata.manifest = bound.manifest;
  metadata.viewDocuments = bound.viewDocuments;
  return positions;
}

export function expectFixedReadHistoryRequests(metadata: RuntimeMetadataState, requests: Request[]) {
  const declarations = metadata.manifest.modules.flatMap(module => module.dataAccess ?? []);
  const historyRequests = requests.filter(request => new URL(request.url).pathname.includes('/history'));
  expect(historyRequests.length).toBeGreaterThan(0);
  for (const request of historyRequests) {
    const matches = declarations.filter(declaration => declaration.id === request.headers.get('X-NewP-Data-Declaration'));
    expect(matches).toHaveLength(1);
    const declaration = matches[0];
    expect(declaration.provider).toBe(`${declaration.moduleId.toLowerCase()}.backend`);
    expect(declaration.featurePositions).toContain(request.headers.get('X-NewP-Feature'));
    expect(declaration.requirements).toEqual([{ role: 'target', stage: 'read', shape: 'object', resources: declaration.resources,
      anyOf: [['read']], minimumCount: 1, maximumCount: 1 }]);
  }
}

export async function mountIndependentHistory(owner: 'Part' | 'Document', options: IndependentHistoryOptions | boolean = false, grantHistory = true) {
  const settings: IndependentHistoryOptions = typeof options === 'boolean' ? { fullDetail: options, historyAllowed: grantHistory } : options;
  const { fullDetail = false, historyAllowed = true, checkHistory, fetchOverride } = settings;
  const workspace = (await loadComponentSource<{ default: FrontendBehaviorBindings }>('Workspace', 'index.ts')).default;
  const part = (await loadComponentSource<{ default: FrontendBehaviorBindings }>('Part', 'index.ts')).default;
  const documentModule = (await loadComponentSource<{ default: FrontendBehaviorBindings }>('Document', 'index.ts')).default;
  const prefix = owner.toLowerCase();
  const resourceKey = `${prefix}.${prefix}`;
  const api = owner === 'Part' ? '/api/part/parts' : '/api/document/documents';
  const metadata = authoring();
  const points = installHistoryFeatures(metadata)[owner];
  const grants = [points.readPosition, ...(historyAllowed ? [points.historyPosition] : [])];
  const user = { id: 'reader', ecode: 'reader', ename: 'Reader', revision: 1, mustChangePassword: false, isLocalAdministrator: false, featurePositions: grants, timeZoneId: 'UTC' };
  const requests: Request[] = [];
  const unexpectedRequests: string[] = [];
  const record = { EID: 'entity', ECODE: 'E1', ENAME: 'Independent object', Description: '', OwnerId: null, OwnerDepartmentId: null,
    CreatedBy: 'reader', CreatedAt: '2026-10-18T00:00:00Z', UpdatedBy: 'reader', UpdatedAt: '2026-10-18T00:00:00Z' };
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    requests.push(request); const url = new URL(request.url);
    const overridden = await fetchOverride?.(request);
    if (overridden) return overridden;
    if (url.pathname.startsWith(`${api}/entity/history/check/`)) return checkHistory?.(request) ?? new Response(null, { status: 204 });
    if (url.pathname.endsWith('/current-user')) return json(user);
    if (url.pathname === `${api}/entity/permissions/catalog`) return json({ actions: [{ key: 'read', title: '读取', enabled: true }], subjectTypes: [] });
    if (url.pathname === `${api}/ownership/eligibility`) return new Response('{}', { status: 403 });
    if (url.pathname === api) return json({ items: [record], total: 1, page: 1, pageSize: 20 });
    if (url.pathname === `${api}/entity`) return json({ fields: record, summaries: owner === 'Document'
      ? { files: { status: 'ready', data: { documentId: 'entity', featurePosition: points.readPosition } } } : {} });
    if (url.pathname === `${api}/entity/files`) return json({ documentId: 'entity', versionId: 'version', iterationId: 'iteration', files: [], downloadAllowed: false });
    if (url.pathname === `${api}/entity/history`) return json({ items: [{ ...item, objectType: owner }], total: 1, pageIndex: 1, pageSize: 20 });
    if (url.pathname === `${api}/entity/history/event-1`) return json({ ...item, objectType: owner, actor: { name: '独立详情最新操作者', status: 'available' } });
    unexpectedRequests.push(`${request.method} ${url.pathname}`);
    throw new Error(`Unexpected independent history request: ${request.method} ${url.pathname}`);
  }));
  const capsules = ['Workspace', 'Part', 'Document'].map(moduleId => readComponentJson(moduleId, 'newp.plugin.json'));
  const loaded = await loadActiveModuleRuntimeWith({
    fetchCatalog: async () => ({ ok: true, status: 200, json: async () => ({ schemaVersion: '1.0', snapshotId: 'independent-history', planHash: 'independent-history',
      modules: capsules.map(capsule => ({ moduleId: capsule.moduleId, version: capsule.version, displayName: capsule.displayName, dependencies: capsule.dependencies,
        frontend: { moduleId: capsule.moduleId, behaviorKey: capsule.frontend.behaviorKey, bindingMode: 'behavior-bindings', bindings: capsule.frontend.bindings } })) }) }),
    internalModules: { Workspace: async () => ({ default: workspace }), Part: async () => ({ default: part }), Document: async () => ({ default: documentModule }) },
  });
  expect(() => validateCompositionCandidate(loaded.modules, metadata)).not.toThrow();
  ModuleRegistry.replaceAll(loaded.modules, 'independent-history');
  const words = Object.fromEntries([{ moduleId: 'Audit', dicKey: 'ObjectHistory' }, { moduleId: owner, dicKey: owner },
    { moduleId: owner, dicKey: `${owner}History` }].flatMap(({ moduleId, dicKey }) => {
    const dictionary = readComponentJson(moduleId, `metadata/dictionaries/${dicKey}.dic.json`)[dicKey];
    return Object.entries(dictionary).map(([key, value]) => [`${dicKey}.${key}`, (value as { zhs: string }).zhs]);
  }));
  const nav = metadata.navDocuments.flatMap(document => document.navigation.items);
  const target = nav.find(entry => entry.key === `${prefix}.history`)!;
  const pages = buildPageEntriesFromNavDocuments(metadata.navDocuments);
  const historyEntry = pages.find(entry => entry.path === target.path)!;
  const listTarget = nav.find(entry => entry.targetKey === `${resourceKey}.list` && entry.entryType === 'standard-list')!;
  const listEntry = pages.find(entry => entry.path === listTarget.path)!;
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const wrap = (content: React.ReactNode) => <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: words }}>
    <AuthProvider initialUser={user}><AuthorizationProvider featurePositions={grants}><CurrentPermissionScope><RuntimeMetadataProvider value={metadata}>
      <EntryNavigationProvider>{content}</EntryNavigationProvider>
    </RuntimeMetadataProvider></CurrentPermissionScope></AuthorizationProvider></AuthProvider>
  </LocalizationProvider>;
  const path = listTarget.path!;
  const router = createMemoryRouter([
    { path, element: wrap(<RouteAccess featurePosition={listEntry.featurePosition}>{renderPageEntry(listEntry)}</RouteAccess>) },
    { path: target.path!, element: wrap(<RouteAccess featurePosition={historyEntry.featurePosition}>{renderPageEntry(historyEntry)}</RouteAccess>) },
  ], { initialEntries: [path] });
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); ModuleRegistry.replaceAll([], 'independent-history-cleanup'); vi.unstubAllGlobals(); expect(unexpectedRequests).toEqual([]); });
  await act(async () => root.render(<RouterProvider router={router} />));
  if (fullDetail) {
    // 从真实列表控件进入属性；对象、读取位置和Drawer实例由正式宿主产生。
    await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="entity"]')).not.toBeNull());
    await act(async () => container.querySelector<HTMLButtonElement>('tr[data-row-key="entity"] button[aria-label="更多"]')!.click());
    const properties = () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')]
      .find(control => control.textContent?.trim() === words[`${owner}.OpenProperties`]);
    await vi.waitFor(() => expect(properties()).toBeDefined());
    await act(async () => properties()!.click());
    await vi.waitFor(() => expect(document.querySelector('.ant-drawer .np-crud-detail-sections')).not.toBeNull());
    const detailRead = requests.find(request => new URL(request.url).pathname === `${api}/entity`);
    expect(detailRead?.headers.get('X-NewP-Feature')).toBe(points.readPosition);
  }
  return { container, router, requests, points, api, root, metadata };
}
