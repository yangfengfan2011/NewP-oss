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
import '../../../ui/src/ObjectOwnershipInitializationInput';

vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
const { default: part } = await loadComponentSource<{ default: FrontendBehaviorBindings }>('Part', 'index.ts');
await loadComponentSource('Part', 'partResource.ts');
const list = readComponentJson('Part', 'metadata/views/PartList.view.json');
const detail = readComponentJson('Part', 'metadata/views/PartDetail.view.json');
const dictionary = readComponentJson('Part', 'metadata/dictionaries/Part.dic.json');

it('carries the actual pending initialization binding from PageEntry through selection and submission', async () => {
  const matches = (requirements as ActionDataAccessRequirement[]).filter(item => item.viewKey === list.view.key
    && item.surface === 'pageActions' && item.actionKey === 'initializeOwnershipPending');
  expect(matches).toHaveLength(1);
  const declaration = matches[0];
  expect(declaration.featurePositions).toHaveLength(1);
  const actionPosition = declaration.featurePositions[0];
  const readPositions = formalDataAccessFor(list.view.key, 'read').featurePositions
    .filter(position => position.endsWith('/navigation:part.parts'));
  expect(readPositions).toHaveLength(1);
  const readPosition = readPositions[0];
  const user = { id: 'administrator', ecode: 'administrator', ename: '管理员', revision: 1, mustChangePassword: false,
    featurePositions: [readPosition, actionPosition], isLocalAdministrator: true };
  const fields = { EID: 'Part_missing', ECODE: 'NULL-01', ENAME: '待补齐零组件' };
  const requests: Request[] = [];
  const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    requests.push(request);
    const url = new URL(request.url);
    if (url.pathname.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (url.pathname.endsWith('current-user')) return json(user);
    if (url.pathname.includes('/ownership')) {
      if (url.pathname.endsWith('/eligibility')) return json({ allowed: true });
      if (request.headers.get('X-NewP-Data-Declaration') !== declaration.id)
        return json({ code: 'ActionDataAccess.DeclarationInvalid' }, 400);
      if (url.pathname.endsWith('/pending')) return json({ items: [{ id: fields.EID, name: fields.ENAME }], total: 1 });
      if (request.method === 'POST') return json({ changed: 1, unresolved: 0 });
    }
    if (url.pathname === '/api/part/parts') return json({ items: [fields], total: 1, page: 1, pageSize: 20 });
    throw new Error(`Unexpected ownership initialization request ${request.method} ${url.pathname}`);
  }));
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Part', displayName: '零组件', version: '1' }, ...part }], 'ownership-initialization-page');
  const catalog = Object.fromEntries([
    ...Object.entries(platformDictionary.Platform).map(([key, value]) => [`Platform.${key}`, value.zhs]),
    ...Object.entries(dictionary.Part).map(([key, value]) => [`Part.${key}`, (value as { zhs: string }).zhs]),
  ]);
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  const page = <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: catalog }}>
    <AuthProvider initialUser={user}><AuthorizationProvider featurePositions={user.featurePositions}><PlatformQueryProvider scopeKey="ownership-initialization-page">
      <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Part', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: [
        { key: 'part.parts', position: readPosition, kind: 'navigation' },
        { key: 'initializeOwnershipPending', position: actionPosition, parentPosition: readPosition, kind: 'action', actionScope: 'list.pageActions' },
      ] }] }, navDocuments: [], modelDocuments: [model], viewDocuments: [list, detail] }}>
        <PageEntry page={{ pageKey: 'platform.list', parameters: { viewKey: list.view.key, featurePosition: readPosition } }} />
      </RuntimeMetadataProvider>
    </PlatformQueryProvider></AuthorizationProvider></AuthProvider>
  </LocalizationProvider>;
  const router = createMemoryRouter([{ path: '/parts', element: page }], { initialEntries: ['/parts'] });
  onTestFinished(async () => {
    await act(async () => root.unmount()); router.dispose(); container.remove(); vi.unstubAllGlobals();
    ModuleRegistry.replaceAll([], 'ownership-initialization-page-cleanup'); invalidatePlatformAntiforgeryToken();
  });
  const button = (label: string) => [...document.querySelectorAll<HTMLButtonElement>('button')]
    .find(item => item.textContent?.replace(/\s/g, '') === label);
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(button('待补齐归属')).toBeDefined());
  await act(async () => button('待补齐归属')!.click());
  await vi.waitFor(() => expect(requests.some(request => new URL(request.url).pathname.endsWith('/ownership/pending'))).toBe(true));
  expect(requests.find(request => new URL(request.url).pathname.endsWith('/ownership/pending'))!.headers.get('X-NewP-Data-Declaration')).toBe(declaration.id);
  await vi.waitFor(() => expect(button('选择当前许可页')?.disabled).toBe(false));
  await act(async () => button('选择当前许可页')!.click());
  await vi.waitFor(() => expect(button('确认')?.disabled).toBe(false));
  await act(async () => button('确认')!.click());
  await vi.waitFor(() => expect(document.querySelector('[role="dialog"]')).toBeNull());
  const writes = requests.filter(request => request.method === 'POST' && new URL(request.url).pathname.endsWith('/ownership/initialize'));
  expect(writes, requests.map(request => `${request.method} ${new URL(request.url).pathname}`).join('\n')).toHaveLength(1);
  expect(await writes[0].clone().json()).toEqual({ ids: [fields.EID] });
  expect(writes[0].headers.get('X-NewP-Data-Declaration')).toBe(declaration.id);
  expect(writes[0].headers.get('X-NewP-Feature')).toBe(actionPosition);
});
