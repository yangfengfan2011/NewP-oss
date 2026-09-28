import personMessages from '../../../../../modules/Organization/metadata/dictionaries/Organization.Person.dic.json';
import './authorizedFeatureUiFixture';
// @vitest-environment jsdom

import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry, type StandardCrudResourceRegistration } from '@icp-platform/sdk';
import organizationModule from '../../../../modules/organization/src/index';
import PersonListPage from '../../../../modules/organization/src/pages/PersonListPage';
import personDetailDictionary from '../../../../../modules/Organization/metadata/dictionaries/PersonDetailView.dic.json';
import personListDictionary from '../../../../../modules/Organization/metadata/dictionaries/PersonListView.dic.json';
import organizationRuntimeModels from '../../../../../tests/Kernel.Tests/Fixtures/OrganizationRuntimeModels.contract.json';
import personDetailViewDocument from '../../../../../modules/Organization/metadata/views/PersonDetail.view.json';
import personListViewDocument from '../../../../../modules/Organization/metadata/views/PersonList.view.json';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

function countTextOccurrences(text: string, keyword: string): number {
  return text.split(keyword).length - 1;
}

async function flushProTableRequest(): Promise<void> {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

const serviceMocks = vi.hoisted(() => ({
  setPersonLifecycleStatus: vi.fn(),
  getCompanyLookup: vi.fn(async () => ({
    options: [],
    currentOptions: [],
    page: 1,
    pageSize: 20,
    hasMore: false,
  })),
  getDepartmentLookup: vi.fn(async () => ({
    options: [],
    currentOptions: [],
    page: 1,
    pageSize: 20,
    hasMore: false,
  })),
}));

vi.mock('../../../../modules/organization/src/services/organizationService', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../../modules/organization/src/services/organizationService')>();

  return {
    ...actual,
    setPersonLifecycleStatus: serviceMocks.setPersonLifecycleStatus,
    getCompanyLookup: serviceMocks.getCompanyLookup,
    getDepartmentLookup: serviceMocks.getDepartmentLookup,
    getEmploymentStatusLabel: (value: string) => value,
  };
});

function flattenZhsCatalog(
  dictionary: typeof personListDictionary | typeof personDetailDictionary,
  dictionaryKey: 'PersonListView' | 'PersonDetailView',
): Record<string, string> {
  const entries = dictionary[dictionaryKey] as Record<string, { zhs: string }>;
  return Object.fromEntries(
    Object.entries(entries).map(([key, value]) => [
      `${dictionaryKey}.${key}`,
      value.zhs,
    ]),
  );
}

const standardCrudListViewMetadata = {
  manifest: { modules: [] },
  navDocuments: [{
    type: 'nav' as const,
    navigation: {
      items: [{
        key: 'organization.person.center',
        path: '/organization/persons',
        entryType: 'standard-list' as const,
        targetKey: 'organization.person.list',
        permissions: ['Organization.Person.View'],
      }],
    },
  }],
  viewDocuments: [createPersonListViewDocumentWithRowActionVisibility(), personDetailViewDocument],
  modelDocuments: [organizationRuntimeModels.person],
};

const personListCatalog = {
  ...Object.fromEntries(Object.entries(personMessages['Organization.Person']).map(([key, value]) => [`Organization.Person.${key}`, value.zhs])),
  ...flattenZhsCatalog(personListDictionary, 'PersonListView'),
  ...flattenZhsCatalog(personDetailDictionary, 'PersonDetailView'),
};

let personResource: StandardCrudResourceRegistration;

function createPersonListViewDocumentWithRowActionVisibility(): typeof personListViewDocument {
  return {
    ...personListViewDocument,
    view: {
      ...personListViewDocument.view,
      rowActions: personListViewDocument.view.rowActions.map((action) => {
        if (action.standardAction === 'disable') {
          return {
            ...action,
            visibleWhen: { field: 'IsEnabled', operator: 'equals', value: true },
          };
        }
        if (action.standardAction === 'enable') {
          return {
            ...action,
            visibleWhen: { field: 'IsEnabled', operator: 'equals', value: false },
          };
        }

        return action;
      }),
    },
  } as typeof personListViewDocument;
}

