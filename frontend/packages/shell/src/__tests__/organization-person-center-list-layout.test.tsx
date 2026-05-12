// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { ModuleRegistry } from '@icp-platform/sdk';
import organizationModule from '../../../../modules/organization/src/index';
import PersonListPage from '../../../../modules/organization/src/pages/PersonListPage';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import StandardCrudListPageHost from '../pages/StandardCrudListPageHost';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const serviceMocks = vi.hoisted(() => ({
  disablePerson: vi.fn(),
}));

vi.mock('../../../../modules/organization/src/services/organizationService', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../../modules/organization/src/services/organizationService')>();

  return {
    ...actual,
    disablePerson: serviceMocks.disablePerson,
    getCompanies: async () => [],
    getDepartments: async () => [],
    getEmploymentStatusLabel: (value: string) => value,
  };
});

describe('organization person center list layout', () => {
  let container: HTMLDivElement;
  let root: Root;

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
          }],
          total: 1,
          page: 1,
          pageSize: 20,
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    );

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <RuntimeMetadataProvider
          value={{
            manifest: { modules: [] },
            navDocuments: [],
            viewDocuments: [{
              type: 'view',
              view: {
                key: 'organization.person.list',
                mode: 'list',
                titleKey: '人员中心',
                descriptionKey: '人员列表维护',
              },
            }],
            modelDocuments: [],
          }}
        >
          <StandardCrudListPageHost viewKey="organization.person.list" />
        </RuntimeMetadataProvider>,
      );
    });
    await act(async () => undefined);

    expect(fetch).toHaveBeenCalledWith('/api/organization/persons', { method: 'GET' });
    expect(container.textContent).toContain('人员中心');
    expect(container.textContent).toContain('关键字');
    expect(container.textContent).toContain('新建');
    expect(container.textContent).toContain('刷新');
    expect(container.textContent).toContain('工号');
    expect(container.textContent).toContain('姓名');
    expect(container.textContent).toContain('张三');
    expect(container.textContent).toContain('查看');
    expect(container.textContent).toContain('编辑');
    expect(container.textContent).toContain('停用');
    expect(container.textContent).toContain('共 1 条');
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
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    serviceMocks.disablePerson.mockRejectedValue(new Error('停用人员失败'));

    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <RuntimeMetadataProvider
          value={{
            manifest: { modules: [] },
            navDocuments: [],
            viewDocuments: [{
              type: 'view',
              view: {
                key: 'organization.person.list',
                mode: 'list',
                titleKey: '人员中心',
                descriptionKey: '人员列表维护',
              },
            }],
            modelDocuments: [],
          }}
        >
          <StandardCrudListPageHost viewKey="organization.person.list" />
        </RuntimeMetadataProvider>,
      );
    });
    await act(async () => undefined);

    const disableButton = Array.from(container.querySelectorAll('button')).find(
      (button) => button.textContent?.includes('停用'),
    );

    await act(async () => {
      disableButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    expect(serviceMocks.disablePerson).toHaveBeenCalledWith('person-1');
    expect(container.textContent).toContain('停用人员失败');
  });
});
