// @vitest-environment jsdom
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { beforeAll, expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider, invalidatePlatformAntiforgeryToken } from '@icp-platform/core';
import { ModuleRegistry, type ActionDataAccessRequirement } from '@icp-platform/sdk';
import fixture from '../../../../../tests/Kernel.Tests/Fixtures/PublishedStandardDataRuntime.contract.json';
import platformDictionary from '../../../../modules/platform/metadata/dictionaries/Platform.dic.json';
import { componentRoot, loadComponentSource } from '../../test-support/componentTestInputs';
import { loadActiveModuleRuntimeWith, type ActiveModuleRuntimeCatalog } from '../active-module-runtime-loader';
import { PageEntry } from '../composition/PageOutlet';
import { AuthProvider } from '../contexts/AuthContext';
import { bindActionDataAccess } from '../metadata/runtimeMetadataLoader';
import type { RuntimeMetadataState, RuntimeNavItemDocument, RuntimeViewDocument } from '../metadata/runtimeMetadataManifest';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { buildPageEntriesFromNavDocuments, filterFallbackRoutesByMetadataEntries, resolveRouteableMetadataEntries } from '../navigation/pageEntryAdapter';
import { compileStandardCrudRouteProjection } from '../navigation/standardCrudRouteAdapter';
import '../pages/StandardCrudListPageHost';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;
beforeAll(async () => { await Promise.all([import('@ant-design/pro-table'), import('../pages/StandardCrudListDrawerRuntime')]); });

// D9完整runtime输入由PublicPackage_CompilesCanonicalProviderTarget的正式Stage导出，包含同候选全部原生模块目录。
const source = fixture as unknown as {
  packageSha256: string; blueprint: unknown; projection: { resources: Array<{ apiRoute: string; pageRoute: string }> };
  documents: Record<string, { type: string }>;
  dataAccess: ActionDataAccessRequirement[]; features: NonNullable<RuntimeMetadataState['manifest']['modules'][number]['features']>;
  nativeCatalog: ActiveModuleRuntimeCatalog;
};
const nativeEntries = source.nativeCatalog.modules.filter(entry => entry.frontend && entry.frontend.bindingMode !== 'standard-data');
const implementations = new Map(await Promise.all(nativeEntries.map(async entry =>
  [entry.moduleId, await loadComponentSource(entry.moduleId, 'index.ts')] as const)));
function readMetadata(root: string): Array<{ type: string }> {
  return readdirSync(root, { withFileTypes: true }).flatMap(entry => entry.isDirectory()
    ? readMetadata(join(root, entry.name))
    : entry.name.endsWith('.json') ? [JSON.parse(readFileSync(join(root, entry.name), 'utf8'))] : []);
}
const documents = [...nativeEntries.flatMap(entry => readMetadata(join(componentRoot(entry.moduleId), 'metadata'))), ...Object.values(source.documents)];
const navDocuments = documents.filter(document => document.type === 'nav') as RuntimeNavItemDocument[];
const viewDocuments = documents.filter(document => document.type === 'view') as RuntimeViewDocument[];
const loaded = await loadActiveModuleRuntimeWith({
  fetchCatalog: async () => ({ ok: true, status: 200, json: async () => source.nativeCatalog }),
  internalModules: Object.fromEntries(nativeEntries.filter(entry => entry.frontend?.moduleId)
    .map(entry => [entry.frontend!.moduleId!, async () => implementations.get(entry.moduleId)!])),
  importModule: async url => {
    const entry = nativeEntries.find(entry => entry.frontend?.url === url);
    if (!entry) throw new Error(`未登记的正式模块地址：${url}`);
    return implementations.get(entry.moduleId)!;
  },
  fetchJson: async url => {
    if (url.endsWith('/authoring/newp.plugin.json')) return source.blueprint;
    if (url.endsWith('/projections/standard-data.json')) return source.projection;
    throw new Error(`未登记的正式标准数据输入：${url}`);
  },
});

it('compiles every actual native query/history/composed entry together with the complete verified no-Detail package', () => {
  const routes = loaded.modules.flatMap(module => module.routes ?? []);
  const entries = resolveRouteableMetadataEntries(buildPageEntriesFromNavDocuments(navDocuments), routes);
  const projection = compileStandardCrudRouteProjection(loaded.modules, navDocuments, viewDocuments,
    filterFallbackRoutesByMetadataEntries(routes, entries));
  const resource = source.projection.resources[0];
  expect(projection.detailEntries.some(entry => entry.path.startsWith(resource.pageRoute))).toBe(false);
  expect(projection.drawerRedirects).toContainEqual({ from: `${resource.pageRoute}/new`, to: `${resource.pageRoute}?drawer=create` });
  expect(viewDocuments.some(document => !document.view.model && Array.isArray(document.view.rowActions)
    && document.view.rowActions.some(action => action.standardAction === 'view' && action.inputKey))).toBe(true);
  expect(viewDocuments.some(document => Array.isArray(document.view.nodeTypes))).toBe(true);
  expect(viewDocuments.some(document => document.view.detailViewKey)).toBe(true);
  expect(Object.values(source.documents).some(document => 'view' in document
    && (document as RuntimeViewDocument).view.key === 'partner.asset.detail')).toBe(true);
});

