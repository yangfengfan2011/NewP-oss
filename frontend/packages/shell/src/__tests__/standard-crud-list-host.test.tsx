import { FeaturePlacementContext } from '../authorization/FeaturePlacementContext';
// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useLocation, useNavigate, type NavigateFunction } from 'react-router-dom';
import { AuthorizationProvider, LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry, createStandardCrudRestDataAdapter } from '@icp-platform/sdk';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { StandardCrudHostEnvironmentContext, type StandardCrudHostEnvironment } from '../standard-crud/standardCrudHostEnvironment';
import { createEmbeddedHostEnvironment } from './standardCrudHostEnvironmentTestSupport';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';
import exampleRecordListViewDocument from '../../../../../modules/Example/metadata/views/ExampleRecordList.view.json';
import exampleRecordDetailViewDocument from '../../../../../modules/Example/metadata/views/ExampleRecordDetail.view.json';
import exampleRecordModelDocument from '../../../../../modules/Example/metadata/models/ExampleRecord.model.json';
import exampleRecordListDictionary from '../../../../../modules/Example/metadata/dictionaries/ExampleRecordListView.dic.json';

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

async function waitForBodyText(expectedText: string): Promise<void> {
  for (let attempt = 0; attempt < 100; attempt += 1) {
    if (document.body.textContent?.includes(expectedText)) {
      return;
    }
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 10));
    });
  }

  throw new Error(`Timed out waiting for body text: ${expectedText}`);
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

