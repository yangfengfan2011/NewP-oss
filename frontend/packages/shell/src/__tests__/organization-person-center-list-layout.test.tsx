// @vitest-environment jsdom

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import organizationModule from '../../../../modules/organization/src/index';
import PersonListPage from '../../../../modules/organization/src/pages/PersonListPage';
import personListDictionary from '../../../../../modules/Organization/metadata/dictionaries/PersonListView.dic.json';
import personModelDocument from '../../../../../modules/Organization/metadata/models/Person.model.json';
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
}));

vi.mock('../../../../modules/organization/src/services/organizationService', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../../modules/organization/src/services/organizationService')>();

  return {
    ...actual,
    setPersonLifecycleStatus: serviceMocks.setPersonLifecycleStatus,
    getCompanies: async () => [],
    getDepartments: async () => [],
    getEmploymentStatusLabel: (value: string) => value,
  };
});

function flattenZhsCatalog(dictionary: typeof personListDictionary): Record<string, string> {
  return Object.fromEntries(
    Object.entries(dictionary.PersonListView).map(([key, value]) => [
      `PersonListView.${key}`,
      value.zhs,
    ]),
  );
}

const standardCrudListViewMetadata = {
  manifest: { modules: [] },
  navDocuments: [],
  viewDocuments: [createPersonListViewDocumentWithRowActionVisibility()],
  modelDocuments: [personModelDocument],
};

const personListCatalog = flattenZhsCatalog(personListDictionary);

