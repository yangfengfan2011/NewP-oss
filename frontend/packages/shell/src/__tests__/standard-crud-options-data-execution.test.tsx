// @vitest-environment jsdom
import { act, type ReactNode } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { beforeAll, expect, it, onTestFinished, vi } from 'vitest';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry, type ActionDataAccessRequirement, type StandardCrudFieldDefinition } from '@icp-platform/sdk';
import organizationBindings from '../../../../modules/platform/organization/src/index';
import list from '../../../../modules/platform/organization/metadata/views/PersonList.view.json';
import detail from '../../../../modules/platform/organization/metadata/views/PersonDetail.view.json';
import companyList from '../../../../modules/platform/organization/metadata/views/CompanyList.view.json';
import companyDetail from '../../../../modules/platform/organization/metadata/views/CompanyDetail.view.json';
import runtimeModels from '../../../../../tests/Kernel.Tests/Fixtures/OrganizationRuntimeModels.contract.json';
import compiledRequirements from '../../../../../tests/Kernel.Tests/Fixtures/OrganizationStandardReadDataAccess.contract.json';
import listDictionary from '../../../../modules/platform/organization/metadata/dictionaries/PersonListView.dic.json';
import detailDictionary from '../../../../modules/platform/organization/metadata/dictionaries/PersonDetailView.dic.json';
import companyListDictionary from '../../../../modules/platform/organization/metadata/dictionaries/CompanyListView.dic.json';
import companyDetailDictionary from '../../../../modules/platform/organization/metadata/dictionaries/CompanyDetailView.dic.json';
import platformDictionary from '../../../../modules/platform/metadata/dictionaries/Platform.dic.json';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { bindActionDataAccess } from '../metadata/runtimeMetadataLoader';
import { PageEntry } from '../composition/PageOutlet';
import { AuthProvider } from '../contexts/AuthContext';
import { FeaturePlacementContext } from '../authorization/FeaturePlacementContext';
import { RouteCapabilityOutlet } from '../capabilities/RouteCapabilityOutlet';
import { createStandardCrudLookupQueryKey } from '../standard-crud/standardCrudQueryKeys';
import '../pages/StandardCrudListPageHost';

vi.unmock('@icp-platform/core');
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
beforeAll(async () => {
  await import('../pages/StandardCrudListDrawerRuntime');
  await import('../../../../modules/platform/organization/src/standard-crud/personStandardCrudResource.primary');
});

const declarations = compiledRequirements as ActionDataAccessRequirement[];
const matches = declarations.filter(item => item.viewKey === list.view.key && item.surface === 'lookupBindings' && item.actionKey === 'organization.positions');
expect(matches).toHaveLength(1);
const declaration = matches[0];
const positionFor = (suffix: string) => {
  const positions = declaration.featurePositions.filter(position => position.endsWith(suffix));
  expect(positions).toHaveLength(1);
  return positions[0];
};
const entryPosition = positionFor('/tab:persons');
const createPosition = positionFor('/surface:list.pageActions/action:create');
const editPosition = positionFor('/surface:list.rowActions/action:edit');
const detailEditPosition = entryPosition + '/surface:detail.pageActions/action:edit';
const detailMatches = declarations.filter(item => item.viewKey === detail.view.key && item.surface === 'lookupBindings' && item.actionKey === 'organization.positions');
expect(detailMatches).toHaveLength(1);
const detailDeclaration = detailMatches[0];
expect(detailDeclaration.featurePositions).toContain(detailEditPosition);
const readDeclaration = (viewKey: string) => {
  const reads = declarations.filter(item => item.viewKey === viewKey && item.surface === 'read');
  expect(reads).toHaveLength(1);
  return reads[0];
};
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } });

it('isolates lookup identity by the bound provider declaration without unrelated provider churn', () => {
  const field: StandardCrudFieldDefinition = { key: 'PositionId', modelField: 'PositionId', labelKey: '职位', kind: 'lookup', lookup: { providerKey: 'organization.positions' } };
  const key = (dataAccessByProvider: Record<string, ActionDataAccessRequirement>) => createStandardCrudLookupQueryKey({
    resourceKey: 'organization.person', featurePosition: entryPosition, fields: [field], providers: [], values: {}, dataAccessByProvider,
  });
  const original = key({ 'organization.positions': declaration });
  expect(key({ 'organization.positions': { ...declaration, id: declaration.id + '-other' } })).not.toEqual(original);
  expect(key({ 'organization.positions': declaration, unrelated: { ...declaration, id: 'unrelated' } })).toEqual(original);
});

