// @vitest-environment jsdom

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { LocalizationProvider } from '@icp-platform/core';
import PersonCreatePage from '../../../../modules/organization/src/pages/PersonCreatePage';

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
  createPerson: vi.fn(),
}));

vi.mock('../../../../modules/organization/src/services/organizationService', async () => {
  const actual = await vi.importActual<object>('../../../../modules/organization/src/services/organizationService');
  return {
    ...actual,
    getCompanies: serviceMocks.getCompanies,
    getDepartments: serviceMocks.getDepartments,
    getPositions: serviceMocks.getPositions,
    createPerson: serviceMocks.createPerson,
  };
});

vi.mock('../../../../modules/organization/src/runtime/personCenterRuntime', () => ({
  getPersonDetailRuntimeSchema: () => ({
    dicKey: 'PersonDetailView',
    titleKey: 'Title',
    descriptionKey: 'Description',
    sections: [
      {
        key: 'basic',
        titleKey: 'Section_Basic',
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
        pathname: '/organization/persons/new',
        assign: assignMock,
      },
    });

    serviceMocks.getCompanies.mockResolvedValue([
      { id: 'company-1', code: 'C1', name: '一公司', isEnabled: true, sortOrder: 1, createdAt: '2026-04-21T00:00:00Z' },
    ]);
    serviceMocks.getDepartments.mockResolvedValue([]);
    serviceMocks.getPositions.mockResolvedValue([]);
    serviceMocks.createPerson.mockResolvedValue({
      id: 'person-1',
      personNo: 'P0001',
      name: '张三',
      companyId: '',
      departmentId: '',
      positionId: null,
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

  it('creates person from the retained Organization create page', async () => {
    await act(async () => {
      root = renderWithLocalization(<PersonCreatePage />, container);
    });
    await flushUi();

    expect(container.textContent).toContain('新建人员');
    expect(container.textContent).toContain('* 必填');

    const inputs = Array.from(container.querySelectorAll('input'));
    const personNoInput = inputs.find((input) => input.getAttribute('placeholder')?.includes('工号'));
    const nameInput = inputs.find((input) => input.getAttribute('placeholder')?.includes('姓名'));
    const requiredFields = Array.from(container.querySelectorAll('[required]'));

    expect(personNoInput).toBeDefined();
    expect(nameInput).toBeDefined();
    expect(requiredFields).toHaveLength(2);

    await act(async () => {
      setInputValue(personNoInput as HTMLInputElement, 'P0001');
      setInputValue(nameInput as HTMLInputElement, '张三');
    });
    await flushUi();

    const form = container.querySelector('form');
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
    expect(assignMock).toHaveBeenCalledWith('/organization/persons/person-1');
  });
});
