// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useLocation, useNavigate, type NavigateFunction } from 'react-router-dom';
import { LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';

const platformFetchMock = vi.hoisted(() => vi.fn((input: RequestInfo | URL, init?: RequestInit) => globalThis.fetch(input, init)));

vi.mock('@icp-platform/core', async (importOriginal) => ({
  ...await importOriginal<typeof import('@icp-platform/core')>(),
  platformFetch: platformFetchMock,
}));

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

let testNavigate: NavigateFunction | undefined;
let currentLocationSearch = '';

function NavigationProbe() {
  testNavigate = useNavigate();
  currentLocationSearch = useLocation().search;
  return null;
}

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

const personModelDocument = {
  type: 'model' as const,
  Person: {
    fields: [
      {
        name: 'PersonNo',
        storage: { type: 'string' },
        uiType: 'text',
        form: {
          create: { mode: 'editable', required: true },
          edit: { mode: 'readonly' },
        },
      },
      {
        name: 'ENAME',
        storage: { type: 'string' },
        uiType: 'text',
        form: {
          create: { mode: 'editable', required: true },
          edit: { mode: 'editable', required: true },
        },
      },
      {
        name: 'DepartmentId',
        storage: { type: 'guid', nullable: true },
        uiType: 'lookup',
        dataSource: { type: 'provider', key: 'organization.departments' },
        form: {
          create: { mode: 'editable' },
          edit: { mode: 'editable' },
        },
      },
      {
        name: 'EmploymentStatus',
        storage: { type: 'string', nullable: true },
        uiType: 'select',
        contract: { valueType: 'enum', clrType: 'EmploymentStatus' },
        dataSource: { type: 'enum', key: 'EmploymentStatus' },
        form: {
          create: { mode: 'editable', defaultValue: 'FullTime' },
          edit: { mode: 'editable' },
        },
      },
      {
        name: 'IsEnabled',
        storage: { type: 'bool' },
        uiType: 'switch',
        form: {
          create: { mode: 'editable', defaultValue: true },
          edit: { mode: 'editable' },
        },
      },
    ],
    sections: [
      { key: 'basic', titleKey: 'Section_Basic', fields: ['PersonNo', 'ENAME'] },
      { key: 'organization', titleKey: 'Section_Organization', fields: ['DepartmentId', 'EmploymentStatus', 'IsEnabled'] },
    ],
  },
};

const personListViewDocument = {
  type: 'view' as const,
  view: {
    key: 'organization.person.list',
    dicKey: 'PersonListView',
    titleKey: 'Title',
    descriptionKey: 'Description',
    mode: 'list',
    model: 'Person',
    query: {
      fields: [
        { key: 'keyword', labelKey: 'Query_Keyword', source: 'preset', preset: 'keyword' },
        { key: 'employmentStatus', labelKey: 'Query_EmploymentStatus', source: 'model', field: 'EmploymentStatus' },
      ],
      advancedFilter: {
        enabled: true,
        titleKey: 'AdvancedFilter_More',
        sections: [{
          key: 'organization',
          titleKey: 'Section_Organization',
          fields: [
            { key: 'departmentId', labelKey: 'Query_Department', source: 'model', field: 'DepartmentId' },
            { key: 'isEnabled', labelKey: 'Query_IsEnabled', source: 'model', field: 'IsEnabled' },
          ],
        }],
      },
    },
    columns: [
      { key: 'personNo', labelKey: 'Column_PersonNo', source: 'model', field: 'PersonNo' },
      { key: 'name', labelKey: 'Column_Name', source: 'model', field: 'ENAME' },
      { key: 'departmentName', labelKey: 'Column_DepartmentName', source: 'projection', field: 'DepartmentName' },
    ],
    pageActions: [
      { key: 'create', labelKey: 'Action_Create', standardAction: 'create', variant: 'primary' },
      { key: 'refresh', labelKey: 'Action_Refresh', standardAction: 'refresh' },
    ],
    rowActions: [
      { key: 'view', labelKey: 'Action_View', standardAction: 'view' },
      { key: 'edit', labelKey: 'Action_Edit', standardAction: 'edit' },
      {
        key: 'delete',
        labelKey: 'Action_Delete',
        standardAction: 'delete',
        variant: 'danger',
        handlerKey: 'deletePerson',
        confirmMessageKey: 'Action_Delete_Confirm',
      },
      {
        key: 'disable',
        labelKey: 'Action_Disable',
        standardAction: 'disable',
        variant: 'danger',
        handlerKey: 'disablePerson',
        confirmMessageKey: 'Action_Disable_Confirm',
      },
      {
        key: 'enable',
        labelKey: 'Action_Enable',
        standardAction: 'enable',
        handlerKey: 'enablePerson',
        confirmMessageKey: 'Action_Enable_Confirm',
      },
    ],
  },
};

const personListCatalog = {
  'PersonListView.Title': '人员中心',
  'PersonListView.Description': '维护组织模块中的人员基础信息。',
  'PersonListView.Query_Keyword': '关键字',
  'PersonListView.Query_EmploymentStatus': '任职状态',
  'PersonListView.Query_Department': '部门',
  'PersonListView.Query_IsEnabled': '启用状态',
  'PersonListView.AdvancedFilter_More': '更多筛选',
  'PersonListView.Section_Basic': '基础信息',
  'PersonListView.Section_Organization': '组织信息',
  'PersonListView.Column_PersonNo': '人员编号',
  'PersonListView.Column_Name': '姓名',
  'PersonListView.Column_DepartmentName': '部门',
  'PersonListView.Action_Create': '新建人员',
  'PersonListView.Action_Refresh': '刷新',
  'PersonListView.Action_View': '查看',
  'PersonListView.Action_Edit': '编辑',
  'PersonListView.Action_Delete': '删除',
  'PersonListView.Action_Delete_Confirm': '确认删除该人员吗？',
  'PersonListView.Action_Disable': '停用',
  'PersonListView.Action_Disable_Confirm': '确认停用该人员？',
  'PersonListView.Action_Enable': '启用',
  'PersonListView.Action_Enable_Confirm': '确认启用该人员？',
  'PersonListView.Export_Current': '导出当前',
  'PersonListView.Export_All': '导出全部',
  'PersonListView.Field_PersonNo': '人员编号',
  'PersonListView.Field_ENAME': '姓名',
  'PersonListView.Field_DepartmentId': '部门',
  'PersonListView.Field_EmploymentStatus': '任职状态',
  'PersonListView.Field_IsEnabled': '启用状态',
};

describe('StandardCrudListPageHost', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    vi.restoreAllMocks();
    window.localStorage.clear();
    currentLocationSearch = '';
    if (root) {
      await act(async () => root.unmount());
    }
    container?.remove();
  });

  async function renderHost(
    catalogOverrides: Record<string, string> = {},
    viewDocument: typeof personListViewDocument = personListViewDocument,
    initialEntry = '/organization/persons',
  ) {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <MemoryRouter initialEntries={[initialEntry]}>
          <NavigationProbe />
          <LocalizationProvider
            defaultLanguage="zhs"
            supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
            catalogs={{
              zhs: {
                ...personListCatalog,
                ...catalogOverrides,
              },
            }}
          >
            <RuntimeMetadataProvider
              value={{
                manifest: { modules: [] },
                navDocuments: [],
                modelDocuments: [personModelDocument],
                viewDocuments: [viewDocument],
              }}
            >
              <StandardCrudListPageHost viewKey="organization.person.list" />
            </RuntimeMetadataProvider>
          </LocalizationProvider>
        </MemoryRouter>,
      );
    });
  }

  function createExportablePersonListViewDocument(): typeof personListViewDocument {
    return {
      ...personListViewDocument,
      view: {
        ...personListViewDocument.view,
        export: {
          modes: ['current', 'all'],
        },
      },
    };
  }

  function createSortablePersonListViewDocument(): typeof personListViewDocument {
    return {
      ...personListViewDocument,
      view: {
        ...personListViewDocument.view,
        query: {
          ...personListViewDocument.view.query,
          defaultSort: { field: 'PersonNo', direction: 'asc' },
          defaultPageSize: 20,
        },
        columns: personListViewDocument.view.columns.map((column) => (
          column.key === 'personNo' || column.key === 'name'
            ? { ...column, sortable: true }
            : column
        )),
      },
    };
  }

  function mockStandardCrudModule(
    overrides: Partial<NonNullable<ReturnType<typeof ModuleRegistry.getAllModules>[number]['standardCrudResources']>[number]> = {},
  ) {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'organization.person',
            apiRoute: '/api/organization/persons',
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            createEmptyFilters: () => ({ legacyKeyword: 'legacy' }),
            buildListQuery: () => ({ legacyKeyword: 'legacy', legacyUsed: 'true' }),
            mapListItem: (value: unknown) => value as { id: string; name: string },
            getRowId: (row: { id: string }) => row.id,
            enumOptions: {
              EmploymentStatus: [
                { value: 'FullTime', label: '正式' },
                { value: 'PartTime', label: '兼职' },
              ],
            },
            lookupProviders: [{
              key: 'organization.departments',
              load: async () => [],
            }],
            actionHandlers: {
              refresh: vi.fn(),
              deletePerson: vi.fn(),
              disablePerson: vi.fn(),
              enablePerson: vi.fn(),
            },
            ...overrides,
          },
        ],
      },
    ]);
  }

  it('loads rows through standard CRUD registration', async () => {
    mockStandardCrudModule();
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({
        items: [{ id: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentName: '人力资源部' }],
        total: 1,
        page: 1,
        pageSize: 20,
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost();
    await flushProTableRequest();

    expect(fetch).toHaveBeenCalledWith(
      '/api/organization/persons?fields=EID%2CPersonNo%2CENAME%2CDepartmentName&pageIndex=1&pageSize=20',
      { method: 'GET' },
    );
    expect(container.textContent).toContain('张三');
    expect(countTextOccurrences(container.textContent ?? '', '共 1 条')).toBe(0);
    expect(container.querySelector('.ant-pagination')).toBeNull();
  });

  it('restores committed filters paging and server sorting from the URL', async () => {
    mockStandardCrudModule();
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({
        items: [{ id: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentName: '人力资源部' }],
        total: 80,
        page: 2,
        pageSize: 50,
        sortBy: 'ENAME',
        sortDirection: 'desc',
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost(
      {},
      createSortablePersonListViewDocument(),
      '/organization/persons?keyword=%E5%BC%A0&page=2&pageSize=50&sortBy=ENAME&sortDirection=desc',
    );
    await flushProTableRequest();

    expect(fetch).toHaveBeenCalledWith(
      '/api/organization/persons?keyword=%E5%BC%A0&fields=EID%2CPersonNo%2CENAME%2CDepartmentName&pageIndex=2&pageSize=50&sortBy=ENAME&sortDirection=desc',
      { method: 'GET' },
    );
    expect(container.querySelector('.ant-pagination')?.textContent).toContain('共 80 条');
  });

  it('falls back to the last valid page when refreshed data no longer reaches the URL page', async () => {
    mockStandardCrudModule();
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => (
      new Response(JSON.stringify({ items: [], total: 20, page: 1, pageSize: 20 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    ));

    await renderHost(
      {},
      createSortablePersonListViewDocument(),
      '/organization/persons?page=3&pageSize=20&sortBy=PersonNo&sortDirection=asc',
    );
    await flushProTableRequest();
    await flushProTableRequest();

    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(fetchMock.mock.calls[0]?.[0]).toContain('pageIndex=3');
    expect(fetchMock.mock.calls[1]?.[0]).toContain('pageIndex=1');
  });

  it('submits Enter through the shared query path and resets paging to the first page', async () => {
    mockStandardCrudModule();
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ items: [], total: 80, page: 1, pageSize: 20 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost(
      {},
      createSortablePersonListViewDocument(),
      '/organization/persons?keyword=old&page=2&pageSize=20&sortBy=PersonNo&sortDirection=asc',
    );
    await flushProTableRequest();

    const keywordInput = container.querySelector('input.np-filter-control') as HTMLInputElement;
    await act(async () => {
      const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
      valueSetter?.call(keywordInput, 'new');
      keywordInput.dispatchEvent(new Event('input', { bubbles: true }));
      keywordInput.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    });
    await flushProTableRequest();

    expect(fetchMock).toHaveBeenLastCalledWith(
      '/api/organization/persons?keyword=new&fields=EID%2CPersonNo%2CENAME%2CDepartmentName&pageIndex=1&pageSize=20&sortBy=PersonNo&sortDirection=asc',
      { method: 'GET' },
    );
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('restores browser history without duplicate or drifting requests', async () => {
    mockStandardCrudModule();
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ items: [], total: 80, page: 1, pageSize: 20 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    await renderHost({}, createSortablePersonListViewDocument());
    await flushProTableRequest();

    await act(async () => {
      testNavigate?.('?keyword=%E5%BC%A0&page=2&pageSize=20&sortBy=ENAME&sortDirection=desc');
    });
    await flushProTableRequest();
    await act(async () => {
      testNavigate?.(-1);
    });
    await flushProTableRequest();

    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(fetchMock.mock.calls[1]?.[0]).toContain('keyword=%E5%BC%A0');
    expect(fetchMock.mock.calls[1]?.[0]).toContain('pageIndex=2');
    expect(fetchMock.mock.calls[2]?.[0]).not.toContain('keyword=');
    expect(fetchMock.mock.calls[2]?.[0]).toContain('pageIndex=1');
  });

  it('requests view columns and row action state fields through fields parameter', async () => {
    mockStandardCrudModule();
    vi.spyOn(globalThis, 'fetch').mockImplementation(async () => new Response(JSON.stringify({
      items: [{ EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentName: '研发部', IsEnabled: true }],
      total: 1,
      page: 1,
      pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    const viewDocument = {
      ...personListViewDocument,
      view: {
        ...personListViewDocument.view,
        rowActions: personListViewDocument.view.rowActions.map((action) =>
          action.key === 'disable'
            ? { ...action, visibleWhen: { field: 'IsEnabled', operator: 'equals', value: true } }
            : action),
      },
    };

    await renderHost({}, viewDocument);
    await flushProTableRequest();

    expect(fetch).toHaveBeenCalledWith(
      '/api/organization/persons?fields=EID%2CPersonNo%2CENAME%2CDepartmentName%2CIsEnabled&pageIndex=1&pageSize=20',
      { method: 'GET' },
    );
  });

  it('renders the metadata title once without expanding the compact header with description text', async () => {
    mockStandardCrudModule();
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ items: [], total: 0, page: 1, pageSize: 20 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost();
    await flushProTableRequest();

    expect(container.querySelectorAll('.np-crud-workspace-title')).toHaveLength(1);
    expect(container.querySelector('.np-crud-workspace-title')?.textContent).toBe('人员中心');
    expect(container.textContent).not.toContain('维护组织模块中的人员基础信息。');
    expect(container.textContent).not.toContain('人员中心列表');
    expect(container.textContent).not.toContain('Title');
    expect(container.textContent).not.toContain('Description');
  });

  it('prefers metadata page actions over custom header actions', async () => {
    mockStandardCrudModule({
      renderHeaderActions: () => <button type="button">自定义操作</button>,
    });
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({
        items: [{ id: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentName: '人力资源部' }],
        total: 1,
        page: 1,
        pageSize: 20,
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost();
    await flushProTableRequest();

    expect(container.textContent).not.toContain('自定义操作');
    expect(container.textContent).toContain('新建人员');
    expect(container.textContent).toContain('刷新');
  });

  it('exports current page through standard CRUD backend using view columns', async () => {
    mockStandardCrudModule();
    Object.defineProperty(URL, 'createObjectURL', {
      configurable: true,
      value: vi.fn(() => 'blob:standard-crud-export'),
    });
    Object.defineProperty(URL, 'revokeObjectURL', {
      configurable: true,
      value: vi.fn(),
    });
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(
        new Response(JSON.stringify({
          items: [{ id: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentName: '人力资源部' }],
          total: 1,
          page: 1,
          pageSize: 20,
        }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      )
      .mockResolvedValueOnce(
        new Response(new Uint8Array([80, 75]), {
          status: 200,
          headers: {
            'Content-Type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
          },
        }),
      );

    await renderHost({}, createExportablePersonListViewDocument());
    await flushProTableRequest();

    await act(async () => {
      const exportButton = Array.from(container.querySelectorAll('button')).find((button) =>
        button.textContent?.replace(/\s+/g, '') === '导出');
      exportButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushProTableRequest();
    await act(async () => {
      const currentPageItem = Array.from(document.body.querySelectorAll('.ant-dropdown-menu-item')).find((item) =>
        item.textContent?.replace(/\s+/g, '') === '导出当前');
      currentPageItem?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      currentPageItem?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushProTableRequest();

    expect(fetchMock).toHaveBeenCalledTimes(2);
    const [exportUrl, exportInit] = fetchMock.mock.calls[1];
    expect(exportUrl).toBe('/api/organization/persons/export');
    expect(exportInit).toMatchObject({ method: 'POST' });
    const body = JSON.parse(String((exportInit as RequestInit).body));
    expect(body.mode).toBe('current');
    expect(body.query).toMatchObject({ pageIndex: 1, pageSize: 20 });
    expect(body.columns).toEqual([
      { key: 'personNo', field: 'PersonNo', title: '人员编号' },
      { key: 'name', field: 'ENAME', title: '姓名' },
      { key: 'departmentName', field: 'DepartmentName', title: '部门' },
    ]);
  });

  it('exports only visible columns in the persisted preference order', async () => {
    mockStandardCrudModule();
    window.localStorage.setItem(
      'icp.standard-crud.columns:anonymous:organization.person.list',
      JSON.stringify({
        name: { show: true, order: 0 },
        personNo: { show: true, order: 1 },
        departmentName: { show: false, order: 2 },
      }),
    );
    Object.defineProperty(URL, 'createObjectURL', {
      configurable: true,
      value: vi.fn(() => 'blob:standard-crud-export'),
    });
    Object.defineProperty(URL, 'revokeObjectURL', {
      configurable: true,
      value: vi.fn(),
    });
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({ items: [], total: 0, page: 1, pageSize: 20 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }))
      .mockResolvedValueOnce(new Response(new Uint8Array([80, 75]), { status: 200 }));

    await renderHost({}, createExportablePersonListViewDocument());
    await flushProTableRequest();
    await act(async () => {
      const exportButton = Array.from(container.querySelectorAll('button')).find((button) =>
        button.textContent?.replace(/\s+/g, '') === '导出');
      exportButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushProTableRequest();
    await act(async () => {
      const currentPageItem = Array.from(document.body.querySelectorAll('.ant-dropdown-menu-item')).find((item) =>
        item.textContent?.replace(/\s+/g, '') === '导出当前');
      currentPageItem?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushProTableRequest();

    const body = JSON.parse(String((fetchMock.mock.calls[1]?.[1] as RequestInit).body));
    expect(body.columns).toEqual([
      { key: 'name', field: 'ENAME', title: '姓名' },
      { key: 'personNo', field: 'PersonNo', title: '人员编号' },
    ]);
  });

  it('renders the dedicated initial loading state before the first response', async () => {
    mockStandardCrudModule();
    let resolveRequest: ((response: Response) => void) | undefined;
    vi.spyOn(globalThis, 'fetch').mockImplementation(() => new Promise<Response>((resolve) => {
      resolveRequest = resolve;
    }));

    await renderHost();
    expect(container.textContent).toContain('正在加载列表');

    await act(async () => {
      resolveRequest?.(new Response(JSON.stringify({ items: [], total: 0, page: 1, pageSize: 20 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }));
    });
    await flushProTableRequest();
  });

  it('renders error state without an empty state after load failure', async () => {
    mockStandardCrudModule();
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new Error('列表接口不可用'));

    await renderHost();
    await flushProTableRequest();

    expect(container.textContent).toContain('列表接口不可用');
    expect(container.textContent).not.toContain('暂无符合筛选条件的数据。');
    expect(Array.from(container.querySelectorAll('button')).some((button) => button.textContent?.replace(/\s+/g, '') === '重试')).toBe(true);
  });

  it('distinguishes an empty dataset from a filtered empty result', async () => {
    mockStandardCrudModule();
    vi.spyOn(globalThis, 'fetch').mockImplementation(async () =>
      new Response(JSON.stringify({ items: [], total: 0, page: 1, pageSize: 20 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost();
    await flushProTableRequest();
    expect(container.textContent).toContain('当前数据集为空');

    await act(async () => root.unmount());
    container.remove();
    await renderHost({}, personListViewDocument, '/organization/persons?keyword=missing');
    await flushProTableRequest();
    expect(container.textContent).toContain('暂无符合筛选条件的数据');
    expect(container.textContent).not.toContain('当前数据集为空');
  });

  it('retries a failed request without changing the committed query context', async () => {
    mockStandardCrudModule();
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockRejectedValueOnce(new Error('列表接口不可用'))
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ items: [], total: 0, page: 2, pageSize: 20 }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      );

    await renderHost({}, personListViewDocument, '/organization/persons?keyword=missing&page=2');
    await flushProTableRequest();
    await act(async () => {
      const retryButton = Array.from(container.querySelectorAll('button')).find((button) => button.textContent?.replace(/\s+/g, '') === '重试');
      retryButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushProTableRequest();

    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(String(fetchMock.mock.calls[1]?.[0])).toContain('keyword=missing');
    expect(String(fetchMock.mock.calls[1]?.[0])).toContain('pageIndex=2');
  });

  it('uses normalized paging metadata in the footer without a duplicate top summary', async () => {
    mockStandardCrudModule();
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ items: [{ id: 'Person_21', name: '李四' }], total: 41, page: 2, pageSize: 20 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost();
    await flushProTableRequest();

    expect(container.textContent).toContain('共 41 条');
    expect(container.textContent).not.toContain('第 2 / 3 页');
    expect(container.textContent).toContain('3');
  });

  it('renders metadata columns actions filters and form fields without legacy registration declarations', async () => {
    const disablePerson = vi.fn();
    mockStandardCrudModule({
      actionHandlers: {
        refresh: vi.fn(),
        deletePerson: vi.fn(),
        disablePerson,
        enablePerson: vi.fn(),
      },
      buildColumns: undefined,
      renderFilters: undefined,
      listActions: undefined,
      fields: undefined,
      formSections: undefined,
    });
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({
        items: [{ id: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentName: '人力资源部', EmploymentStatus: 'FullTime' }],
        total: 1,
        page: 1,
        pageSize: 20,
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost();
    await flushProTableRequest();

    expect(fetch).toHaveBeenCalledWith(
      '/api/organization/persons?fields=EID%2CPersonNo%2CENAME%2CDepartmentName&pageIndex=1&pageSize=20',
      { method: 'GET' },
    );
    expect(container.textContent).toContain('关键字');
    expect(container.textContent).toContain('任职状态');
    expect(container.textContent).toContain('更多筛选');
    expect(container.textContent).toContain('新建人员');
    expect(container.textContent).toContain('刷新');
    expect(container.textContent).toContain('人员编号');
    expect(container.textContent).toContain('姓名');
    expect(container.textContent).toContain('部门');
    expect(countTextOccurrences(container.textContent ?? '', '查看')).toBe(0);
    expect(container.querySelector('.np-table-row-view-button')).toBeNull();
    expect(countTextOccurrences(container.textContent ?? '', '编辑')).toBe(1);
    expect(container.textContent).not.toContain('删除');
    expect(container.textContent).toContain('更多');
    expect(container.textContent).not.toContain('停用');
    expect(container.textContent).not.toContain('启用');
    expect(container.querySelectorAll('.np-row-action-button')).toHaveLength(1);
    await act(async () => {
      const moreButton = Array.from(container.querySelectorAll('button')).find((button) =>
        button.textContent?.replace(/\s+/g, '') === '更多');
      moreButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    expect(document.body.textContent).toContain('删除');
    expect(document.body.textContent).toContain('查看');
    expect(document.body.textContent).toContain('停用');
    expect(document.body.textContent).toContain('启用');
    expect(container.textContent).not.toContain('公司');
    expect(container.querySelector('.np-standard-crud-filter-grid .ant-select')).not.toBeNull();

    await act(async () => {
      const createButton = Array.from(container.querySelectorAll('button')).find((button) =>
        button.textContent?.replace(/\s+/g, '') === '新建人员');
      createButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    expect(document.body.textContent).toContain('基础信息');
    expect(document.body.textContent).toContain('组织信息');
    expect(document.body.textContent).toContain('人员编号');
    expect(document.body.textContent).toContain('姓名');
  });

  it('protects dirty drawer values with Ant Modal before closing', async () => {
    mockStandardCrudModule();
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({
        items: [{ id: 'Person_1', PersonNo: 'P001', ENAME: '张三' }], total: 1, page: 1, pageSize: 20,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三' }, summaries: {},
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    await renderHost();
    await flushProTableRequest();
    const editButton = Array.from(container.querySelectorAll('button')).find((button) => button.textContent?.trim() === '编辑');
    await act(async () => editButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushProTableRequest();
    const nameInput = document.body.querySelector('input[value="张三"]') as HTMLInputElement;
    await act(async () => {
      const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
      valueSetter?.call(nameInput, '李四');
      nameInput.dispatchEvent(new Event('input', { bubbles: true }));
    });
    const cancelButton = Array.from(document.body.querySelectorAll('button')).find((button) => button.textContent?.replace(/\s+/g, '') === '取消');
    await act(async () => cancelButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushProTableRequest();

    expect(document.body.textContent).toContain('放弃未保存的更改？');
    expect(document.body.querySelector('.ant-drawer')).not.toBeNull();
  });

  it('restores dirty drawer URL on browser back and clears it after discard', async () => {
    mockStandardCrudModule();
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({
        items: [{ id: 'Person_1', PersonNo: 'P001', ENAME: '张三' }], total: 1, page: 1, pageSize: 20,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三' }, summaries: {},
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    await renderHost();
    await flushProTableRequest();
    const editButton = Array.from(container.querySelectorAll('button')).find((button) => button.textContent?.trim() === '编辑');
    await act(async () => editButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushProTableRequest();
    const nameInput = document.body.querySelector('input[value="张三"]') as HTMLInputElement;
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(nameInput, '李四');
      nameInput.dispatchEvent(new Event('input', { bubbles: true }));
      testNavigate?.(-1);
    });
    await flushProTableRequest();

    expect(document.body.textContent).toContain('放弃未保存的更改？');
    expect(currentLocationSearch).toContain('drawer=edit');

    const discardButton = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.replace(/\s+/g, '') === '放弃更改');
    await act(async () => discardButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushProTableRequest();

    expect(currentLocationSearch).not.toContain('drawer=');
  });

  it('keeps input and exposes reload after a 409 save conflict', async () => {
    mockStandardCrudModule();
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({
        items: [{ id: 'Person_1', PersonNo: 'P001', ENAME: '张三' }], total: 1, page: 1, pageSize: 20,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三' }, summaries: {},
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        title: '记录已经变化', errors: { ENAME: ['姓名已被其他人修改'] },
      }), { status: 409, headers: { 'Content-Type': 'application/problem+json' } }));

    await renderHost();
    await flushProTableRequest();
    const editButton = Array.from(container.querySelectorAll('button')).find((button) => button.textContent?.trim() === '编辑');
    await act(async () => editButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushProTableRequest();
    const nameInput = document.body.querySelector('input[value="张三"]') as HTMLInputElement;
    await act(async () => {
      const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
      valueSetter?.call(nameInput, '李四');
      nameInput.dispatchEvent(new Event('input', { bubbles: true }));
      const saveButton = Array.from(document.body.querySelectorAll('button')).find((button) => button.textContent?.trim() === '保存修改');
      saveButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushProTableRequest();

    expect(document.body.textContent).toContain('记录已发生变化');
    expect(document.body.textContent).toContain('姓名已被其他人修改');
    expect(document.body.textContent).toContain('重新加载当前数据');
    expect((document.body.querySelector('input[value="李四"]') as HTMLInputElement | null)).not.toBeNull();
  });

  it('renders a clear error when registration is missing', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([]);

    await renderHost();

    expect(container.textContent).toContain('未找到标准 CRUD 前端资源注册项');
  });

  it('renders advanced filter actions above filter fields', async () => {
    mockStandardCrudModule();
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ items: [], total: 0, page: 1, pageSize: 20 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost();
    await flushProTableRequest();

    await act(async () => {
      const advancedFilterButton = Array.from(container.querySelectorAll('button')).find((button) =>
        button.textContent?.replace(/\s+/g, '') === '更多筛选');
      advancedFilterButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    const drawerBody = document.body.querySelector('.ant-drawer-body');
    const actions = drawerBody?.querySelector('.np-advanced-filter-drawer-actions');
    const firstSection = drawerBody?.querySelector('.np-advanced-filter-section');

    expect(document.body.querySelector('.ant-drawer-footer')).toBeNull();
    expect(actions).not.toBeNull();
    expect(firstSection).not.toBeNull();
    expect(actions?.compareDocumentPosition(firstSection as Node) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(actions?.textContent).toContain('重置条件');
    expect(actions?.textContent).toContain('应用筛选');
  });
});
