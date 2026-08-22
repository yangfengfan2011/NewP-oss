// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { ModuleRegistry, createStandardCrudRestDataAdapter } from '@icp-platform/sdk';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';

const platformFetchMock = vi.hoisted(() => vi.fn((input: RequestInfo | URL, init?: RequestInit) => globalThis.fetch(input, init)));

vi.mock('@icp-platform/core', async (importOriginal) => ({
  ...await importOriginal<typeof import('@icp-platform/core')>(),
  platformFetch: platformFetchMock,
}));

function createPersonDataAdapter() {
  return createStandardCrudRestDataAdapter({
    apiRoute: '/api/organization/persons',
    fetcher: (input, init) => globalThis.fetch(input, init),
    mapListItem: (value) => value as Record<string, unknown>,
    getRowId: () => 'row-1',
  });
}

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

async function waitForText(container: HTMLElement, expectedText: string): Promise<void> {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    if (container.textContent?.includes(expectedText)) {
      return;
    }
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 10));
    });
  }

  throw new Error(`Timed out waiting for text: ${expectedText}`);
}

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

  async function renderHost(
    modelDocument: Record<string, unknown>,
    detailSections: Record<string, unknown>[],
  ) {
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
          <PlatformQueryProvider scopeKey="user-1">
            <RuntimeMetadataProvider
              value={{
              manifest: { modules: [] },
              navDocuments: [],
              viewDocuments: [
                {
                  type: 'view',
                  view: {
                    key: 'organization.person.detail',
                    dicKey: 'PersonDetailView',
                    titleKey: 'Title',
                    descriptionKey: 'Description',
                    mode: 'detail',
                    model: 'Person',
                    sections: detailSections,
                    pageActions: [
                      {
                        key: 'edit',
                        labelKey: 'Action_Edit',
                        standardAction: 'edit',
                      },
                    ],
                  },
                },
              ],
              modelDocuments: [modelDocument],
              }}
            >
              <StandardCrudDetailPageHost viewKey="organization.person.detail" id="Person_1" />
            </RuntimeMetadataProvider>
          </PlatformQueryProvider>
        </LocalizationProvider>,
      );
    });
  }

  it('loads the standard edit form only after the metadata edit action is selected', async () => {
    const modelDocument = createPersonModelDocument(
      [{ name: 'ENAME', storage: { type: 'string' }, uiType: 'text', form: { edit: { mode: 'editable' } } }],
      [{ key: 'basic', titleKey: '基础信息', fields: ['ENAME'] }],
    );

    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'organization.person',
            dataAdapter: createPersonDataAdapter(),
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            concurrencyRevisionField: 'Revision',
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

    await renderHost(modelDocument, [
      { key: 'basic', titleKey: '基础信息', fields: ['ENAME'] },
    ]);
    await act(async () => undefined);
    await waitForText(container, '基础信息');

    expect(container.textContent).toContain('基础信息');
    expect(container.querySelector('form')).toBeNull();
    expect(container.querySelector('input[value="张三"]')).toBeNull();

    await openEditor(container);

    expect(container.querySelector('input[value="张三"]')).not.toBeNull();
    expect(Array.from(container.querySelectorAll('button')).some((button) => button.textContent === '保存修改')).toBe(true);
  });

  it('submits normalized standard form values through the standard CRUD client', async () => {
    const modelDocument = createPersonModelDocument(
      [{ name: 'ENAME', storage: { type: 'string' }, uiType: 'text', form: { edit: { mode: 'editable' } } }],
      [{ key: 'basic', titleKey: '基础信息', fields: ['ENAME'] }],
    );

    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'organization.person',
            dataAdapter: createPersonDataAdapter(),
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            concurrencyRevisionField: 'Revision',
          },
        ],
      },
    ]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ fields: { EID: 'Person_1', ENAME: ' 张三 ', Revision: 7 }, summaries: {} }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost(modelDocument, [
      { key: 'basic', titleKey: '基础信息', fields: ['ENAME'] },
    ]);
    await act(async () => undefined);

    await openEditor(container);

    await act(async () => {
      container.querySelector('form')?.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    });

    expect(fetch).toHaveBeenCalledWith('/api/organization/persons/Person_1', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ENAME: '张三', Revision: 7 }),
    });
  });

  it('clears dependent lookup values when parent lookup changes', async () => {
    const modelDocument = createPersonModelDocument(
      [
        createLookupField('CompanyId', 'companies'),
        createLookupField('DepartmentId', 'departments', ['CompanyId']),
        createLookupField('PositionId', 'positions', ['DepartmentId']),
      ],
      [{ key: 'organization', titleKey: '组织信息', fields: ['CompanyId', 'DepartmentId', 'PositionId'] }],
    );

    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'organization.person',
            dataAdapter: createPersonDataAdapter(),
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            lookupProviders: [
              { key: 'companies', load: vi.fn(async () => [{ value: 'Company_2', label: '二号公司' }]) },
              { key: 'departments', load: vi.fn(async () => [{ value: 'Dept_1', label: '一部' }]) },
              { key: 'positions', load: vi.fn(async () => [{ value: 'Position_1', label: '主管' }]) },
            ],
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

    await renderHost(modelDocument, [
      { key: 'organization', titleKey: '组织信息', fields: ['CompanyId', 'DepartmentId', 'PositionId'] },
    ]);
    await flushAsyncWork();

    await openEditor(container);

    const companySelect = container.querySelector('.np-standard-crud-select .ant-select-selector');
    expect(companySelect).not.toBeNull();
    await act(async () => {
      companySelect?.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    });
    await flushAsyncWork();

    const companyOption = Array.from(document.body.querySelectorAll('.ant-select-item-option')).find(
      (option) => option.textContent?.includes('二号公司'),
    );
    expect(companyOption).toBeDefined();
    await act(async () => {
      companyOption?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
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

function createPersonModelDocument(fields: Record<string, unknown>[], sections: Record<string, unknown>[]) {
  return {
    type: 'model',
    Person: { fields, sections },
  };
}

function createLookupField(name: string, providerKey: string, dependsOn: string[] = []) {
  return {
    name,
    storage: { type: 'guid', nullable: true },
    uiType: 'lookup',
    form: { edit: { mode: 'editable' } },
    dataSource: { type: 'provider', key: providerKey, dependsOn },
  };
}

async function flushAsyncWork(): Promise<void> {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

async function openEditor(container: HTMLDivElement): Promise<void> {
  const editButton = Array.from(container.querySelectorAll('button')).find(
    (button) => button.textContent?.trim() === 'Action_Edit',
  );
  expect(editButton).toBeDefined();
  await act(async () => {
    editButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  });
  await flushAsyncWork();
  await vi.waitFor(() => expect(container.querySelector('form')).not.toBeNull());
}
