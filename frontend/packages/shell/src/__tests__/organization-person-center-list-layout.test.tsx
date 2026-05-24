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
  disablePerson: vi.fn(),
  enablePerson: vi.fn(),
}));

vi.mock('../../../../modules/organization/src/services/organizationService', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../../modules/organization/src/services/organizationService')>();

  return {
    ...actual,
    disablePerson: serviceMocks.disablePerson,
    enablePerson: serviceMocks.enablePerson,
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
  viewDocuments: [personListViewDocument],
  modelDocuments: [personModelDocument],
};

const personListCatalog = flattenZhsCatalog(personListDictionary);

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
        matches: false,
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
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
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

    expect(fetch).toHaveBeenCalledWith('/api/organization/persons?pageIndex=1&pageSize=20', { method: 'GET' });
    expect(container.querySelector('.ant-pro-table')).not.toBeNull();
    expect(container.querySelector('.np-standard-crud-table')).not.toBeNull();
    expect(container.querySelector('.np-crud-workspace-header .ant-btn-primary')?.textContent).toContain('新建人员');
    expect(container.querySelector('.np-crud-workspace-filter .ant-btn-primary')?.textContent?.replace(/\s+/g, '')).toContain('搜索');
    expect(container.querySelector('.np-crud-table-section-fill')).not.toBeNull();
    expect(container.querySelector('.ant-table')).not.toBeNull();
    expect(container.querySelector('.ant-pagination')).not.toBeNull();
    expect(container.querySelector('.np-search-table-footer')).not.toBeNull();
    expect(container.querySelector('.ant-table-selection-column')).not.toBeNull();
    expect(container.querySelector('.ant-table-column-sorters')).not.toBeNull();
    expect(container.querySelector('.np-table-column-resize-handle')).not.toBeNull();
    expect(container.querySelector('.np-table-row-icon')).not.toBeNull();
    const tableHeaderTexts = Array.from(container.querySelectorAll('.ant-table-thead th')).map((node) =>
      node.textContent?.replace(/\s+/g, ''),
    );
    expect(tableHeaderTexts).not.toContain('序号');
    expect(tableHeaderTexts).not.toContain('图标');
    const tableColumnWidths = Array.from(container.querySelectorAll('col')).map(
      (node) => (node as HTMLTableColElement).style.width,
    );
    expect(tableColumnWidths).toEqual(expect.arrayContaining(['40px', '40px']));
    expect(container.textContent).not.toContain('人员中心');
    expect(container.textContent).not.toContain('人员列表维护');
    expect(container.querySelector('.np-crud-workspace-filter input.np-filter-control')).not.toBeNull();
    expect(container.querySelector<HTMLInputElement>('.np-crud-workspace-filter input.np-filter-control')?.placeholder).toBe('按工号 / 姓名搜索');
    expect(container.querySelector('.ant-select')).not.toBeNull();
    expect(container.textContent).not.toContain('全部公司');
    expect(container.textContent).not.toContain('公司');
    const filterSelects = Array.from(container.querySelectorAll('.np-person-filter-grid .ant-select'));
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
    expect(container.textContent).toContain('兼职');
    expect(container.textContent).toContain('正式');
    expect(container.textContent).toContain('启用');
    expect(container.textContent).not.toContain('PartTime');
    expect(container.textContent).not.toContain('FullTime');
    expect(container.textContent).not.toContain('true');
    const bodyRows = Array.from(container.querySelectorAll('.ant-table-tbody > tr'));
    expect(bodyRows[0]?.classList.contains('np-table-row-odd')).toBe(true);
    expect(bodyRows[1]?.classList.contains('np-table-row-even')).toBe(true);
    const firstActionCell = bodyRows[0]?.querySelector('td:last-child');
    expect(firstActionCell?.textContent).not.toContain('查看');
    expect(firstActionCell?.textContent).toContain('编辑');
    expect(firstActionCell?.textContent).toContain('删除');
    expect(firstActionCell?.textContent).toContain('更多');
    expect(firstActionCell?.textContent).not.toContain('停用');
    expect(firstActionCell?.textContent).not.toContain('启用');
    expect(firstActionCell?.querySelectorAll('.np-row-action-button')).toHaveLength(2);
    expect(firstActionCell?.querySelector('.np-row-actions-more')).not.toBeNull();
    expect(bodyRows[0]?.querySelector('.np-table-row-view-button')).not.toBeNull();
    await act(async () => {
      firstActionCell?.querySelector('.np-row-actions-more')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    expect(document.body.textContent).toContain('删除');
    expect(document.body.textContent).toContain('停用');
    expect(document.body.textContent).toContain('启用');
    expect(container.querySelector('a[href="/organization/persons/person-1"]')).toBeNull();
    expect(countTextOccurrences(container.textContent ?? '', '共 2 条')).toBe(1);
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
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
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
          }],
          total: 1,
          page: 1,
          pageSize: 20,
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    );
    serviceMocks.disablePerson.mockRejectedValue(new Error('停用人员失败'));

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

    expect(serviceMocks.disablePerson).toHaveBeenCalledWith('person-1');
    expect(container.textContent).toContain('停用人员失败');
  });
});
