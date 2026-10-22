// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry, type ActionDataAccessRequirement, type FrontendBehaviorBindings } from '@icp-platform/sdk';
import { loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import requirements from '../../../../../tests/Kernel.Tests/Fixtures/OrganizationDataAccess.contract.json';
import features from '../../../../../tests/Kernel.Tests/Fixtures/PositionButtonFeatures.contract.json';
import model from '../../../../../tests/Kernel.Tests/Fixtures/PositionRuntime.contract.json';
import platformDictionary from '../../../../modules/platform/metadata/dictionaries/Platform.dic.json';
import { AuthProvider } from '../contexts/AuthContext';
import { PageEntry } from '../composition/PageOutlet';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { bindActionDataAccess } from '../metadata/runtimeMetadataLoader';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;
const bindings = (await loadComponentSource<{ default: FrontendBehaviorBindings }>('Organization', 'index.ts')).default;
const list = readComponentJson('Organization', 'metadata/views/PositionList.view.json');
const detail = readComponentJson('Organization', 'metadata/views/PositionDetail.view.json');
const declarations = requirements as ActionDataAccessRequirement[];
const source = (viewKey: string, surface: string, actionKey = '') => declarations.find(item => item.viewKey === viewKey && item.surface === surface && item.actionKey === actionKey)!;
const entryPosition = source(list.view.key, 'read').featurePositions[0];

it.each(['row', 'detail'] as const)('sends the actual Position %s edit button and feature through the formal host', async entry => {
  // 与已有目录宿主测试一致，开启桌面断点；jsdom默认false会把真实名称列隐藏。
  vi.spyOn(window, 'matchMedia').mockImplementation(query => ({ matches: true, media: query, onchange: null,
    addListener: vi.fn(), removeListener: vi.fn(), addEventListener: vi.fn(), removeEventListener: vi.fn(), dispatchEvent: vi.fn() }));
  let fields = { EID: 'Position_one', ENAME: '岗位原名', Description: '', IsEnabled: true,
    CreatedBy: 'actor', CreatedBy_DisplayValue: '账号', UpdatedBy: 'actor', UpdatedBy_DisplayValue: '账号',
    CreatedAt: '2026-10-20T00:00:00Z', CreatedAt_DisplayValue: '2026-10-20', UpdatedAt: '2026-10-20T00:00:00Z', UpdatedAt_DisplayValue: '2026-10-20' };
  const expected = source(entry === 'row' ? list.view.key : detail.view.key, entry === 'row' ? 'rowActions' : 'pageActions', 'edit');
  const positions = features.map(point => point.position);
  const user = { id: 'actor', ecode: 'actor', ename: '账号', revision: 1, mustChangePassword: false, featurePositions: positions };
  const writes: Array<{ button: string | null; position: string | null }> = [];
  const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    const path = new URL(request.url).pathname;
    if (path.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (path.endsWith('current-user')) return json(user);
    if (request.method === 'PUT') {
      writes.push({ button: request.headers.get('X-NewP-Button'), position: request.headers.get('X-NewP-Feature') });
      expect(request.headers.has('X-NewP-Data-Declaration')).toBe(false);
      if (writes.at(-1)!.button !== expected.id || !expected.featurePositions.includes(writes.at(-1)!.position!)) return json({ code: 'WRONG_BUTTON' }, 400);
      fields = { ...fields, ...await request.json() }; return json({ fields });
    }
    if (path.endsWith('/positions')) return json({ items: [fields], total: 1, page: 1, pageSize: 20 });
    expect(request.headers.get('X-NewP-Data-Declaration')).toBe(source(detail.view.key, 'read').id);
    return json({ fields });
  }));
  const catalog: Record<string, string> = Object.fromEntries(Object.entries(platformDictionary.Platform).map(([key, value]) => [`Platform.${key}`, value.zhs]));
  for (const file of ['PositionListView', 'PositionDetailView']) {
    const dictionary = readComponentJson('Organization', `metadata/dictionaries/${file}.dic.json`)[file];
    for (const [key, value] of Object.entries(dictionary)) catalog[`${file}.${key}`] = (value as { zhs: string }).zhs;
  }
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Organization', displayName: '组织', version: '1' }, ...bindings }], `position-button-${entry}`);
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const page = <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: catalog }}>
    <AuthProvider initialUser={user}><AuthorizationProvider featurePositions={positions}><PlatformQueryProvider scopeKey={`position-button-${entry}`}>
      <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Organization', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features, dataAccess: declarations }] },
        navDocuments: [], modelDocuments: [model], viewDocuments: [bindActionDataAccess(list, declarations), bindActionDataAccess(detail, declarations)] }}>
        <PageEntry page={{ pageKey: 'platform.list', parameters: { viewKey: list.view.key, featurePosition: entryPosition } }} />
      </RuntimeMetadataProvider>
    </PlatformQueryProvider></AuthorizationProvider></AuthProvider>
  </LocalizationProvider>;
  const router = createMemoryRouter([{ path: '/', element: page }], { initialEntries: ['/'] });
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); vi.unstubAllGlobals(); vi.restoreAllMocks(); ModuleRegistry.replaceAll([], 'position-button-cleanup'); });
  await act(async () => root.render(<RouterProvider router={router} />));
  const button = (text: string, scope: ParentNode = document) => [...scope.querySelectorAll<HTMLButtonElement>('button')].find(node => node.textContent?.replace(/\s/g, '') === text);
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="Position_one"]'), container.textContent ?? '').not.toBeNull());
  if (entry === 'detail') {
    await vi.waitFor(() => expect(button('岗位原名', container), container.textContent ?? '').toBeDefined());
    await act(async () => button('岗位原名', container)!.click());
    await vi.waitFor(() => expect(button('编辑', document.querySelector('.ant-drawer-content')!)).toBeDefined());
    await act(async () => button('编辑', document.querySelector('.ant-drawer-content')!)!.click());
  } else await act(async () => button('编辑', container)!.click());
  await vi.waitFor(() => expect(document.querySelector('form #ENAME')).not.toBeNull());
  const input = document.querySelector<HTMLInputElement>('form #ENAME')!;
  await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, '岗位新名'); input.dispatchEvent(new Event('input', { bubbles: true })); });
  await act(async () => document.querySelector<HTMLFormElement>('.ant-drawer form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  await vi.waitFor(() => expect(writes).toEqual([{ button: expected.id, position: expected.featurePositions[0] }]));
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer-open')).toBeNull());
});
