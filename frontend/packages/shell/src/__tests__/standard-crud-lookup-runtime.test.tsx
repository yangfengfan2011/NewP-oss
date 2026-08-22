// @vitest-environment jsdom

import { act, StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { describe, expect, it, vi } from 'vitest';
import { PlatformQueryProvider } from '@icp-platform/core';
import {
  type StandardCrudFieldDefinition,
  type StandardCrudLookupProvider,
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
        values: { tenantId: 'tenant-a' },
        dependencyValues: { tenantId: 'tenant-a' },
      },
      { signal: controller.signal },
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
        { value: 'existing', label: '既有部门' },
        { value: 'finance', label: '财务部' },
      ],
      error: null,
    });
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
      options: [{ value: 'existing', label: '既有部门' }],
      error: '部门加载失败',
    });
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
