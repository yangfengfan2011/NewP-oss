// @vitest-environment jsdom

import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import baseOrganizationModule from '../../../../modules/organization/src/index';
import personListDictionary from '../../../../../modules/Organization/metadata/dictionaries/PersonListView.dic.json';
import personModelDocument from '../../../../../modules/Organization/metadata/models/Person.model.json';
import personListViewDocument from '../../../../../modules/Organization/metadata/views/PersonList.view.json';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

interface Deferred<T> {
  promise: Promise<T>;
  resolve: (value: T | PromiseLike<T>) => void;
  reject: (reason?: unknown) => void;
}

const organizationServiceMocks = vi.hoisted(() => ({
  getCompanies: vi.fn(async () => []),
  getDepartments: vi.fn(async () => []),
  getPositions: vi.fn(async () => []),
}));

function flattenZhsCatalog(dictionary: typeof personListDictionary): Record<string, string> {
  return Object.fromEntries(
    Object.entries(dictionary.PersonListView).map(([key, value]) => [
      `PersonListView.${key}`,
      value.zhs,
    ]),
  );
}

const personListCatalog = {
  ...flattenZhsCatalog(personListDictionary),
};

// 该样板验证管理员写入交互；显式补齐授权，避免把资源路由的 View 门禁误当成当前主体权限。
const organizationModule = {
  ...baseOrganizationModule,
  standardCrudResources: baseOrganizationModule.standardCrudResources?.map((resource) =>
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
let currentLocationSearch = '';

vi.mock('../../../../modules/organization/src/services/organizationService', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../../modules/organization/src/services/organizationService')>();

  return {
    ...actual,
    getCompanies: organizationServiceMocks.getCompanies,
    getDepartments: organizationServiceMocks.getDepartments,
    getPositions: organizationServiceMocks.getPositions,
  };
});

async function flushAsync() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

function findAntdButtonByText(container: ParentNode, text: string): HTMLElement | undefined {
  return Array.from(container.querySelectorAll<HTMLElement>('button, a, [role="button"], [role="menuitem"]')).find(
    (button) => button.textContent?.replace(/\s+/g, '').includes(text.replace(/\s+/g, '')),
  );
}

function findAntdButtonsByText(container: ParentNode, text: string): HTMLElement[] {
  return Array.from(container.querySelectorAll<HTMLElement>('button, a, [role="button"], [role="menuitem"]')).filter(
    (button) => button.textContent?.replace(/\s+/g, '').includes(text.replace(/\s+/g, '')),
  );
}

function findTableRowByText(container: ParentNode, text: string): HTMLTableRowElement | null {
  return Array.from(container.querySelectorAll('tbody tr')).find((row) => row.textContent?.includes(text)) ?? null;
}

function findRowActionButton(container: ParentNode, rowText: string, buttonText: string): HTMLElement | undefined {
  const row = findTableRowByText(container, rowText);
  if (!row) {
    return undefined;
  }

  return findAntdButtonByText(row, buttonText);
}

async function findOverflowRowActionButton(container: ParentNode, rowText: string, buttonText: string): Promise<HTMLElement | undefined> {
  const directButton = findRowActionButton(container, rowText, buttonText);
  if (directButton) {
    return directButton;
  }

  const row = findTableRowByText(container, rowText);
  const moreButton = row ? findAntdButtonByText(row, '更多') : undefined;
  if (!moreButton) {
    return undefined;
  }

  await act(async () => {
    moreButton.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  });
  await flushAsync();

  const visibleDropdown = Array.from(document.body.querySelectorAll<HTMLElement>('.ant-dropdown'))
    .filter((dropdown) => !dropdown.classList.contains('ant-dropdown-hidden'))
    .at(-1);
  return findAntdButtonByText(visibleDropdown ?? document.body, buttonText);
}

async function clickPopconfirmButton(label: '确认' | '取消') {
  const button = Array.from(document.body.querySelectorAll<HTMLElement>('button')).find(
    (item) => item.textContent?.replace(/\s+/g, '') === label,
  );

  expect(button).toBeDefined();

  await act(async () => {
    button?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  });
}

function findInputByLabel(container: ParentNode, labelText: string): HTMLInputElement | null {
  const label = Array.from(container.querySelectorAll('label')).find((candidate) => candidate.textContent?.includes(labelText));
  if (!label) {
    return null;
  }

  const fieldId = label.getAttribute('for');
  if (!fieldId) {
    const nestedInput = label.querySelector('input');
    return nestedInput instanceof HTMLInputElement ? nestedInput : null;
  }

  const ownerDocument = container instanceof Document ? container : container.ownerDocument;
  const field = ownerDocument?.getElementById(fieldId);
  return field instanceof HTMLInputElement ? field : null;
}

function createDeferred<T>(): Deferred<T> {
  let resolve!: Deferred<T>['resolve'];
  let reject!: Deferred<T>['reject'];
  const promise = new Promise<T>((innerResolve, innerReject) => {
    resolve = innerResolve;
    reject = innerReject;
  });

  return { promise, resolve, reject };
}

function setReactInputValue(input: HTMLInputElement, value: string) {
  const descriptor = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
  descriptor?.set?.call(input, value);
  input.dispatchEvent(new Event('input', { bubbles: true }));
  input.dispatchEvent(new Event('change', { bubbles: true }));
}

function renderHost(
  container: HTMLElement,
  initialEntries = ['/organization/persons'],
  viewDocument: typeof personListViewDocument = personListViewDocument,
) {
  const root = createRoot(container);
  root.render(
    <MemoryRouter initialEntries={initialEntries}>
      <LocationProbe />
      <LocalizationProvider
        defaultLanguage="zhs"
        supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
        catalogs={{ zhs: personListCatalog }}
      >
        <RuntimeMetadataProvider
          value={{
            manifest: { modules: [] },
            navDocuments: [],
            viewDocuments: [viewDocument],
            modelDocuments: [personModelDocument],
          }}
        >
          <StandardCrudListPageHost viewKey="organization.person.list" />
        </RuntimeMetadataProvider>
      </LocalizationProvider>
    </MemoryRouter>,
  );
  return root;
}

function LocationProbe() {
  const location = useLocation();
  currentLocationSearch = location.search;
  return null;
}

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

describe('人员中心 CRUD Pro 样板', () => {
  let container: HTMLDivElement;
  let root: Root | null = null;

  beforeEach(() => {
    organizationServiceMocks.getCompanies.mockResolvedValue([]);
    organizationServiceMocks.getDepartments.mockResolvedValue([]);
    organizationServiceMocks.getPositions.mockResolvedValue([]);
    currentLocationSearch = '';

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
    if (root) {
      await act(async () => root?.unmount());
    }
    container?.remove();
  });

  it('新建、查看、编辑都在右侧抽屉中打开', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.includes('/person-1')) {
        return new Response(JSON.stringify({
          fields: {
            EID: 'person-1',
            PersonNo: 'P0001',
            ENAME: '张三',
            IsEnabled: true,
          },
          summaries: {},
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }

      return new Response(JSON.stringify({
        items: [{ EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', EmploymentStatus: 'PartTime', IsEnabled: true }],
        total: 1,
        page: 1,
        pageSize: 20,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = renderHost(container);
    });
    await flushAsync();

    expect(container.textContent).toContain('人员编号');
    expect(container.textContent).toContain('姓名');
    expect(container.textContent).toContain('部门');
    expect(container.textContent).toContain('公司');
    expect(container.querySelector<HTMLInputElement>('.np-crud-workspace-filter input.np-filter-control')?.placeholder).toBe('按工号 / 姓名搜索');
    expect(container.textContent).toContain('兼职');
    expect(container.textContent).toContain('启用');
    expect(container.textContent).not.toContain('PartTime');
    expect(container.textContent).not.toContain('true');
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('fields='),
      { method: 'GET' },
    );
    expect(decodeURIComponent(String((globalThis.fetch as Mock).mock.calls[0][0]))).toContain(
      'fields=EID,PersonNo,ENAME,CompanyName,DepartmentName,EmploymentStatus,IsEnabled',
    );

    const filterLabels = Array.from(container.querySelectorAll('.np-crud-workspace-filter label'));
    expect(filterLabels.length).toBeGreaterThan(0);
    expect(filterLabels.every((label) => label.classList.contains('np-filter-field'))).toBe(true);
    expect(filterLabels.some((label) => label.classList.contains('np-filter-control'))).toBe(false);
    expect(container.querySelector('.np-crud-workspace-filter input.np-filter-control')).not.toBeNull();

    const createButton = findAntdButtonByText(container, '新建');
    createButton?.focus();
    await act(async () => createButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    expect(document.body.querySelector('.ant-drawer-title')?.textContent).toContain('新建人员');
    expect(document.body.textContent).toContain('新建人员');
    expect(document.body.querySelector('.ant-drawer')).not.toBeNull();
    expect(findInputByLabel(document.body, '人员编号')?.placeholder).toBe('请输入人员编号');
    expect(findInputByLabel(document.body, '姓名')?.placeholder).toBe('请输入姓名');

    const closeButton = document.body.querySelector('.ant-drawer-close') as HTMLButtonElement;
    await act(async () => closeButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();
    expect(document.body.textContent).not.toContain('编辑人员');
    expect(document.body.querySelector('.ant-drawer')).toBeNull();

    const viewButton = await findOverflowRowActionButton(container, '张三', '查看');
    expect(viewButton).toBeDefined();
    await act(async () => viewButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();
    expect(document.body.textContent).toContain('查看人员');
    expect(document.body.querySelector('.ant-drawer-title')?.textContent).toContain('查看人员');
    expect(document.body.querySelector('.ant-descriptions')?.textContent).toContain('张三');

    const editButton = findAntdButtonByText(document.body, '编辑');
    expect(editButton?.textContent?.replace(/\s+/g, '')).toContain('编辑');
    await act(async () => editButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();
    expect(document.body.textContent).toContain('编辑人员');
    expect(document.body.querySelector('.ant-drawer-title')?.textContent).toContain('编辑人员');
  });

  it('冷启动后直接查看人员时使用详情中的公司和部门名称', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.includes('/person-1')) {
        return new Response(JSON.stringify({
          fields: {
            EID: 'person-1',
            PersonNo: 'P0001',
            ENAME: '张三',
            CompanyId: 'Company_1',
            CompanyName: '示例公司',
            DepartmentId: 'Department_1',
            DepartmentName: '研发部',
            EmploymentStatus: 'FullTime',
            IsEnabled: true,
          },
          summaries: {},
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }

      return new Response(JSON.stringify({
        items: [{
          EID: 'person-1',
          PersonNo: 'P0001',
          ENAME: '张三',
          CompanyName: '示例公司',
          DepartmentName: '研发部',
          EmploymentStatus: 'FullTime',
          IsEnabled: true,
        }],
        total: 1,
        page: 1,
        pageSize: 20,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = renderHost(container);
    });
    await flushAsync();

    const viewButton = await findOverflowRowActionButton(container, '张三', '查看');
    await act(async () => viewButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();

    const descriptionsText = Array.from(document.body.querySelectorAll('.ant-descriptions'))
      .map((section) => section.textContent ?? '')
      .join('');
    expect(descriptionsText).toContain('示例公司');
    expect(descriptionsText).not.toContain('Company_1');
    expect(descriptionsText).toContain('研发部');
    expect(descriptionsText).not.toContain('Department_1');
  });

  it('高级筛选通过右侧抽屉展示并复用本地化标签', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [{ EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true }],
      total: 1,
      page: 1,
      pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = renderHost(container);
    });
    await flushAsync();

    const advancedFilterButton = findAntdButtonByText(container, '高级筛选');
    expect(advancedFilterButton).toBeDefined();

    await act(async () => advancedFilterButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();

    expect(document.body.querySelector('.ant-drawer-title')?.textContent).toContain('高级筛选');
    expect(document.body.querySelector('.ant-drawer-right')).not.toBeNull();
    expect(document.body.querySelector('.ant-popover')).toBeNull();
    expect(document.body.textContent).toContain('组织信息');
    expect(document.body.textContent).toContain('部门');
    expect(document.body.textContent).toContain('岗位');
    expect(document.body.textContent).toContain('启用状态');
    expect(document.body.textContent).not.toContain('AdvancedFilter_Organization');
    expect(document.body.textContent).not.toContain('Query_Department');
  });

  it('projection 字段缺值时仍渲染列表行', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({
        items: [{ EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentId: 'Dept_1' }],
        total: 1,
        page: 1,
        pageSize: 20,
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = renderHost(container);
    });
    await flushAsync();

    expect(container.textContent).toContain('张三');
    expect(container.textContent).not.toContain('Cannot read');
  });

  it('平台新建按钮不依赖资源 create handler', async () => {
    const resource = organizationModule.standardCrudResources?.[0];
    if (!resource) {
      throw new Error('未找到人员标准 CRUD 资源。');
    }

    const sampleModule = {
      ...organizationModule,
      standardCrudResources: [{
        ...resource,
        actionHandlers: Object.fromEntries(
          Object.entries(resource.actionHandlers ?? {}).filter(([key]) => key !== 'create'),
        ),
      }],
    };
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([sampleModule]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [{ EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true }],
      total: 1,
      page: 1,
      pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = renderHost(container);
    });
    await flushAsync();

    const createButton = findAntdButtonByText(container, '新建');
    expect(createButton).toBeDefined();

    await act(async () => createButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();

    expect(document.body.querySelector('.ant-drawer-title')?.textContent).toContain('新建人员');
  });

  it('乱序详情响应不会回写当前编辑抽屉的数据', async () => {
    const person1Detail = createDeferred<Response>();
    const person2Detail = createDeferred<Response>();

    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.includes('/person-1')) {
        return person1Detail.promise;
      }
      if (url.includes('/person-2')) {
        return person2Detail.promise;
      }

      return new Response(JSON.stringify({
        items: [
          { EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true },
          { EID: 'person-2', PersonNo: 'P0002', ENAME: '李四', IsEnabled: true },
        ],
        total: 2,
        page: 1,
        pageSize: 20,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = renderHost(container);
    });
    await flushAsync();

    const editButtons = findAntdButtonsByText(container, '编辑');
    expect(editButtons).toHaveLength(2);

    await act(async () => {
      editButtons[0]?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      editButtons[1]?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    person2Detail.resolve(new Response(JSON.stringify({
      fields: {
        EID: 'person-2',
        PersonNo: 'P0002',
        ENAME: '李四',
        IsEnabled: true,
      },
      summaries: {},
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    await flushAsync();

    expect(findInputByLabel(document.body, '姓名')?.value).toBe('李四');

    person1Detail.resolve(new Response(JSON.stringify({
      fields: {
        EID: 'person-1',
        PersonNo: 'P0001',
        ENAME: '张三',
        IsEnabled: true,
      },
      summaries: {},
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    await flushAsync();

    expect(document.body.querySelector('.ant-drawer-title')?.textContent).toContain('编辑人员');
    expect(findInputByLabel(document.body, '姓名')?.value).toBe('李四');
  });

  it('编辑详情加载失败时保留详情抽屉错误而不是打开空编辑表单', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.includes('/person-1')) {
        throw new Error('详情加载失败，请稍后重试');
      }

      return new Response(JSON.stringify({
        items: [{ EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true }],
        total: 1,
        page: 1,
        pageSize: 20,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = renderHost(container);
    });
    await flushAsync();

    const editButton = findAntdButtonByText(container, '编辑');
    await act(async () => editButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();

    expect(document.body.querySelector('.ant-drawer-title')?.textContent).toContain('查看人员');
    expect(document.body.textContent).toContain('详情加载失败，请稍后重试');
    expect(document.body.textContent).not.toContain('保存修改');
    expect(findInputByLabel(document.body, '姓名')).toBeNull();
  });

  it('新建抽屉中的工号字段可编辑', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [{ EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true }],
      total: 1,
      page: 1,
      pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = renderHost(container);
    });
    await flushAsync();

    const createButton = findAntdButtonByText(container, '新建');
    await act(async () => createButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();

    const personNoInput = findInputByLabel(document.body, '人员编号');
    expect(document.body.querySelector('.ant-drawer-title')?.textContent).toContain('新建人员');
    expect(personNoInput).not.toBeNull();
    expect(personNoInput?.readOnly).toBe(false);
  });

  it('通过路由兼容查询参数打开新建抽屉', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [],
      total: 0,
      page: 1,
      pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = renderHost(container, ['/organization/persons?drawer=create']);
    });
    await flushAsync();

    expect(document.body.textContent).toContain('新建人员');
    expect(document.body.querySelector('.ant-drawer')).not.toBeNull();
  });

  it('用户打开和关闭抽屉时同步清理 URL 兼容参数', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [],
      total: 0,
      page: 1,
      pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = renderHost(container, ['/organization/persons?tab=active']);
    });
    await flushAsync();

    const createButton = findAntdButtonByText(container, '新建');
    createButton?.focus();
    await act(async () => createButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();

    expect(currentLocationSearch).toContain('tab=active');
    expect(currentLocationSearch).toContain('drawer=create');

    const closeButton = document.body.querySelector('.ant-drawer-close') as HTMLButtonElement;
    await act(async () => closeButton.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();

    expect(currentLocationSearch).toBe('?tab=active');
    expect(document.activeElement).toBe(createButton);
  });

  it('人员中心声明删除时按危险确认动作渲染', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [{ EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true }],
      total: 1,
      page: 1,
      pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = renderHost(container);
    });
    await flushAsync();

    const deleteButton = await findOverflowRowActionButton(container, '张三', '删除');
    expect(deleteButton).toBeDefined();

    await act(async () => deleteButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();

    expect(document.body.textContent).toContain('确认删除该人员吗？');
  });

  it('按人员启用状态展示启用停用，并将危险动作收纳到更多', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [
        { EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true },
        { EID: 'person-2', PersonNo: 'P0002', ENAME: '李四', IsEnabled: false },
      ],
      total: 2,
      page: 1,
      pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = renderHost(container, ['/organization/persons'], createPersonListViewDocumentWithRowActionVisibility());
    });
    await flushAsync();

    const enabledRow = findTableRowByText(container, '张三');
    const disabledRow = findTableRowByText(container, '李四');
    const enabledActionCell = enabledRow?.querySelector('td:last-child');
    const disabledActionCell = disabledRow?.querySelector('td:last-child');
    expect(enabledActionCell?.textContent).toContain('编辑');
    expect(enabledActionCell?.textContent).toContain('更多');
    expect(enabledActionCell?.textContent).not.toContain('删除');
    expect(enabledActionCell?.textContent).not.toContain('停用');
    expect(enabledActionCell?.textContent).not.toContain('启用');
    expect(disabledActionCell?.textContent).toContain('编辑');
    expect(disabledActionCell?.textContent).not.toContain('启用');
    expect(disabledActionCell?.textContent).toContain('更多');
    expect(disabledActionCell?.textContent).not.toContain('停用');

    await act(async () => {
      findAntdButtonByText(enabledRow ?? container, '更多')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushAsync();

    const enabledOverflowMenu = document.body.querySelector('.ant-dropdown-menu');
    expect(enabledOverflowMenu?.textContent).toContain('删除');
    expect(enabledOverflowMenu?.textContent).toContain('停用');
    expect(enabledOverflowMenu?.textContent).not.toContain('启用');

    await act(async () => {
      findAntdButtonByText(disabledRow ?? container, '更多')?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushAsync();

    const disabledOverflowMenu = Array.from(document.body.querySelectorAll('.ant-dropdown-menu'))
      .find((menu) => menu.textContent?.includes('启用'));
    expect(disabledOverflowMenu?.textContent).toContain('删除');
    expect(disabledOverflowMenu?.textContent).toContain('启用');
    expect(disabledOverflowMenu?.textContent).not.toContain('停用');
  });

  it('metadata 未声明删除时不展示删除入口', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [{ EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true }],
      total: 1,
      page: 1,
      pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = renderHost(container, ['/organization/persons'], {
        ...personListViewDocument,
        view: {
          ...personListViewDocument.view,
          rowActions: personListViewDocument.view.rowActions.filter((action) => action.standardAction !== 'delete'),
        },
      });
    });
    await flushAsync();

    expect(container.textContent).not.toContain('删除');
    expect(container.textContent).toContain('启用');

    const moreButton = findAntdButtonByText(container, '更多');
    expect(moreButton).toBeDefined();
    await act(async () => {
      moreButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushAsync();

    const overflowMenu = document.body.querySelector('.ant-dropdown-menu');
    expect(overflowMenu?.textContent).toContain('停用');
    expect(overflowMenu?.textContent).not.toContain('删除');
  });

  it('delete handler 即使不主动 refresh，确认删除成功后 Host 也会刷新一次列表', async () => {
    const resource = organizationModule.standardCrudResources?.[0];
    if (!resource) {
      throw new Error('未找到人员标准 CRUD 资源。');
    }

    const deleteHandler = vi.fn(async () => {});
    const sampleModule = {
      ...organizationModule,
      standardCrudResources: [{
        ...resource,
        actionHandlers: {
          ...(resource.actionHandlers ?? {}),
          delete: deleteHandler,
        },
      }],
    };
    let listRequestCount = 0;
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([sampleModule]);
    vi.spyOn(globalThis, 'fetch').mockImplementation(async () => {
      listRequestCount += 1;
      return new Response(JSON.stringify({
        items: [{ EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true }],
        total: 1,
        page: 1,
        pageSize: 20,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = renderHost(container);
    });
    await flushAsync();

    const deleteButton = await findOverflowRowActionButton(container, '张三', '删除');
    expect(deleteButton).toBeDefined();

    await act(async () => deleteButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();
    await clickPopconfirmButton('确认');
    await flushAsync();

    expect(deleteHandler).toHaveBeenCalledTimes(1);
    expect(listRequestCount).toBe(2);
  });

  it('删除失败只在对应行展示错误，不刷新列表，且保留当前行详情抽屉', async () => {
    const resource = organizationModule.standardCrudResources?.[0];
    if (!resource) {
      throw new Error('未找到人员标准 CRUD 资源。');
    }

    const deleteHandler = vi.fn(async () => {
      throw new Error('删除张三失败');
    });
    const sampleModule = {
      ...organizationModule,
      standardCrudResources: [{
        ...resource,
        actionHandlers: {
          ...(resource.actionHandlers ?? {}),
          delete: deleteHandler,
        },
      }],
    };
    let listRequestCount = 0;
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([sampleModule]);
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.includes('/person-1')) {
        return new Response(JSON.stringify({
          fields: {
            EID: 'person-1',
            PersonNo: 'P0001',
            ENAME: '张三',
            IsEnabled: true,
          },
          summaries: {},
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }

      listRequestCount += 1;
      return new Response(JSON.stringify({
        items: [
          { EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true },
          { EID: 'person-2', PersonNo: 'P0002', ENAME: '李四', IsEnabled: true },
        ],
        total: 2,
        page: 1,
        pageSize: 20,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = renderHost(container);
    });
    await flushAsync();

    const viewButton = await findOverflowRowActionButton(container, '张三', '查看');
    expect(viewButton).toBeDefined();
    await act(async () => viewButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();

    expect(document.body.querySelector('.ant-drawer-title')?.textContent).toContain('查看人员');

    const deleteButton = await findOverflowRowActionButton(container, '张三', '删除');
    expect(deleteButton).toBeDefined();
    await act(async () => deleteButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();
    await clickPopconfirmButton('确认');
    await flushAsync();

    const firstRow = findTableRowByText(container, '张三');
    const secondRow = findTableRowByText(container, '李四');
    expect(firstRow?.textContent).toContain('删除张三失败');
    expect(secondRow?.textContent).not.toContain('删除张三失败');
    expect(Array.from(container.querySelectorAll('[role="alert"]')).filter((item) => item.textContent?.includes('删除张三失败'))).toHaveLength(1);
    expect(listRequestCount).toBe(1);
    expect(document.body.querySelector('.ant-drawer-title')?.textContent).toContain('查看人员');
    expect(document.body.textContent).toContain('查看人员');
  });

  it('删除失败时展示后端透传的业务错误', async () => {
    const resource = organizationModule.standardCrudResources?.[0];
    if (!resource) {
      throw new Error('未找到人员标准 CRUD 资源。');
    }

    const deleteHandler = vi.fn(async () => {
      throw new Error('当前人员已绑定账号，不能删除。');
    });
    const sampleModule = {
      ...organizationModule,
      standardCrudResources: [{
        ...resource,
        actionHandlers: {
          ...(resource.actionHandlers ?? {}),
          delete: deleteHandler,
        },
      }],
    };
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([sampleModule]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [{ EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true }],
      total: 1,
      page: 1,
      pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = renderHost(container);
    });
    await flushAsync();

    const deleteButton = await findOverflowRowActionButton(container, '张三', '删除');
    expect(deleteButton).toBeDefined();
    await act(async () => deleteButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();
    await clickPopconfirmButton('确认');
    await flushAsync();

    expect(container.textContent).toContain('当前人员已绑定账号，不能删除。');
  });

  it('删除等待期间打开其他记录详情后，成功回调不会误关新抽屉', async () => {
    const resource = organizationModule.standardCrudResources?.[0];
    if (!resource) {
      throw new Error('未找到人员标准 CRUD 资源。');
    }

    const deferredDelete = createDeferred<void>();
    const deleteHandler = vi.fn(() => deferredDelete.promise);
    const sampleModule = {
      ...organizationModule,
      standardCrudResources: [{
        ...resource,
        actionHandlers: {
          ...(resource.actionHandlers ?? {}),
          delete: deleteHandler,
        },
      }],
    };
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([sampleModule]);
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.includes('/person-1')) {
        return new Response(JSON.stringify({
          fields: {
            EID: 'person-1',
            PersonNo: 'P0001',
            ENAME: '张三',
            IsEnabled: true,
          },
          summaries: {},
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }

      if (url.includes('/person-2')) {
        return new Response(JSON.stringify({
          fields: {
            EID: 'person-2',
            PersonNo: 'P0002',
            ENAME: '李四',
            IsEnabled: true,
          },
          summaries: {},
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }

      return new Response(JSON.stringify({
        items: [
          { EID: 'person-1', PersonNo: 'P0001', ENAME: '张三', IsEnabled: true },
          { EID: 'person-2', PersonNo: 'P0002', ENAME: '李四', IsEnabled: true },
        ],
        total: 2,
        page: 1,
        pageSize: 20,
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    });

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = renderHost(container);
    });
    await flushAsync();

    const firstViewButton = await findOverflowRowActionButton(container, '张三', '查看');
    expect(firstViewButton).toBeDefined();
    await act(async () => firstViewButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();
    expect(document.body.textContent).toContain('张三');

    const deleteButton = await findOverflowRowActionButton(container, '张三', '删除');
    expect(deleteButton).toBeDefined();
    await act(async () => deleteButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();
    await clickPopconfirmButton('确认');
    await flushAsync();

    const secondViewButton = await findOverflowRowActionButton(container, '李四', '查看');
    expect(secondViewButton).toBeDefined();
    await act(async () => secondViewButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();
    expect(document.body.textContent).toContain('李四');

    deferredDelete.resolve();
    await flushAsync();

    expect(document.body.querySelector('.ant-drawer')).not.toBeNull();
    expect(document.body.textContent).toContain('李四');
  });

  it('刷新当前查询时不重置筛选条件', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
    const fetcher = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      items: [],
      total: 0,
      page: 1,
      pageSize: 20,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = renderHost(container);
    });
    await flushAsync();

    const keyword = findInputByLabel(container, '关键字');
    expect(keyword).toBeInstanceOf(HTMLInputElement);

    await act(async () => {
      setReactInputValue(keyword as HTMLInputElement, 'P0001');
    });

    const searchButton = findAntdButtonByText(document.body, '搜索');
    expect(searchButton).toBeDefined();
    await act(async () => searchButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();

    const refreshButton = findAntdButtonByText(document.body, '刷新');
    expect(refreshButton).toBeDefined();
    await act(async () => refreshButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await flushAsync();

    expect(String(fetcher.mock.calls.at(-1)?.[0] ?? '')).toContain('keyword=P0001');
  });
});
