// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { LocalizationProvider } from '@icp-platform/core';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import { ModuleRegistry } from '@icp-platform/sdk';
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

const personDetailModelDocument = {
  type: 'model' as const,
  Person: {
    fields: [
      {
        name: 'PersonNo',
        storage: { type: 'string' },
        uiType: 'text',
        form: {
          create: { mode: 'editable' },
          edit: { mode: 'readonly' },
        },
      },
      {
        name: 'ENAME',
        storage: { type: 'string' },
        uiType: 'text',
        form: {
          create: { mode: 'editable' },
          edit: { mode: 'editable', required: true },
        },
      },
      {
        name: 'DepartmentId',
        storage: { type: 'guid', nullable: true },
        uiType: 'lookup',
        dataSource: { type: 'provider', key: 'organization.departments' },
        form: {
          create: { mode: 'editable' },
          edit: { mode: 'editable' },
        },
      },
    ],
    sections: [
      { key: 'basic', titleKey: 'Section_Basic', fields: ['PersonNo', 'ENAME'] },
      { key: 'organization', titleKey: 'Section_Organization', fields: ['DepartmentId'] },
    ],
  },
};

const personDetailViewDocument = {
  type: 'view' as const,
  view: {
    key: 'organization.person.detail',
    dicKey: 'PersonDetailView',
    titleKey: 'Title',
    descriptionKey: 'Description',
    mode: 'detail',
    model: 'Person',
    sections: [
      { key: 'basic', titleKey: 'Section_Basic', fields: ['PersonNo', 'ENAME'] },
      { key: 'organization', titleKey: 'Section_Organization', fields: ['DepartmentId'] },
    ],
    summaryBlocks: ['userLink'],
    pageActions: [
      {
        key: 'disable',
        labelKey: 'Action_Disable',
        standardAction: 'disable',
        variant: 'default',
        handlerKey: 'disablePerson',
      },
    ],
  },
};

const personDetailCatalog = {
  'PersonDetailView.Title': '人员详情',
  'PersonDetailView.Description': '查看和维护人员详情信息。',
  'PersonDetailView.Section_Basic': '基础信息',
  'PersonDetailView.Section_Organization': '组织信息',
  'PersonDetailView.Action_Disable': '停用人员',
  'PersonDetailView.Field_PersonNo': '人员编号',
  'PersonDetailView.Field_ENAME': '姓名',
  'PersonDetailView.Field_DepartmentId': '部门',
};