const organizationModuleWithPersonnelWrites = {
  ...organizationModule,
  standardCrudResources: organizationModule.standardCrudResources?.map((resource) =>
    resource.resourceKey === 'organization.person'
      ? {
          ...resource,
          permissions: [
            'Organization.Person.View',
            'Organization.Person.Create',
            'Organization.Person.Update',
            'Organization.Person.Delete',
          ],
        }
      : resource),
};

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

  function renderStandardCrudListHost() {
    root = createRoot(container);
    root.render(
      <MemoryRouter initialEntries={['/organization/persons']}>
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
          catalogs={{ zhs: personListCatalog }}
        >
          <RuntimeMetadataProvider value={standardCrudListViewMetadata}>
            <StandardCrudListPageHost viewKey="organization.person.list" />
          </RuntimeMetadataProvider>
        </LocalizationProvider>
      </MemoryRouter>,
    );
  }

  beforeEach(() => {
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
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModuleWithPersonnelWrites]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({
          items: [{
            EID: 'person-1',
            PersonNo: 'P0001',
            ENAME: '张三',
            CompanyId: 'company-1',
            CompanyName: '总部',
            DepartmentId: 'department-1',
            DepartmentName: '人力资源部',
            EmploymentStatus: 'FullTime',
            IsEnabled: true,
            Revision: 4,
          }, {
            EID: 'person-2',
            PersonNo: 'P0002',
            ENAME: '李四',
            CompanyId: 'company-1',
            CompanyName: '总部',
            DepartmentId: 'department-2',
            DepartmentName: '研发部',
            EmploymentStatus: 'PartTime',
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
      '/api/organization/persons?fields=EID%2CPersonNo%2CENAME%2CCompanyName%2CDepartmentName%2CEmploymentStatus%2CIsEnabled&pageIndex=1&pageSize=20&sortBy=PersonNo&sortDirection=asc',
      { method: 'GET' },
    );
    expect(container.querySelector('.ant-pro-table')).not.toBeNull();
    expect(container.querySelector('.np-standard-crud-table')).not.toBeNull();
    expect(container.querySelectorAll('.np-crud-workspace-title')).toHaveLength(1);
    expect(container.querySelector('.np-crud-workspace-title')?.textContent).toBe('人员中心');
    expect(container.querySelectorAll('.np-crud-workspace-header .ant-btn')).toHaveLength(1);
    expect(container.querySelector('.np-crud-workspace-header .ant-btn-primary')?.textContent).toContain('新建人员');
    expect(container.querySelectorAll('.ant-btn-primary')).toHaveLength(1);
    expect(container.querySelector('.np-crud-workspace-filter.ant-card')).not.toBeNull();
    expect(container.querySelector('.np-crud-workspace-content.ant-card')).not.toBeNull();
    expect(container.querySelector('.np-crud-table-section-fill')).toBeNull();
    expect(container.querySelector('.ant-table')).not.toBeNull();
    expect(container.querySelector('.ant-pagination')).toBeNull();
    expect(container.querySelector('.np-search-table-footer')).toBeNull();
    expect(container.querySelector('.ant-table-selection-column')).toBeNull();
    expect(container.querySelector('.np-table-column-resize-handle')).toBeNull();
    expect(container.querySelector('.np-table-row-icon')).toBeNull();
    expect(container.querySelector('.np-table-row-number-cell')).toBeNull();
    const tableHeaderTexts = Array.from(container.querySelectorAll('.ant-table-thead th')).map((node) =>
      node.textContent?.replace(/\s+/g, ''),
    );
    expect(tableHeaderTexts).not.toContain('序号');
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
    expect(settingColumnTexts.filter(Boolean)).toEqual(['人员编号', '姓名', '公司', '部门', '在职状态', '启用状态', '操作']);
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
    expect(container.textContent).toContain('启用');
    expect(container.textContent).not.toContain('PartTime');
    expect(container.textContent).not.toContain('FullTime');
    expect(container.textContent).not.toContain('true');
    expect(container.querySelectorAll('.ant-tag')).not.toHaveLength(0);
    const bodyRows = Array.from(container.querySelectorAll('.ant-table-tbody > tr.ant-table-row'));
    expect(bodyRows[0]?.classList.contains('np-table-row-odd')).toBe(false);
    expect(bodyRows[1]?.classList.contains('np-table-row-even')).toBe(false);
    const firstActionCell = bodyRows[0]?.querySelector('td:last-child');
    expect(firstActionCell?.textContent).toContain('查看');
    expect(firstActionCell?.textContent).toContain('编辑');
    expect(firstActionCell?.textContent).toContain('更多');
    expect(firstActionCell?.textContent).not.toContain('删除');
    expect(firstActionCell?.textContent).not.toContain('停用');
    expect(firstActionCell?.textContent).not.toContain('启用');
    expect(firstActionCell?.querySelectorAll('.np-row-action-button')).toHaveLength(2);
    expect(firstActionCell?.querySelector('.np-row-actions-more')).not.toBeNull();
    expect(bodyRows[0]?.querySelector('.np-table-row-view-button')).toBeNull();
    await act(async () => {
      firstActionCell?.querySelector('.np-row-actions-more')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushProTableRequest();
    const overflowMenu = document.body.querySelector('.np-row-actions-overflow-menu');
    expect(overflowMenu?.textContent).toContain('删除');
    expect(overflowMenu?.textContent).toContain('停用');
    expect(overflowMenu?.textContent).not.toContain('启用');
    expect(container.querySelector('a[href="/organization/persons/person-1"]')).toBeNull();
    expect(countTextOccurrences(container.textContent ?? '', '共 2 条')).toBe(0);
    expect(countTextOccurrences(container.textContent ?? '', '新建人员')).toBe(1);
  });

  it('registers person center as a standard CRUD frontend resource', () => {
    const resource = organizationModule.standardCrudResources?.find((item) => item.resourceKey === 'organization.person');

    expect(resource).toMatchObject({
      resourceKey: 'organization.person',
      apiRoute: '/api/organization/persons',
      listViewKey: 'organization.person.list',
      detailViewKey: 'organization.person.detail',
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

  it('shows a row action error when disabling a person fails', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModuleWithPersonnelWrites]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({
          items: [{
            EID: 'person-1',
            PersonNo: 'P0001',
            ENAME: '张三',
            CompanyId: 'company-1',
            CompanyName: '总部',
            DepartmentId: 'department-1',
            DepartmentName: '人力资源部',
            EmploymentStatus: 'FullTime',
            IsEnabled: true,
            Revision: 4,
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

    await act(async () => {
      container.querySelector('.np-row-actions-more')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    const disableMenuItem = Array.from(document.body.querySelectorAll('.np-row-action-menu-button')).find(
      (item) => item.textContent?.includes('停用'),
    );
    await act(async () => {
      disableMenuItem?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    const confirmButton = Array.from(document.body.querySelectorAll('button')).find(
      (button) => button.textContent?.replace(/\s+/g, '') === '确认',
    );
    await act(async () => {
      confirmButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    expect(serviceMocks.setPersonLifecycleStatus).toHaveBeenCalledWith('person-1', {
      isEnabled: false,
      expectedRevision: 4,
      reason: '人员中心停用人员',
    });
    expect(container.textContent).toContain('停用人员失败');
  });
});
