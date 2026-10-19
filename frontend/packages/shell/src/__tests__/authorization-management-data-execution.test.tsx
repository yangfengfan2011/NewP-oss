// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { beforeAll, expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry, type ActionDataAccessRequirement } from '@icp-platform/sdk';
import bindings from '../../../../modules/platform/authorization/src';
import navigation from '../../../../modules/platform/authorization/metadata/navigation/Authorization.nav.json';
import moduleRuntime from '../../../../../tests/Kernel.Tests/Fixtures/AuthorizationManagementRuntime.contract.json';
import { bindNavigationDataAccess } from '../metadata/runtimeMetadataLoader';
import type { RuntimeNavItemDocument } from '../metadata/runtimeMetadataManifest';
import { PageEntry } from '../composition/PageOutlet';
import list from '../../../../modules/platform/authorization/metadata/views/RoleList.view.json';
import detail from '../../../../modules/platform/authorization/metadata/views/RoleDetail.view.json';
import dictionary from '../../../../modules/platform/authorization/metadata/dictionaries/RoleView.dic.json';
import roleModel from '../../../../../tests/Kernel.Tests/Fixtures/AuthorizationRoleRuntime.contract.json';
import { RuntimeMetadataProvider } from './formalRuntimeMetadataProvider';

vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
beforeAll(async () => { await Promise.all([import('../../../../modules/platform/authorization/src/FeatureWorkbench'), import('@ant-design/pro-table'), import('@ant-design/pro-descriptions'), import('../pages/StandardCrudListDrawerRuntime')]); });
const declarations = moduleRuntime.dataAccess as ActionDataAccessRequirement[];
const sources = declarations.filter(item => item.execution === 'authorization.workbench.read');
expect(sources).toHaveLength(1);
const source = sources[0];
const points = moduleRuntime.features;
const readPoints = points.filter(point => point.behaviorKey === 'authorization.workbench.read');
const savePoints = points.filter(point => point.behaviorKey === 'authorization.workbench.save');
expect(readPoints).toHaveLength(1); expect(savePoints).toHaveLength(1);
const readPosition = readPoints[0].position;
const savePosition = savePoints[0].position;
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });

it('binds the actual custom page reference and rejects missing or duplicate compiled sources', () => {
  const document = navigation as RuntimeNavItemDocument;
  const bound = bindNavigationDataAccess(document, declarations);
  const entry = bound.navigation.items.filter(item => item.page?.pageKey === 'authorization.workbench');
  expect(entry).toHaveLength(1); expect(entry[0].page?.dataAccess).toEqual(source);
  expect(() => bindNavigationDataAccess(document, [])).toThrow('页面数据声明');
  expect(() => bindNavigationDataAccess(document, [...declarations, source])).toThrow('页面数据声明');
});

it('uses the real Workbench PageEntry read source for catalog and selected role without adding it to feature-only save', async () => {
  const requests: Request[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    const path = new URL(request.url).pathname;
    if (path.endsWith('/antiforgery-token')) return json({ requestToken: 'token' });
    requests.push(request);
    if (request.method === 'PUT') {
      expect(request.headers.get('X-NewP-Feature')).toBe(savePosition);
      expect(request.headers.get('X-NewP-Data-Declaration')).toBeNull();
      return new Response(null, { status: 204 });
    }
    expect(request.headers.get('X-NewP-Feature')).toBe(readPosition);
    if (path.endsWith('/catalog')) return json({ points });
    if (request.headers.get('X-NewP-Data-Declaration') !== source.id) return json({ title: '读取来源缺失' }, 400);
    if (path.endsWith('/roles')) return json({ items: [{ EID: 'role-one', ECODE: 'R311', ENAME: '工作台目标', IsEnabled: true }], total: 1 });
    if (path.endsWith('/roles/role-one')) return json({ role: { id: 'role-one', code: 'R311', displayName: '工作台目标', isEnabled: true }, positions: [] });
    throw new Error(`Unexpected request: ${request.method} ${path}`);
  }));
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Authorization', displayName: '授权', version: '1' }, ...bindings }], 'authorization-management-execution');
  const boundNavigation = bindNavigationDataAccess(navigation as RuntimeNavItemDocument, declarations);
  const entries = boundNavigation.navigation.items.filter(item => item.page?.pageKey === 'authorization.workbench');
  expect(entries).toHaveLength(1);
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const router = createMemoryRouter([{ path: '/', element: <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: {} }}>
    <AuthorizationProvider featurePositions={points.map(point => point.position)}>
      <PageEntry page={{ ...entries[0].page!, parameters: { featurePosition: readPosition } }} />
    </AuthorizationProvider></LocalizationProvider> }]);
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); ModuleRegistry.replaceAll([], 'authorization-management-cleanup'); vi.unstubAllGlobals(); });
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(container.textContent).toContain('工作台目标'));
  const role = [...container.querySelectorAll('button')].find(button => button.textContent?.includes('R311'));
  expect(role).toBeDefined(); await act(async () => role!.click());
  await vi.waitFor(() => expect(container.querySelector('.ant-tree-checkbox')).not.toBeNull());
  await act(async () => (container.querySelector('.ant-tree-checkbox') as HTMLElement).click());
  const save = [...container.querySelectorAll('button')].find(button => button.textContent === '保存授权');
  expect(save).toBeDefined(); await act(async () => save!.click());
  await vi.waitFor(() => expect(requests.filter(request => request.method === 'PUT')).toHaveLength(1));
  expect(requests.filter(request => new URL(request.url).pathname.endsWith('/roles/role-one') && request.method === 'GET')).toHaveLength(1);
});

