// @vitest-environment jsdom

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { LocalizationProvider } from '@icp-platform/core';
import PersonListPage from '../../../../modules/organization/src/pages/PersonListPage';

const localizationProps = {
  defaultLanguage: 'zhs',
  supportedLanguages: [{ code: 'zhs', displayName: '简体中文' }],
  catalogs: {
    zhs: {},
  },
};

const serviceMocks = vi.hoisted(() => ({
  getCompanies: vi.fn(),
  getDepartments: vi.fn(),
  getPositions: vi.fn(),
  getPersons: vi.fn(),
  getPersonDetail: vi.fn(),
  updatePerson: vi.fn(),
  createPerson: vi.fn(),
}));

vi.mock('../../../../modules/organization/src/services/organizationService', async () => {
  const actual = await vi.importActual<object>('../../../../modules/organization/src/services/organizationService');
  return {
    ...actual,
    getCompanies: serviceMocks.getCompanies,
    getDepartments: serviceMocks.getDepartments,
    getPositions: serviceMocks.getPositions,
    getPersons: serviceMocks.getPersons,
    getPersonDetail: serviceMocks.getPersonDetail,
    updatePerson: serviceMocks.updatePerson,
    createPerson: serviceMocks.createPerson,
  };
});

vi.mock('../../../../modules/organization/src/runtime/personCenterRuntime', () => ({
  getPersonListRuntimeSchema: () => ({
    dicKey: 'PersonListView',
    titleKey: 'Title',
    descriptionKey: 'Description',
    editorMode: 'drawer',
    detailViewKey: 'organization.person.detail',
    listColumns: [
      { key: 'personNo', labelKey: 'Column.PersonNo' },
      { key: 'name', labelKey: 'Column.Name' },
    ],
    queryFields: [
      { key: 'keyword', labelKey: 'Query.Keyword', source: 'preset', preset: 'keyword' },
      { key: 'companyId', labelKey: 'Query.Company', source: 'model', field: 'CompanyId' },
      { key: 'departmentId', labelKey: 'Query.Department', source: 'model', field: 'DepartmentId' },
      { key: 'employmentStatus', labelKey: 'Query.EmploymentStatus', source: 'model', field: 'EmploymentStatus' },
    ],
    pageActions: [
      { key: 'create', labelKey: 'Action.Create' },
      { key: 'refresh', labelKey: 'Action.Refresh' },
    ],
    advancedFilter: {
      enabled: true,
      titleKey: 'AdvancedFilter.Title',
      sections: [
        {
          key: 'organization',
          titleKey: 'AdvancedFilter.Organization',
          fields: [
            { key: 'companyId', labelKey: 'Query.Company', source: 'model', field: 'CompanyId' },
          ],
        },
      ],
    },
    rowActions: [{ key: 'view', labelKey: 'Action.View' }],
    exportModes: [],
  }),
  getPersonDetailRuntimeSchema: () => ({
    dicKey: 'PersonDetailView',
    titleKey: 'Title',
    descriptionKey: 'Description',
    sections: [
      {
        key: 'basic',
        titleKey: 'Section.Basic',
        fields: ['PersonNo', 'Name', 'CompanyId', 'DepartmentId', 'PositionId', 'EmploymentStatus'],
      },
    ],
    summaryBlocks: [],
    pageActions: [],
  }),
}));

function renderWithLocalization(element: React.ReactNode, container: HTMLDivElement): Root {
  const root = createRoot(container);
  root.render(<LocalizationProvider {...localizationProps}>{element}</LocalizationProvider>);
  return root;
}

async function flushUi(): Promise<void> {
  await act(async () => {
    await Promise.resolve();
  });
}

