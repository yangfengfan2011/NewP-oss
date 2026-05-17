// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry } from '@icp-platform/sdk';
import organizationModule from '../../../../modules/organization/src/index';
import PersonDetailPage from '../../../../modules/organization/src/pages/PersonDetailPage';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

vi.mock('../../../../modules/organization/src/services/organizationService', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../../modules/organization/src/services/organizationService')>();

  return {
    ...actual,
    getCompanies: async () => [],
    getDepartments: async () => [],
    getPositions: async () => [],
    getEmploymentStatusLabel: (value: string) => value,
    updatePerson: async () => {
      throw new Error('not used');
    },
  };
});

describe('organization person center detail layout', () => {
  let container: HTMLDivElement;
  let root: Root;

  async function renderStandardDetailHost() {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
          catalogs={{ zhs: {} }}
        >
          <RuntimeMetadataProvider
            value={{
              manifest: { modules: [] },
              navDocuments: [],
              viewDocuments: [{
                type: 'view',
                view: {
                  key: 'organization.person.detail',
                  mode: 'detail',
                  titleKey: '人员详情',
                  descriptionKey: '人员详情维护',
                  summaryBlocks: ['userLink', 'authorizationSummary'],
                },
              }],
              modelDocuments: [],
            }}
          >
            <StandardCrudDetailPageHost viewKey="organization.person.detail" id="person-1" />
          </RuntimeMetadataProvider>
        </LocalizationProvider>,
      );
    });
    await act(async () => undefined);
  }

  afterEach(async () => {
    vi.restoreAllMocks();

    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    if (container) {
      container.remove();
    }
  });

  it('renders person detail through the standard CRUD detail host', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({
          fields: {
            EID: 'person-1',
            PersonNo: 'P0001',
            ENAME: '张三',
            CompanyId: 'company-1',
            CompanyName: '总部',
            DepartmentId: 'department-1',
            DepartmentName: '人力资源部',
            EmploymentStatus: 'FullTime',
            IsEnabled: true,
            CreatedAt: '2026-04-21T00:00:00Z',
          },
          summaries: {
            userLink: {
              status: 'ready',
              data: {
                PersonId: 'person-1',
                UserId: 'user-1',
                LinkedAt: '2026-04-22T00:00:00Z',
                LinkedBy: 'admin',
              },
            },
            authorizationSummary: { status: 'ready', data: ['Organization.Person.View'] },
          },
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    );

    await renderStandardDetailHost();

    expect(fetch).toHaveBeenCalledWith('/api/organization/persons/person-1', { method: 'GET' });
    expect(container.textContent).toContain('人员详情');
    expect(container.textContent).toContain('停用');
    expect(container.textContent).toContain('基础信息');
    expect(container.textContent).toContain('组织信息');
    expect(container.textContent).toContain('账号绑定');
    expect(container.textContent).toContain('授权摘要');
  });

  it('keeps platform summary fallback during compatibility detail rendering', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([organizationModule]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({
          fields: {
            EID: 'person-1',
            PersonNo: 'P0001',
            ENAME: '张三',
            CompanyId: 'company-1',
            CompanyName: '总部',
            DepartmentId: 'department-1',
            DepartmentName: '人力资源部',
            EmploymentStatus: 'FullTime',
            IsEnabled: true,
            CreatedAt: '2026-04-21T00:00:00Z',
          },
          summaries: {
            userLink: { status: 'notConfigured', data: null },
            authorizationSummary: { status: 'dependencyUnavailable', data: null },
          },
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    );

    await renderStandardDetailHost();

    expect(container.textContent).toContain('暂未配置');
    expect(container.textContent).toContain('依赖不可用');
  });

  it('keeps the legacy direct detail page as a compatibility message', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(<PersonDetailPage />);
    });

    expect(container.textContent).toContain('人员详情已由 Shell 标准 CRUD 详情路由接管');
  });
});
