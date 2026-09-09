// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, StrictMode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { LocalizationProvider, PlatformQueryProvider } from '@icp-platform/core';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import {
  ModuleRegistry,
  StandardCrudRequestError,
  createStandardCrudRestDataAdapter,
  type StandardCrudDetail,
  type StandardCrudResourceRegistration,
} from '@icp-platform/sdk';
import { EmbeddedCrudContext } from '../composition/pageCompositionRuntime';
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
          edit: { mode: 'hidden' },
        },
      },
      {
        name: 'Revision',
        storage: { type: 'number' },
        uiType: 'number',
        form: {
          create: { mode: 'hidden' },
          edit: { mode: 'hidden' },
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
      {
        key: 'organization',
        titleKey: 'Section_Organization',
        fields: [{ field: 'DepartmentId', rendererKey: 'organization.reference' }],
      },
    ],
    summaryBlocks: ['userLink'],
    pageActions: [
      {
        key: 'edit',
        labelKey: 'Action_Edit',
        standardAction: 'edit',
      },
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
  'PersonDetailView.Action_Edit': '编辑',
  'PersonDetailView.Action_Disable': '停用人员',
  'PersonDetailView.Action_DataUnavailable': '动作所需数据不可用',
  'PersonDetailView.Field_PersonNo': '人员编号',
  'PersonDetailView.Field_ENAME': '姓名',
  'PersonDetailView.Field_DepartmentId': '部门',
};

const referenceDetailFieldRenderers = {
  'organization.reference': ({ value, detailFields }: {
    value: unknown;
    detailFields: Record<string, unknown>;
  }) => String(detailFields.DepartmentName ?? value ?? ''),
};

function createPersonDataAdapter(rowId = 'Person_1') {
  return createStandardCrudRestDataAdapter({
    apiRoute: '/api/organization/persons',
    fetcher: (input, init) => globalThis.fetch(input, init),
    mapListItem: (value) => value as Record<string, unknown>,
    getRowId: () => rowId,
  });
}

function createPersonRegistration(): StandardCrudResourceRegistration {
  return {
    resourceKey: 'organization.person',
    dataAdapter: createPersonDataAdapter(),
    listViewKey: 'organization.person.list',
    detailViewKey: 'organization.person.detail',
    detailFieldRenderers: referenceDetailFieldRenderers,
    lookupProviders: [{ key: 'organization.departments', load: async () => [] }],
    actionHandlers: { disablePerson: vi.fn() },
  };
}

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

  async function renderHost(
    id: string | null,
    registration?: StandardCrudResourceRegistration,
    detailViewDocument = personDetailViewDocument,
    strictMode = false,
  ) {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      renderHostWithId(id, registration, detailViewDocument, strictMode);
    });
  }

  function renderHostWithId(
    id: string | null,
    registration?: StandardCrudResourceRegistration,
    detailViewDocument = personDetailViewDocument,
    strictMode = false,
    refreshVersion?: number,
  ) {
    const host = (
      <LocalizationProvider
        defaultLanguage="zhs"
        supportedLanguages={[{ code: 'zhs', displayName: '简体中文' }]}
        catalogs={{
          zhs: personDetailCatalog,
        }}
      >
        <PlatformQueryProvider scopeKey="user-1">
          <RuntimeMetadataProvider
            value={{
              manifest: { modules: [] },
              navDocuments: [],
              modelDocuments: [personDetailModelDocument],
              viewDocuments: [detailViewDocument],
            }}
          >
            <EmbeddedCrudContext.Provider value={refreshVersion === undefined ? null : {
              input: { parameters: {}, recordId: id ?? undefined }, refreshVersion,
              reportState: () => undefined, committed: async () => undefined,
            }}>
            <StandardCrudDetailPageHost
              viewKey="organization.person.detail"
              id={id}
              registration={registration}
            />
            </EmbeddedCrudContext.Provider>
          </RuntimeMetadataProvider>
        </PlatformQueryProvider>
      </LocalizationProvider>
    );
    root.render(strictMode ? <StrictMode>{host}</StrictMode> : host);
  }

  it('consumes each embedded refresh version once across success, failure and rerenders, with explicit read retry', async () => {
    let failNext = false;
    const fetcher = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => {
      // Bound the RED case: a runaway host must fail by request count, not hang the suite.
      if (fetcher.mock.calls.length > 8) return new Promise<Response>(() => undefined);
      if (failNext) { failNext = false; throw new Error('refresh read failed'); }
      return new Response(JSON.stringify({ fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentId: null }, summaries: {} }));
    });
    const registration = createPersonRegistration();
    await renderHost('Person_1', registration);
    expect(fetcher).toHaveBeenCalledTimes(1);
    const renderVersion = async (version: number) => {
      await act(async () => renderHostWithId('Person_1', registration, personDetailViewDocument, false, version));
    };
    await renderVersion(1);
    expect(fetcher).toHaveBeenCalledTimes(2);
    await renderVersion(1);
    expect(fetcher).toHaveBeenCalledTimes(2);
    failNext = true;
    await renderVersion(2);
    expect(fetcher).toHaveBeenCalledTimes(3);
    expect(container.textContent).toContain('refresh read failed');
    await renderVersion(2);
    expect(fetcher).toHaveBeenCalledTimes(3);
    const retry = [...container.querySelectorAll('button')].find(button => button.textContent === '重试');
    expect(retry).toBeDefined();
    await act(async () => retry!.click());
    expect(fetcher).toHaveBeenCalledTimes(4);
    await renderVersion(3);
    expect(fetcher).toHaveBeenCalledTimes(5);
    expect(container.textContent).not.toContain('refresh read failed');
  });

  it('loads detail through standard CRUD registration', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'organization.person',
            dataAdapter: createPersonDataAdapter('row-1'),
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            detailRoutePath: '/organization/persons/:id',
            detailFieldRenderers: referenceDetailFieldRenderers,
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
      new Response(JSON.stringify({ fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentId: null }, summaries: {} }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost('Person_1');
    await act(async () => undefined);

    expect(fetch).toHaveBeenCalledWith(
      '/api/organization/persons/Person_1',
      { method: 'GET', signal: expect.any(AbortSignal) },
    );
    await vi.waitFor(
      () => expect(container.textContent).toContain('张三'),
      { timeout: 5_000 },
    );
  });

  it('starts one detail read generation during StrictMode replay', async () => {
    let resolveDetail!: (detail: { normalized: StandardCrudDetail; value: StandardCrudDetail }) => void;
    const pending = new Promise<{ normalized: StandardCrudDetail; value: StandardCrudDetail }>((resolve) => {
      resolveDetail = resolve;
    });
    const get = vi.fn(() => pending);
    const registration: StandardCrudResourceRegistration = {
      ...createPersonRegistration(),
      dataAdapter: { ...createPersonDataAdapter(), get },
    };

    await renderHost('Person_1', registration, personDetailViewDocument, true);

    expect(get).toHaveBeenCalledTimes(1);
    await act(async () => {
      resolveDetail({
        normalized: { fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentId: null }, summaries: {} },
        value: { fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentId: null }, summaries: {} },
      });
      await pending;
    });
  });

  it('retries a failed initial detail read with one new generation', async () => {
    const recoveredDetail = {
      fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentId: null },
      summaries: {},
    };
    const get = vi.fn()
      .mockRejectedValueOnce(new Error('DETAIL_READ_FAILED'))
      .mockResolvedValueOnce({ normalized: recoveredDetail, value: recoveredDetail });
    const registration: StandardCrudResourceRegistration = {
      ...createPersonRegistration(),
      dataAdapter: { ...createPersonDataAdapter(), get },
    };

    await renderHost('Person_1', registration);
    await vi.waitFor(() => expect(container.textContent).toContain('DETAIL_READ_FAILED'));
    const retry = Array.from(container.querySelectorAll('button'))
      .find((button) => button.textContent === '重试')!;

    await act(async () => {
      retry.click();
      retry.click();
    });
    await vi.waitFor(() => expect(container.textContent).toContain('张三'), { timeout: 5_000 });

    expect(get).toHaveBeenCalledTimes(2);
  });

  it('renders null as an em dash and degrades only a missing field in an otherwise available section', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      fields: { EID: 'Person_1', PersonNo: null, DepartmentId: null },
      summaries: {},
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    await renderHost('Person_1', createPersonRegistration());
    await vi.waitFor(() => expect(container.querySelector('[data-field-key="ENAME"]')).not.toBeNull());

    expect(container.querySelector('[data-field-key="PersonNo"]')?.textContent).toBe('—');
    expect(container.querySelector('[data-section-key="basic"]')).toBeNull();
    expect(container.querySelector('[data-field-key="ENAME"]')?.textContent).toBe('数据不可用');
    expect(warn).toHaveBeenCalledWith(
      '[STANDARD_CRUD_DETAIL_CONTRACT]',
      expect.objectContaining({
        resourceKey: 'organization.person',
        viewKey: 'organization.person.detail',
        sectionKey: 'basic',
        fieldKey: 'ENAME',
      }),
    );
  });

  it('degrades a whole section only when every declared field is absent', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      fields: { EID: 'Person_1', DepartmentId: null },
      summaries: {},
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    await renderHost('Person_1', createPersonRegistration());
    await vi.waitFor(() => expect(container.querySelector('[data-section-key="basic"]')).not.toBeNull());

    expect(container.querySelector('[data-section-key="basic"]')?.textContent).toContain('数据不可用');
    expect(container.querySelector('[data-section-key="organization"]')).toBeNull();
  });

  it('fails the whole detail and hides actions when the response EID does not match the route', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      fields: { EID: 'Person_2', PersonNo: 'P002', ENAME: '李四', DepartmentId: null },
      summaries: {},
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    await renderHost('Person_1', createPersonRegistration());
    await vi.waitFor(() => expect(container.textContent).toContain('STANDARD_CRUD_DETAIL_CRITICAL_ID_MISMATCH'));

    expect(container.textContent).not.toContain('李四');
    expect(container.textContent).not.toContain('编辑');
    expect(container.textContent).not.toContain('停用人员');
  });

  it('keeps dependency-bound actions visible but disabled with a localized reason', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const dependencyView = {
      ...personDetailViewDocument,
      view: {
        ...personDetailViewDocument.view,
        pageActions: personDetailViewDocument.view.pageActions.map((action) => ({
          ...action,
          dataDependency: { fields: ['Revision'], reasonKey: 'Action_DataUnavailable' },
        })),
      },
    };
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({
      fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentId: null },
      summaries: {},
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    await renderHost('Person_1', createPersonRegistration(), dependencyView);
    await vi.waitFor(() => expect(container.textContent).toContain('动作所需数据不可用'));

    const edit = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === '编辑');
    const disable = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === '停用人员');
    expect(edit?.disabled).toBe(true);
    expect(disable?.disabled).toBe(true);
  });

  it('uses a route-loaded detail registration without publishing it to ModuleRegistry', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([]);
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ fields: { EID: 'Person_2', PersonNo: 'P002', ENAME: '李四', DepartmentId: null }, summaries: {} }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    const registration: StandardCrudResourceRegistration = {
      resourceKey: 'organization.person',
      dataAdapter: createPersonDataAdapter('Person_2'),
      listViewKey: 'organization.person.list',
      detailViewKey: 'organization.person.detail',
      detailFieldRenderers: referenceDetailFieldRenderers,
      lookupProviders: [{
        key: 'organization.departments',
        load: async () => [],
      }],
      actionHandlers: {
        disablePerson: vi.fn(),
      },
    };

    await renderHost('Person_2', registration);

    expect(container.textContent).toContain('李四');
  });

  it('uses the resource detail loader for a module-owned API shape', async () => {
    const loadDetail = vi.fn().mockResolvedValue({
      fields: { EID: 'Person_2', PersonNo: 'P002', ENAME: '模块详情', DepartmentId: null },
      summaries: {},
    });
    const baseRegistration = createPersonRegistration();
    const registration = {
      ...baseRegistration,
      dataAdapter: {
        ...baseRegistration.dataAdapter,
        get: async (id: string) => {
          const normalized = await loadDetail(id);
          return { normalized, value: normalized };
        },
      },
    };
    const fetchSpy = vi.spyOn(globalThis, 'fetch');

    await renderHost('Person_2', registration);
    await vi.waitFor(() => expect(container.textContent).toContain('模块详情'));

    expect(loadDetail).toHaveBeenCalledWith('Person_2');
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('omits the page heading while preserving localized sections and actions', async () => {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'organization.person',
            dataAdapter: createPersonDataAdapter('row-1'),
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            detailRoutePath: '/organization/persons/:id',
            detailFieldRenderers: referenceDetailFieldRenderers,
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
      new Response(JSON.stringify({ fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentId: null }, summaries: {} }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await renderHost('Person_1');
    await act(async () => undefined);

    expect(container.querySelector('.sh-page-header')).toBeNull();
    expect(container.textContent).not.toContain('人员详情');
    expect(container.textContent).not.toContain('查看和维护人员详情信息。');
    expect(container.textContent).toContain('基础信息');
    expect(container.textContent).toContain('组织信息');
    expect(container.querySelector('.sh-page-actions')?.textContent).toContain('编辑');
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
            dataAdapter: createPersonDataAdapter('row-1'),
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            detailRoutePath: '/organization/persons/:id',
            detailFieldRenderers: referenceDetailFieldRenderers,
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
      new Response(JSON.stringify({ fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentId: null }, summaries: { userLink: { status: 'notConfigured', data: null } } }), {
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

  it('renders metadata sections form fields and detail actions without legacy registration declarations', async () => {
    const disablePerson = vi.fn();
    const detailViewWithInput = {
      ...personDetailViewDocument,
      view: {
        ...personDetailViewDocument.view,
        pageActions: personDetailViewDocument.view.pageActions.map((action) => action.standardAction === 'disable'
          ? { ...action, inputKey: 'disableReason' }
          : action),
      },
    };
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'organization.person',
            dataAdapter: createPersonDataAdapter('row-1'),
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            detailRoutePath: '/organization/persons/:id',
            permissions: ['Organization.Person.View'],
            detailFieldRenderers: referenceDetailFieldRenderers,
            lookupProviders: [{
              key: 'organization.departments',
              load: async () => [],
            }],
            actionHandlers: {
              disablePerson,
            },
            actionInputRenderers: {
              disableReason: ({ value, onChange }) => (
                <textarea
                  aria-label="停用原因"
                  value={String(value ?? '')}
                  onChange={(event) => onChange(event.target.value)}
                />
              ),
            },
            fields: undefined,
            formSections: undefined,
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

    await renderHost('Person_1', undefined, detailViewWithInput);
    await act(async () => undefined);

    expect(container.textContent).toContain('基础信息');
    expect(container.textContent).toContain('组织信息');
    expect(container.textContent).toContain('人员编号');
    expect(container.textContent).toContain('姓名');
    expect(container.textContent).toContain('部门');
    expect(container.textContent).toContain('人力资源部');
    expect(container.textContent).toContain('编辑');
    expect(container.textContent).toContain('停用人员');

    await act(async () => {
      const actionButton = Array.from(container.querySelectorAll('button')).find((button) =>
        button.textContent?.replace(/\s+/g, '') === '停用人员');
      actionButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    const reason = document.body.querySelector('textarea[aria-label="停用原因"]') as HTMLTextAreaElement | null;
    expect(reason).not.toBeNull();
    await act(async () => {
      const valueSetter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')?.set;
      valueSetter?.call(reason, '人员离职');
      reason?.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await act(async () => {
      const confirmButton = Array.from(document.body.querySelectorAll('button')).find((button) =>
        button.textContent?.replace(/\s+/g, '') === '确认');
      confirmButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await Promise.resolve();
    });

    expect(disablePerson).toHaveBeenCalledWith(expect.objectContaining({
      id: 'Person_1',
      input: '人员离职',
    }));
  });

  it('applies module action state and presents an opaque action result on the detail surface', async () => {
    const view = {
      ...personDetailViewDocument,
      view: {
        ...personDetailViewDocument.view,
        pageActions: personDetailViewDocument.view.pageActions.map((action) => action.key === 'disable'
          ? { ...action, inputKey: 'reason' }
          : action),
      },
    };
    const registration: StandardCrudResourceRegistration = {
      ...createPersonRegistration(),
      dataAdapter: {
        ...createPersonDataAdapter(),
        get: async () => {
          const normalized = {
            fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentId: null, Revision: 4 },
            summaries: {},
          };
          return { normalized, value: normalized };
        },
      },
      actionStateResolver: ({ action }) => ({
        visible: action.key !== 'edit',
        available: true,
      }),
      actionInputRenderers: {
        reason: ({ value, onChange }) => (
          <input aria-label="动作原因" value={String(value ?? '')} onChange={(event) => onChange(event.target.value)} />
        ),
      },
      actionHandlers: {
        disablePerson: vi.fn().mockResolvedValue({ operation: 'action', payload: { receipt: 'opaque-action-result' } }),
      },
      operationResultPresenters: [{
        supports: (result) => result.operation === 'action',
        render: ({ result, close }) => (
          <div>
            <span>{String((result.payload as { receipt?: string }).receipt)}</span>
            <button type="button" onClick={close}>关闭动作结果</button>
          </div>
        ),
      }],
    };

    await renderHost('Person_1', registration, view);
    await vi.waitFor(() => expect(container.textContent).toContain('停用人员'));
    expect(container.textContent).not.toContain('编辑');
    const disable = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === '停用人员');
    await act(async () => disable?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    const input = document.body.querySelector<HTMLInputElement>('input[aria-label="动作原因"]');
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(input, '风险处置');
      input?.dispatchEvent(new Event('input', { bubbles: true }));
      const confirm = Array.from(document.body.querySelectorAll('button')).find((button) =>
        button.textContent?.replace(/\s+/g, '') === '确认');
      confirm?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await Promise.resolve();
    });

    expect(container.textContent).toContain('opaque-action-result');
  });

  it('keeps 409 action feedback across the full detail refresh lifecycle without replaying the command', async () => {
    const initialDetail = {
      fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentId: null, Revision: 4 },
      summaries: {},
    };
    const latestDetail = {
      fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '李四', DepartmentId: null, Revision: 5 },
      summaries: {},
    };
    let resolveLatestDetail!: (detail: { normalized: typeof latestDetail; value: typeof latestDetail }) => void;
    const latestDetailRequest = new Promise<{ normalized: typeof latestDetail; value: typeof latestDetail }>((resolve) => {
      resolveLatestDetail = resolve;
    });
    const get = vi.fn()
      .mockResolvedValueOnce({ normalized: initialDetail, value: initialDetail })
      .mockReturnValueOnce(latestDetailRequest);
    const disablePerson = vi.fn().mockRejectedValueOnce(new StandardCrudRequestError('LIFECYCLE_CONFLICT', 409));
    const view = {
      ...personDetailViewDocument,
      view: {
        ...personDetailViewDocument.view,
        pageActions: personDetailViewDocument.view.pageActions.map((action) => action.key === 'disable'
          ? { ...action, inputKey: 'reason' }
          : action),
      },
    };
    const registration: StandardCrudResourceRegistration = {
      ...createPersonRegistration(),
      dataAdapter: {
        ...createPersonDataAdapter(),
        get,
      },
      actionInputRenderers: {
        reason: ({ value, onChange }) => (
          <input aria-label="动作原因" value={String(value ?? '')} onChange={(event) => onChange(event.target.value)} />
        ),
      },
      actionHandlers: { disablePerson },
    };

    await renderHost('Person_1', registration, view);
    await vi.waitFor(() => expect(container.textContent).toContain('停用人员'));
    expect(disablePerson).not.toHaveBeenCalled();

    const disable = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === '停用人员');
    await act(async () => disable?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await vi.waitFor(() => expect(document.body.querySelector('input[aria-label="动作原因"]')).not.toBeNull());
    await act(async () => {
      const confirm = Array.from(document.body.querySelectorAll('button')).find((button) =>
        button.textContent?.replace(/\s+/g, '') === '确认');
      confirm?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await Promise.resolve();
    });

    await vi.waitFor(() => expect(get).toHaveBeenCalledTimes(2));
    expect(disablePerson).toHaveBeenCalledTimes(1);
    expect(document.body.querySelector('input[aria-label="动作原因"]')).toBeNull();
    expect(container.textContent).toContain('LIFECYCLE_CONFLICT');
    expect(container.textContent).toContain('张三');
    expect(Array.from(container.querySelectorAll('button'))
      .find((button) => button.textContent === '停用人员')?.disabled).toBe(true);

    await act(async () => {
      resolveLatestDetail({ normalized: latestDetail, value: latestDetail });
      await latestDetailRequest;
    });
    await vi.waitFor(() => expect(container.textContent).toContain('李四'));
    expect(get).toHaveBeenCalledTimes(2);
    expect(disablePerson).toHaveBeenCalledTimes(1);
    expect(container.textContent).toContain('LIFECYCLE_CONFLICT');

    const retry = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === '停用人员');
    await act(async () => retry?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await vi.waitFor(() => expect(document.body.querySelector('input[aria-label="动作原因"]')).not.toBeNull());
    expect(disablePerson).toHaveBeenCalledTimes(1);
  });

  it('reloads the latest snapshot after a 409 edit conflict and only retries after an explicit resubmit', async () => {
    const initialDetail = {
      fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentId: null, Revision: 4 },
      summaries: {},
    };
    const latestDetail = {
      fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '王五', DepartmentId: null, Revision: 5 },
      summaries: {},
    };
    const refreshedDetail = {
      fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '李四', DepartmentId: null, Revision: 6 },
      summaries: {},
    };
    const get = vi.fn()
      .mockResolvedValueOnce({ normalized: initialDetail, value: initialDetail })
      .mockResolvedValueOnce({ normalized: latestDetail, value: latestDetail })
      .mockResolvedValueOnce({ normalized: refreshedDetail, value: refreshedDetail });
    const update = vi.fn()
      .mockRejectedValueOnce(new StandardCrudRequestError('记录已经变化', 409, { ENAME: '姓名已被其他人修改' }))
      .mockResolvedValueOnce({ operation: 'update', payload: { receipt: 'opaque-update-result' } });
    const registration: StandardCrudResourceRegistration = {
      ...createPersonRegistration(),
      dataAdapter: {
        ...createPersonDataAdapter(),
        get,
        update,
      },
      concurrencyRevisionField: 'Revision',
      operationResultPresenters: [{
        supports: (result) => result.operation === 'update',
        render: ({ result, close }) => (
          <div>
            <span>{String((result.payload as { receipt?: string }).receipt)}</span>
            <button type="button" onClick={close}>关闭更新结果</button>
          </div>
        ),
      }],
    };

    await renderHost('Person_1', registration);
    await vi.waitFor(() => expect(container.textContent).toContain('编辑'));

    const editButton = Array.from(container.querySelectorAll('button')).find((button) => button.textContent === '编辑');
    await act(async () => editButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await vi.waitFor(() => expect(document.body.querySelector<HTMLInputElement>('#ENAME')).not.toBeNull());

    const nameInput = document.body.querySelector<HTMLInputElement>('#ENAME');
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(nameInput, '李四');
      nameInput?.dispatchEvent(new Event('input', { bubbles: true }));
      const saveButton = Array.from(document.body.querySelectorAll('button')).find((button) =>
        button.textContent?.trim() === '保存修改');
      saveButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    await vi.waitFor(() => expect(document.body.textContent).toContain('重新加载当前数据'));
    expect(document.body.textContent).toContain('姓名已被其他人修改');
    expect(document.body.querySelector<HTMLInputElement>('#ENAME')?.value).toBe('李四');
    expect(update).toHaveBeenNthCalledWith(
      1,
      'Person_1',
      { ENAME: '李四', Revision: 4 },
      initialDetail.fields,
    );

    const reloadButton = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.trim() === '重新加载当前数据');
    await act(async () => reloadButton?.dispatchEvent(new MouseEvent('click', { bubbles: true })));
    await vi.waitFor(() => expect(document.body.querySelector<HTMLInputElement>('#ENAME')?.value).toBe('王五'));

    const latestNameInput = document.body.querySelector<HTMLInputElement>('#ENAME');
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(latestNameInput, '李四');
      latestNameInput?.dispatchEvent(new Event('input', { bubbles: true }));
      const retrySaveButton = Array.from(document.body.querySelectorAll('button')).find((button) =>
        button.textContent?.trim() === '保存修改');
      retrySaveButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    });

    await vi.waitFor(() => expect(container.textContent).toContain('opaque-update-result'));
    expect(update).toHaveBeenNthCalledWith(
      2,
      'Person_1',
      { ENAME: '李四', Revision: 5 },
      latestDetail.fields,
    );
    expect(get).toHaveBeenCalledTimes(3);
  });
});