function setInputValue(input: HTMLInputElement, value: string): void {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set;
  setter?.call(input, value);
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

describe('organization person create flow', () => {
  let container: HTMLDivElement;
  let root: Root;
  let assignMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    globalThis.IS_REACT_ACT_ENVIRONMENT = true;
    container = document.createElement('div');
    document.body.appendChild(container);
    assignMock = vi.fn();
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: {
        ...window.location,
        pathname: '/organization/persons',
        assign: assignMock,
      },
    });

    serviceMocks.getCompanies.mockResolvedValue([
      { id: 'company-1', code: 'C1', name: '一公司', isEnabled: true, sortOrder: 1, createdAt: '2026-04-21T00:00:00Z' },
    ]);
    serviceMocks.getDepartments.mockResolvedValue([
      {
        id: 'department-1',
        companyId: 'company-1',
        parentDepartmentId: null,
        code: 'D1',
        name: '研发部',
        isEnabled: true,
        sortOrder: 1,
        createdAt: '2026-04-21T00:00:00Z',
      },
    ]);
    serviceMocks.getPositions.mockResolvedValue([]);
    serviceMocks.getPersons.mockResolvedValue({ items: [], total: 0 });
    serviceMocks.getPersonDetail.mockResolvedValue({
      id: 'person-1',
      personNo: 'P0001',
      name: '张三',
      companyId: 'company-1',
      companyName: '一公司',
      departmentId: 'department-1',
      departmentName: '研发部',
      positionId: null,
      positionName: null,
      employmentStatus: 'FullTime',
      employmentStatusLabel: '正式',
      isEnabled: true,
      createdAt: '2026-04-21T00:00:00Z',
      userLinkSummary: { status: 'notConfigured', data: null },
      authorizationSummary: { status: 'notConfigured', data: null },
    });
  });

  afterEach(async () => {
    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    container.remove();
    vi.clearAllMocks();
    globalThis.IS_REACT_ACT_ENVIRONMENT = false;
  });

  it('opens create drawer on person list page instead of navigating to create route', async () => {
    await act(async () => {
      root = renderWithLocalization(<PersonListPage />, container);
    });
    await flushUi();

    // 列表页默认骨架应贴近 demo：页面头部 + 轻筛选条 + 列表区，
    // 不再把对象化摘要卡片当作默认列表头布局。
    expect(container.textContent).not.toContain('结果总数');
    expect(container.textContent).not.toContain('公司筛选');
    expect(container.textContent).not.toContain('筛选区');
    expect(container.textContent).not.toContain('用于管理人员档案');
    expect(container.textContent).not.toContain('这里承接列表主体');

    const createButton = Array.from(container.querySelectorAll('button')).find(
      (button) => button.textContent?.includes('新建'),
    );

    expect(createButton).toBeDefined();
    expect(createButton?.hasAttribute('disabled')).toBe(false);

    await act(async () => {
      createButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    expect(assignMock).not.toHaveBeenCalled();
    expect(document.body.querySelector('[role="dialog"][aria-label="新建人员"]')).not.toBeNull();
  });

  it('creates person from list drawer and keeps the user on the list page', async () => {
    serviceMocks.createPerson.mockResolvedValue({
      id: 'person-1',
      personNo: 'P0001',
      name: '张三',
      companyId: 'company-1',
      companyName: '一公司',
      departmentId: 'department-1',
      departmentName: '研发部',
      positionId: null,
      positionName: null,
      employmentStatus: 'FullTime',
      employmentStatusLabel: '正式',
      isEnabled: true,
      createdAt: '2026-04-21T00:00:00Z',
      userLinkSummary: { status: 'notConfigured', data: null },
      authorizationSummary: { status: 'notConfigured', data: null },
    });

    await act(async () => {
      root = renderWithLocalization(<PersonListPage />, container);
    });
    await flushUi();

    const createButton = Array.from(container.querySelectorAll('button')).find(
      (button) => button.textContent?.includes('新建'),
    );

    await act(async () => {
      createButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });
    await flushUi();

    const drawer = document.body.querySelector('[role="dialog"][aria-label="新建人员"]');
    expect(drawer).toBeDefined();
    expect(drawer?.textContent).toContain('* 必填');

    const inputs = Array.from(drawer?.querySelectorAll('input') ?? []);
    const selects = Array.from(drawer?.querySelectorAll('select') ?? []);
    const personNoInput = inputs.find((input) => input.getAttribute('placeholder')?.includes('工号'));
    const nameInput = inputs.find((input) => input.getAttribute('placeholder')?.includes('姓名'));
    const requiredFields = Array.from(drawer?.querySelectorAll('[required]') ?? []);

    expect(personNoInput).toBeDefined();
    expect(nameInput).toBeDefined();
    expect(requiredFields).toHaveLength(2);

    await act(async () => {
      setInputValue(personNoInput as HTMLInputElement, 'P0001');
      setInputValue(nameInput as HTMLInputElement, '张三');
    });
    await flushUi();

    const form = drawer?.querySelector('form');
    expect(form).toBeDefined();

    await act(async () => {
      form?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    });
    await flushUi();

    expect(serviceMocks.createPerson).toHaveBeenCalledWith({
      personNo: 'P0001',
      name: '张三',
      companyId: null,
      departmentId: null,
      positionId: null,
      employmentStatus: 'FullTime',
      isEnabled: true,
    });
    expect(assignMock).not.toHaveBeenCalled();
    expect(document.body.querySelector('[role="dialog"][aria-label="新建人员"]')).toBeNull();
  });
});