it('opens the actual generated PageEntry and keeps its independent edit read and single save through the combined registry', async () => {
  const external = loaded.modules.find(module => module.metadata.moduleId === source.nativeCatalog.modules.find(entry => entry.frontend?.bindingMode === 'standard-data')!.moduleId)!;
  const registration = external.standardCrudResources![0];
  const generatedList = Object.values(source.documents).find(document => 'view' in document
    && (document as RuntimeViewDocument).view.key === registration.listViewKey) as RuntimeViewDocument;
  const editLabel = generatedList.view.rowActions.find((action: { key: string }) => action.key === 'edit').labelKey;
  const edit = source.dataAccess.find(item => item.viewKey === registration.listViewKey && item.actionKey === 'edit')!;
  const listRead = source.dataAccess.find(item => item.viewKey === registration.listViewKey && item.surface === 'read')!;
  const prepare = source.dataAccess.find(item => !item.businessBinding && item.featurePositions.includes(edit.featurePositions[0])
    && item.surface === 'read' && item.id !== listRead.id)!;
  const featurePositions = source.features.map(point => point.position);
  const fields = { EID: 'Asset_formal', Code: 'FORMAL', Name: '真实标准包', PurchasedAt: null };
  const requests: Array<{ method: string; path: string; headers: Headers }> = [];
  const user = { id: 'actor', ecode: 'actor', ename: '管理员', revision: 1, mustChangePassword: false, isLocalAdministrator: true, featurePositions };
  const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = input instanceof Request ? new URL(input.url).pathname : String(input);
    const method = input instanceof Request ? input.method : init?.method ?? 'GET';
    if (path.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (path.endsWith('current-user')) return json(user);
    requests.push({ method, path, headers: new Headers(input instanceof Request ? input.headers : init?.headers) });
    if (method === 'PUT') {
      const payload = input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body));
      fields.Name = payload.Name;
    }
    return json(method === 'GET' ? path.includes('?') ? { items: [fields], total: 1 } : { fields, summaries: {} } : { succeeded: true });
  }));
  ModuleRegistry.replaceAll(loaded.modules, 'published-standard-data-route');
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  onTestFinished(async () => { await act(async () => root.unmount()); container.remove(); ModuleRegistry.replaceAll([], 'clean'); vi.unstubAllGlobals(); invalidatePlatformAntiforgeryToken(); });
  const platform = Object.fromEntries(Object.entries(platformDictionary.Platform).map(([key, value]) => [`Platform.${key}`, value.zhs]));
  const page = (
    <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: platform }}>
      <AuthProvider initialUser={user}><AuthorizationProvider featurePositions={featurePositions}><PlatformQueryProvider scopeKey="published-standard-data-route">
        <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: external.metadata.moduleId, metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: source.features, dataAccess: source.dataAccess }] },
          navDocuments, modelDocuments: documents.filter(document => document.type === 'model'),
          viewDocuments: viewDocuments.map(document => Object.values(source.documents).includes(document)
            ? bindActionDataAccess(document, source.dataAccess) : document) }}>
          <PageEntry page={{ pageKey: 'platform.list', parameters: { viewKey: registration.listViewKey, featurePosition: listRead.featurePositions[0] } }} />
        </RuntimeMetadataProvider>
      </PlatformQueryProvider></AuthorizationProvider></AuthProvider>
    </LocalizationProvider>
  );
  const router = createMemoryRouter([{ path: '*', element: page }], { initialEntries: [source.projection.resources[0].pageRoute] });
  onTestFinished(() => router.dispose());
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(container.textContent).toContain(fields.Name));
  const listLoads = requests.filter(request => request.method === 'GET' && request.path.includes('?')).length;
  const refresh = container.querySelector<HTMLButtonElement>('[aria-label="刷新"]');
  expect(refresh).not.toBeNull();
  await act(async () => refresh!.click());
  await vi.waitFor(() => expect(requests.filter(request => request.method === 'GET' && request.path.includes('?')).length).toBeGreaterThan(listLoads));
  const editControl = [...container.querySelectorAll<HTMLButtonElement>('.ant-table button')].find(button => button.textContent === editLabel);
  expect(editControl, container.textContent ?? '').toBeDefined();
  await act(async () => editControl!.click());
  await vi.waitFor(() => expect(document.querySelector<HTMLInputElement>('.ant-drawer #Name')?.value).toBe(fields.Name));
  const read = requests.find(request => request.method === 'GET' && request.path.endsWith(`/${fields.EID}`))!;
  expect(read.headers.get('X-NewP-Data-Declaration')).toBe(prepare.id);
  expect(read.headers.get('X-NewP-Button')).toBeNull();
  expect(read.headers.get('X-NewP-Feature')).toBe(edit.featurePositions[0]);
  const nameInput = document.querySelector<HTMLInputElement>('.ant-drawer #Name')!;
  await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(nameInput, '正式编辑'); nameInput.dispatchEvent(new Event('input', { bubbles: true })); });
  await act(async () => document.querySelector<HTMLFormElement>('.ant-drawer form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  await vi.waitFor(() => expect(requests.filter(request => request.method === 'PUT')).toHaveLength(1));
  const write = requests.find(request => request.method === 'PUT')!;
  expect(write.headers.get('X-NewP-Button')).toBe(edit.id);
  expect(write.headers.get('X-NewP-Data-Declaration')).toBeNull();
  expect(write.headers.get('X-NewP-Feature')).toBe(edit.featurePositions[0]);
  await vi.waitFor(async () => { await act(async () => undefined); expect(container.textContent).toContain('正式编辑'); });
  await vi.waitFor(async () => { await act(async () => undefined); expect(document.querySelector('.ant-drawer-open')).toBeNull(); });
});
