// @vitest-environment jsdom
import { readFileSync, readdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import organizationBindings from '../../../../modules/platform/organization/src';
import runtimeModels from '../../../../../tests/Kernel.Tests/Fixtures/OrganizationRuntimeModels.contract.json';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { buildPageEntriesFromNavDocuments } from '../navigation/pageEntryAdapter';
import { compileStandardCrudRouteProjection } from '../navigation/standardCrudRouteAdapter';
import { renderPageEntry } from '../navigation/pageRouteRenderer';
import { RouteCapabilityOutlet } from '../capabilities/RouteCapabilityOutlet';
import type { RuntimeMetadataState } from '../metadata/runtimeMetadataManifest';

// 先装载实际人员 capability，交互仍通过真实路由与受控注册执行。
await organizationBindings.standardCrudCapabilities!.find(binding => binding.targetKey === 'organization.person.list')!.load();

vi.mock('@icp-platform/core', async original => ({ ...await original<typeof import('@icp-platform/core')>(), platformFetch: (input: RequestInfo | URL, init?: RequestInit) => fetch(input, init) }));
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
let root: Root;
let container: HTMLDivElement;
let router: ReturnType<typeof createMemoryRouter>;
afterEach(async () => { await act(async () => root?.unmount()); container?.remove(); router?.dispose(); vi.restoreAllMocks(); });
async function waitFor(test: () => boolean) {
  for (let i = 0; i < 100 && !test(); i++) await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)); });
  expect(test(), document.body.textContent ?? '').toBe(true);
}
async function click(text: string) {
  const target = [...document.querySelectorAll('button,a,[role="tab"]')].find(item => item.textContent?.replace(/\s/g, '') === text.replace(/\s/g, ''));
  expect(target, text).toBeDefined();
  await act(async () => target!.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true })));
}
it('loads the real personnel capability through an old address, keeps embedded queries local, and opens its explicitly owned full detail', async () => {
  vi.spyOn(window, 'matchMedia').mockImplementation(query => ({ matches: true, media: query, onchange: null, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {}, dispatchEvent: () => true }));
  const folder = resolve(__dirname, '../../../../modules/platform/organization/metadata');
  const read = (name: string) => readdirSync(resolve(folder, name)).map(file => JSON.parse(readFileSync(resolve(folder, name, file), 'utf8').replace(/^\uFEFF/, '')));
  const metadata: RuntimeMetadataState = { manifest: { modules: [] }, navDocuments: read('navigation'), viewDocuments: read('views'), modelDocuments: Object.values(runtimeModels) as RuntimeMetadataState['modelDocuments'] };
  const catalog: Record<string, string> = {};
  for (const document of read('dictionaries')) for (const [name, words] of Object.entries(document)) {
    if (typeof words !== 'object' || !words) continue;
    for (const [key, text] of Object.entries(words)) catalog[`${name}.${key}`] = (text as { zhs: string }).zhs;
  }
  const modules = [{ ...organizationBindings, metadata: { moduleId: 'Organization', displayName: 'Organization', version: '1' } }];
  ModuleRegistry.replaceAll(modules, 'organization-management-page');
  const calls: string[] = [];
  const person = { EID: 'p1', PersonNo: 'P001', ENAME: '张三', IsEnabled: true, EmploymentStatus: 'FullTime', BirthDate: null, CompanyId: null, DepartmentId: null, PositionId: null, AccountEID: null, EmploymentStartedAt: null, EmploymentEndedAt: null };
  vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
    const url = String(input); calls.push(url);
    const data = url.includes('/persons/p1') ? { fields: person, summaries: {} }
      : url.includes('/persons?') ? { items: [person], total: 1, page: 1, pageSize: 20 }
        : { items: [], total: 0, page: 1, pageSize: 20 };
    return new Response(JSON.stringify(data), { status: 200, headers: { 'Content-Type': 'application/json' } });
  });
  const projection = compileStandardCrudRouteProjection(modules, metadata.navDocuments, metadata.viewDocuments, []);
  const wrap = (element: React.ReactNode) => <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: catalog }}>
    <AuthorizationProvider permissions={['View', 'Create', 'Update', 'Delete'].map(action => `Organization.Person.${action}`)}><PlatformQueryProvider scopeKey="organization-management"><RuntimeMetadataProvider value={metadata}>{element}</RuntimeMetadataProvider></PlatformQueryProvider></AuthorizationProvider>
  </LocalizationProvider>;
  router = createMemoryRouter([
    ...buildPageEntriesFromNavDocuments(metadata.navDocuments).map(entry => ({ path: entry.path, element: wrap(renderPageEntry(entry)) })),
    ...projection.detailEntries.map(entry => ({ path: entry.path, element: wrap(<RouteCapabilityOutlet capability={{ kind: 'standard-crud-detail', targetKey: entry.viewKey }} />) })),
  ], { initialEntries: ['/organization/persons'] });
  container = document.createElement('div'); document.body.append(container);
  await act(async () => { root = createRoot(container); root.render(<RouterProvider router={router} />); });
  await waitFor(() => container.textContent?.includes('张三') ?? false);
  expect(router.state.location.pathname + router.state.location.search).toBe('/organization/structure?tab=persons');
  expect(calls.some(url => url.includes('/tree/'))).toBe(false);
  expect(container.querySelectorAll('.sh-page')).toHaveLength(1);
  const activePanel = container.querySelector('.ant-tabs-tabpane-active')!;
  expect(activePanel.querySelector('.np-crud-embedded')).not.toBeNull();
  expect(activePanel.querySelector('.np-crud-workspace:not(.np-crud-embedded)')).toBeNull();
  expect(activePanel.querySelector('.np-tree-workspace')).toBeNull();
  const keyword = container.querySelector('input[placeholder*="关键字"]') ?? container.querySelector('input');
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(keyword, '张');
    keyword!.dispatchEvent(new Event('input', { bubbles: true }));
  });
  await click('搜索');
  await waitFor(() => calls.some(url => url.includes('keyword=')));
  expect(router.state.location.search).toBe('?tab=persons');
  await click('张三');
  await waitFor(() => document.body.textContent?.includes('查看完整详情') ?? false);
  await click('公司');
  await waitFor(() => !document.querySelector('.ant-drawer-open'));
  expect(router.state.location.search).toBe('?tab=companies');
  await click('人员');
  await waitFor(() => document.body.textContent?.includes('查看完整详情') ?? false);
  await click('查看完整详情');
  await waitFor(() => router.state.location.pathname === '/organization/persons/p1' && (container.textContent?.includes('张三') ?? false));
  expect(container.querySelector('[data-route-capability-error]')).toBeNull();
  expect(calls.some(url => url.includes('/persons/p1'))).toBe(true);
}, 15000);