it('binds the exact formal lookup provider and rejects missing or ambiguous projections', () => {
  const bound = bindActionDataAccess({ ...list, type: 'view' }, declarations);
  expect(bound.view.lookupBindings).toMatchObject({
    'organization.companies': list.view.lookupBindings['organization.companies'],
    'organization.departments': list.view.lookupBindings['organization.departments'],
    'organization.positions': { behavior: list.view.lookupBindings['organization.positions'].behavior, dataAccess: declaration },
  });
  expect(() => bindActionDataAccess({ ...list, type: 'view' }, [])).toThrow('数据声明');
  expect(() => bindActionDataAccess({ ...list, type: 'view' }, [...declarations, declaration])).toThrow('数据声明');
});

it('opens the real Company PageEntry detail drawer with its own read declaration at the original list position', async () => {
  const media = window.matchMedia;
  const desktopMedia = vi.spyOn(window, 'matchMedia').mockImplementation(query => ({ ...media(query), matches: query.includes('min-width') }));
  const listRead = readDeclaration(companyList.view.key);
  const detailRead = readDeclaration(companyDetail.view.key);
  expect(listRead.featurePositions).toHaveLength(1);
  const [position] = listRead.featurePositions;
  expect(detailRead.featurePositions).toContain(position);
  const user = { id: 'company-reader', ecode: 'reader', ename: '读取账号', revision: 1, mustChangePassword: false,
    isLocalAdministrator: false, timeZoneId: 'UTC', featurePositions: [position] };
  const fields = { EID: 'company-one', ECODE: 'CO-1', ENAME: '独立公司详情', IsEnabled: true, SortOrder: '001',
    CreatedBy: 'reader', UpdatedBy: 'reader', CreatedAt: '2026-10-19T00:00:00Z', UpdatedAt: '2026-10-19T00:00:00Z' };
  const requests: Array<{ path: string; position: string | null; declaration: string | null }> = [];
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    const path = new URL(request.url).pathname;
    if (path.endsWith('/current-user')) return json(user);
    const expected = path === '/api/organization/companies' ? listRead
      : path === '/api/organization/companies/company-one' ? detailRead : undefined;
    if (!expected) throw new Error(`Unexpected company read ${request.method} ${path}`);
    requests.push({ path, position: request.headers.get('X-NewP-Feature'), declaration: request.headers.get('X-NewP-Data-Declaration') });
    if (request.headers.get('X-NewP-Feature') !== position || request.headers.get('X-NewP-Data-Declaration') !== expected.id)
      return json({ title: '读取声明与来源不匹配', code: 'DECLARATION_MISMATCH' }, 400);
    return expected === listRead ? json({ items: [fields], total: 1, page: 1, pageSize: 20 }) : json({ fields, summaries: {} });
  }));
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Organization', displayName: '组织', version: '1' }, ...organizationBindings }], 'company-read-execution');
  const catalog = Object.fromEntries([
    ...Object.entries(companyListDictionary.CompanyListView).map(([key, value]) => [`CompanyListView.${key}`, value.zhs]),
    ...Object.entries(companyDetailDictionary.CompanyDetailView).map(([key, value]) => [`CompanyDetailView.${key}`, value.zhs]),
    ...Object.entries(platformDictionary.Platform).map(([key, value]) => [`Platform.${key}`, value.zhs]),
  ]);
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const page = <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: catalog }}>
    <AuthProvider initialUser={user}><AuthorizationProvider featurePositions={user.featurePositions}><PlatformQueryProvider scopeKey="company-read-execution">
      <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Organization', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], dataAccess: declarations,
        features: [{ key: 'companies', kind: 'navigation', position }] }] }, navDocuments: [], modelDocuments: [runtimeModels.company],
        viewDocuments: [bindActionDataAccess({ ...companyList, type: 'view' }, declarations), bindActionDataAccess({ ...companyDetail, type: 'view' }, declarations)] }}>
        <PageEntry page={{ pageKey: 'platform.list', parameters: { viewKey: companyList.view.key, featurePosition: position } }} />
      </RuntimeMetadataProvider>
    </PlatformQueryProvider></AuthorizationProvider></AuthProvider>
  </LocalizationProvider>;
  const router = createMemoryRouter([{ path: '/', element: page }]);
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); ModuleRegistry.replaceAll([], 'company-read-cleanup'); desktopMedia.mockRestore(); vi.unstubAllGlobals(); });
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="company-one"]')).not.toBeNull());
  const open = [...container.querySelectorAll<HTMLButtonElement>('tr[data-row-key="company-one"] button')].find(button => button.textContent === fields.ENAME);
  expect(open, container.querySelector('tr[data-row-key="company-one"]')!.outerHTML).toBeDefined(); await act(async () => open!.click());
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer .np-crud-detail-sections')?.textContent).toContain(fields.ENAME));
  expect(requests).toEqual([
    { path: '/api/organization/companies', position, declaration: listRead.id },
    { path: '/api/organization/companies/company-one', position, declaration: detailRead.id },
  ]);
  expect(detailRead.id).not.toBe(listRead.id);
});

