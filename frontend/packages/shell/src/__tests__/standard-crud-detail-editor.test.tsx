// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { LocalizationProvider } from '@icp-platform/core';
import { ModuleRegistry, type StandardCrudFieldDefinition, type StandardCrudFormSection } from '@icp-platform/sdk';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe('StandardCrudDetailPageHost editor fallback', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    vi.restoreAllMocks();

    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    container?.remove();
  });

  async function renderHost() {
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
              modelDocuments: [],
              viewDocuments: [
                {
                  type: 'view',
                  view: {
                    key: 'organization.person.detail',
                    dicKey: 'PersonDetailView',
                    titleKey: 'Title',
                    descriptionKey: 'Description',
                    mode: 'detail',
                  },
                },
              ],
            }}
          >
            <StandardCrudDetailPageHost viewKey="organization.person.detail" id="Person_1" />
          </RuntimeMetadataProvider>
        </LocalizationProvider>,
      );
    });
  }

  it('renders a standard edit form when registration declares fields without a custom detail renderer', async () => {
    const fields: StandardCrudFieldDefinition[] = [
      { key: 'name', labelKey: '姓名', modelField: 'ENAME', kind: 'text' },
    ];
    const formSections: StandardCrudFormSection[] = [
      { key: 'basic', titleKey: '基础信息', fields: ['name'] },
    ];

    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'organization.person',
            apiRoute: '/api/organization/persons',
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            fields,
            formSections,
            createEmptyFilters: () => ({}),
            buildListQuery: () => ({}),
            mapListItem: (value: unknown) => value,
            getRowId: () => 'row-1',
            buildColumns: () => [],
          },
        ],
      },
    ]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ fields: { EID: 'Person_1', ENAME: '张三' }, summaries: {} }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost();
    await act(async () => undefined);

    expect(container.textContent).toContain('基础信息');
    expect(container.querySelector('input[value="张三"]')).not.toBeNull();
    expect(Array.from(container.querySelectorAll('button')).some((button) => button.textContent === '保存修改')).toBe(true);
  });

  it('submits normalized standard form values through the standard CRUD client', async () => {
    const fields: StandardCrudFieldDefinition[] = [
      { key: 'name', labelKey: '姓名', modelField: 'ENAME', kind: 'text', trim: true },
    ];

    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'organization.person',
            apiRoute: '/api/organization/persons',
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            fields,
            formSections: [{ key: 'basic', titleKey: '基础信息', fields: ['name'] }],
            createEmptyFilters: () => ({}),
            buildListQuery: () => ({}),
            mapListItem: (value: unknown) => value,
            getRowId: () => 'row-1',
            buildColumns: () => [],
          },
        ],
      },
    ]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ fields: { EID: 'Person_1', ENAME: ' 张三 ' }, summaries: {} }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost();
    await act(async () => undefined);

    await act(async () => {
      container.querySelector('form')?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    });

    expect(fetch).toHaveBeenCalledWith('/api/organization/persons/Person_1', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ENAME: '张三' }),
    });
  });

  it('clears dependent lookup values when parent lookup changes', async () => {
    const fields: StandardCrudFieldDefinition[] = [
      {
        key: 'companyId',
        labelKey: '所属公司',
        modelField: 'CompanyId',
        kind: 'lookup',
        emptyAsNull: true,
        lookup: { providerKey: 'companies', currentValueLabelField: 'CompanyName' },
      },
      {
        key: 'departmentId',
        labelKey: '所属部门',
        modelField: 'DepartmentId',
        kind: 'lookup',
        emptyAsNull: true,
        lookup: { providerKey: 'departments', dependsOn: ['companyId'], currentValueLabelField: 'DepartmentName' },
      },
      {
        key: 'positionId',
        labelKey: '岗位',
        modelField: 'PositionId',
        kind: 'lookup',
        emptyAsNull: true,
        lookup: { providerKey: 'positions', dependsOn: ['departmentId'], currentValueLabelField: 'PositionName' },
      },
    ];

    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'organization.person',
            apiRoute: '/api/organization/persons',
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            fields,
            formSections: [{ key: 'organization', titleKey: '组织信息', fields: ['companyId', 'departmentId', 'positionId'] }],
            lookupProviders: [
              { key: 'companies', load: vi.fn(async () => [{ value: 'Company_2', label: '二号公司' }]) },
              { key: 'departments', load: vi.fn(async () => [{ value: 'Dept_1', label: '一部' }]) },
              { key: 'positions', load: vi.fn(async () => [{ value: 'Position_1', label: '主管' }]) },
            ],
            createEmptyFilters: () => ({}),
            buildListQuery: () => ({}),
            mapListItem: (value: unknown) => value,
            getRowId: () => 'row-1',
            buildColumns: () => [],
          },
        ],
      },
    ]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({
          fields: {
            EID: 'Person_1',
            CompanyId: 'Company_1',
            CompanyName: '一号公司',
            DepartmentId: 'Dept_1',
            DepartmentName: '一部',
            PositionId: 'Position_1',
            PositionName: '主管',
          },
          summaries: {},
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    );

    await renderHost();
    await act(async () => undefined);

    const companySelect = container.querySelector('select') as HTMLSelectElement;
    await act(async () => {
      companySelect.value = 'Company_2';
      companySelect.dispatchEvent(new Event('change', { bubbles: true }));
    });

    await act(async () => {
      container.querySelector('form')?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    });

    expect(fetch).toHaveBeenCalledWith('/api/organization/persons/Person_1', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ CompanyId: 'Company_2', DepartmentId: null, PositionId: null }),
    });
  });
});
