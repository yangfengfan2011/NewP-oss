// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { beforeAll, expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import documentBindings from '../../../../modules/document/src';
import model from '../../../../../tests/Kernel.Tests/Fixtures/DocumentRuntime.contract.json';
import list from '../../../../../modules/Document/metadata/views/DocumentList.view.json';
import detail from '../../../../../modules/Document/metadata/views/DocumentDetail.view.json';
import dictionary from '../../../../../modules/Document/metadata/dictionaries/Document.dic.json';
import navigation from '../../../../../modules/Document/metadata/navigation/Document.nav.json';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { PageEntry } from '../composition/PageOutlet';
import { AuthProvider } from '../contexts/AuthContext';
import { compileStandardCrudRouteProjection } from '../navigation/standardCrudRouteAdapter';

vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
beforeAll(async () => {
  await Promise.all([import('../pages/StandardCrudListPageHost'), import('../pages/StandardCrudDetailPageHost'), import('../../../../modules/document/src/documentResource'), import('@ant-design/pro-descriptions')]);
});

it.each(['success', 'receipt'] as const)('uses the formal Document routes and standard create/read-only properties after %s', async scenario => {
  const writes: unknown[] = [];
  const user = { id: 'actor', ecode: 'actor', ename: '账号', revision: 1, mustChangePassword: false,
    isLocalAdministrator: false, timeZoneId: 'Asia/Taipei', featurePositions: ['document-entry', 'document-create'] };
  const fields = { EID: 'document-one', ECODE: 'D-001', ENAME: '首个图文档', Description: '说明', CreatedBy: 'actor', CreatedBy_DisplayValue: '账号',
    UpdatedBy: 'actor', UpdatedBy_DisplayValue: '账号', CreatedAt: '2026-10-08T00:00:00Z', CreatedAt_DisplayValue: '2026-10-08 08:00', UpdatedAt: '2026-10-08T00:00:00Z', UpdatedAt_DisplayValue: '2026-10-08 08:00' };
  const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = input instanceof Request ? new URL(input.url).pathname + new URL(input.url).search : String(input);
    const method = input instanceof Request ? input.method : init?.method;
    if (path.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (path.endsWith('current-user')) return json(user);
    if (path.startsWith('/api/document/documents') && method === 'POST') {
      writes.push(input instanceof Request ? await input.clone().json() : JSON.parse(String(init?.body)));
      return json(scenario === 'success' ? { fields, summaries: {} } : { succeeded: true });
    }
    if (path === '/api/document/documents/document-one') return json({ fields, summaries: {} });
    if (path.startsWith('/api/document/documents?')) return json({ items: writes.length && scenario === 'success' ? [fields] : [], total: writes.length && scenario === 'success' ? 1 : 0, page: 1, pageSize: 20 });
    throw new Error(`Unexpected request ${method} ${path}`);
  }));
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Document', displayName: '图文档', version: '1' }, ...documentBindings }], 'document-test');
  const nav = { ...navigation, navigation: { items: navigation.navigation.items.map(item => ({ ...item, featurePosition: 'document-entry' })) } };
  const projection = compileStandardCrudRouteProjection(ModuleRegistry.getAllModules(), [nav], [list, detail], []);
  expect(projection.detailEntries).toEqual([{ path: '/documents/:id', viewKey: detail.view.key, featurePosition: 'document-entry' }]);
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  const catalogs = { zhs: Object.fromEntries(Object.entries(dictionary.Document).map(([key, value]) => [`Document.${key}`, value.zhs])) };
  const wrap = (viewKey: string, pageKey: string) => <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={catalogs}>
    <AuthProvider initialUser={user}><AuthorizationProvider featurePositions={user.featurePositions}><PlatformQueryProvider scopeKey="document-test">
      <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Document', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features: [
        { key: 'document.documents', position: 'document-entry', kind: 'navigation' },
        { key: 'create', position: 'document-create', kind: 'action', parentPosition: 'document-entry', actionScope: 'list.pageActions' },
      ] }] }, navDocuments: [nav], modelDocuments: [model], viewDocuments: [list, detail] }}>
        <PageEntry page={{ pageKey, parameters: { viewKey, featurePosition: 'document-entry' } }} />
      </RuntimeMetadataProvider>
    </PlatformQueryProvider></AuthorizationProvider></AuthProvider>
  </LocalizationProvider>;
  const router = createMemoryRouter([{ path: '/documents', element: wrap(list.view.key, 'platform.list') },
    ...projection.detailEntries.map(entry => ({ path: entry.path, element: wrap(entry.viewKey, 'platform.detail') }))], { initialEntries: ['/documents'] });
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); vi.unstubAllGlobals(); ModuleRegistry.replaceAll([], 'document-test-cleanup'); });
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(container.textContent).toContain('当前数据集为空'));
  const button = (text: string) => [...document.querySelectorAll<HTMLButtonElement>('button')].find(node => node.textContent?.replace(/\s/g, '') === text);
  await act(async () => button('新建图文档')!.click());
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer form')).not.toBeNull());
  for (const [label, value] of [['编码', 'D-001'], ['名称', '首个图文档'], ['说明', '说明']]) {
    const field = [...document.querySelectorAll<HTMLLabelElement>('.ant-drawer label')].find(node => node.textContent?.includes(label))!;
    const input = document.getElementById(field.htmlFor) as HTMLInputElement | HTMLTextAreaElement;
    await act(async () => {
      Object.getOwnPropertyDescriptor(input instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype, 'value')!.set!.call(input, value);
      input.dispatchEvent(new Event('input', { bubbles: true }));
    });
  }
  await act(async () => document.querySelector<HTMLFormElement>('.ant-drawer form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  await vi.waitFor(() => expect(writes, document.body.textContent ?? "").toEqual([{ ECODE: 'D-001', ENAME: '首个图文档', Description: '说明' }]));
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer-open')).toBeNull());
  if (scenario === 'success') {
    await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="document-one"]')).not.toBeNull());
    await act(async () => [...container.querySelectorAll<HTMLElement>('.ant-table *')].find(node => node.children.length === 0 && node.textContent === '更多')!.click());
    await vi.waitFor(() => expect(document.body.textContent).toContain('打开属性'));
    await act(async () => [...document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(node => node.textContent === '打开属性')!.click());
    await vi.waitFor(() => expect(document.querySelector('.ant-drawer-open')?.textContent).toContain('审计信息'));
    expect(document.querySelector('.ant-drawer-open form')).toBeNull();
    expect(document.querySelector('.ant-drawer-open input')).toBeNull();
  } else expect(container.querySelector('.ant-table')?.textContent).not.toContain('首个图文档');
  expect(button('编辑')).toBeUndefined(); expect(button('删除')).toBeUndefined();
  await act(async () => button('刷新')!.click());
  expect(writes).toHaveLength(1);
});
