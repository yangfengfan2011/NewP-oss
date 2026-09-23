// @vitest-environment jsdom

import { act, StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { describe, expect, it, vi } from 'vitest';
import { PlatformQueryProvider } from '@icp-platform/core';
import { StandardCrudForm } from '@icp-platform/ui/crud-form';
import {
  createStandardCrudRestLookupProvider,
  loadStandardCrudLookupPage,
  type StandardCrudFieldDefinition,
  type StandardCrudLookupProvider,
  type StandardCrudLookupPage,
  type StandardCrudOption,
} from '@icp-platform/sdk';
import {
  loadStandardCrudLookupOptions,
  useStandardCrudLookupRuntime,
} from '../standard-crud/standardCrudLookupRuntime';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

function lookupField(overrides: Partial<StandardCrudFieldDefinition> = {}): StandardCrudFieldDefinition {
  return {
    key: 'departmentId',
    labelKey: '部门',
    modelField: 'DEPARTMENT_ID',
    kind: 'lookup',
    lookup: {
      providerKey: 'departments',
      dependsOn: ['tenantId'],
      currentValueLabelField: 'departmentName',
    },
    ...overrides,
  };
}

function provider(options: StandardCrudOption[]): StandardCrudLookupProvider {
  return {
    key: 'departments',
    load: vi.fn(async () => options),
  };
}

describe('standard CRUD lookup runtime', () => {
  it('shares one typed lookup result across StrictMode and two same-key callers', async () => {
    let resolveLookup!: (value: StandardCrudOption[]) => void;
    const pendingLookup = new Promise<StandardCrudOption[]>((resolve) => {
      resolveLookup = resolve;
    });
    const departments: StandardCrudLookupProvider = {
      key: 'departments',
      load: vi.fn(() => pendingLookup),
    };
    const fields = [lookupField()];
    const providers = [departments];
    const values = { tenantId: 'tenant-a' };
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);

    function LookupRuntimeProbe({ testId }: { testId: string }) {
      const state = useStandardCrudLookupRuntime({
        resourceKey: 'organization.person',
        fields,
        providers,
        values,
      });
      return <span data-testid={testId}>{state.lookupOptions.departmentId?.[0]?.label ?? ''}</span>;
    }

    try {
      await act(async () => {
        root.render(
          <StrictMode>
            <PlatformQueryProvider scopeKey="user-1">
              <LookupRuntimeProbe testId="first" />
              <LookupRuntimeProbe testId="second" />
            </PlatformQueryProvider>
          </StrictMode>,
        );
        await Promise.resolve();
      });

      await act(async () => {
        resolveLookup([{ value: 'finance', label: '财务部' }]);
        await pendingLookup;
        await Promise.resolve();
      });

      expect(departments.load).toHaveBeenCalledTimes(1);
      expect(container.querySelector('[data-testid="first"]')?.textContent).toBe('财务部');
      expect(container.querySelector('[data-testid="second"]')?.textContent).toBe('财务部');
    } finally {
      await act(async () => {
        resolveLookup([{ value: 'finance', label: '财务部' }]);
        await pendingLookup;
      });
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('retries a failed provider exactly once only after explicit retry', async () => {
    const departments: StandardCrudLookupProvider = {
      key: 'departments',
      load: vi.fn()
        .mockRejectedValueOnce(new Error('部门加载失败'))
        .mockResolvedValueOnce([{ value: 'finance', label: '财务部' }]),
    };
    const fields = [lookupField()];
    const providers = [departments];
    const firstValues = { tenantId: 'tenant-a' };
    const retryValues = { tenantId: 'tenant-a' };
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);
    let retryLookup = () => undefined;

    function LookupRuntimeProbe({ values }: { values: Record<string, unknown> }) {
      const state = useStandardCrudLookupRuntime({
        resourceKey: 'organization.person',
        fields,
        providers,
        values,
      });
      retryLookup = state.retry;
      return <span>{state.lookupErrors.departmentId ?? state.lookupOptions.departmentId?.[0]?.label ?? ''}</span>;
    }

    try {
      await act(async () => {
        root.render(
          <StrictMode>
            <PlatformQueryProvider scopeKey="user-1">
              <LookupRuntimeProbe values={firstValues} />
            </PlatformQueryProvider>
          </StrictMode>,
        );
        await Promise.resolve();
        await Promise.resolve();
      });

      expect(departments.load).toHaveBeenCalledTimes(1);
      expect(container.textContent).toContain('部门加载失败');

      await act(async () => {
        root.render(
          <StrictMode>
            <PlatformQueryProvider scopeKey="user-1">
              <LookupRuntimeProbe values={retryValues} />
            </PlatformQueryProvider>
          </StrictMode>,
        );
        await Promise.resolve();
        await Promise.resolve();
      });

      expect(departments.load).toHaveBeenCalledTimes(1);

      await act(async () => {
        retryLookup();
        await Promise.resolve();
        await Promise.resolve();
      });

      expect(departments.load).toHaveBeenCalledTimes(2);
      expect(departments.load).toHaveBeenLastCalledWith(
        {
          fieldKey: 'departmentId',
          keyword: '',
          page: 1,
          pageSize: 20,
          currentValueIds: [],
          values: retryValues,
          dependencyValues: { tenantId: 'tenant-a' },
        },
        { signal: expect.any(AbortSignal) },
      );
      expect(container.textContent).toContain('财务部');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('starts a new lookup request when a declared dependency changes', async () => {
    const departments: StandardCrudLookupProvider = {
      key: 'departments',
      load: vi.fn(async (request: Parameters<StandardCrudLookupProvider['load']>[0]) => [{
        value: String(request.dependencyValues.tenantId),
        label: String(request.dependencyValues.tenantId),
      }]),
    };
    const fields = [lookupField()];
    const providers = [departments];
    const tenantAValues = { tenantId: 'tenant-a' };
    const tenantBValues = { tenantId: 'tenant-b' };
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);

    function LookupRuntimeProbe({ values }: { values: Record<string, unknown> }) {
      const state = useStandardCrudLookupRuntime({
        resourceKey: 'organization.person',
        fields,
        providers,
        values,
      });
      return <span>{state.lookupOptions.departmentId?.[0]?.label ?? ''}</span>;
    }

    try {
      await act(async () => {
        root.render(
          <StrictMode>
            <PlatformQueryProvider scopeKey="user-1">
              <LookupRuntimeProbe values={tenantAValues} />
            </PlatformQueryProvider>
          </StrictMode>,
        );
        await Promise.resolve();
        await Promise.resolve();
      });
      expect(departments.load).toHaveBeenCalledTimes(1);

      await act(async () => {
        root.render(
          <StrictMode>
            <PlatformQueryProvider scopeKey="user-1">
              <LookupRuntimeProbe values={tenantBValues} />
            </PlatformQueryProvider>
          </StrictMode>,
        );
        await Promise.resolve();
        await Promise.resolve();
      });

      expect(departments.load).toHaveBeenCalledTimes(2);
      expect(departments.load).toHaveBeenLastCalledWith(
        {
          fieldKey: 'departmentId',
          keyword: '',
          page: 1,
          pageSize: 20,
          currentValueIds: [],
          values: tenantBValues,
          dependencyValues: { tenantId: 'tenant-b' },
        },
        { signal: expect.any(AbortSignal) },
      );
      expect(container.textContent).toContain('tenant-b');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('does not let a slow old lookup key overwrite a faster new key', async () => {
    let resolveTenantA!: (value: StandardCrudOption[]) => void;
    let resolveTenantB!: (value: StandardCrudOption[]) => void;
    const tenantAResult = new Promise<StandardCrudOption[]>((resolve) => {
      resolveTenantA = resolve;
    });
    const tenantBResult = new Promise<StandardCrudOption[]>((resolve) => {
      resolveTenantB = resolve;
    });
    const departments: StandardCrudLookupProvider = {
      key: 'departments',
      load: vi.fn((request) => request.dependencyValues.tenantId === 'tenant-a'
        ? tenantAResult
        : tenantBResult),
    };
    const fields = [lookupField()];
    const providers = [departments];
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);

    function LookupRuntimeProbe({ tenantId }: { tenantId: string }) {
      const state = useStandardCrudLookupRuntime({
        resourceKey: 'organization.person',
        fields,
        providers,
        values: { tenantId },
      });
      return <span>{state.lookupOptions.departmentId?.[0]?.label ?? ''}</span>;
    }

    try {
      await act(async () => {
        root.render(
          <StrictMode>
            <PlatformQueryProvider scopeKey="user-1">
              <LookupRuntimeProbe tenantId="tenant-a" />
            </PlatformQueryProvider>
          </StrictMode>,
        );
        await Promise.resolve();
      });

      await act(async () => {
        root.render(
          <StrictMode>
            <PlatformQueryProvider scopeKey="user-1">
              <LookupRuntimeProbe tenantId="tenant-b" />
            </PlatformQueryProvider>
          </StrictMode>,
        );
        await Promise.resolve();
      });
      expect(departments.load).toHaveBeenCalledTimes(2);

      await act(async () => {
        resolveTenantB([{ value: 'tenant-b', label: 'tenant-b' }]);
        await tenantBResult;
        await Promise.resolve();
      });
      expect(container.textContent).toBe('tenant-b');

      await act(async () => {
        resolveTenantA([{ value: 'tenant-a', label: 'tenant-a' }]);
        await tenantAResult;
        await Promise.resolve();
      });
      expect(container.textContent).toBe('tenant-b');
    } finally {
      resolveTenantA([]);
      resolveTenantB([]);
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('keeps the request alive for grace re-subscription and aborts after the final leave', async () => {
    vi.useFakeTimers();
    let receivedSignal: AbortSignal | undefined;
    const departments: StandardCrudLookupProvider = {
      key: 'departments',
      load: vi.fn((_request, context) => {
        receivedSignal = context?.signal;
        return new Promise<StandardCrudOption[]>(() => undefined);
      }),
    };
    const fields = [lookupField()];
    const providers = [departments];
    const values = { tenantId: 'tenant-a' };
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);

    function LookupRuntimeProbe() {
      useStandardCrudLookupRuntime({
        resourceKey: 'organization.person',
        fields,
        providers,
        values,
      });
      return null;
    }

    const renderProbe = (visible: boolean) => (
      <StrictMode>
        <PlatformQueryProvider scopeKey="user-1">
          {visible ? <LookupRuntimeProbe /> : null}
        </PlatformQueryProvider>
      </StrictMode>
    );

    try {
      await act(async () => {
        root.render(renderProbe(true));
        await Promise.resolve();
      });
      expect(departments.load).toHaveBeenCalledTimes(1);
      expect(receivedSignal?.aborted).toBe(false);

      await act(async () => {
        root.render(renderProbe(false));
      });
      await act(async () => {
        vi.advanceTimersByTime(25);
      });
      await act(async () => {
        root.render(renderProbe(true));
        await Promise.resolve();
      });
      expect(departments.load).toHaveBeenCalledTimes(1);
      expect(receivedSignal?.aborted).toBe(false);

      await act(async () => {
        root.render(renderProbe(false));
      });
      await act(async () => {
        vi.advanceTimersByTime(51);
      });
      expect(receivedSignal?.aborted).toBe(true);
    } finally {
      await act(async () => root.unmount());
      container.remove();
      vi.useRealTimers();
    }
  });

  it('searches remotely, appends pages and deduplicates options in the shared runtime', async () => {
    const departments: StandardCrudLookupProvider = {
      key: 'departments',
      load: vi.fn(async (request) => {
        if (request.keyword === 'remote') {
          return {
            options: [{ value: 'remote', label: '远程结果' }],
            currentOptions: [],
            page: request.page,
            pageSize: request.pageSize,
            hasMore: false,
          };
        }

        return {
          options: request.page === 1
            ? [{ value: 'a', label: 'A' }, { value: 'b', label: 'B' }]
            : [{ value: 'b', label: 'B' }, { value: 'c', label: 'C' }],
          currentOptions: [],
          page: request.page,
          pageSize: request.pageSize,
          hasMore: request.page === 1,
        };
      }),
    };
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);
    let loadMore = () => undefined;
    let search = (_fieldKey: string, _keyword: string) => undefined;

    function LookupRuntimeProbe() {
      const state = useStandardCrudLookupRuntime({
        resourceKey: 'organization.person',
        fields: [lookupField()],
        providers: [departments],
        values: { tenantId: 'tenant-a' },
      });
      loadMore = () => state.loadMore('departmentId');
      search = state.search;
      return <span>{(state.lookupOptions.departmentId ?? []).map((item) => item.label).join(',')}</span>;
    }

    try {
      await act(async () => {
        root.render(
          <PlatformQueryProvider scopeKey="user-1">
            <LookupRuntimeProbe />
          </PlatformQueryProvider>,
        );
        await Promise.resolve();
        await Promise.resolve();
      });
      expect(container.textContent).toBe('A,B');

      await act(async () => {
        loadMore();
        await Promise.resolve();
        await Promise.resolve();
      });
      expect(container.textContent).toBe('A,B,C');

      await act(async () => {
        search('departmentId', 'remote');
        await Promise.resolve();
        await Promise.resolve();
      });
      expect(container.textContent).toBe('远程结果');
      expect(departments.load).toHaveBeenLastCalledWith(
        expect.objectContaining({ keyword: 'remote', page: 1, pageSize: 20, currentValueIds: [] }),
        { signal: expect.any(AbortSignal) },
      );
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('does not let an older remote search overwrite a newer keyword', async () => {
    let resolveOld!: (value: StandardCrudOption[]) => void;
    let resolveNew!: (value: StandardCrudOption[]) => void;
    const oldResult = new Promise<StandardCrudOption[]>((resolve) => { resolveOld = resolve; });
    const newResult = new Promise<StandardCrudOption[]>((resolve) => { resolveNew = resolve; });
    const departments: StandardCrudLookupProvider = {
      key: 'departments',
      load: vi.fn((request) => {
        if (request.keyword === 'old') return oldResult;
        if (request.keyword === 'new') return newResult;
        return Promise.resolve([]);
      }),
    };
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);
    let search = (_fieldKey: string, _keyword: string) => undefined;

    function LookupRuntimeProbe() {
      const state = useStandardCrudLookupRuntime({
        resourceKey: 'organization.person',
        fields: [lookupField()],
        providers: [departments],
        values: { tenantId: 'tenant-a' },
      });
      search = state.search;
      return <span>{state.lookupOptions.departmentId?.[0]?.label ?? ''}</span>;
    }

    try {
      await act(async () => {
        root.render(
          <PlatformQueryProvider scopeKey="user-1">
            <LookupRuntimeProbe />
          </PlatformQueryProvider>,
        );
        await Promise.resolve();
        await Promise.resolve();
      });
      await act(async () => {
        search('departmentId', 'old');
        await Promise.resolve();
      });
      await act(async () => {
        search('departmentId', 'new');
        await Promise.resolve();
      });

      await act(async () => {
        resolveNew([{ value: 'new', label: '新结果' }]);
        await newResult;
        await Promise.resolve();
      });
      expect(container.textContent).toBe('新结果');

      await act(async () => {
        resolveOld([{ value: 'old', label: '旧结果' }]);
        await oldResult;
        await Promise.resolve();
      });
      expect(container.textContent).toBe('新结果');
    } finally {
      resolveOld([]);
      resolveNew([]);
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('loads lookup options from the matching provider', async () => {
    const options: StandardCrudOption[] = [{ value: 'finance', label: '财务部' }];
    const departments = provider(options);
    const values = { tenantId: 'tenant-a' };

    const result = await loadStandardCrudLookupOptions({
      field: lookupField(),
      providers: [departments],
      values,
      detailFields: {},
    });

    expect(result).toEqual({ options, error: null });
    expect(departments.load).toHaveBeenCalledWith({
      fieldKey: 'departmentId',
      keyword: '',
      page: 1,
      pageSize: 20,
      currentValueIds: [],
      values,
      dependencyValues: { tenantId: 'tenant-a' },
    });
  });

  it('forwards the exact lookup execution signal only when one is provided', async () => {
    const options: StandardCrudOption[] = [{ value: 'finance', label: '财务部' }];
    const departments = provider(options);
    const controller = new AbortController();

    await loadStandardCrudLookupOptions({
      field: lookupField(),
      providers: [departments],
      values: { tenantId: 'tenant-a' },
      detailFields: {},
    }, { signal: controller.signal });

    expect(departments.load).toHaveBeenCalledWith(
      {
        fieldKey: 'departmentId',
        keyword: '',
        page: 1,
        pageSize: 20,
        currentValueIds: [],
        values: { tenantId: 'tenant-a' },
        dependencyValues: { tenantId: 'tenant-a' },
      },
      { signal: controller.signal },
    );
  });

  it('serializes paged lookup requests through the standard REST provider', async () => {
    const fetcher = vi.fn(async () => new Response(JSON.stringify({
      items: [{ value: 'position-2', label: '架构师' }],
      currentItems: [{ value: 'position-old', label: '旧岗位（已停用）', disabled: true }],
      page: 2,
      pageSize: 20,
      hasMore: true,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    const provider = createStandardCrudRestLookupProvider({
      key: 'organization.positions',
      apiRoute: '/api/organization/positions',
      fetcher,
    });
    const controller = new AbortController();

    const result = await provider.load({
      fieldKey: 'positionId',
      keyword: '架构',
      page: 2,
      pageSize: 20,
      currentValueIds: ['position-old'],
      values: {},
      dependencyValues: {},
    }, { signal: controller.signal });

    expect(result).toEqual({
      options: [{ value: 'position-2', label: '架构师' }],
      currentOptions: [{ value: 'position-old', label: '旧岗位（已停用）', disabled: true }],
      page: 2,
      pageSize: 20,
      hasMore: true,
    });
    expect(fetcher).toHaveBeenCalledWith(
      '/api/organization/positions/options?keyword=%E6%9E%B6%E6%9E%84&page=2&pageSize=20&currentValueIds=position-old',
      { headers: { Accept: 'application/json' }, signal: controller.signal },
    );
  });

  it('preserves the current value when provider results do not include it', async () => {
    const departments = provider([{ value: 'finance', label: '财务部' }]);

    const result = await loadStandardCrudLookupOptions({
      field: lookupField(),
      providers: [departments],
      values: {
        tenantId: 'tenant-a',
        departmentId: 'existing',
      },
      detailFields: { departmentName: '既有部门' },
    });

    expect(result).toEqual({
      options: [
        { value: 'existing', label: '当前值不可用', disabled: true },
        { value: 'finance', label: '财务部' },
      ],
      error: null,
    });
  });

  it('prefers the provider-resolved current value over the local identifier fallback', async () => {
    const departments: StandardCrudLookupProvider = {
      key: 'departments',
      load: vi.fn(async (request) => ({
        options: [],
        currentOptions: [{ value: request.currentValueIds[0] ?? '', label: '已停用部门', disabled: true }],
        page: request.page,
        pageSize: request.pageSize,
        hasMore: false,
      })),
    };

    const result = await loadStandardCrudLookupPage({
      field: lookupField(),
      providers: [departments],
      values: { tenantId: 'tenant-a', departmentId: 'department-old' },
      detailFields: {},
    });

    expect(result.options).toEqual([
      { value: 'department-old', label: '已停用部门', disabled: true },
    ]);
    expect(result.currentOptions).toEqual(result.options);
  });

  it('skips provider loading when any dependency value is empty', async () => {
    const departments = provider([{ value: 'finance', label: '财务部' }]);

    const result = await loadStandardCrudLookupOptions({
      field: lookupField(),
      providers: [departments],
      values: { tenantId: '' },
      detailFields: {},
    });

    expect(result).toEqual({ options: [], error: null });
    expect(departments.load).not.toHaveBeenCalled();
  });

  it('reports missing provider before dependency short circuit', async () => {
    const result = await loadStandardCrudLookupOptions({
      field: lookupField({ lookup: { providerKey: 'missing', dependsOn: ['tenantId'] } }),
      providers: [],
      values: { tenantId: '' },
      detailFields: {},
    });

    expect(result).toEqual({ options: [], error: '未找到 lookup provider：missing' });
  });

  it('returns stable error and preserves current value when provider fails', async () => {
    const departments: StandardCrudLookupProvider = {
      key: 'departments',
      load: vi.fn(async () => {
        throw new Error('部门加载失败');
      }),
    };

    const result = await loadStandardCrudLookupOptions({
      field: lookupField(),
      providers: [departments],
      values: {
        tenantId: 'tenant-a',
        departmentId: 'existing',
      },
      detailFields: { departmentName: '既有部门' },
    });

    expect(result).toEqual({
      options: [{ value: 'existing', label: '当前值不可用', disabled: true }],
      error: '部门加载失败',
    });
  });

  it('keeps the old detail name and raw ID out of the rendered lookup while authorization is pending', async () => {
    let resolveLookup!: (page: StandardCrudLookupPage) => void;
    const pending = new Promise<StandardCrudLookupPage>((resolve) => { resolveLookup = resolve; });
    const departments: StandardCrudLookupProvider = { key: 'departments', load: vi.fn(() => pending) };
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);
    function Probe() {
      const values = { tenantId: 'tenant-a', departmentId: 'existing' };
      const state = useStandardCrudLookupRuntime({
        resourceKey: 'organization.person',
        fields: [lookupField()], providers: [departments],
        values,
        detailFields: { departmentName: '私有原名' },
      });
      return <StandardCrudForm formId="person-lookup" mode="edit" fields={[lookupField()]}
        sections={[]} values={values} lookupOptions={state.lookupOptions}
        lookupLoading={state.lookupLoading} lookupErrors={state.lookupErrors}
        onChange={() => {}} onSubmit={() => {}} saving={false}
        showSubmitAction={false} titleForSection={(key) => key} />;
    }
    try {
      await act(async () => root.render(<PlatformQueryProvider scopeKey="pending-lookup"><Probe /></PlatformQueryProvider>));
      expect(container.querySelector('.ant-select-selection-item')?.textContent).toBe('正在读取当前值');
      expect(container.textContent).not.toContain('existing');
      expect(container.textContent).not.toContain('私有原名');
      await act(async () => resolveLookup({
        options: [], currentOptions: [{ value: 'existing', label: '***', disabled: true }],
        page: 1, pageSize: 20, hasMore: false,
      }));
      expect(container.querySelector('.ant-select-selection-item')?.textContent).toBe('***');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('keeps an unresolved ID in state and displays an explicit lookup error after failure', async () => {
    let rejectLookup!: (error: Error) => void;
    const pending = new Promise<StandardCrudLookupPage>((_resolve, reject) => { rejectLookup = reject; });
    const departments: StandardCrudLookupProvider = { key: 'departments', load: vi.fn(() => pending) };
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);
    function Probe() {
      const state = useStandardCrudLookupRuntime({
        resourceKey: 'organization.person',
        fields: [lookupField()], providers: [departments],
        values: { tenantId: 'tenant-a', departmentId: 'existing' },
        detailFields: { departmentName: '私有原名' },
      });
      return <span>{state.lookupOptions.departmentId?.[0]?.label}|{state.lookupErrors.departmentId}</span>;
    }
    try {
      await act(async () => root.render(<PlatformQueryProvider scopeKey="failed-lookup"><Probe /></PlatformQueryProvider>));
      expect(container.textContent).toContain('正在读取当前值');
      await act(async () => rejectLookup(new Error('读取计划故障')));
      expect(container.textContent).toBe('当前值不可用|读取计划故障');
      expect(container.textContent).not.toContain('existing');
      expect(container.textContent).not.toContain('私有原名');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('does not show a prior authorization scope name while the next scope is loading', async () => {
    let resolveNext!: (page: StandardCrudLookupPage) => void;
    const next = new Promise<StandardCrudLookupPage>((resolve) => { resolveNext = resolve; });
    const firstPage: StandardCrudLookupPage = {
      options: [], currentOptions: [{ value: 'existing', label: '旧主体可读名称' }],
      page: 1, pageSize: 20, hasMore: false,
    };
    const departments: StandardCrudLookupProvider = {
      key: 'departments',
      load: vi.fn().mockResolvedValueOnce(firstPage).mockImplementationOnce(() => next),
    };
    const container = document.createElement('div');
    const root = createRoot(container);
    function Probe() {
      const state = useStandardCrudLookupRuntime({
        resourceKey: 'organization.person', fields: [lookupField()], providers: [departments],
        values: { tenantId: 'tenant-a', departmentId: 'existing' },
        detailFields: { departmentName: '旧主体可读名称' },
      });
      return <span>{state.lookupOptions.departmentId?.[0]?.label}</span>;
    }
    try {
      await act(async () => root.render(<PlatformQueryProvider scopeKey="old-scope"><Probe /></PlatformQueryProvider>));
      expect(container.textContent).toBe('旧主体可读名称');
      await act(async () => root.render(<PlatformQueryProvider scopeKey="new-scope"><Probe /></PlatformQueryProvider>));
      expect(container.textContent).toBe('正在读取当前值');
      await act(async () => resolveNext({
        options: [], currentOptions: [{ value: 'existing', label: '***', disabled: true }],
        page: 1, pageSize: 20, hasMore: false,
      }));
      expect(container.textContent).toBe('***');
    } finally {
      await act(async () => root.unmount());
    }
  });

  it('uses the authorized current value mask even when detail fields contain an older name', async () => {
    const departments: StandardCrudLookupProvider = {
      key: 'departments',
      load: vi.fn(async (request) => ({
        options: [],
        currentOptions: [{ value: request.currentValueIds[0] ?? '', label: '***', disabled: true }],
        page: request.page,
        pageSize: request.pageSize,
        hasMore: false,
      })),
    };

    const result = await loadStandardCrudLookupPage({
      field: lookupField(),
      providers: [departments],
      values: { tenantId: 'tenant-a', departmentId: 'existing' },
      detailFields: { departmentName: '私有原名' },
    });

    expect(result.currentOptions).toEqual([{ value: 'existing', label: '***', disabled: true }]);
    expect(result.options).toEqual(result.currentOptions);
    expect(JSON.stringify(result)).not.toContain('私有原名');
  });

  it('returns field options for non lookup fields', async () => {
    const options: StandardCrudOption[] = [{ value: 'enabled', label: '启用' }];

    const result = await loadStandardCrudLookupOptions({
      field: {
        key: 'status',
        labelKey: '状态',
        modelField: 'STATUS',
        kind: 'enum',
        options,
      },
      providers: [],
      values: {},
      detailFields: {},
    });

    expect(result).toEqual({ options, error: null });
  });
});

it('does not reload independent positions on company or name changes', async () => {
  const departments = provider([]);
  const positions = { key: 'positions', load: vi.fn(async () => []) };
  const fields = [lookupField(), lookupField({ key: 'positionId', lookup: { providerKey: 'positions' } })];
  const providers = [departments, positions];
  const root = createRoot(document.createElement('div'));
  function Probe({ tenantId, name }: { tenantId: string; name: string }) {
    useStandardCrudLookupRuntime({ resourceKey: 'organization.person', fields, providers, values: { tenantId, name } });
    return null;
  }
  const render = async (tenantId: string, name: string, scope = 'u1') => {
    await act(async () => root.render(<PlatformQueryProvider scopeKey={scope}><Probe tenantId={tenantId} name={name} /></PlatformQueryProvider>));
  };
  try {
    await render('a', 'first');
    await render('b', 'first');
    expect(departments.load).toHaveBeenCalledTimes(2);
    expect(positions.load).toHaveBeenCalledTimes(1);
    await render('b', 'second');
    expect(departments.load).toHaveBeenCalledTimes(2);
    expect(positions.load).toHaveBeenCalledTimes(1);
    await render('b', 'second', 'u2');
    expect(positions.load).toHaveBeenCalledTimes(2);
  } finally { await act(async () => root.unmount()); }
});