describe('StandardCrudDetailPageHost', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    vi.restoreAllMocks();

    if (root) {
      await act(async () => root.unmount());
    }

    container?.remove();
  });

  async function renderHost(id: string | null) {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      renderHostWithId(id);
    });
  }

  function renderHostWithId(id: string | null) {
    root.render(
      <LocalizationProvider
        defaultLanguage="zhs"
        supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
        catalogs={{
          zhs: personDetailCatalog,
        }}
      >
        <RuntimeMetadataProvider
          value={{
            manifest: { modules: [] },
            navDocuments: [],
            modelDocuments: [personDetailModelDocument],
            viewDocuments: [personDetailViewDocument],
          }}
        >
          <StandardCrudDetailPageHost viewKey="organization.person.detail" id={id} />
        </RuntimeMetadataProvider>
      </LocalizationProvider>,
    );
  }

  it('loads detail through standard CRUD registration', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'organization.person',
            apiRoute: '/api/organization/persons',
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            detailRoutePath: '/organization/persons/:id',
            createEmptyFilters: () => ({}),
            buildListQuery: () => ({}),
            mapListItem: (value: unknown) => value,
            getRowId: () => 'row-1',
            lookupProviders: [{
              key: 'organization.departments',
              load: async () => [],
            }],
            actionHandlers: {
              disablePerson: vi.fn(),
            },
            mapDetail: (detail) => ({ id: String(detail.fields.EID), name: String(detail.fields.ENAME) }),
            renderDetail: ({ detail }) => <div data-testid="detail">{(detail as { name: string }).name}</div>,
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

    await renderHost('Person_1');
    await act(async () => undefined);

    expect(fetch).toHaveBeenCalledWith('/api/organization/persons/Person_1', { method: 'GET' });
    expect(container.textContent).toContain('张三');
  });

  it('resolves detail title and description from the runtime dictionary', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'organization.person',
            apiRoute: '/api/organization/persons',
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            detailRoutePath: '/organization/persons/:id',
            createEmptyFilters: () => ({}),
            buildListQuery: () => ({}),
            mapListItem: (value: unknown) => value,
            getRowId: () => 'row-1',
            lookupProviders: [{
              key: 'organization.departments',
              load: async () => [],
            }],
            actionHandlers: {
              disablePerson: vi.fn(),
            },
            renderDetail: () => <div>详情内容</div>,
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

    await renderHost('Person_1');
    await act(async () => undefined);

    expect(container.textContent).toContain('人员详情');
    expect(container.textContent).toContain('查看和维护人员详情信息。');
    expect(container.textContent).not.toContain('Title');
    expect(container.textContent).not.toContain('Description');
  });

  it('renders a stable error when id is missing', async () => {
    await renderHost(null);

    expect(container.textContent).toContain('当前地址中没有有效的资源标识');
  });

  it('renders summary blocks before default field dump when module detail renderer is absent', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'organization.person',
            apiRoute: '/api/organization/persons',
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            detailRoutePath: '/organization/persons/:id',
            createEmptyFilters: () => ({}),
            buildListQuery: () => ({}),
            mapListItem: (value: unknown) => value,
            getRowId: () => 'row-1',
            lookupProviders: [{
              key: 'organization.departments',
              load: async () => [],
            }],
            actionHandlers: {
              disablePerson: vi.fn(),
            },
          },
        ],
      },
    ]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ fields: { EID: 'Person_1', ENAME: '张三' }, summaries: { userLink: { status: 'notConfigured', data: null } } }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost('Person_1');
    await act(async () => undefined);

    const text = container.textContent ?? '';
    expect(text).toContain('暂未配置');
    expect(text).toContain('姓名');
    expect(text).toContain('张三');
    expect(text.indexOf('暂未配置')).toBeLessThan(text.indexOf('姓名'));
  });

  it('does not expose stale header actions after the route id changes', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'organization.person',
            apiRoute: '/api/organization/persons',
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            detailRoutePath: '/organization/persons/:id',
            createEmptyFilters: () => ({}),
            buildListQuery: () => ({}),
            mapListItem: (value: unknown) => value,
            getRowId: () => 'row-1',
            lookupProviders: [{
              key: 'organization.departments',
              load: async () => [],
            }],
            actionHandlers: {
              disablePerson: vi.fn(),
            },
            mapDetail: (detail) => ({ id: String(detail.fields.EID), name: String(detail.fields.ENAME) }),
            renderDetailHeaderActions: ({ id, detail }) => (
              <button type="button">{`${id}:${(detail as { id: string }).id}`}</button>
            ),
            renderDetail: ({ detail }) => <div data-testid="detail">{(detail as { name: string }).name}</div>,
          },
        ],
      },
    ]);
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const path = String(input);
      return new Response(
        JSON.stringify({
          fields: {
            EID: path.endsWith('/Person_2') ? 'Person_2' : 'Person_1',
            ENAME: path.endsWith('/Person_2') ? '李四' : '张三',
          },
          summaries: {},
        }),
        {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        },
      );
    });

    await renderHost('Person_1');
    await act(async () => undefined);

    expect(container.textContent).toContain('Person_1:Person_1');

    await act(async () => {
      renderHostWithId('Person_2');
    });

    expect(container.textContent).not.toContain('Person_2:Person_1');
  });

  it('renders metadata sections form fields and detail actions without legacy registration declarations', async () => {
    const disablePerson = vi.fn();
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'organization.person',
            apiRoute: '/api/organization/persons',
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            detailRoutePath: '/organization/persons/:id',
            permissions: ['Organization.Person.View'],
            createEmptyFilters: () => ({}),
            buildListQuery: () => ({}),
            mapListItem: (value: unknown) => value,
            getRowId: () => 'row-1',
            lookupProviders: [{
              key: 'organization.departments',
              load: async () => [],
            }],
            actionHandlers: {
              disablePerson,
            },
            fields: undefined,
            formSections: undefined,
            detailActions: undefined,
          },
        ],
      },
    ]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({
        fields: {
          EID: 'Person_1',
          PersonNo: 'P001',
          ENAME: '张三',
          DepartmentId: 'Dept_1',
          DepartmentName: '人力资源部',
        },
        summaries: {
          userLink: { status: 'notConfigured', data: null },
        },
      }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost('Person_1');
    await act(async () => undefined);

    expect(container.textContent).toContain('基础信息');
    expect(container.textContent).toContain('组织信息');
    expect(container.textContent).toContain('人员编号');
    expect(container.textContent).toContain('姓名');
    expect(container.textContent).toContain('部门');
    expect(container.textContent).toContain('停用人员');

    await act(async () => {
      const actionButton = Array.from(container.querySelectorAll('button')).find((button) =>
        button.textContent?.replace(/\s+/g, '') === '停用人员');
      actionButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    expect(disablePerson).toHaveBeenCalledWith(expect.objectContaining({ id: 'Person_1' }));
  });
});