describe('organization person center list layout', () => {
  let container: HTMLDivElement;
  let root: Root;

  beforeAll(async () => {
    await Promise.all([
      import('@ant-design/pro-descriptions'),
      import('../pages/StandardCrudListDrawerRuntime'),
      import('../pages/StandardCrudListOverlayRuntime'),
    ]);
    const capability = organizationModule.standardCrudCapabilities?.find(
      (item) => item.kind === 'standard-list' && item.targetKey === 'organization.person.list',
    );
    if (!capability) {
      throw new Error('未找到人员标准 CRUD 列表 capability。');
    }

    personResource = {
      ...(await capability.load()).default,
      permissions: [
        'Organization.Person.View',
        'Organization.Person.Create',
        'Organization.Person.Update',
        'Organization.Person.Delete',
      ],
    };
  });

  function renderStandardCrudListHost(
    strictMode = false,
    initialEntry = '/organization/persons',
  ) {
    root = createRoot(container);
    const host = (
      <MemoryRouter initialEntries={[initialEntry]}>
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
          catalogs={{ zhs: personListCatalog }}
        >
          <RuntimeMetadataProvider value={standardCrudListViewMetadata}>
            <PlatformQueryProvider scopeKey="user-1">
              <StandardCrudListPageHost viewKey="organization.person.list" registration={personResource} />
            </PlatformQueryProvider>
          </RuntimeMetadataProvider>
        </LocalizationProvider>
      </MemoryRouter>
    );
    root.render(strictMode ? <StrictMode>{host}</StrictMode> : host);
  }

  beforeEach(() => {
    ModuleRegistry.replaceAll([{ metadata: { moduleId: 'organization', displayName: 'organization', version: '1.0.0' }, standardCrudCapabilities: organizationModule.standardCrudCapabilities }], 'organization-detail-test');
    const getComputedStyle = window.getComputedStyle.bind(window);
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      value: vi.fn().mockImplementation((query: string) => ({
        matches: true,
        media: query,
        onchange: null,
        addListener: vi.fn(),
        removeListener: vi.fn(),
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(),
      })),
    });
    Object.defineProperty(window, 'getComputedStyle', {
      writable: true,
      value: (element: Element, pseudoElement?: string | null) => {
        if (pseudoElement) {
          return {
            getPropertyValue: () => '',
            width: '0px',
            height: '0px',
          };
        }

        return getComputedStyle(element);
      },
    });
  });

  afterEach(async () => {
    vi.restoreAllMocks();
    ModuleRegistry.replaceAll([], 'detail-test-cleanup');
    vi.clearAllMocks();

    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    if (container) {
      container.remove();
    }
  });

  it('renders person center through the standard CRUD list host', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({
          items: [{
            EID: 'person-1',
            PersonNo: 'P0001',
            ENAME: '张三',
            BirthDate: '2000-02-29',
            BirthDate_DisplayValue: '2000/2/29',
            CompanyId: 'company-1',
            CompanyId_DisplayValue: '总部',
            DepartmentId: 'department-1',
            DepartmentName: '人力资源部',
            EmploymentStatus: 'FullTime',
            EmploymentStartedAt: '2030-01-02T00:04:05Z',
            EmploymentStartedAt_DisplayValue: '2030/1/2 08:04:05',
            EmploymentEndedAt: null,
            EmploymentEndedAt_DisplayValue: null,
            IsEnabled: true,
          }, {
            EID: 'person-2',
            PersonNo: 'P0002',
            ENAME: '李四',
            BirthDate: null,
            BirthDate_DisplayValue: null,
            CompanyId: 'company-1',
            CompanyId_DisplayValue: '总部',
            DepartmentId: 'department-2',
            DepartmentName: '研发部',
            EmploymentStatus: 'PartTime',
            EmploymentStartedAt: null,
            EmploymentStartedAt_DisplayValue: null,
            EmploymentEndedAt: null,
            EmploymentEndedAt_DisplayValue: null,
            IsEnabled: true,
          }],
          total: 2,
          page: 1,
          pageSize: 20,
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    );

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      renderStandardCrudListHost();
    });
    await flushProTableRequest();

    expect(fetch).toHaveBeenCalledWith(
      '/api/organization/persons?fields=EID%2CPersonNo%2CENAME%2CBirthDate%2CCompanyId%2CDepartmentName%2CPositionName%2CEmploymentStatus%2CEmploymentStartedAt%2CEmploymentEndedAt%2CIsEnabled%2CAccountEID&pageIndex=1&pageSize=20&sortBy=PersonNo&sortDirection=asc',
      { method: 'GET', signal: expect.any(AbortSignal) },
    );
    expect(container.querySelector('.ant-pro-table')).not.toBeNull();
    expect(container.querySelector('.np-standard-crud-table')).not.toBeNull();
    expect(container.querySelector('.np-crud-workspace-header')).toBeNull();
    expect(container.querySelector('.np-standard-crud-table .ant-btn-primary')?.textContent).toContain('新建人员');
    expect(container.querySelectorAll('.ant-btn-primary')).toHaveLength(1);
    expect(container.querySelector('.np-crud-workspace-panel.ant-card')).not.toBeNull();
    expect(container.querySelector('.np-crud-workspace-body .np-crud-workspace-filter')).not.toBeNull();
    expect(container.querySelector('.np-crud-workspace-body .np-crud-workspace-content')).not.toBeNull();
    expect(container.querySelector('.np-crud-table-section-fill')).toBeNull();
    expect(container.querySelector('.ant-table')).not.toBeNull();
    expect(container.querySelector('.ant-pagination')).not.toBeNull();
    expect(container.querySelector('.np-search-table-footer')).not.toBeNull();
    expect(container.querySelector('.ant-pagination')?.textContent).toContain('第 1 / 1 页');
    expect(container.querySelector('.ant-pagination')?.textContent).toContain('共 2 条');
    expect(container.querySelector('.ant-table-selection-column')).toBeNull();
    expect(container.querySelector('.np-table-column-resize-handle')).toBeNull();
    expect(container.querySelector('.np-table-row-icon')).toBeNull();
    const rowNumberHeader = container.querySelector('th.np-table-row-number-cell.ant-table-cell-fix-left');
    expect(rowNumberHeader).not.toBeNull();
    expect(rowNumberHeader?.textContent?.trim()).toBe('序号');
    expect(rowNumberHeader?.getAttribute('aria-label')).toBe('序号');
    expect(Array.from(container.querySelectorAll('td.np-table-row-number-cell')).map((node) => node.textContent)).toEqual(['1', '2']);
    const tableHeaderTexts = Array.from(container.querySelectorAll('.ant-table-thead th')).map((node) =>
      node.textContent?.replace(/\s+/g, ''),
    );
    expect(tableHeaderTexts).toContain('序号');
    expect(tableHeaderTexts).not.toContain('图标');
    const columnSettingIcon = container.querySelector('.anticon-setting');
    expect(columnSettingIcon).not.toBeNull();
    await act(async () => {
      columnSettingIcon?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushProTableRequest();
    const settingColumnTexts = Array.from(
      document.body.querySelectorAll('.ant-pro-table-column-setting-list .ant-tree-treenode'),
    ).map((node) => node.textContent?.replace(/\s+/g, '') ?? '');
    expect(settingColumnTexts.filter(Boolean)).toEqual([
      '人员编号', '姓名', '出生日期', '公司', '部门', '岗位', '任职状态', '入职时间', '离职时间', '启用状态', '操作',
    ]);
    expect(container.textContent).not.toContain('人员列表维护');
    expect(container.querySelector('.np-crud-workspace-filter input.np-filter-control')).not.toBeNull();
    expect(container.querySelector<HTMLInputElement>('.np-crud-workspace-filter input.np-filter-control')?.placeholder).toBe('按工号 / 姓名搜索');
    expect(container.querySelector('.ant-select')).not.toBeNull();
    expect(container.textContent).not.toContain('全部公司');
    const filterSelects = Array.from(container.querySelectorAll('.np-standard-crud-filter-grid .ant-select'));
    expect(filterSelects).toHaveLength(1);
    expect(filterSelects[0]?.classList.contains('np-filter-select')).toBe(true);
    expect(filterSelects[0]?.classList.contains('np-filter-control')).toBe(false);
    expect(container.textContent).toContain('新建人员');
    expect(container.textContent).toContain('刷新');
    expect(container.textContent).toContain('高级筛选');
    expect(container.textContent).not.toContain('人员列表');
    expect(container.textContent).toContain('关键字');
    expect(container.textContent).toContain('人员编号');
    expect(container.textContent).toContain('姓名');
    expect(container.textContent).toContain('张三');
    expect(container.textContent).toContain('李四');
    expect(container.textContent).toContain('2000/2/29');
    expect(container.textContent).toContain('2030/1/2 08:04:05');
    expect(container.textContent).not.toContain('2000-02-29');
    expect(container.textContent).not.toContain('2030-01-02T00:04:05Z');
    expect(container.textContent).toContain('总部');
    expect(container.textContent).not.toContain('company-1');
    expect(container.textContent).toContain('启用');
    expect(container.textContent).not.toContain('PartTime');
    expect(container.textContent).not.toContain('FullTime');
    expect(container.textContent).not.toContain('true');
    expect(container.querySelectorAll('.ant-tag')).not.toHaveLength(0);
    const bodyRows = Array.from(container.querySelectorAll('.ant-table-tbody > tr.ant-table-row'));
    expect(bodyRows[0]?.classList.contains('np-table-row-odd')).toBe(false);
    expect(bodyRows[1]?.classList.contains('np-table-row-even')).toBe(false);
    const firstActionCell = bodyRows[0]?.querySelector('td:last-child');
    expect(firstActionCell?.textContent).not.toContain('查看');
    expect(firstActionCell?.textContent).toContain('编辑');
    expect(firstActionCell?.textContent).toContain('更多');
    expect(firstActionCell?.textContent).not.toContain('删除');
    expect(firstActionCell?.textContent).not.toContain('停用');
    expect(firstActionCell?.textContent).not.toContain('启用');
    expect(firstActionCell?.querySelectorAll('.np-row-action-button')).toHaveLength(1);
    expect(firstActionCell?.querySelector('.np-row-actions-more')).not.toBeNull();
    expect(bodyRows[0]?.querySelector('.np-table-row-view-button')).toBeNull();
    await act(async () => {
      firstActionCell?.querySelector('.np-row-actions-more')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushProTableRequest();
    const overflowMenu = document.body.querySelector('.ant-dropdown-menu');
    expect(overflowMenu?.textContent).not.toContain('查看');
    expect(overflowMenu?.textContent).toContain('删除');
    expect(overflowMenu?.textContent).toContain('停用');
    expect(overflowMenu?.textContent).not.toContain('启用');
    expect(container.querySelector('a[href="/organization/persons/person-1"]')).toBeNull();
    expect(countTextOccurrences(container.textContent ?? '', '共 2 条')).toBe(1);
    expect(countTextOccurrences(container.textContent ?? '', '新建人员')).toBe(1);
  });

  it('renders the metadata-owned CalendarDate range in the person filter section', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [], total: 0, page: 1, pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => renderStandardCrudListHost());
    await flushProTableRequest();
    const advancedFilterButton = Array.from(container.querySelectorAll('button')).find(
      (button) => button.textContent?.includes('高级筛选'),
    );
    await act(async () => advancedFilterButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushProTableRequest();

    expect(document.body.textContent).toContain('人员信息');
    expect(document.body.textContent).toContain('组织信息');
    expect(document.body.textContent).toContain('出生日期范围');
    expect(document.body.querySelector('.np-advanced-filter-section .ant-picker-range')).not.toBeNull();
    expect(container.textContent).toContain('当前数据集为空，可使用页面主动作创建第一条记录。');
  });

  it('restores explicit birth date URL bounds and reset removes both applied parameters', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [], total: 0, page: 1, pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => renderStandardCrudListHost(
      false,
      '/organization/persons?birthDateFrom=2000-02-29&birthDateTo=2010-12-31',
    ));
    await flushProTableRequest();

    expect(fetchSpy.mock.calls.some(([input]) => {
      const url = String(input);
      return url.startsWith('/api/organization/persons?')
        && url.includes('birthDateFrom=2000-02-29')
        && url.includes('birthDateTo=2010-12-31');
    })).toBe(true);

    const advancedFilterButton = Array.from(container.querySelectorAll('button')).find(
      (button) => button.textContent?.includes('高级筛选'),
    );
    await act(async () => advancedFilterButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushProTableRequest();
    const rangeInputs = Array.from(document.body.querySelectorAll<HTMLInputElement>('.ant-picker-range input'));
    expect(rangeInputs.map((input) => input.value)).toEqual(['2000-02-29', '2010-12-31']);
    const requestsBeforeDraft = fetchSpy.mock.calls.filter(
      ([input]) => String(input).startsWith('/api/organization/persons?'),
    ).length;
    await act(async () => {
      const setValue = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
      setValue?.call(rangeInputs[0], '2001-01-01');
      rangeInputs[0]?.dispatchEvent(new InputEvent('input', { bubbles: true, data: '2001-01-01' }));
    });
    await flushProTableRequest();
    expect(fetchSpy.mock.calls.filter(
      ([input]) => String(input).startsWith('/api/organization/persons?'),
    )).toHaveLength(requestsBeforeDraft);

    const reset = Array.from(document.body.querySelectorAll('button')).find(
      (button) => button.textContent?.includes('重置条件'),
    );
    await act(async () => reset?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushProTableRequest();

    const personRequests = fetchSpy.mock.calls
      .map(([input]) => String(input))
      .filter((url) => url.startsWith('/api/organization/persons?'));
    expect(personRequests.at(-1)).not.toContain('birthDateFrom');
    expect(personRequests.at(-1)).not.toContain('birthDateTo');
  });

  it('single-flights the real narrow company lookup while the StrictMode list still loads', async () => {
    const actualService = await vi.importActual<
      typeof import('../../../../modules/organization/src/services/organizationService')
    >('../../../../modules/organization/src/services/organizationService');
    serviceMocks.getCompanyLookup.mockImplementation(actualService.getCompanyLookup);
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
      if (url.startsWith('/api/organization/person-lookups/companies?')) {
        return new Response(JSON.stringify({
          items: [{ value: 'company-1', label: '总部', disabled: false }],
          currentItems: [],
          page: 1,
          pageSize: 20,
          hasMore: false,
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }
      if (url.startsWith('/api/organization/positions/options?')) {
        return new Response(JSON.stringify({
          items: [], currentItems: [], page: 1, pageSize: 20, hasMore: false,
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }
      if (url.startsWith('/api/organization/persons?')) {
        return new Response(JSON.stringify({
          items: [{
            EID: 'person-1',
            PersonNo: 'P0001',
            ENAME: '张三',
            CompanyId: 'company-1',
            CompanyId_DisplayValue: '总部',
            DepartmentName: '研发部',
            EmploymentStatus: 'FullTime',
            IsEnabled: true,
          }],
          total: 1,
          page: 1,
          pageSize: 20,
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }

      throw new Error(`unexpected request: ${url}`);
    });
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => renderStandardCrudListHost(true));
    await flushProTableRequest();

    await vi.waitFor(() => expect(container.textContent).toContain('张三'));
    expect(serviceMocks.getCompanyLookup).toHaveBeenCalledTimes(1);
    expect(serviceMocks.getCompanyLookup).toHaveBeenCalledWith(
      expect.objectContaining({ fieldKey: 'companyId', page: 1, pageSize: 20 }),
      true,
      expect.any(AbortSignal),
      undefined,
    );
    expect(fetchSpy.mock.calls.filter(([url, init]) => (
      typeof url === 'string'
      && url.startsWith('/api/organization/person-lookups/companies?')
      && init?.method === 'GET'
    ))).toHaveLength(1);
    expect(fetchSpy.mock.calls.filter(([url, init]) => (
      typeof url === 'string'
      && url.startsWith('/api/organization/persons?')
      && init?.method === 'GET'
    ))).toHaveLength(1);
  });

  it('loads person center as a standard CRUD frontend resource', () => {
    expect(personResource).toMatchObject({
      resourceKey: 'organization.person',
      listViewKey: 'organization.person.list',
      detailViewKey: 'organization.person.detail',
      dataAdapter: {
        list: expect.any(Function),
        get: expect.any(Function),
        getRowId: expect.any(Function),
      },
    });
  });

  it('keeps the legacy direct list page as a compatibility message', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(<PersonListPage />);
    });

    expect(container.textContent).toContain('人员中心主列表已由 Shell 标准 CRUD 宿主接管');
  });

  it('opens the basic drawer from the name and exposes the full detail boundary', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.startsWith('/api/organization/positions/options?')) {
        return new Response(JSON.stringify({
          items: [], currentItems: [], page: 1, pageSize: 20, hasMore: false,
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }
      if (url.startsWith('/api/organization/persons?')) {
        return new Response(JSON.stringify({
          items: [{ EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true }],
          total: 1,
          page: 1,
          pageSize: 20,
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }
      if (url === '/api/organization/persons/person-1') {
        return new Response(JSON.stringify({
          fields: {
            EID: 'person-1',
            PersonNo: 'P0001',
            ENAME: '张三',
            CompanyId: 'company-1',
            CompanyId_DisplayValue: '总部',
            DepartmentId: 'department-1',
            DepartmentName: '人力资源部',
            PositionId: 'position-1',
            PositionName: '经理',
            EmploymentStatus: 'FullTime',
            IsEnabled: true,
            CreatedBy: 'creator-1',
            CreatedAt: '2026-04-21T00:00:00Z',
            UpdatedBy: 'editor-2',
            UpdatedAt: '2026-04-23T00:00:00Z',
            AccountEID: 'account-1',
          },
          summaries: {
            userBinding: { status: 'ready', data: { AccountEid: 'account-1' } },
            authorizationSummary: { status: 'ready', data: ['Organization.Person.View'] },
          },
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }

      throw new Error(`unexpected request: ${url}`);
    });
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => renderStandardCrudListHost());
    await flushProTableRequest();
    await vi.waitFor(() => expect(fetchSpy.mock.calls.some(([input]) => (
      String(input).startsWith('/api/organization/persons?')
    ))).toBe(true));
    await vi.waitFor(() => expect(container.querySelector('.np-standard-crud-cell-action')).not.toBeNull());
    await act(async () => {
      container.querySelector('.np-standard-crud-cell-action')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushProTableRequest();
    await vi.waitFor(() => expect(fetchSpy.mock.calls.some(([input]) => (
      String(input) === '/api/organization/persons/person-1'
    ))).toBe(true));

    await vi.waitFor(() => {
      expect(document.body.textContent).toContain('查看人员');
      expect(document.body.textContent).toContain('查看完整详情');
      expect(document.body.textContent).toContain('基础信息');
      expect(document.body.textContent).toContain('组织信息');
      expect(document.body.textContent).toContain('审计信息');
      expect(document.body.textContent).toContain('总部');
      expect(document.body.textContent).toContain('creator-1');
      expect(document.body.textContent).toContain('editor-2');
    });
    expect(document.body.textContent).not.toContain('账号绑定');
    expect(document.body.textContent).not.toContain('授权摘要');
    expect(document.body.textContent).not.toContain('person-1');
    expect(document.body.textContent).not.toContain('company-1');
    expect(document.body.textContent).not.toContain('account-1');
  });

  it('keeps the drawer edit action available without a person version dependency', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.startsWith('/api/organization/positions/options?')) {
        return new Response(JSON.stringify({
          items: [], currentItems: [], page: 1, pageSize: 20, hasMore: false,
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }
      if (url.startsWith('/api/organization/persons?')) {
        return new Response(JSON.stringify({
          items: [{ EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true }],
          total: 1,
          page: 1,
          pageSize: 20,
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }
      if (url === '/api/organization/persons/person-1') {
        return new Response(JSON.stringify({
          fields: {
            EID: 'person-1',
            PersonNo: 'P0001',
            ENAME: '张三',
            CompanyId: null,
            DepartmentId: null,
            PositionId: null,
            EmploymentStatus: 'FullTime',
            IsEnabled: true,
          },
          summaries: {},
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }

      throw new Error(`unexpected request: ${url}`);
    });
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => renderStandardCrudListHost());
    await flushProTableRequest();
    await vi.waitFor(() => expect(fetchSpy.mock.calls.some(([input]) => (
      String(input).startsWith('/api/organization/persons?')
    ))).toBe(true));
    await vi.waitFor(() => expect(container.querySelector('.np-standard-crud-cell-action')).not.toBeNull());
    await act(async () => {
      container.querySelector('.np-standard-crud-cell-action')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    await vi.waitFor(() => expect(fetchSpy.mock.calls.some(([input]) => (
      String(input) === '/api/organization/persons/person-1'
    ))).toBe(true));
    const editButton = Array.from(document.body.querySelectorAll('.ant-drawer-footer button')).find(
      (button) => button.textContent?.replace(/\s+/g, '') === '编辑',
    );
    expect(document.body.textContent).not.toContain('动作所需数据不可用');
    expect(editButton?.disabled).toBe(false);
  });

  it('disables deletion for a permanently bound person and explains the guard', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [{
        EID: 'person-1',
        PersonNo: 'P0001',
        ENAME: '张三',
        IsEnabled: true,
        AccountEID: 'account-1',
      }],
      total: 1,
      page: 1,
      pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => renderStandardCrudListHost());
    await flushProTableRequest();
    await vi.waitFor(() => expect(container.querySelector('.np-row-actions-more')).not.toBeNull());
    await act(async () => {
      container.querySelector('.np-row-actions-more')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    await vi.waitFor(() => expect(Array.from(document.body.querySelectorAll('[role="menuitem"]')).some(
      (item) => item.textContent?.includes('删除'),
    )).toBe(true));
    const deleteItem = Array.from(document.body.querySelectorAll('[role="menuitem"]')).find(
      (item) => item.textContent?.includes('删除'),
    );
    expect(deleteItem?.getAttribute('aria-disabled')).toBe('true');
    expect(deleteItem?.textContent).toContain('已绑定用户账号，永久禁止删除');
  });

  it('shows a row action error when disabling a person fails', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({
          items: [{
            EID: 'person-1',
            PersonNo: 'P0001',
            ENAME: '张三',
            CompanyId: 'company-1',
            CompanyId_DisplayValue: '总部',
            DepartmentId: 'department-1',
            DepartmentName: '人力资源部',
            EmploymentStatus: 'FullTime',
            IsEnabled: true,
          }],
          total: 1,
          page: 1,
          pageSize: 20,
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    );
    serviceMocks.setPersonLifecycleStatus.mockRejectedValue(new Error('停用人员失败'));

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      renderStandardCrudListHost();
    });
    await flushProTableRequest();
    await vi.waitFor(() => expect(container.querySelector('.np-row-actions-more')).not.toBeNull());

    await act(async () => {
      container.querySelector('.np-row-actions-more')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await vi.waitFor(() => expect(Array.from(document.body.querySelectorAll('[role="menuitem"]')).some(
      (item) => item.textContent?.includes('停用'),
    )).toBe(true));
    const disableMenuItem = Array.from(document.body.querySelectorAll('[role="menuitem"]')).find(
      (item) => item.textContent?.includes('停用'),
    );
    await act(async () => {
      disableMenuItem?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushProTableRequest();
    await vi.waitFor(() => {
      expect(document.body.querySelector('textarea[aria-label="停用原因"]')).toBeInstanceOf(HTMLTextAreaElement);
    });
    const reasonInput = document.body.querySelector('textarea[aria-label="停用原因"]') as HTMLTextAreaElement;
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')?.set?.call(reasonInput, '人员中心停用人员');
      reasonInput.dispatchEvent(new Event('input', { bubbles: true }));
    });
    const confirmButton = Array.from(document.body.querySelectorAll('button')).find(
      (button) => button.textContent?.replace(/\s+/g, '') === '确认',
    );
    await act(async () => {
      confirmButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    expect(serviceMocks.setPersonLifecycleStatus).toHaveBeenCalledWith('person-1', {
      isEnabled: false,
      reason: '人员中心停用人员',
    }, 'ui-fixture:list.rowActions:disable');
    expect(document.querySelector('.ant-message')?.textContent).toContain('操作失败，请稍后重试。');
  });
});