const personDetailViewDocument = {
  type: 'view' as const,
  view: {
    key: 'organization.person.detail',
    dicKey: 'PersonDetailView',
    titleKey: 'Title',
    descriptionKey: 'Description',
    mode: 'detail',
    model: 'Person',
    sections: [
      { key: 'basic', titleKey: 'Section_Basic', fields: ['PersonNo', 'ENAME'] },
      { key: 'organization', titleKey: 'Section_Organization', fields: ['DepartmentId', 'EmploymentStatus', 'IsEnabled'] },
    ],
    pageActions: [
      { key: 'edit', labelKey: 'Action_Edit', standardAction: 'edit' },
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
    options: {
      strictMode?: boolean;
      feature?: { entry: string; create: string };
      embedded?: StandardCrudHostEnvironment;
      modelDocument?: typeof personModelDocument;
    } = {},
  ) {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      const host = (
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
            <PlatformQueryProvider scopeKey="user-1">
              <RuntimeMetadataProvider
                value={{
                  manifest: { modules: options.feature ? [{ moduleId: "Organization", metadataBasePath: "", navFiles: [], viewFiles: [], modelFiles: [], features: [{ key: "create", kind: "action", position: options.feature.create, parentPosition: options.feature.entry, actionScope: "list.pageActions" }] }] : [] },
                  navDocuments: [],
                  modelDocuments: [options.modelDocument ?? personModelDocument],
                  viewDocuments: [viewDocument, personDetailViewDocument],
                }}
              >
                <StandardCrudHostEnvironmentContext.Provider value={options.embedded ?? null}><StandardCrudListPageHost viewKey="organization.person.list" /></StandardCrudHostEnvironmentContext.Provider>
              </RuntimeMetadataProvider>
            </PlatformQueryProvider>
          </LocalizationProvider>
        </MemoryRouter>
      );
      const authorizedHost = options.feature ? <AuthorizationProvider permissions={[]} featurePositions={[options.feature.entry, options.feature.create]}><FeaturePlacementContext.Provider value={{ entryPosition: options.feature.entry }}>{host}</FeaturePlacementContext.Provider></AuthorizationProvider> : host;
      root.render(options.strictMode ? <StrictMode>{authorizedHost}</StrictMode> : authorizedHost);
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

  function createRuntimePersonListViewDocument(): typeof personListViewDocument {
    return {
      ...personListViewDocument,
      view: {
        ...personListViewDocument.view,
        columnSource: 'runtime',
        columns: undefined,
        rowActions: [],
        pageActions: [],
      },
    } as typeof personListViewDocument;
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
            dataAdapter: createStandardCrudRestDataAdapter({
              apiRoute: '/api/organization/persons',
              fetcher: (input, init) => globalThis.fetch(input, init),
              mapListItem: (value) => value as { id: string; name: string },
              getRowId: (row) => row.id,
            }),
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
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
      { method: 'GET', signal: expect.any(AbortSignal) },
    );
    expect(container.textContent).toContain('张三');
    expect(countTextOccurrences(container.textContent ?? '', '共 1 条')).toBe(1);
    expect(container.querySelector('.ant-pagination')).not.toBeNull();
    expect(container.querySelector('.ant-pagination')?.textContent).toContain('第 1 / 1 页');
    expect(container.querySelector('td.np-table-row-number-cell')?.textContent).toBe('1');
  });

  it('renders a companion display value for a metadata column without replacing its raw field contract', async () => {
    mockStandardCrudModule();
    const companyListViewDocument = {
      ...personListViewDocument,
      view: {
        ...personListViewDocument.view,
        columns: [
          { key: 'personNo', labelKey: 'Column_PersonNo', source: 'model', field: 'PersonNo' },
          { key: 'company', labelKey: 'Column_Company', source: 'model', field: 'CompanyId' },
        ],
      },
    } as typeof personListViewDocument;
    const companyModelDocument = {
      ...personModelDocument,
      Person: {
        ...personModelDocument.Person,
        fields: [
          ...personModelDocument.Person.fields,
          {
            name: 'CompanyId',
            storage: { type: 'guid', nullable: true },
            uiType: 'text',
            form: {
              create: { mode: 'editable' },
              edit: { mode: 'editable' },
            },
          },
        ],
      },
    } as typeof personModelDocument;
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({
        items: [{
          id: 'Person_1',
          PersonNo: 'P001',
          CompanyId: 'company-raw-id',
          CompanyId_DisplayValue: '示例公司',
        }],
        total: 1,
        page: 1,
        pageSize: 20,
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost(
      { 'PersonListView.Column_Company': '公司' },
      companyListViewDocument,
      '/organization/persons',
      { modelDocument: companyModelDocument },
    );
    await flushProTableRequest();

    expect(fetch).toHaveBeenCalledWith(
      '/api/organization/persons?fields=EID%2CPersonNo%2CCompanyId&pageIndex=1&pageSize=20',
      { method: 'GET', signal: expect.any(AbortSignal) },
    );
    expect(container.textContent).toContain('示例公司');
    expect(container.textContent).not.toContain('company-raw-id');
  });

  it('keeps one fixed refresh button and reloads the current query without List View page actions', async () => {
    mockStandardCrudModule();
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
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
    const listViewWithoutPageActions = {
      ...personListViewDocument,
      view: {
        ...personListViewDocument.view,
        pageActions: [],
      },
    } as typeof personListViewDocument;

    await renderHost({}, listViewWithoutPageActions);
    await flushProTableRequest();

    const refreshButtons = Array.from(container.querySelectorAll<HTMLButtonElement>('button[aria-label="刷新"]'));
    expect(refreshButtons).toHaveLength(1);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const initialRequest = String(fetchMock.mock.calls[0]?.[0]);

    await act(async () => refreshButtons[0].dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushProTableRequest();

    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(String(fetchMock.mock.calls[1]?.[0])).toBe(initialRequest);
  });

  it('does not render an action column when the List View has no row actions', async () => {
    mockStandardCrudModule({ detailViewKey: undefined });
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
    const listViewWithoutRowActions = {
      ...personListViewDocument,
      view: {
        ...personListViewDocument.view,
        rowActions: [],
      },
    } as typeof personListViewDocument;

    await renderHost({}, listViewWithoutRowActions);
    await flushProTableRequest();

    const headers = Array.from(container.querySelectorAll('.ant-table-thead th'))
      .map((header) => header.textContent?.trim());
    const row = container.querySelector('.ant-table-tbody tr.ant-table-row');
    expect(headers).not.toContain('操作');
    expect(row?.querySelectorAll('td')).toHaveLength(4);
    expect(row?.querySelector('td.ant-table-cell-fix-right')).toBeNull();

    const columnSetting = container.querySelector<HTMLElement>('.ant-pro-table-list-toolbar-setting-item');
    expect(columnSetting).not.toBeNull();
    await act(async () => columnSetting?.querySelector('svg')?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushProTableRequest();
    const columnSettingOverlay = document.body.querySelector<HTMLElement>('.ant-pro-table-column-setting-overlay');
    expect(columnSettingOverlay).not.toBeNull();
    expect(columnSettingOverlay?.textContent).not.toContain('操作');
  });

  it('keeps StrictMode query replay read-only, shares one request, and runs one confirmed delete handler', async () => {
    const createCommand = vi.fn(async () => ({ operation: 'create' as const, payload: undefined }));
    const updateCommand = vi.fn(async () => ({ operation: 'update' as const, payload: undefined }));
    const removeCommand = vi.fn(async () => undefined);
    const deleteCommand = vi.fn(async () => undefined);
    const disableAction = vi.fn(async () => undefined);
    const enableAction = vi.fn(async () => undefined);
    const dataAdapter = createStandardCrudRestDataAdapter({
      apiRoute: '/api/organization/persons',
      fetcher: (input, init) => globalThis.fetch(input, init),
      mapListItem: (value) => value as { id: string; name: string },
      getRowId: (row) => row.id,
    });
    mockStandardCrudModule({
      dataAdapter: {
        ...dataAdapter,
        create: createCommand,
        update: updateCommand,
        remove: removeCommand,
      },
      actionHandlers: {
        deletePerson: deleteCommand,
        disablePerson: disableAction,
        enablePerson: enableAction,
      },
    });
    let releaseList!: () => void;
    const listGate = new Promise<void>((resolve) => { releaseList = resolve; });
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => {
      await listGate;
      return new Response(JSON.stringify({
        items: [{ id: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentName: '人力资源部' }],
        total: 1,
        page: 1,
        pageSize: 20,
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    });
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <StrictMode>
          <MemoryRouter initialEntries={['/organization/persons']}>
            <LocalizationProvider
              defaultLanguage="zhs"
              supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
              catalogs={{ zhs: personListCatalog }}
            >
              <PlatformQueryProvider scopeKey="user-1">
                <RuntimeMetadataProvider
                  value={{
                    manifest: { modules: [] },
                    navDocuments: [],
                    modelDocuments: [personModelDocument],
                    viewDocuments: [personListViewDocument, personDetailViewDocument],
                  }}
                >
                  <StandardCrudListPageHost viewKey="organization.person.list" />
                  <StandardCrudListPageHost viewKey="organization.person.list" />
                </RuntimeMetadataProvider>
              </PlatformQueryProvider>
            </LocalizationProvider>
          </MemoryRouter>
        </StrictMode>,
      );
    });

    const listCalls = fetchMock.mock.calls.filter(([input]) => String(input).startsWith('/api/organization/persons'));
    expect(listCalls).toHaveLength(1);
    expect(createCommand).not.toHaveBeenCalled();
    expect(updateCommand).not.toHaveBeenCalled();
    expect(removeCommand).not.toHaveBeenCalled();
    expect(deleteCommand).not.toHaveBeenCalled();
    expect(disableAction).not.toHaveBeenCalled();
    expect(enableAction).not.toHaveBeenCalled();

    await act(async () => releaseList());
    await flushProTableRequest();
    await waitForBodyText('张三');

    expect(createCommand).not.toHaveBeenCalled();
    expect(updateCommand).not.toHaveBeenCalled();
    expect(removeCommand).not.toHaveBeenCalled();
    expect(deleteCommand).not.toHaveBeenCalled();
    expect(disableAction).not.toHaveBeenCalled();
    expect(enableAction).not.toHaveBeenCalled();

    const moreButton = Array.from(container.querySelectorAll('button')).find((button) =>
      button.textContent?.replace(/\s+/g, '') === '更多');
    expect(moreButton).toBeDefined();
    await act(async () => moreButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushProTableRequest();

    const visibleDropdown = Array.from(document.body.querySelectorAll<HTMLElement>('.ant-dropdown'))
      .filter((dropdown) => !dropdown.classList.contains('ant-dropdown-hidden'))
      .at(-1);
    const deleteButton = Array.from(
      (visibleDropdown ?? document.body).querySelectorAll<HTMLElement>('button, a, [role="button"], [role="menuitem"]'),
    ).find((button) => button.textContent?.replace(/\s+/g, '') === '删除');
    expect(deleteButton).toBeDefined();
    await act(async () => deleteButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('确认删除该人员吗？');
    expect(document.body.textContent).toContain('确认删除该人员吗？');

    const confirmButton = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.replace(/\s+/g, '') === '确认');
    expect(confirmButton).toBeDefined();
    await act(async () => confirmButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushProTableRequest();

    expect(deleteCommand).toHaveBeenCalledTimes(1);
    expect(deleteCommand).toHaveBeenCalledWith(expect.objectContaining({ id: 'Person_1' }));
    expect(createCommand).not.toHaveBeenCalled();
    expect(updateCommand).not.toHaveBeenCalled();
    expect(removeCommand).not.toHaveBeenCalled();
    expect(disableAction).not.toHaveBeenCalled();
    expect(enableAction).not.toHaveBeenCalled();
  });

  it('keeps computed rows on the existing list adapter when columns come from metadata', async () => {
    const columnProvider = { load: vi.fn(async () => ({ columns: [] })) };
    const list = vi.fn(async () => ({
      items: [{ id: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentName: '计算部门' }],
      total: 1,
      page: 1,
      pageSize: 20,
    }));
    mockStandardCrudModule({
      columnProvider,
      dataAdapter: {
        list,
        get: async () => ({ normalized: { fields: {}, summaries: {} }, value: { fields: {}, summaries: {} } }),
        getRowId: (row) => String((row as { id: string }).id),
      },
    });

    await renderHost();
    await flushProTableRequest();

    expect(columnProvider.load).not.toHaveBeenCalled();
    expect(list).toHaveBeenCalledTimes(1);
    expect(container.textContent).toContain('计算部门');
  });

  it('loads and validates runtime columns before reading rows with the same context token', async () => {
    let resolveColumns: ((value: {
      columns: Array<{ key: string; field: string; title: string; valueKind: 'text' | 'number' }>;
      contextToken: string;
    }) => void) | undefined;
    const columnResult = new Promise<{
      columns: Array<{ key: string; field: string; title: string; valueKind: 'text' | 'number' }>;
      contextToken: string;
    }>((resolve) => { resolveColumns = resolve; });
    const list = vi.fn(async () => ({
      items: [{ id: 'member-1', memberName: '示例成员', currentWeekDay1: 8 }],
      total: 1,
      page: 1,
      pageSize: 20,
    }));
    const loadColumns = vi.fn(() => columnResult);
    mockStandardCrudModule({
      dataAdapter: {
        list,
        get: async () => ({ normalized: { fields: {}, summaries: {} }, value: { fields: {}, summaries: {} } }),
        getRowId: (row) => String((row as { id: string }).id),
      },
      columnProvider: { load: loadColumns },
    });
    await renderHost({}, createRuntimePersonListViewDocument());
    expect(loadColumns).toHaveBeenCalledTimes(1);
    expect(list).not.toHaveBeenCalled();

    await act(async () => {
      resolveColumns?.({
        columns: [
          { key: 'member', field: 'memberName', title: '成员', valueKind: 'text' },
          { key: 'current-week.day-1', field: 'currentWeekDay1', title: '8月17日', valueKind: 'number' },
        ],
        contextToken: 'opaque-week-token',
      });
      await columnResult;
    });
    await flushProTableRequest();

    expect(list).toHaveBeenCalledWith(
      expect.objectContaining({ fields: 'EID,memberName,currentWeekDay1', pageIndex: 1, pageSize: 20 }),
      expect.objectContaining({
        columnContextToken: 'opaque-week-token',
        signal: expect.any(AbortSignal),
      }),
    );
    expect(container.textContent).toContain('8月17日');
    expect(container.textContent).toContain('示例成员');
    expect(container.textContent).toContain('8');
  });

  it('shares one column-first runtime generation across StrictMode replay', async () => {
    let releaseColumns: (() => void) | undefined;
    let releaseRows: (() => void) | undefined;
    const columnsGate = new Promise<void>((resolve) => { releaseColumns = resolve; });
    const rowsGate = new Promise<void>((resolve) => { releaseRows = resolve; });
    const columnSignals: AbortSignal[] = [];
    const rowSignals: AbortSignal[] = [];
    const loadColumns = vi.fn(async ({ signal }: { signal: AbortSignal }) => {
      columnSignals.push(signal);
      await columnsGate;
      return {
        columns: [{ key: 'member', field: 'memberName', title: '成员', valueKind: 'text' as const }],
        contextToken: 'strict-week-token',
      };
    });
    const list = vi.fn(async (_request, readContext) => {
      if (readContext?.signal) rowSignals.push(readContext.signal);
      await rowsGate;
      return {
        items: [{ id: 'member-1', memberName: 'StrictMode 成员' }],
        total: 1,
        page: 1,
        pageSize: 20,
      };
    });
    mockStandardCrudModule({
      columnProvider: { load: loadColumns },
      dataAdapter: {
        list,
        get: async () => ({ normalized: { fields: {}, summaries: {} }, value: { fields: {}, summaries: {} } }),
        getRowId: (row) => String((row as { id: string }).id),
      },
    });

    await renderHost({}, createRuntimePersonListViewDocument(), '/organization/persons', { strictMode: true });
    expect(loadColumns).toHaveBeenCalledTimes(1);
    expect(list).not.toHaveBeenCalled();

    await act(async () => {
      releaseColumns?.();
      await columnsGate;
    });
    await flushProTableRequest();
    expect(list).toHaveBeenCalledTimes(1);
    expect(columnSignals).toHaveLength(1);
    expect(columnSignals[0]?.aborted).toBe(false);
    expect(rowSignals).toHaveLength(1);
    expect(rowSignals[0]?.aborted).toBe(false);

    await act(async () => {
      releaseRows?.();
      await rowsGate;
    });
    await waitForBodyText('StrictMode 成员');
    expect(loadColumns).toHaveBeenCalledTimes(1);
    expect(list).toHaveBeenCalledTimes(1);
  });

  it('does not read rows when a runtime column set is invalid', async () => {
    const list = vi.fn();
    mockStandardCrudModule({
      dataAdapter: {
        list,
        get: async () => ({ normalized: { fields: {}, summaries: {} }, value: { fields: {}, summaries: {} } }),
        getRowId: () => 'member-1',
      },
      columnProvider: { load: async () => ({ columns: [] }) },
    });

    await renderHost({}, createRuntimePersonListViewDocument());
    await waitForBodyText('runtime column provider returned an empty column set');

    expect(list).not.toHaveBeenCalled();
    expect(Array.from(container.querySelectorAll('button')).some((button) => button.textContent?.replace(/\s/g, '') === '重试')).toBe(true);
  });

  it('does not read rows when the runtime column context token is blank', async () => {
    const list = vi.fn();
    mockStandardCrudModule({
      dataAdapter: {
        list,
        get: async () => ({ normalized: { fields: {}, summaries: {} }, value: { fields: {}, summaries: {} } }),
        getRowId: () => 'member-1',
      },
      columnProvider: {
        load: async () => ({
          columns: [{ key: 'member', field: 'memberName', title: '成员' }],
          contextToken: '   ',
        }),
      },
    });

    await renderHost({}, createRuntimePersonListViewDocument());
    await waitForBodyText('runtime column provider returned an empty context token');

    expect(list).not.toHaveBeenCalled();
  });

  it('restores a persisted runtime column order after refresh changes titles for the same stable keys', async () => {
    window.localStorage.setItem(
      'icp.standard-crud.columns:anonymous:organization.person.list',
      JSON.stringify({
        'current-week.day-1': { show: true, order: 0 },
        member: { show: true, order: 1 },
      }),
    );
    const loadColumns = vi.fn()
      .mockResolvedValueOnce({
        columns: [
          { key: 'member', field: 'memberName', title: '成员' },
          { key: 'current-week.day-1', field: 'currentWeekDay1', title: '8月17日' },
        ],
        contextToken: 'week-1',
      })
      .mockResolvedValueOnce({
        columns: [
          { key: 'member', field: 'memberName', title: '成员' },
          { key: 'current-week.day-1', field: 'currentWeekDay1', title: '8月24日' },
        ],
        contextToken: 'week-2',
      });
    const list = vi.fn(async () => ({
      items: [{ id: 'member-1', memberName: '示例成员', currentWeekDay1: 3 }],
      total: 1,
      page: 1,
      pageSize: 20,
    }));
    mockStandardCrudModule({
      columnProvider: { load: loadColumns },
      dataAdapter: {
        list,
        get: async () => ({ normalized: { fields: {}, summaries: {} }, value: { fields: {}, summaries: {} } }),
        getRowId: (row) => String((row as { id: string }).id),
      },
    });

    await renderHost({}, createRuntimePersonListViewDocument());
    await waitForBodyText('8月17日');
    await flushProTableRequest();
    const initialHeaders = Array.from(container.querySelectorAll('.ant-table-thead th')).map((header) => header.textContent ?? '');
    expect(initialHeaders.indexOf('8月17日')).toBeLessThan(initialHeaders.indexOf('成员'));

    await act(async () => container.querySelector<HTMLButtonElement>('button[aria-label="刷新"]')?.click());
    await waitForBodyText('8月24日');
    await flushProTableRequest();

    const refreshedHeaders = Array.from(container.querySelectorAll('.ant-table-thead th')).map((header) => header.textContent ?? '');
    expect(refreshedHeaders.indexOf('8月24日')).toBeLessThan(refreshedHeaders.indexOf('成员'));
    expect(container.textContent).not.toContain('8月17日');
    expect(loadColumns).toHaveBeenCalledTimes(2);
    expect(list).toHaveBeenCalledTimes(2);
    expect(list.mock.calls[1]?.[1]).toMatchObject({
      columnContextToken: 'week-2',
      signal: expect.any(AbortSignal),
    });
  });

  it('normalizes an obsolete runtime sort before issuing one row request for the refreshed column set', async () => {
    const loadColumns = vi.fn()
      .mockResolvedValueOnce({
        columns: [{ key: 'workload', field: 'oldWorkload', title: '旧工作量', sortable: true }],
        contextToken: 'old-week',
      })
      .mockResolvedValueOnce({
        columns: [{ key: 'workload', field: 'newWorkload', title: '新工作量', sortable: true }],
        contextToken: 'new-week',
      });
    let refreshedRequestSearch = '';
    const list = vi.fn()
      .mockResolvedValueOnce({ items: [{ id: 'member-1', oldWorkload: 2 }], total: 1, page: 1, pageSize: 20 })
      .mockImplementationOnce(async () => {
        refreshedRequestSearch = currentLocationSearch;
        return { items: [{ id: 'member-1', newWorkload: 4 }], total: 1, page: 1, pageSize: 20 };
      });
    mockStandardCrudModule({
      columnProvider: { load: loadColumns },
      dataAdapter: {
        list,
        get: async () => ({ normalized: { fields: {}, summaries: {} }, value: { fields: {}, summaries: {} } }),
        getRowId: (row) => String((row as { id: string }).id),
      },
    });

    await renderHost(
      {},
      createRuntimePersonListViewDocument(),
      '/organization/persons?sortBy=oldWorkload&sortDirection=desc',
    );
    await waitForBodyText('旧工作量');
    expect(list.mock.calls[0]?.[0]).toMatchObject({ sortBy: 'oldWorkload', sortDirection: 'desc' });

    await act(async () => container.querySelector<HTMLButtonElement>('button[aria-label="刷新"]')?.click());
    await waitForBodyText('新工作量');
    await flushProTableRequest();

    expect(currentLocationSearch).not.toContain('sortBy');
    expect(currentLocationSearch).not.toContain('sortDirection');
    expect(refreshedRequestSearch).not.toContain('sortBy');
    expect(refreshedRequestSearch).not.toContain('sortDirection');
    expect(list).toHaveBeenCalledTimes(2);
    expect(list.mock.calls[1]?.[0]).not.toHaveProperty('sortBy');
    expect(list.mock.calls[1]?.[0]).not.toHaveProperty('sortDirection');
    expect(list.mock.calls[1]?.[1]).toMatchObject({
      columnContextToken: 'new-week',
      signal: expect.any(AbortSignal),
    });
  });

  it('renders a runtime ISO date without shifting it in a negative time zone', async () => {
    const NativeDate = globalThis.Date;
    class NegativeOffsetDate extends NativeDate {
      private readonly parsedAsUtcDateOnly: boolean;

      constructor(value?: string | number, month?: number, date?: number) {
        if (typeof value === 'number' && month !== undefined) {
          super(value, month, date ?? 1);
          this.parsedAsUtcDateOnly = false;
        } else if (value !== undefined) {
          super(value);
          this.parsedAsUtcDateOnly = value === '2026-08-17';
        } else {
          super();
          this.parsedAsUtcDateOnly = false;
        }
      }

      override toLocaleDateString(): string {
        return this.parsedAsUtcDateOnly ? '2026-08-16' : '2026-08-17';
      }
    }
    vi.stubGlobal('Date', NegativeOffsetDate);
    mockStandardCrudModule({
      columnProvider: {
        load: async () => ({
          columns: [{ key: 'work-date', field: 'workDate', title: '工作日期', valueKind: 'date' }],
          contextToken: 'date-week',
        }),
      },
      dataAdapter: {
        list: async () => ({
          items: [{ id: 'member-1', workDate: '2026-08-17' }],
          total: 1,
          page: 1,
          pageSize: 20,
        }),
        get: async () => ({ normalized: { fields: {}, summaries: {} }, value: { fields: {}, summaries: {} } }),
        getRowId: (row) => String((row as { id: string }).id),
      },
    });

    try {
      await renderHost({}, createRuntimePersonListViewDocument());
      await waitForBodyText('2026-08-17');
    } finally {
      vi.unstubAllGlobals();
    }
  });

  it.each([
    [
      [
        { key: 'member', field: 'memberName', title: '成员' },
        { key: 'member', field: 'otherName', title: '其他成员' },
      ],
      'runtime column key "member" is duplicated',
    ],
    [[{ key: 'actions', field: 'actions', title: '动作' }], 'runtime column key "actions" is reserved'],
    [[{ key: 'member', field: '', title: '成员' }], 'runtime column "member" must declare field'],
    [[{ key: 'member', field: 'memberName', title: '' }], 'runtime column "member" must declare title'],
    [[{ key: 'member', field: 'memberName', title: '成员', valueKind: 'renderer' }], 'runtime column "member" has unsupported valueKind "renderer"'],
  ])('rejects malformed runtime column descriptors before reading rows', async (columns, message) => {
    const list = vi.fn();
    mockStandardCrudModule({
      dataAdapter: {
        list,
        get: async () => ({ normalized: { fields: {}, summaries: {} }, value: { fields: {}, summaries: {} } }),
        getRowId: () => 'member-1',
      },
      columnProvider: { load: async () => ({ columns }) } as never,
    });

    await renderHost({}, createRuntimePersonListViewDocument());
    await waitForBodyText(message as string);

    expect(list).not.toHaveBeenCalled();
  });

  it('restarts the complete runtime column generation when a row request is retried', async () => {
    const loadColumns = vi.fn(async () => ({
      columns: [{ key: 'member', field: 'memberName', title: '成员', valueKind: 'text' as const }],
      contextToken: 'week-token',
    }));
    const list = vi.fn()
      .mockRejectedValueOnce(new Error('示例行加载失败'))
      .mockResolvedValueOnce({
        items: [{ id: 'member-1', memberName: '恢复后的成员' }],
        total: 1,
        page: 1,
        pageSize: 20,
      });
    mockStandardCrudModule({
      dataAdapter: {
        list,
        get: async () => ({ normalized: { fields: {}, summaries: {} }, value: { fields: {}, summaries: {} } }),
        getRowId: (row) => String((row as { id: string }).id),
      },
      columnProvider: { load: loadColumns },
    });

    await renderHost({}, createRuntimePersonListViewDocument());
    await waitForBodyText('示例行加载失败');
    const retryButton = Array.from(container.querySelectorAll('button')).find(
      (button) => button.textContent?.replace(/\s/g, '') === '重试',
    );
    await act(async () => retryButton?.click());
    await waitForBodyText('恢复后的成员');

    expect(loadColumns).toHaveBeenCalledTimes(2);
    expect(list).toHaveBeenCalledTimes(2);
    expect(list.mock.calls[1]?.[1]).toMatchObject({
      columnContextToken: 'week-token',
      signal: expect.any(AbortSignal),
    });
  });

  it('discards a stale runtime column generation after committed filters change', async () => {
    type ColumnResult = { columns: Array<{ key: string; field: string; title: string; valueKind: 'text' }>; contextToken: string };
    let resolveFirst: ((value: ColumnResult) => void) | undefined;
    let resolveSecond: ((value: ColumnResult) => void) | undefined;
    const first = new Promise<ColumnResult>((resolve) => { resolveFirst = resolve; });
    const second = new Promise<ColumnResult>((resolve) => { resolveSecond = resolve; });
    const loadColumns = vi.fn()
      .mockReturnValueOnce(first)
      .mockReturnValueOnce(second);
    const list = vi.fn(async () => ({
      items: [{ id: 'member-2', memberName: '新代际成员' }],
      total: 1,
      page: 1,
      pageSize: 20,
    }));
    mockStandardCrudModule({
      dataAdapter: {
        list,
        get: async () => ({ normalized: { fields: {}, summaries: {} }, value: { fields: {}, summaries: {} } }),
        getRowId: (row) => String((row as { id: string }).id),
      },
      columnProvider: { load: loadColumns },
    });

    await renderHost({}, createRuntimePersonListViewDocument());
    await act(async () => testNavigate?.('/organization/persons?keyword=new'));
    expect(loadColumns).toHaveBeenCalledTimes(2);

    await act(async () => {
      resolveSecond?.({
        columns: [{ key: 'new-member', field: 'memberName', title: '新列', valueKind: 'text' }],
        contextToken: 'new-token',
      });
      await second;
    });
    await waitForBodyText('新代际成员');
    await act(async () => {
      resolveFirst?.({
        columns: [{ key: 'old-member', field: 'memberName', title: '旧列', valueKind: 'text' }],
        contextToken: 'old-token',
      });
      await first;
    });

    expect(container.textContent).toContain('新列');
    expect(container.textContent).not.toContain('旧列');
    expect(list).toHaveBeenCalledTimes(1);
    expect(list.mock.calls[0]?.[1]).toMatchObject({
      columnContextToken: 'new-token',
      signal: expect.any(AbortSignal),
    });
  });

  it('reuses runtime columns and discards stale rows when sorting within one generation', async () => {
    type RowResult = { items: Array<{ id: string; memberName: string }>; total: number; page: number; pageSize: number };
    let resolveFirst: ((value: RowResult) => void) | undefined;
    let resolveSecond: ((value: RowResult) => void) | undefined;
    const first = new Promise<RowResult>((resolve) => { resolveFirst = resolve; });
    const second = new Promise<RowResult>((resolve) => { resolveSecond = resolve; });
    const loadColumns = vi.fn(async () => ({
      columns: [{ key: 'member', field: 'memberName', title: '成员', valueKind: 'text' as const, sortable: true }],
      contextToken: 'stable-token',
    }));
    const list = vi.fn()
      .mockReturnValueOnce(first)
      .mockReturnValueOnce(second)
      .mockResolvedValue({
        items: [{ id: 'member-3', memberName: '第二页结果' }],
        total: 40,
        page: 2,
        pageSize: 20,
      });
    mockStandardCrudModule({
      dataAdapter: {
        list,
        get: async () => ({ normalized: { fields: {}, summaries: {} }, value: { fields: {}, summaries: {} } }),
        getRowId: (row) => String((row as { id: string }).id),
      },
      columnProvider: { load: loadColumns },
    });

    await renderHost({}, createRuntimePersonListViewDocument());
    await flushProTableRequest();
    const memberHeader = Array.from(container.querySelectorAll('.ant-table-thead th')).find((header) =>
      header.textContent?.includes('成员'));
    await act(async () => memberHeader?.querySelector<HTMLElement>('.ant-table-column-sorters')?.click());
    await flushProTableRequest();
    expect(list).toHaveBeenCalledTimes(2);

    await act(async () => {
      resolveSecond?.({ items: [{ id: 'member-2', memberName: '新排序结果' }], total: 40, page: 1, pageSize: 20 });
      await second;
    });
    await waitForBodyText('新排序结果');
    await act(async () => {
      resolveFirst?.({ items: [{ id: 'member-1', memberName: '旧排序结果' }], total: 40, page: 1, pageSize: 20 });
      await first;
    });

    expect(loadColumns).toHaveBeenCalledTimes(1);
    expect(list.mock.calls[1]?.[0]).toMatchObject({ sortBy: 'memberName', sortDirection: 'asc' });
    expect(list.mock.calls[1]?.[1]).toMatchObject({
      columnContextToken: 'stable-token',
      signal: expect.any(AbortSignal),
    });
    expect(container.textContent).toContain('新排序结果');
    expect(container.textContent).not.toContain('旧排序结果');

    await act(async () => testNavigate?.('/organization/persons?page=2&pageSize=20&sortBy=memberName&sortDirection=asc'));
    await waitForBodyText('第二页结果');

    expect(loadColumns).toHaveBeenCalledTimes(1);
    expect(list.mock.calls[2]?.[0]).toMatchObject({ pageIndex: 2, pageSize: 20 });
    expect(list.mock.calls[2]?.[1]).toMatchObject({
      columnContextToken: 'stable-token',
      signal: expect.any(AbortSignal),
    });
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
      { method: 'GET', signal: expect.any(AbortSignal) },
    );
    expect(container.querySelector('.ant-pagination')?.textContent).toContain('共 80 条');
    expect(container.querySelector('td.np-table-row-number-cell')?.textContent).toBe('51');
    const nameHeader = Array.from(container.querySelectorAll('.ant-table-thead th')).find((header) =>
      header.textContent?.includes('姓名'));
    expect(nameHeader?.querySelector('.ant-table-column-sorter-down.active')).not.toBeNull();
  });

  it('cycles a controlled sortable column from ascending to descending', async () => {
    mockStandardCrudModule();
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
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

    await renderHost(
      {},
      createSortablePersonListViewDocument(),
      '/organization/persons?page=1&pageSize=20&sortBy=PersonNo&sortDirection=asc',
    );
    await flushProTableRequest();

    const personNoHeader = Array.from(container.querySelectorAll('.ant-table-thead th')).find((header) =>
      header.textContent?.includes('人员编号'));
    expect(personNoHeader?.querySelector('.ant-table-column-sorter-up.active')).not.toBeNull();

    await act(async () => {
      personNoHeader?.querySelector<HTMLElement>('.ant-table-column-sorters')?.click();
    });
    await flushProTableRequest();

    expect(fetchMock).toHaveBeenLastCalledWith(
      '/api/organization/persons?fields=EID%2CPersonNo%2CENAME%2CDepartmentName&pageIndex=1&pageSize=20&sortBy=PersonNo&sortDirection=desc',
      { method: 'GET', signal: expect.any(AbortSignal) },
    );
    const descendingPersonNoHeader = Array.from(
      container.querySelectorAll<HTMLElement>('.ant-table-thead th'),
    ).find((header) => header.textContent?.includes('人员编号'));

    expect(
      descendingPersonNoHeader?.querySelector('.ant-table-column-sorter-down.active'),
    ).not.toBeNull();
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
      { method: 'GET', signal: expect.any(AbortSignal) },
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
    const nameHeader = Array.from(container.querySelectorAll('.ant-table-thead th')).find((header) =>
      header.textContent?.includes('姓名'));
    expect(nameHeader?.querySelector('.ant-table-column-sorter-down.active')).not.toBeNull();
    await act(async () => {
      testNavigate?.(-1);
    });
    await flushProTableRequest();

    const personNoHeader = Array.from(container.querySelectorAll('.ant-table-thead th')).find((header) =>
      header.textContent?.includes('人员编号'));
    expect(personNoHeader?.querySelector('.ant-table-column-sorter-up.active')).not.toBeNull();

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
      { method: 'GET', signal: expect.any(AbortSignal) },
    );
  });

  it('omits the page heading while retaining create actions in the table toolbar', async () => {
    mockStandardCrudModule();
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ items: [], total: 0, page: 1, pageSize: 20 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost();
    await flushProTableRequest();

    expect(container.querySelector('.np-crud-workspace-header')).toBeNull();
    expect(container.querySelector('.np-standard-crud-table')?.textContent).toContain('新建人员');
    expect(container.textContent).not.toContain('人员中心');
    expect(container.textContent).not.toContain('维护组织模块中的人员基础信息。');
    expect(container.textContent).not.toContain('人员中心列表');
    expect(container.textContent).not.toContain('Title');
    expect(container.textContent).not.toContain('Description');
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

  it.each([
    { mode: 'current', menuLabel: '导出当前' },
    { mode: 'all', menuLabel: '导出全部' },
  ])('keeps the visible CompanyId raw field in $mode export columns', async ({ mode, menuLabel }) => {
    mockStandardCrudModule({
      lookupProviders: [
        { key: 'organization.departments', load: async () => [] },
        { key: 'organization.companies', load: async () => [] },
      ],
    });
    const companyListViewDocument = {
      ...createExportablePersonListViewDocument(),
      view: {
        ...createExportablePersonListViewDocument().view,
        columns: [
          { key: 'company', labelKey: 'Column_Company', source: 'model', field: 'CompanyId' },
        ],
      },
    } as typeof personListViewDocument;
    const companyModelDocument = {
      ...personModelDocument,
      Person: {
        ...personModelDocument.Person,
        fields: [
          ...personModelDocument.Person.fields,
          {
            name: 'CompanyId',
            storage: { type: 'guid', nullable: true },
            uiType: 'lookup',
            dataSource: { type: 'provider', key: 'organization.companies' },
            form: {
              create: { mode: 'editable' },
              edit: { mode: 'editable' },
            },
          },
        ],
      },
    } as typeof personModelDocument;
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

    await renderHost(
      { 'PersonListView.Column_Company': '公司' },
      companyListViewDocument,
      '/organization/persons',
      { modelDocument: companyModelDocument },
    );
    await flushProTableRequest();
    await act(async () => {
      const exportButton = Array.from(container.querySelectorAll('button')).find((button) =>
        button.textContent?.replace(/\s+/g, '') === '导出');
      exportButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushProTableRequest();
    await act(async () => {
      const menuItem = Array.from(document.body.querySelectorAll('.ant-dropdown-menu-item')).find((item) =>
        item.textContent?.replace(/\s+/g, '') === menuLabel);
      menuItem?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushProTableRequest();

    expect(fetchMock).toHaveBeenCalledTimes(2);
    const body = JSON.parse(String((fetchMock.mock.calls[1]?.[1] as RequestInit).body));
    expect(body.mode).toBe(mode);
    expect(body.columns).toEqual([
      { key: 'company', field: 'CompanyId', title: '公司' },
    ]);
    expect(JSON.stringify(body.columns)).not.toContain('CompanyName');
    expect(JSON.stringify(body.columns)).not.toContain('CompanyId_DisplayValue');
  });

  it('projects the formal Example user column into the Shell export request as raw authoring', async () => {
    const exampleListCatalog = Object.fromEntries(
      Object.entries(exampleRecordListDictionary.ExampleRecordListView)
        .map(([key, translations]) => [`ExampleRecordListView.${key}`, translations.zhs]),
    );
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: { moduleId: 'example', displayName: 'Example', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'example.record',
            dataAdapter: createStandardCrudRestDataAdapter({
              apiRoute: '/api/example/records',
              fetcher: (input, init) => globalThis.fetch(input, init),
              mapListItem: (value) => ({
                ...(value as Record<string, unknown>),
                id: String((value as Record<string, unknown>).EID),
              }),
              getRowId: (row) => row.id,
            }),
            listViewKey: 'example.record.list',
            detailViewKey: 'example.record.detail',
          },
        ],
      },
    ]);
    Object.defineProperty(URL, 'createObjectURL', {
      configurable: true,
      value: vi.fn(() => 'blob:example-record-export'),
    });
    Object.defineProperty(URL, 'revokeObjectURL', {
      configurable: true,
      value: vi.fn(),
    });
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const fetchMock = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({
        items: [{
          EID: 'ExampleRecord_1',
          ECODE: 'ISSUE137-001',
          ENAME: '用户可读导出',
          ReviewerUserEID: 'User_issue137_raw',
          ReviewerUserEID_DisplayValue: 'Issue 137 Reviewer',
        }],
        total: 1,
        page: 1,
        pageSize: 20,
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }))
      .mockResolvedValueOnce(new Response(new Uint8Array([80, 75]), { status: 200 }));

    container = document.createElement('div');
    document.body.appendChild(container);
    await act(async () => {
      root = createRoot(container);
      root.render(
        <MemoryRouter initialEntries={['/examples/standard-crud/persistent-records']}>
          <LocalizationProvider
            defaultLanguage="zhs"
            supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
            catalogs={{
              zhs: exampleListCatalog,
            }}
          >
            <PlatformQueryProvider scopeKey="issue-137-user-export">
              <RuntimeMetadataProvider
                value={{
                  manifest: { modules: [] },
                  navDocuments: [],
                  modelDocuments: [exampleRecordModelDocument],
                  viewDocuments: [exampleRecordListViewDocument, exampleRecordDetailViewDocument],
                }}
              >
                <StandardCrudListPageHost viewKey="example.record.list" />
              </RuntimeMetadataProvider>
            </PlatformQueryProvider>
          </LocalizationProvider>
        </MemoryRouter>,
      );
    });
    await flushProTableRequest();

    await act(async () => {
      const exportButton = Array.from(container.querySelectorAll('button')).find((button) =>
        button.textContent?.replace(/\s+/g, '') === '导出');
      exportButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushProTableRequest();
    const menuLabels = Array.from(document.body.querySelectorAll('.ant-dropdown-menu-item'))
      .map((item) => item.textContent?.replace(/\s+/g, ''));
    expect(menuLabels).toEqual(expect.arrayContaining(['导出当前', '导出全部']));

    await act(async () => {
      const currentPageItem = Array.from(document.body.querySelectorAll('.ant-dropdown-menu-item')).find((item) =>
        item.textContent?.replace(/\s+/g, '') === '导出当前');
      currentPageItem?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushProTableRequest();

    expect(fetchMock).toHaveBeenCalledTimes(2);
    const [exportUrl, exportInit] = fetchMock.mock.calls[1];
    expect(exportUrl).toBe('/api/example/records/export');
    const body = JSON.parse(String((exportInit as RequestInit).body));
    expect(body.mode).toBe('current');
    expect(body.columns).toEqual([
      { key: 'code', field: 'ECODE', title: '示例编码' },
      { key: 'name', field: 'ENAME', title: '示例名称' },
      { key: 'reviewerUserEid', field: 'ReviewerUserEID', title: '复核账号' },
      { key: 'recordSource', field: 'RecordSource', title: '记录来源' },
    ]);
    expect(JSON.stringify(body.columns)).not.toContain('ReviewerUserEID_DisplayValue');
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
    const searchButton = container.querySelector<HTMLButtonElement>('button[aria-label="搜索"]');
    const resetButton = container.querySelector<HTMLButtonElement>('button[aria-label="重置"]');
    expect(searchButton?.disabled).toBe(true);
    expect(searchButton?.classList.contains('ant-btn-loading')).toBe(false);
    expect(resetButton?.disabled).toBe(true);

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
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockImplementation(async () =>
      new Response(JSON.stringify({ items: [], total: 0, page: 1, pageSize: 20 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost();
    await flushProTableRequest();
    expect(container.textContent).toContain('当前数据集为空');
    expect(container.querySelector('.ant-pagination')?.textContent).toContain('共 0 条');
    expect(container.querySelector('.ant-pagination')?.textContent).toContain('第 1 / 1 页');
    expect(fetchMock).toHaveBeenCalledTimes(1);

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

    await renderHost({}, personListViewDocument, '/organization/persons?page=2');
    await flushProTableRequest();

    expect(container.textContent).toContain('共 41 条');
    expect(container.textContent).toContain('第 2 / 3 页');
    expect(container.textContent).toContain('3');
  });

  it('renders metadata columns actions filters and form fields without legacy registration declarations', async () => {
    const disablePerson = vi.fn();
    mockStandardCrudModule({
      actionHandlers: {
        deletePerson: vi.fn(),
        disablePerson,
        enablePerson: vi.fn(),
      },
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
      { method: 'GET', signal: expect.any(AbortSignal) },
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
    await waitForBodyText('基础信息');

    expect(document.body.textContent).toContain('基础信息');
    expect(document.body.textContent).toContain('组织信息');
    expect(document.body.textContent).toContain('人员编号');
    expect(document.body.textContent).toContain('姓名');
  });

  it('reuses one detail generation when the compatibility route changes a drawer from view to edit', async () => {
    const get = vi.fn().mockResolvedValue({
      normalized: {
        fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentId: null },
        summaries: {},
      },
      value: {
        fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentId: null },
        summaries: {},
      },
    });
    const restAdapter = createStandardCrudRestDataAdapter({
      apiRoute: '/api/organization/persons',
      fetcher: (input, init) => globalThis.fetch(input, init),
      mapListItem: (value) => value as { id: string; name: string },
      getRowId: (row) => row.id,
    });
    mockStandardCrudModule({ dataAdapter: { ...restAdapter, get } });
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [{ id: 'Person_1', PersonNo: 'P001', ENAME: '张三' }],
      total: 1,
      page: 1,
      pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    await renderHost({}, personListViewDocument, '/organization/persons?drawer=view&id=Person_1', { strictMode: true });
    await vi.waitFor(() => expect(get).toHaveBeenCalledTimes(1), { timeout: 3_000 });

    await act(async () => testNavigate?.('/organization/persons?drawer=edit&id=Person_1'));
    await vi.waitFor(
      () => expect(document.body.querySelector<HTMLInputElement>('#ENAME')).not.toBeNull(),
      { timeout: 3_000 },
    );

    expect(get).toHaveBeenCalledTimes(1);
    expect(document.body.querySelector<HTMLInputElement>('#ENAME')?.value).toBe('张三');
  });

  it('retries a failed initial drawer detail read with one new generation', async () => {
    const recoveredDetail = {
      normalized: {
        fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentId: null },
        summaries: {},
      },
      value: {
        fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentId: null },
        summaries: {},
      },
    };
    const get = vi.fn()
      .mockRejectedValueOnce(new Error('DRAWER_DETAIL_READ_FAILED'))
      .mockResolvedValueOnce(recoveredDetail);
    const restAdapter = createStandardCrudRestDataAdapter({
      apiRoute: '/api/organization/persons',
      fetcher: (input, init) => globalThis.fetch(input, init),
      mapListItem: (value) => value as { id: string; name: string },
      getRowId: (row) => row.id,
    });
    mockStandardCrudModule({ dataAdapter: { ...restAdapter, get } });
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [{ id: 'Person_1', PersonNo: 'P001', ENAME: '张三' }],
      total: 1,
      page: 1,
      pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    await renderHost({}, personListViewDocument, '/organization/persons?drawer=view&id=Person_1');
    await vi.waitFor(
      () => expect(document.body.querySelector('.ant-drawer')?.textContent).toContain('DRAWER_DETAIL_READ_FAILED'),
      { timeout: 3_000 },
    );
    const drawer = document.body.querySelector('.ant-drawer')!;
    const retry = Array.from(drawer.querySelectorAll('button'))
      .find((button) => button.textContent?.replace(/\s+/g, '') === '重试')!;

    await act(async () => {
      retry.click();
      retry.click();
    });
    await vi.waitFor(
      () => expect(document.body.querySelector('.ant-drawer')?.textContent).toContain('张三'),
      { timeout: 5_000 },
    );

    expect(get).toHaveBeenCalledTimes(2);
  });

  it('does not offer query retry when drawer form preparation fails', async () => {
    const get = vi.fn().mockResolvedValue({
      normalized: {
        fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentId: null },
        summaries: {},
      },
      value: {
        fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentId: null },
        summaries: {},
      },
    });
    const restAdapter = createStandardCrudRestDataAdapter({
      apiRoute: '/api/organization/persons',
      fetcher: (input, init) => globalThis.fetch(input, init),
      mapListItem: (value) => value as { id: string; name: string },
      getRowId: (row) => row.id,
    });
    mockStandardCrudModule({ dataAdapter: { ...restAdapter, get } });
    const invalidFormModelDocument = {
      ...personModelDocument,
      Person: {
        ...personModelDocument.Person,
        fields: personModelDocument.Person.fields.map((field, index) => index === 0
          ? { ...field, uiType: 'unsupported-test-type' }
          : field),
      },
    } as unknown as typeof personModelDocument;
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [{ id: 'Person_1', PersonNo: 'P001', ENAME: '张三' }],
      total: 1,
      page: 1,
      pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    await renderHost(
      {},
      personListViewDocument,
      '/organization/persons?drawer=view&id=Person_1',
      { modelDocument: invalidFormModelDocument },
    );
    await vi.waitFor(
      () => expect(document.body.querySelector('.ant-drawer')?.textContent).toContain('unsupported uiType'),
      { timeout: 3_000 },
    );

    const drawerButtons = Array.from(document.body.querySelectorAll('.ant-drawer button'));
    expect(drawerButtons.some((button) => button.textContent?.replace(/\s+/g, '') === '重试')).toBe(false);
    expect(get).toHaveBeenCalledTimes(1);
  });

  it('keeps a slow drawer result from replacing the active id after compatibility navigation', async () => {
    interface LoadedPersonDetail {
      normalized: { fields: Record<string, unknown>; summaries: Record<string, unknown> };
      value: { fields: Record<string, unknown>; summaries: Record<string, unknown> };
    }
    const pendingById = new Map<string, {
      resolve: (detail: LoadedPersonDetail) => void;
      signal: AbortSignal;
    }>();
    const get = vi.fn((id: string, context?: { signal?: AbortSignal }) => new Promise<LoadedPersonDetail>((resolve) => {
      pendingById.set(id, { resolve, signal: context!.signal! });
    }));
    const restAdapter = createStandardCrudRestDataAdapter({
      apiRoute: '/api/organization/persons',
      fetcher: (input, init) => globalThis.fetch(input, init),
      mapListItem: (value) => value as { id: string; name: string },
      getRowId: (row) => row.id,
    });
    mockStandardCrudModule({ dataAdapter: { ...restAdapter, get } });
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [
        { id: 'Person_A', PersonNo: 'P001', ENAME: '张三' },
        { id: 'Person_B', PersonNo: 'P002', ENAME: '李四' },
      ],
      total: 2,
      page: 1,
      pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    await renderHost({}, personListViewDocument, '/organization/persons?drawer=view&id=Person_A');
    await vi.waitFor(() => expect(pendingById.has('Person_A')).toBe(true), { timeout: 3_000 });

    await act(async () => testNavigate?.('/organization/persons?drawer=view&id=Person_B'));
    await vi.waitFor(() => expect(pendingById.has('Person_B')).toBe(true), { timeout: 3_000 });
    await act(async () => pendingById.get('Person_B')!.resolve({
      normalized: { fields: { EID: 'Person_B', PersonNo: 'P002', ENAME: '李四' }, summaries: {} },
      value: { fields: { EID: 'Person_B', PersonNo: 'P002', ENAME: '李四' }, summaries: {} },
    }));
    await vi.waitFor(
      () => expect(document.body.querySelector('.ant-drawer')?.textContent).toContain('李四'),
      { timeout: 3_000 },
    );

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 60));
    });
    expect(pendingById.get('Person_A')!.signal.aborted).toBe(true);

    await act(async () => pendingById.get('Person_A')!.resolve({
      normalized: { fields: { EID: 'Person_A', PersonNo: 'P001', ENAME: '张三' }, summaries: {} },
      value: { fields: { EID: 'Person_A', PersonNo: 'P001', ENAME: '张三' }, summaries: {} },
    }));
    expect(document.body.querySelector('.ant-drawer')?.textContent).toContain('李四');
    expect(document.body.querySelector('.ant-drawer')?.textContent).not.toContain('张三');
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
    await waitForBodyText('基础信息');
    const nameInput = document.body.querySelector('input[value="张三"]') as HTMLInputElement;
    await act(async () => {
      const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
      valueSetter?.call(nameInput, '李四');
      nameInput.dispatchEvent(new Event('input', { bubbles: true }));
    });
    const cancelButton = Array.from(document.body.querySelectorAll('button')).find((button) => button.textContent?.replace(/\s+/g, '') === '取消');
    await act(async () => cancelButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushProTableRequest();
    await waitForBodyText('放弃未保存的更改？');

    expect(document.body.textContent).toContain('放弃未保存的更改？');
    expect(document.body.querySelector('.ant-drawer')).not.toBeNull();
  });

  it('uses one declared create position from opening the Drawer through submission without old permissions', async () => {
    const create = vi.fn().mockResolvedValue({
      operation: 'create',
      payload: { receipt: 'opaque-result' },
    });
    mockStandardCrudModule({
      dataAdapter: {
        ...createStandardCrudRestDataAdapter({
          apiRoute: '/api/organization/persons',
          fetcher: (input, init) => globalThis.fetch(input, init),
          mapListItem: (value) => value as { id: string; name: string },
          getRowId: (row) => row.id,
        }),
        create,
      },
      operationResultPresenters: [{
        supports: (result) => result.operation === 'create',
        render: ({ result, close }) => (
          <div>
            <span>{String((result.payload as { receipt?: string }).receipt)}</span>
            <button type="button" onClick={close}>关闭结果</button>
          </div>
        ),
      }],
    });
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [], total: 0, page: 1, pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    const feature = { entry: "Organization/navigation:management/tab:persons", create: "Organization/navigation:management/tab:persons/surface:list.pageActions/action:create" };
    await renderHost({}, personListViewDocument, "/organization/persons", { feature });
    await flushProTableRequest();
    const createButton = Array.from(container.querySelectorAll('button')).find((button) =>
      button.textContent?.replace(/\s+/g, '') === '新建人员');
    await act(async () => createButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('基础信息');
    const personNoInput = document.body.querySelector<HTMLInputElement>('#PersonNo');
    const nameInput = document.body.querySelector<HTMLInputElement>('#ENAME');
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(personNoInput, 'P002');
      personNoInput?.dispatchEvent(new Event('input', { bubbles: true }));
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(nameInput, '李四');
      nameInput?.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await act(async () => {
      const saveButton = Array.from(document.body.querySelectorAll('button')).find((button) =>
        button.textContent?.trim() === '保存并关闭');
      saveButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      saveButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushProTableRequest();

    expect(create).toHaveBeenCalledTimes(1);
    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({ PersonNo: 'P002', ENAME: '李四' }),
      {},
      { featurePosition: feature.create },
    );
    expect(document.body.textContent).toContain('opaque-result');
    const closeResult = Array.from(document.body.querySelectorAll('button')).find((button) => button.textContent === '关闭结果');
    await act(async () => closeResult?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    expect(document.body.textContent).not.toContain('opaque-result');
  });

  it('hands an opaque create result to the registered presenter and blocks duplicate submits', async () => {
    const create = vi.fn().mockResolvedValue({
      operation: 'create',
      payload: { receipt: 'opaque-result' },
    });
    mockStandardCrudModule({
      dataAdapter: {
        ...createStandardCrudRestDataAdapter({
          apiRoute: '/api/organization/persons',
          fetcher: (input, init) => globalThis.fetch(input, init),
          mapListItem: (value) => value as { id: string; name: string },
          getRowId: (row) => row.id,
        }),
        create,
      },
      operationResultPresenters: [{
        supports: (result) => result.operation === 'create',
        render: ({ result, close }) => (
          <div>
            <span>{String((result.payload as { receipt?: string }).receipt)}</span>
            <button type="button" onClick={close}>关闭结果</button>
          </div>
        ),
      }],
    });
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [], total: 0, page: 1, pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    await renderHost();
    await flushProTableRequest();
    const createButton = Array.from(container.querySelectorAll('button')).find((button) =>
      button.textContent?.replace(/\s+/g, '') === '新建人员');
    await act(async () => createButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await waitForBodyText('基础信息');
    const personNoInput = document.body.querySelector<HTMLInputElement>('#PersonNo');
    const nameInput = document.body.querySelector<HTMLInputElement>('#ENAME');
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(personNoInput, 'P002');
      personNoInput?.dispatchEvent(new Event('input', { bubbles: true }));
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(nameInput, '李四');
      nameInput?.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await act(async () => {
      const saveButton = Array.from(document.body.querySelectorAll('button')).find((button) =>
        button.textContent?.trim() === '保存并关闭');
      saveButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      saveButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushProTableRequest();

    expect(create).toHaveBeenCalledTimes(1);
    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({ PersonNo: 'P002', ENAME: '李四' }),
      {},
    );
    expect(document.body.textContent).toContain('opaque-result');
    const closeResult = Array.from(document.body.querySelectorAll('button')).find((button) => button.textContent === '关闭结果');
    await act(async () => closeResult?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    expect(document.body.textContent).not.toContain('opaque-result');
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
    await waitForBodyText('基础信息');
    const nameInput = document.body.querySelector('input[value="张三"]') as HTMLInputElement;
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(nameInput, '李四');
      nameInput.dispatchEvent(new Event('input', { bubbles: true }));
      testNavigate?.(-1);
    });
    await flushProTableRequest();
    await waitForBodyText('放弃未保存的更改？');

    expect(document.body.textContent).toContain('放弃未保存的更改？');
    expect(currentLocationSearch).toContain('drawer=edit');

    const discardButton = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.replace(/\s+/g, '') === '放弃更改');
    await act(async () => discardButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushProTableRequest();

    expect(currentLocationSearch).not.toContain('drawer=');
  });

  it('keeps input and exposes reload after a 409 save conflict', async () => {
    mockStandardCrudModule({ concurrencyRevisionField: 'Revision' });
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify({
        items: [{ id: 'Person_1', PersonNo: 'P001', ENAME: '张三' }], total: 1, page: 1, pageSize: 20,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', Revision: 4 }, summaries: {},
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        code: 'StandardCrud.ConcurrencyConflict',
        message: '记录已经变化',
        fieldErrors: [{ field: 'ENAME', code: 'Organization.Person.NameChanged', message: '姓名已被其他人修改' }],
      }), { status: 409, headers: { 'Content-Type': 'application/problem+json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '王五', Revision: 5 }, summaries: {},
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '李四', Revision: 6 }, summaries: {},
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValue(new Response(JSON.stringify({
        items: [{ id: 'Person_1', PersonNo: 'P001', ENAME: '李四' }], total: 1, page: 1, pageSize: 20,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    await renderHost();
    await flushProTableRequest();
    const editButton = Array.from(container.querySelectorAll('button')).find((button) => button.textContent?.trim() === '编辑');
    await act(async () => editButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushProTableRequest();
    await waitForBodyText('基础信息');
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
    const updateCall = vi.mocked(fetch).mock.calls.find(([, init]) => init?.method === 'PUT');
    expect(updateCall?.[0]).toBe('/api/organization/persons/Person_1');
    expect(JSON.parse(String(updateCall?.[1]?.body))).toMatchObject({ ENAME: '李四', Revision: 4 });

    const reloadButton = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.trim() === '重新加载当前数据');
    await act(async () => reloadButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushProTableRequest();
    expect(document.body.querySelector<HTMLInputElement>('#ENAME')?.value).toBe('王五');

    const latestNameInput = document.body.querySelector<HTMLInputElement>('#ENAME');
    await act(async () => {
      const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
      valueSetter?.call(latestNameInput, '李四');
      latestNameInput?.dispatchEvent(new Event('input', { bubbles: true }));
      const retrySaveButton = Array.from(document.body.querySelectorAll('button')).find((button) =>
        button.textContent?.trim() === '保存修改');
      retrySaveButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushProTableRequest();
    const updateCalls = vi.mocked(fetch).mock.calls.filter(([, init]) => init?.method === 'PUT');
    expect(updateCalls).toHaveLength(2);
    expect(JSON.parse(String(updateCalls[1]?.[1]?.body))).toMatchObject({ ENAME: '李四', Revision: 5 });
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
    await waitForBodyText('应用筛选');

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
  it.each([true, false])('executes a tree-target delete through the shared host, publishing only success=%s', async succeeds => {
    const before = { EID: 'Person_1', ENAME: 'Current', DepartmentId: 'parent' };
    const remove = vi.fn(async () => {
      if (!succeeds) throw new Error('引用阻止删除');
      return { operation: 'action' as const, payload: undefined, change: { operation: 'delete' as const, id: 'Person_1', before } };
    });
    const get = vi.fn(async () => ({ normalized: { fields: before, summaries: {} }, value: { fields: before, summaries: {} } }));
    const list = vi.fn(); const committed = vi.fn(async () => undefined); const close = vi.fn();
    mockStandardCrudModule({ actionHandlers: undefined, dataAdapter: { list, get, getRowId: () => 'Person_1', remove } });
    const deleteView = { ...personListViewDocument, view: { ...personListViewDocument.view, rowActions: [{ key: 'delete', labelKey: 'Action_Delete', standardAction: 'delete', variant: 'danger' }] } } as typeof personListViewDocument;
    await renderHost({}, deleteView, '/organization/persons', { embedded: createEmbeddedHostEnvironment({
      presentation: { kind: 'action-drawer', standardAction: 'delete' },
      input: { recordId: 'Person_1' },
      lifecycle: { reportState: vi.fn(), close },
      sync: { committed },
    }) });
    await waitForBodyText('确认删除当前记录');
    const confirm = [...document.body.querySelectorAll('button')].find(button => button.textContent?.replace(/\s/g, '') === '确认')!;
    await act(async () => confirm.click());
    expect(get).toHaveBeenCalledTimes(1); expect(list).not.toHaveBeenCalled();
    expect(remove).toHaveBeenCalledTimes(1); expect(remove).toHaveBeenCalledWith('Person_1', before);
    expect(committed).toHaveBeenCalledTimes(succeeds ? 1 : 0); expect(close).toHaveBeenCalledTimes(succeeds ? 1 : 0);
    if (!succeeds) expect(document.body.textContent).toContain('引用阻止删除');
  });

});
