// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider, invalidatePlatformAntiforgeryToken } from '@icp-platform/core';
import { ModuleRegistry, type ActionDataAccessRequirement, type FrontendBehaviorBindings } from '@icp-platform/sdk';
import { loadComponentSource, readComponentJson } from '../../test-support/componentTestInputs';
import { RuntimeMetadataProvider } from './formalRuntimeMetadataProvider';
import { PageEntry } from '../composition/PageOutlet';
import { AuthProvider } from '../contexts/AuthContext';
import { formalDataAccessFor } from './formalRuntimeMetadataTestSupport';
import requirements from '../../../../../tests/Kernel.Tests/Fixtures/OwnershipDataAccess.contract.json';
import model from '../../../../../tests/Kernel.Tests/Fixtures/PartRuntime.contract.json';
import platformDictionary from '../../../../modules/platform/metadata/dictionaries/Platform.dic.json';
import '../pages/StandardCrudListPageHost';
import '../../../ui/src/ObjectOwnershipTransferInput';

vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
const { default: part } = await loadComponentSource<{ default: FrontendBehaviorBindings }>('Part', 'index.ts');
await loadComponentSource('Part', 'partResource.ts');
const list = readComponentJson('Part', 'metadata/views/PartList.view.json');
const detail = readComponentJson('Part', 'metadata/views/PartDetail.view.json');
const dictionary = readComponentJson('Part', 'metadata/dictionaries/Part.dic.json');

it('carries the actual Part transfer declaration from PageEntry through eligibility, recipient input and submission', async () => {
  const matches = (requirements as ActionDataAccessRequirement[]).filter(item => item.viewKey === list.view.key
    && item.surface === 'rowActions' && item.actionKey === 'transfer');
  expect(matches).toHaveLength(1);
  const declaration = matches[0];
  const readPosition = formalDataAccessFor(list.view.key, 'read').featurePositions[0];
  expect(declaration.featurePositions).toHaveLength(1);
  const transferPosition = declaration.featurePositions[0];
  const user = { id: 'owner', ecode: 'owner', ename: '所属人', revision: 1, mustChangePassword: false,
    featurePositions: [readPosition, transferPosition], isLocalAdministrator: false };
  const fields = { EID: 'Part_owner', ECODE: 'OWNER-01', ENAME: '待转交零组件' };
  const requests: Request[] = [];
  const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    requests.push(request);
    const url = new URL(request.url);
    if (url.pathname.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (url.pathname.endsWith('current-user')) return json(user);
    if (url.pathname.includes('/transfer')) {
      if (request.headers.get('X-NewP-Data-Declaration') !== declaration.id)
        return json({ code: 'ActionDataAccess.DeclarationInvalid' }, 400);
      if (url.pathname.endsWith('/eligibility')) return json({ allowed: true });
      if (url.pathname.endsWith('/accounts')) return json([{ accountId: 'recipient', code: 'receiver', name: '接收人', departmentId: null }]);
      if (request.method === 'POST') return json({ succeeded: true, changed: 1 });
    }
    if (url.pathname === '/api/part/parts') return json({ items: [fields], total: 1, page: 1, pageSize: 20 });
    throw new Error(`Unexpected ownership page request ${request.method} ${url.pathname}`);
  }));
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Part', displayName: '零组件', version: '1' }, ...part }], 'ownership-source-page');
  const catalog = Object.fromEntries([
    ...Object.entries(platformDictionary.Platform).map(([key, value]) => [`Platform.${key}`, value.zhs]),
    ...Object.entries(dictionary.Part).map(([key, value]) => [`Part.${key}`, (value as { zhs: string }).zhs]),
  ]);
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  const page = <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: catalog }}>
    <AuthProvider initialUser={user}><AuthorizationProvider featurePositions={user.featurePositions}><PlatformQueryProvider scopeKey="ownership-source-page">
      <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Part', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: [
        { key: 'part.parts', position: readPosition, kind: 'navigation' },
        { key: 'transfer', position: transferPosition, parentPosition: readPosition, kind: 'action', actionScope: 'list.rowActions' },
      ] }] }, navDocuments: [], modelDocuments: [model], viewDocuments: [list, detail] }}>
        <PageEntry page={{ pageKey: 'platform.list', parameters: { viewKey: list.view.key, featurePosition: readPosition } }} />
      </RuntimeMetadataProvider>
    </PlatformQueryProvider></AuthorizationProvider></AuthProvider>
  </LocalizationProvider>;
  const router = createMemoryRouter([{ path: '/parts', element: page }], { initialEntries: ['/parts'] });
  onTestFinished(async () => {
    await act(async () => root.unmount()); router.dispose(); container.remove(); vi.unstubAllGlobals();
    ModuleRegistry.replaceAll([], 'ownership-source-page-cleanup'); invalidatePlatformAntiforgeryToken();
  });
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="Part_owner"]')).not.toBeNull());
  // 列表只在用户展开真实行菜单时查询资格，不能由测试预先调用resolver。
  await act(async () => [...container.querySelectorAll<HTMLElement>('.ant-table *')]
    .find(item => item.children.length === 0 && item.textContent === '更多')!.click());
  await vi.waitFor(() => expect(requests.some(request => new URL(request.url).pathname.endsWith('/transfer/eligibility'))).toBe(true));
  // 首个资格请求直接校验正式来源，不用菜单等待掩盖丢失的声明。
  expect(requests.find(request => new URL(request.url).pathname.endsWith('/transfer/eligibility'))!.headers.get('X-NewP-Data-Declaration')).toBe(declaration.id);
  const button = (label: string) => [...document.querySelectorAll<HTMLButtonElement>('button')]
    .find(item => item.textContent?.replace(/\s/g, '') === label);
  const transferItem = () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(item => item.textContent === '转交');
  await vi.waitFor(() => expect(transferItem()).toBeDefined());
  await act(async () => transferItem()!.click());
  const recipientInput = () => document.querySelector<HTMLInputElement>('.ant-modal [role="combobox"]');
  await vi.waitFor(() => expect(recipientInput()).not.toBeNull());
  await vi.waitFor(() => expect(recipientInput()!.disabled).toBe(false));
  await act(async () => recipientInput()!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
  await vi.waitFor(() => expect(document.querySelector('[title="接收人 (receiver)"]')).not.toBeNull());
  await act(async () => document.querySelector<HTMLElement>('[title="接收人 (receiver)"]')!.click());
  await vi.waitFor(() => expect(button('确认')?.disabled).toBe(false));
  await act(async () => button('确认')!.click());
  await vi.waitFor(() => expect(document.querySelector('[role="dialog"]')).toBeNull());
  const transferred = requests.filter(request => request.method === 'POST' && new URL(request.url).pathname.endsWith('/transfer'));
  expect(transferred).toHaveLength(1);
  expect(await transferred[0].clone().json()).toEqual({ recipientId: 'recipient' });
  const transferRequests = requests.filter(request => new URL(request.url).pathname.includes('/transfer'));
  expect(transferRequests.some(request => new URL(request.url).pathname.endsWith('/accounts'))).toBe(true);
  expect(transferRequests.every(request => request.headers.get('X-NewP-Data-Declaration') === declaration.id
    && request.headers.get('X-NewP-Feature') === transferPosition)).toBe(true);
});
