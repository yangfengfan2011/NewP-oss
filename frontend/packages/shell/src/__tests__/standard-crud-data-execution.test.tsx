// @vitest-environment jsdom
import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry, type ActionDataAccessRequirement } from '@icp-platform/sdk';
import exampleBindings from '../../../../modules/example/src/index';
import list from '../../../../modules/example/metadata/views/ExampleRecordList.view.json';
import detail from '../../../../modules/example/metadata/views/ExampleRecordDetail.view.json';
import model from '../../../../../tests/Kernel.Tests/Fixtures/ExampleRuntimeModel.contract.json';
import compiledRequirements from '../../../../../tests/Kernel.Tests/Fixtures/ExampleCrudDataAccess.contract.json';
import listDictionary from '../../../../modules/example/metadata/dictionaries/ExampleRecordListView.dic.json';
import detailDictionary from '../../../../modules/example/metadata/dictionaries/ExampleRecordDetailView.dic.json';
import platformDictionary from '../../../../modules/platform/metadata/dictionaries/Platform.dic.json';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { bindActionDataAccess } from '../metadata/runtimeMetadataLoader';
import { PageEntry } from '../composition/PageOutlet';
import { AuthProvider } from '../contexts/AuthContext';
import { FeaturePlacementContext } from '../authorization/FeaturePlacementContext';
import { RouteCapabilityOutlet } from '../capabilities/RouteCapabilityOutlet';
import '../pages/StandardCrudListPageHost';
import '../pages/StandardCrudDetailPageHost';
import '../../../../modules/example/src/standard-crud/exampleRecordStandardCrudResource.primary';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

// 正式装配的 fixture 只提供目录；PageEntry、metadata binder 和真实控件负责选择单次请求上下文。
const declarations = compiledRequirements as ActionDataAccessRequirement[];
const requirementFor = (viewKey: string, surface: string, actionKey = '') => declarations.find(item => item.viewKey === viewKey && item.surface === surface && item.actionKey === actionKey)!;
const idFor = (viewKey: string, surface: string, actionKey = '') => requirementFor(viewKey, surface, actionKey).id;
const entryPosition = requirementFor(list.view.key, 'read').featurePositions[0];
const editPosition = (entry: 'list' | 'detail') => requirementFor(entry === 'list' ? list.view.key : detail.view.key, entry === 'list' ? 'rowActions' : 'pageActions', 'edit').featurePositions[0];