it('binds Position options through the real Person PageEntry and retains each query and editor position, current value and cancellation', async () => {
  const user = { id: 'reader', ecode: 'reader', ename: '读取账号', revision: 1, mustChangePassword: false,
    isLocalAdministrator: false, timeZoneId: 'UTC', featurePositions: [...new Set([...declaration.featurePositions, ...detailDeclaration.featurePositions])] };
  const fields = { EID: 'person-one', PersonNo: 'P-1', ENAME: '人员', IsEnabled: true, PositionId: 'position-old',
    CompanyId: null, DepartmentId: null, EmploymentStatus: 'Employed', CreatedBy: 'reader', UpdatedBy: 'reader',
    CreatedAt: '2026-10-19T00:00:00Z', UpdatedAt: '2026-10-19T00:00:00Z' };
  const requests: Request[] = [];
  const reads: Request[] = [];
  let pending = false;
  let finish!: (response: Response) => void;
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), window.location.origin), init);
    const url = new URL(request.url);
    if (url.pathname.endsWith('/current-user')) return json(user);
    if (url.pathname === '/api/organization/positions/options') {
      requests.push(request);
      const expected = request.headers.get('X-NewP-Feature') === detailEditPosition ? detailDeclaration : declaration;
      if (request.headers.get('X-NewP-Data-Declaration') !== expected.id) return json({ title: '声明缺失', code: 'DECLARATION_REQUIRED' }, 400);
      if (pending) return new Promise<Response>(resolve => { finish = resolve; });
      return json({ items: [{ value: 'position-new', label: '候选职位' }],
        currentItems: url.searchParams.get('currentValueIds') === 'position-old' ? [{ value: 'position-old', label: '当前职位' }] : [],
        page: 1, pageSize: 20, hasMore: false });
    }
    if (url.pathname.includes('/person-lookups/')) return json({ items: [], currentItems: [], page: 1, pageSize: 20, hasMore: false });
    if (url.pathname === '/api/organization/persons/person-one') {
      reads.push(request);
      if (request.headers.get('X-NewP-Data-Declaration') !== readDeclaration(detail.view.key).id) return json({ title: '详情读取声明缺失', code: 'DECLARATION_REQUIRED' }, 400);
      return json({ fields, summaries: {} });
    }
    if (url.pathname === '/api/organization/persons') {
      if (request.headers.get('X-NewP-Data-Declaration') !== readDeclaration(list.view.key).id) return json({ title: '列表读取声明缺失', code: 'DECLARATION_REQUIRED' }, 400);
      return json({ items: [fields], total: 1, page: 1, pageSize: 20 });
    }
    if (url.pathname.includes('/authorization/')) return json({}, 403);
    throw new Error(`Unexpected options request ${request.method} ${url.pathname}`);
  }));
  ModuleRegistry.replaceAll([{ metadata: { moduleId: 'Organization', displayName: '组织', version: '1' }, ...organizationBindings }], 'options-data-execution');
  const container = document.createElement('div'); document.body.append(container); const root = createRoot(container);
  const catalog = Object.fromEntries([
    ...Object.entries(listDictionary.PersonListView).map(([key, value]) => [`PersonListView.${key}`, value.zhs]),
    ...Object.entries(detailDictionary.PersonDetailView).map(([key, value]) => [`PersonDetailView.${key}`, value.zhs]),
    ...Object.entries(platformDictionary.Platform).map(([key, value]) => [`Platform.${key}`, value.zhs]),
  ]);
  const wrap = (content: ReactNode) => <LocalizationProvider defaultLanguage="zhs" supportedLanguages={[{ code: 'zhs', displayName: '中文' }]} catalogs={{ zhs: catalog }}>
    <AuthProvider initialUser={user}><AuthorizationProvider featurePositions={user.featurePositions}><PlatformQueryProvider scopeKey="options-data-execution">
      <RuntimeMetadataProvider value={{ manifest: { modules: [{ moduleId: 'Organization', metadataBasePath: '', navFiles: [], viewFiles: [], modelFiles: [], dataAccess: declarations,
        features: [{ key: 'persons', kind: 'navigation', position: entryPosition },
          { key: 'create', kind: 'action', actionScope: 'list.pageActions', position: createPosition, parentPosition: entryPosition },
          { key: 'edit', kind: 'action', actionScope: 'list.rowActions', position: editPosition, parentPosition: entryPosition },
          { key: 'edit', kind: 'action', actionScope: 'detail.pageActions', position: detailEditPosition, parentPosition: entryPosition }] }] },
        navDocuments: [], modelDocuments: [runtimeModels.person], viewDocuments: [bindActionDataAccess({ ...list, type: 'view' }, declarations), bindActionDataAccess({ ...detail, type: 'view' }, declarations)] }}>
        {content}
      </RuntimeMetadataProvider>
    </PlatformQueryProvider></AuthorizationProvider></AuthProvider>
  </LocalizationProvider>;
  const router = createMemoryRouter([
    { path: '/', element: wrap(<PageEntry page={{ pageKey: 'platform.list', parameters: { viewKey: list.view.key, featurePosition: entryPosition } }} />) },
    { path: '/detail/:id', element: wrap(<FeaturePlacementContext.Provider value={{ entryPosition }}><RouteCapabilityOutlet capability={{ kind: 'standard-crud-detail', targetKey: detail.view.key }} /></FeaturePlacementContext.Provider>) },
  ]);
  onTestFinished(async () => { await act(async () => root.unmount()); router.dispose(); container.remove(); ModuleRegistry.replaceAll([], 'options-cleanup'); vi.unstubAllGlobals(); });
  await act(async () => root.render(<RouterProvider router={router} />));
  await vi.waitFor(() => expect(container.querySelector('tr[data-row-key="person-one"]')).not.toBeNull());
  await vi.waitFor(() => expect(requests.some(request => request.headers.get('X-NewP-Feature') === entryPosition)).toBe(true));
  expect(requests[0].headers.get('X-NewP-Data-Declaration')).toBe(declaration.id);
  const button = (label: string) => [...document.querySelectorAll<HTMLButtonElement>('button')].find(node => node.textContent?.replace(/\s/g, '') === label);
  await act(async () => button('新建人员')!.click());
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer form')).not.toBeNull());
  await vi.waitFor(() => expect(requests.some(request => request.headers.get('X-NewP-Feature') === createPosition)).toBe(true));
  await act(async () => document.querySelector<HTMLButtonElement>('.ant-drawer-close')!.click());
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer')).toBeNull());
  const edit = () => [...container.querySelectorAll<HTMLButtonElement>('tr[data-row-key="person-one"] button')].find(node => node.textContent?.trim() === '编辑');
  await vi.waitFor(() => expect(edit()).toBeDefined()); await act(async () => edit()!.click());
  await vi.waitFor(() => expect(reads.some(request => request.headers.get('X-NewP-Feature') === editPosition)).toBe(true));
  expect(reads.find(request => request.headers.get('X-NewP-Feature') === editPosition)?.headers.get('X-NewP-Data-Declaration')).toBe(readDeclaration(detail.view.key).id);
  await vi.waitFor(() => expect(document.querySelector('.ant-drawer')?.textContent).toContain('当前职位'));
  const currentRequest = requests.find(request => request.headers.get('X-NewP-Feature') === editPosition && new URL(request.url).searchParams.get('currentValueIds') === 'position-old');
  expect(currentRequest?.headers.get('X-NewP-Data-Declaration')).toBe(declaration.id);
  pending = true;
  const search = document.querySelector<HTMLInputElement>('.ant-drawer input#PositionId')!;
  expect(search).not.toBeNull();
  await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(search, '迟到候选'); search.dispatchEvent(new Event('input', { bubbles: true })); });
  await vi.waitFor(() => expect(finish).toBeDefined());
  const cancelledRequest = requests.at(-1)!;
  await act(async () => document.querySelector<HTMLButtonElement>('.ant-drawer-close')!.click());
  await vi.waitFor(() => expect(cancelledRequest.signal.aborted).toBe(true));
  await act(async () => finish(json({ items: [{ value: 'late', label: '迟到结果' }], currentItems: [], page: 1, pageSize: 20, hasMore: false })));
  expect(document.body.textContent).not.toContain('迟到结果');
  expect(requests.every(request => request.headers.get('X-NewP-Data-Declaration') === declaration.id)).toBe(true);
  pending = false;
  await act(async () => router.navigate('/detail/person-one'));
  await vi.waitFor(() => expect(container.querySelector('.np-crud-detail-sections')).not.toBeNull());
  const fullDetailReads = reads.length;
  await act(async () => button('编辑')!.click());
  await vi.waitFor(() => expect(container.querySelector('form')?.textContent).toContain('当前职位'));
  const detailRequest = requests.find(request => request.headers.get('X-NewP-Feature') === detailEditPosition);
  expect(detailRequest?.headers.get('X-NewP-Data-Declaration')).toBe(detailDeclaration.id);
  expect(new URL(detailRequest!.url).searchParams.get('currentValueIds')).toBe('position-old');
  expect(detailDeclaration.id).not.toBe(declaration.id);
  // 完整详情已有正式read快照；旧编辑动作没有prepare契约，不因options的编辑位置再取一次详情。
  expect(reads).toHaveLength(fullDetailReads);
});