it('opens the actual list account-role input and submits through its existing controls', async () => {
  const entry = points.filter(point => point.behaviorKey === 'authorization.role.read'); expect(entry).toHaveLength(1);
  const member = declarations.filter(item => item.viewKey === list.view.key && item.surface === 'rowActions' && item.actionKey === 'userRoles'); expect(member).toHaveLength(1);
  const writes: Request[] = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    const path = new URL(request.url).pathname;
    if (path.endsWith('/antiforgery-token')) return json({ requestToken: 'token' });
    if (request.method === 'PUT') { writes.push(request); expect(request.headers.get('X-NewP-Data-Declaration')).toBeNull(); return new Response(null, { status: 204 }); }
    if (path.endsWith('/users/candidates')) return json([{ id: 'actor311', name: '普通账号', code: 'actor311', status: 'Enabled' }]);
    if (path.endsWith('/users/actor311/roles')) return json({ roleCodes: [] });
    if (path.endsWith('/roles/query')) {
      const isOptions = request.headers.get('X-NewP-Feature') === member[0].featurePositions[0];
      const expected = isOptions ? member[0] : declarations.find(item => item.viewKey === list.view.key && item.surface === 'read')!;
      expect(request.headers.get('X-NewP-Data-Declaration')).toBe(expected.id);
      return json({ items: [{ EID: 'role311', ECODE: 'AUTH311', ENAME: '实际角色', IsEnabled: true }], total: 1 });
    }
    throw new Error(`Unexpected input request ${path}`);
  }));
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Authorization', displayName: '授权', version: '1' }, ...bindings }], 'authorization-role-options');
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const catalogs = Object.fromEntries(Object.entries(dictionary.RoleView).map(([key, value]) => [`RoleView.${key}`, value.zhs]));
  const router = createMemoryRouter([{ path: '/', element: <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: catalogs }}>
    <AuthorizationProvider featurePositions={points.map(point => point.position)}>
      <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Authorization', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: points }] }, navDocuments: [navigation], viewDocuments: [list, detail], modelDocuments: [roleModel] }}>
        <PlatformQueryProvider scopeKey="role-native-controls"><PageEntry page={{ pageKey: 'platform.list', parameters: { viewKey: list.view.key, featurePosition: entry[0].position } }} /></PlatformQueryProvider>
      </RuntimeMetadataProvider>
    </AuthorizationProvider></LocalizationProvider> }]);
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); ModuleRegistry.replaceAll([], 'role-options-cleanup'); vi.unstubAllGlobals(); });
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(container.textContent).toContain('实际角色'));
  const more = [...container.querySelectorAll('button')].find(button => button.textContent === '更多');
  expect(more).toBeDefined(); await act(async () => more!.click());
  const action = [...document.querySelectorAll('[role="menuitem"]')].find(item => item.textContent === '账号角色');
  expect(action).toBeDefined(); await act(async () => (action as HTMLElement).click());
  await vi.waitFor(() => expect(document.querySelector('input[aria-label="账号"]')).not.toBeNull());
  await act(async () => document.querySelector('input[aria-label="账号"]')!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })));
  await vi.waitFor(() => expect(document.body.textContent).toContain('普通账号 (actor311)'));
  await act(async () => (document.querySelector('.ant-select-item-option') as HTMLElement).click());
  await vi.waitFor(() => expect(document.querySelector('.ant-modal input[type="checkbox"]')).not.toBeNull());
  const checkbox = document.querySelector('.ant-modal input[type="checkbox"]') as HTMLInputElement;
  expect(checkbox.closest('label')?.textContent).toBe('实际角色 (AUTH311)');
  await act(async () => checkbox.click());
  const confirm = [...document.querySelectorAll('.ant-modal button')].find(button => button.textContent?.replace(/\s/g, '') === '确认');
  expect(confirm).toBeDefined(); await act(async () => (confirm as HTMLElement).click());
  await vi.waitFor(() => expect(writes).toHaveLength(1));
});
