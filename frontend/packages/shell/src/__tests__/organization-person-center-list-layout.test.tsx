// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { LocalizationProvider } from '@icp-platform/core';
import PersonListPage from '../../../../modules/organization/src/pages/PersonListPage';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const personRows = [{
  id: 'person-1',
  personNo: 'P0001',
  name: '张三',
  companyId: 'company-1',
  companyName: '总部',
  departmentId: 'department-1',
  departmentName: '人力资源部',
  employmentStatus: 'FullTime',
  employmentStatusLabel: '在职',
  isEnabled: true,
}];

vi.mock('../../../../modules/organization/src/services/organizationService', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../../modules/organization/src/services/organizationService')>();

  return {
    ...actual,
    employmentStatusOptions: [],
    getCompanies: async () => [],
    getDepartments: async () => [],
    getPersons: async () => ({ items: personRows, total: personRows.length }),
    getEmploymentStatusLabel: (value: string) => value,
  };
});

describe('organization person center list layout', () => {
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

  it('renders page actions and metadata-driven labels', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);
    window.__ICP_RUNTIME_METADATA__ = {
      manifest: { modules: [] },
      navDocuments: [],
      viewDocuments: [{
        type: 'view',
        view: {
          key: 'organization.person.list',
          dicKey: 'PersonListView',
          titleKey: 'Title',
          descriptionKey: 'Description',
          query: {
            fields: [
              { key: 'keyword', labelKey: 'Query_Keyword', source: 'preset', preset: 'keyword' },
              { key: 'companyId', labelKey: 'Query_Company', source: 'model', field: 'CompanyId' },
              { key: 'departmentId', labelKey: 'Query_Department', source: 'model', field: 'DepartmentId' },
              { key: 'employmentStatus', labelKey: 'Query_EmploymentStatus', source: 'model', field: 'EmploymentStatus' },
            ],
          },
          advancedFilter: {
            enabled: true,
            titleKey: 'AdvancedFilter_Title',
            sections: [
              {
                key: 'organization',
                titleKey: 'AdvancedFilter_Organization',
                fields: [
                  { key: 'companyId', labelKey: 'Query_Company', source: 'model', field: 'CompanyId' },
                  { key: 'departmentId', labelKey: 'Query_Department', source: 'model', field: 'DepartmentId' },
                ],
              },
              {
                key: 'status',
                titleKey: 'AdvancedFilter_Status',
                fields: [
                  { key: 'employmentStatus', labelKey: 'Query_EmploymentStatus', source: 'model', field: 'EmploymentStatus' },
                ],
              },
            ],
          },
          pageActions: [
            { key: 'create', labelKey: 'Action_Create' },
            { key: 'refresh', labelKey: 'Action_Refresh' },
          ],
          rowActions: [
            { key: 'view', labelKey: 'Action_View' },
            { key: 'edit', labelKey: 'Action_Edit' },
            { key: 'disable', labelKey: 'Action_Disable' },
          ],
          columns: [
            { key: 'personNo', labelKey: 'Column_PersonNo' },
            { key: 'name', labelKey: 'Column_Name' },
            { key: 'companyName', labelKey: 'Column_CompanyName' },
            { key: 'departmentName', labelKey: 'Column_DepartmentName' },
            { key: 'employmentStatus', labelKey: 'Column_EmploymentStatus' },
            { key: 'isEnabled', labelKey: 'Column_IsEnabled' },
          ],
          export: {
            modes: ['current', 'all'],
          },
        },
      }],
      modelDocuments: [],
    };

    await act(async () => {
      root = createRoot(container);
      root.render(
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
          catalogs={{
            zhs: {
              'PersonListView.Title': '人员中心',
              'PersonListView.Action_Create': '新建',
              'PersonListView.Action_Refresh': '刷新',
              'PersonListView.Query_Keyword': '关键字',
              'PersonListView.Query_Company': '公司',
              'PersonListView.Query_Department': '部门',
              'PersonListView.Query_EmploymentStatus': '在职状态',
              'PersonListView.Column_PersonNo': '工号',
              'PersonListView.Column_Name': '姓名',
              'PersonListView.Column_CompanyName': '公司',
              'PersonListView.Column_DepartmentName': '部门',
              'PersonListView.Column_EmploymentStatus': '在职状态',
              'PersonListView.Column_IsEnabled': '启用状态',
              'PersonListView.AdvancedFilter_Title': '高级筛选',
              'PersonListView.AdvancedFilter_Organization': '组织范围',
              'PersonListView.AdvancedFilter_Status': '状态条件',
            },
          }}
        >
          <MemoryRouter>
            <PersonListPage />
          </MemoryRouter>
        </LocalizationProvider>,
      );
    });

    expect(container.textContent).toContain('人员中心');
    expect(container.textContent).not.toContain('人员中心说明');
    expect(container.textContent).toContain('新建');
    expect(container.textContent).toContain('刷新');
    expect(container.textContent).toContain('关键字');
    expect(container.textContent).toContain('高级筛选');
    expect(container.textContent).not.toContain('组织范围');
    expect(container.textContent).not.toContain('状态条件');
    expect(container.textContent).toContain('列设置');
    expect(container.textContent).toContain('批量操作');
    expect(container.textContent).toContain('导出当前');
    expect(container.textContent).toContain('导出全部');
    expect(container.textContent).toContain('查看');
    expect(container.textContent).toContain('编辑');
    expect(container.textContent).toContain('停用');
    expect(container.textContent).toContain('共 1 条');

    const advancedFilterTrigger = Array.from(container.querySelectorAll('button')).find(
      (button) => button.textContent?.includes('高级筛选'),
    );

    expect(advancedFilterTrigger).toBeDefined();

    await act(async () => {
      advancedFilterTrigger?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    expect(document.body.textContent).toContain('组织范围');
    expect(document.body.textContent).toContain('状态条件');
    expect(document.body.querySelector('[role="dialog"][aria-label="高级筛选"]')).not.toBeNull();
  });

  it('prefers runtime metadata keys over fallback labels when metadata is present', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);
    window.__ICP_RUNTIME_METADATA__ = {
      manifest: { modules: [] },
      navDocuments: [],
      viewDocuments: [{
        type: 'view',
        view: {
          key: 'organization.person.list',
          dicKey: 'PersonListView',
          titleKey: 'Title',
          descriptionKey: 'Description',
          query: {
            fields: [
              { key: 'keyword', labelKey: 'Query_Keyword', source: 'preset', preset: 'keyword' },
            ],
          },
          pageActions: [
            { key: 'create', labelKey: 'Action_Create' },
            { key: 'refresh', labelKey: 'Action_Refresh' },
          ],
          columns: [
            { key: 'personNo', labelKey: 'Column_PersonNo' },
          ],
        },
      }],
      modelDocuments: [],
    };

    await act(async () => {
      root = createRoot(container);
      root.render(
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
          catalogs={{
            zhs: {
              'PersonListView.Title': '元数据人员中心',
              'PersonListView.Action_Create': '元数据新建',
              'PersonListView.Action_Refresh': '元数据刷新',
              'PersonListView.Query_Keyword': '元数据关键字',
              'PersonListView.Column_PersonNo': '元数据工号',
            },
          }}
        >
          <MemoryRouter>
            <PersonListPage />
          </MemoryRouter>
        </LocalizationProvider>,
      );
    });

    expect(container.textContent).toContain('元数据人员中心');
    expect(container.textContent).not.toContain('元数据人员中心说明');
    expect(container.textContent).toContain('元数据新建');
    expect(container.textContent).toContain('元数据刷新');
    expect(container.textContent).toContain('元数据关键字');
  });
});
