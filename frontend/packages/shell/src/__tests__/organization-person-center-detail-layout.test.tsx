// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { LocalizationProvider } from '@icp-platform/core';
import PersonDetailPage from '../../../../modules/organization/src/pages/PersonDetailPage';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

vi.mock('../../../../modules/organization/src/services/organizationService', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../../modules/organization/src/services/organizationService')>();

  return {
    ...actual,
    employmentStatusOptions: [{ value: 'FullTime', label: '全职' }],
    disablePerson: async () => ({
      id: '1',
      personNo: 'P001',
      name: '张三',
      companyId: 'c1',
      departmentId: 'd1',
      employmentStatus: 'FullTime',
      employmentStatusLabel: '全职',
      isEnabled: false,
      createdAt: '2026-04-21T00:00:00Z',
      userLinkSummary: { status: 'notConfigured', data: null },
      authorizationSummary: { status: 'dependencyUnavailable', data: null },
    }),
    getCompanies: async () => [],
    getDepartments: async () => [],
    getPositions: async () => [],
    getPersonDetail: async () => ({
      id: '1',
      personNo: 'P001',
      name: '张三',
      companyId: 'c1',
      departmentId: 'd1',
      employmentStatus: 'FullTime',
      employmentStatusLabel: '全职',
      isEnabled: true,
      createdAt: '2026-04-21T00:00:00Z',
      userLinkSummary: { status: 'notConfigured', data: null },
      authorizationSummary: { status: 'dependencyUnavailable', data: null },
    }),
    getEmploymentStatusLabel: (value: string) => value,
    updatePerson: async () => {
      throw new Error('not used');
    },
  };
});

describe('organization person center detail layout', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    window.__ICP_RUNTIME_METADATA__ = undefined;

    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    if (container) {
      container.remove();
    }
  });

  it('renders section titles and page actions from metadata', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);
    window.history.pushState({}, '', '/organization/persons/1');

    await act(async () => {
      root = createRoot(container);
      root.render(
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
          catalogs={{
            zhs: {
              'PersonDetailView.Title': '人员详情',
              'PersonDetailView.Description': '人员详情说明',
              'PersonDetailView.Action_Disable': '停用',
              'PersonDetailView.Section_Basic': '基础信息',
              'PersonDetailView.Section_Organization': '组织信息',
              'PersonDetailView.Block_UserLink': '账号绑定',
              'PersonDetailView.Block_AuthorizationSummary': '授权摘要',
            },
          }}
        >
          <MemoryRouter>
            <PersonDetailPage />
          </MemoryRouter>
        </LocalizationProvider>,
      );
    });

    expect(container.textContent).toContain('人员详情');
    expect(container.textContent).toContain('停用');
    expect(container.textContent).toContain('基础信息');
    expect(container.textContent).toContain('组织信息');
    expect(container.textContent).toContain('账号绑定');
    expect(container.textContent).toContain('授权摘要');
  });

  it('prefers runtime metadata section and action labels when metadata is present', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);
    window.history.pushState({}, '', '/organization/persons/1');
    window.__ICP_RUNTIME_METADATA__ = {
      manifest: { modules: [] },
      navDocuments: [],
      viewDocuments: [{
        type: 'view',
        view: {
          key: 'organization.person.detail',
          dicKey: 'PersonDetailView',
          titleKey: 'Title',
          descriptionKey: 'Description',
          sections: ['basic', 'organization'],
          summaryBlocks: ['userLink', 'authorizationSummary'],
          pageActions: [
            { key: 'disable', labelKey: 'Action_Disable' },
          ],
        },
      }],
      modelDocuments: [{
        type: 'model',
        Person: {
          sections: [
            { key: 'basic', titleKey: 'Section_Basic', fields: ['PersonNo', 'Name'] },
            { key: 'organization', titleKey: 'Section_Organization', fields: ['CompanyId', 'DepartmentId', 'EmploymentStatus'] },
          ],
        },
      }],
    };

    await act(async () => {
      root = createRoot(container);
      root.render(
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
          catalogs={{
            zhs: {
              'PersonDetailView.Title': '元数据人员详情',
              'PersonDetailView.Description': '元数据人员详情说明',
              'PersonDetailView.Action_Disable': '元数据停用',
              'PersonDetailView.Section_Basic': '元数据基础信息',
              'PersonDetailView.Section_Organization': '元数据组织信息',
              'PersonDetailView.Block_UserLink': '元数据账号绑定',
              'PersonDetailView.Block_AuthorizationSummary': '元数据授权摘要',
            },
          }}
        >
          <MemoryRouter>
            <PersonDetailPage />
          </MemoryRouter>
        </LocalizationProvider>,
      );
    });

    expect(container.textContent).toContain('元数据人员详情');
    expect(container.textContent).toContain('元数据停用');
    expect(container.textContent).toContain('元数据基础信息');
    expect(container.textContent).toContain('元数据组织信息');
    expect(container.textContent).toContain('元数据账号绑定');
    expect(container.textContent).toContain('元数据授权摘要');
  });
});