it.each(['list', 'detail'] as const)('selects formal read and mutation declarations through the real %s entry without a precheck', async entry => {
  const fields = { EID: 'ExampleRecord_one', ECODE: 'DECL-01', ENAME: '声明记录', ReviewerUserEID: null, RecordSource: 'Manual',
    CreatedBy: 'actor', CreatedBy_DisplayValue: '账号', UpdatedBy: 'actor', UpdatedBy_DisplayValue: '账号',
    CreatedAt: '2026-10-19T00:00:00Z', CreatedAt_DisplayValue: '2026-10-19 08:00', UpdatedAt: '2026-10-19T00:00:00Z', UpdatedAt_DisplayValue: '2026-10-19 08:00' };
  const requests: Array<{ method: string; path: string; declaration: string | null; position: string | null }> = [];
  const user = { id: 'actor', ecode: 'actor', ename: '账号', revision: 1, mustChangePassword: false,
    isLocalAdministrator: false, timeZoneId: 'Asia/Taipei', featurePositions: [...new Set(declarations.flatMap(item => item.featurePositions))] };
  const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });
  let denyWrite = false;
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = input instanceof Request ? new URL(input.url).pathname + new URL(input.url).search : String(input);
    const method = input instanceof Request ? input.method : init?.method ?? 'GET';
    if (path.endsWith('antiforgery-token')) return json({ requestToken: 'token' });
    if (path.endsWith('current-user')) return json(user);
    const headers = new Headers(input instanceof Request ? input.headers : init?.headers);
    const request = { method, path, declaration: headers.get('X-NewP-Data-Declaration'), position: headers.get('X-NewP-Feature') };
    requests.push(request);
    const expected = method === 'DELETE' ? idFor(list.view.key, 'rowActions', 'delete')
      : method === 'POST' ? idFor(list.view.key, 'pageActions', 'create')
      : method === 'PUT' ? idFor(entry === 'list' ? list.view.key : detail.view.key, entry === 'list' ? 'rowActions' : 'pageActions', 'edit')
        : path.includes('?') ? idFor(list.view.key, 'read')
          : request.position === editPosition(entry)
            ? idFor(entry === 'list' ? list.view.key : detail.view.key, entry === 'list' ? 'rowActions' : 'pageActions', 'edit')
            : idFor(detail.view.key, 'read');
    if (request.declaration !== expected) return json({ message: '声明不匹配', code: 'DECLARATION_MISMATCH' }, 400);
    if (method === 'PUT' && denyWrite) return json({ message: '正式提交已拒绝', code: 'DATA_ACTION_DENIED' }, 403);
    if (method === 'DELETE') return new Response(null, { status: 204 });
    if (method === 'POST' || method === 'PUT') return json({ succeeded: true });
    if (path.includes('?')) return json({ items: [fields], total: 1, page: 1, pageSize: 20 });
    return json({ fields, summaries: {} });
  }));
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Example', displayName: '示例', version: '1' }, ...exampleBindings }], 'data-execution-test');
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  const dictionaries: Array<[string, Record<string, { zhs: string }>]> = [['ExampleRecordListView', listDictionary.ExampleRecordListView], ['ExampleRecordDetailView', detailDictionary.ExampleRecordDetailView], ['Platform', platformDictionary.Platform]];
  const catalog = Object.fromEntries(dictionaries
    .flatMap(([prefix, dictionary]) => Object.entries(dictionary).map(([key, value]) => [`${prefix}.${key}`, (value as { zhs: string }).zhs])));
  const features = [
    { key: 'records', kind: 'navigation', position: entryPosition },
    ...declarations.filter(item => item.surface !== 'read').map(item => ({ key: item.actionKey,
      actionScope: `${item.viewKey === list.view.key ? 'list' : 'detail'}.${item.surface}`,
      position: item.featurePositions[0], kind: 'action', parentPosition: entryPosition })),
  ];
  const page = <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: catalog }}>
    <AuthProvider initialUser={user}><AuthorizationProvider featurePositions={user.featurePositions}><PlatformQueryProvider scopeKey="data-execution-test">
      <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Example', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], features, dataAccess: declarations }] },
        navDocuments: [], modelDocuments: [{ ...model, type: 'model' }], viewDocuments: [bindActionDataAccess({ ...list, type: 'view' }, declarations), bindActionDataAccess({ ...detail, type: 'view' }, declarations)] }}>
        {entry === 'list'
          ? <PageEntry page={{ pageKey: 'platform.list', parameters: { viewKey: list.view.key, featurePosition: entryPosition } }} />
          : <FeaturePlacementContext.Provider value={{ entryPosition }}><RouteCapabilityOutlet capability={{ kind: 'standard-crud-detail', targetKey: detail.view.key }} /></FeaturePlacementContext.Provider>}
      </RuntimeMetadataProvider>
    </PlatformQueryProvider></AuthorizationProvider></AuthProvider>
  </LocalizationProvider>;
  const router = createMemoryRouter([{ path: '/:id', element: page }], { initialEntries: ['/' + fields.EID] });
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); vi.unstubAllGlobals(); ModuleRegistry.replaceAll([], 'data-execution-cleanup'); });
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => entry === 'list'
    ? expect(container.querySelector(`tr[data-row-key="${fields.EID}"]`)).not.toBeNull()
    : expect(container.textContent).toContain(fields.ENAME));
  expect(requests[0].declaration).toBe(idFor(entry === 'list' ? list.view.key : detail.view.key, 'read'));
  const button = (text: string) => [...document.querySelectorAll<HTMLButtonElement>('button')].find(node => node.textContent?.replace(/\s/g, '') === text);
  if (entry === 'list') {
    await act(async () => button('新建记录')!.click());
    await vi.waitFor(() => expect(document.querySelector('.ant-drawer form')).not.toBeNull());
    for (const [key, value] of [['ECODE', 'DECL-02'], ['ENAME', '新声明记录']]) {
      const input = document.querySelector<HTMLInputElement>(`.ant-drawer #${key}`)!;
      await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value); input.dispatchEvent(new Event('input', { bubbles: true })); });
    }
    await act(async () => document.querySelector<HTMLFormElement>('.ant-drawer form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
    await vi.waitFor(() => expect(requests.some(request => request.method === 'POST')).toBe(true));
    await vi.waitFor(() => expect(document.querySelector('.ant-drawer-open')).toBeNull());
    await act(async () => [...container.querySelectorAll<HTMLButtonElement>('.ant-table button')].find(node => node.textContent === '编辑')!.click());
  } else await act(async () => button('编辑')!.click());
  await vi.waitFor(() => expect(document.querySelector('form #ENAME')).not.toBeNull());
  const editForm = () => document.querySelector<HTMLFormElement>(entry === 'list' ? '.ant-drawer form' : 'form')!;
  const editDeclaration = idFor(entry === 'list' ? list.view.key : detail.view.key, entry === 'list' ? 'rowActions' : 'pageActions', 'edit');
  await vi.waitFor(() => expect(requests.some(request => request.method === 'GET' && request.declaration === editDeclaration)).toBe(true));
  denyWrite = true;
  await act(async () => editForm().dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  await vi.waitFor(() => expect(document.body.textContent).toContain('当前操作已被拒绝'));
  expect(requests.filter(request => request.method === 'PUT')).toEqual([expect.objectContaining({ declaration: editDeclaration, position: editPosition(entry) })]);
  denyWrite = false;
  await act(async () => editForm().dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })));
  await vi.waitFor(() => expect(requests.filter(request => request.method === 'PUT')).toHaveLength(2));
  await vi.waitFor(() => expect(document.querySelector('form #ENAME')).toBeNull());
  if (entry === 'list') {
    await act(async () => [...container.querySelectorAll<HTMLElement>('.ant-table *')].find(node => node.children.length === 0 && node.textContent === '更多')!.click());
    await vi.waitFor(() => expect([...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].some(node => node.textContent === '删除')).toBe(true));
    await act(async () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(node => node.textContent === '删除')!.click());
    await vi.waitFor(() => expect(button('确认')).toBeDefined());
    await act(async () => button('确认')!.click());
    await vi.waitFor(() => expect(requests.filter(request => request.method === 'DELETE')).toEqual([expect.objectContaining({ declaration: idFor(list.view.key, 'rowActions', 'delete'), position: requirementFor(list.view.key, 'rowActions', 'delete').featurePositions[0] })]));
  }
});
