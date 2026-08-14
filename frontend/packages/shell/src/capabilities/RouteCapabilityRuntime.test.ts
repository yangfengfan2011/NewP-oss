import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  createRecoverableCapabilityLoader,
  ModuleRegistry,
  type FrontendModule,
} from '@icp-platform/sdk';
import { routeCapabilityRuntime } from './RouteCapabilityRuntime';

vi.mock('../pages/StandardListPageHost', () => ({
  default: function MockStandardListPageHost() {
    return null;
  },
}));

vi.mock('../pages/StandardCrudDetailPageHost', () => ({
  default: function MockStandardCrudDetailPageHost() {
    return null;
  },
}));

describe('RouteCapabilityRuntime', () => {
  beforeEach(() => {
    ModuleRegistry.replaceAll([], `snapshot-empty-${crypto.randomUUID()}`);
  });

  it('shares one loader invocation for concurrent requests to the same snapshot capability', async () => {
    function ExamplePage() {
      return null;
    }

    const load = vi.fn().mockResolvedValue({ default: ExamplePage });
    const module = {
      metadata: {
        moduleId: 'example',
        displayName: 'Example',
        version: '1.0.0',
      },
      routeCapabilities: [
        { kind: 'custom', targetKey: 'example.page', load },
      ],
    } as unknown as FrontendModule;
    ModuleRegistry.replaceAll([module], 'snapshot-single-flight');

    const [first, second] = await Promise.all([
      routeCapabilityRuntime.load({ kind: 'custom', targetKey: 'example.page' }),
      routeCapabilityRuntime.load({ kind: 'custom', targetKey: 'example.page' }),
    ]);
    const cached = await routeCapabilityRuntime.load({ kind: 'custom', targetKey: 'example.page' });

    expect(first).toBe(ExamplePage);
    expect(second).toBe(ExamplePage);
    expect(cached).toBe(ExamplePage);
    expect(load).toHaveBeenCalledTimes(1);
  });

  it('loads a lazy standard CRUD implementation once for concurrent authorized route requests', async () => {
    const resource = {
      resourceKey: 'records.resource',
      apiRoute: '/api/records',
      listViewKey: 'records.list',
      detailViewKey: 'records.detail',
      createEmptyFilters: () => ({}),
      buildListQuery: () => ({}),
      mapListItem: (value: unknown) => value as Record<string, unknown>,
      getRowId: () => 'record-1',
    };
    const load = vi.fn().mockResolvedValue({ default: resource });
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'records', displayName: 'Records', version: '1.0.0' },
      standardCrudCapabilities: [{
        kind: 'standard-list',
        targetKey: 'records.list',
        load,
      }],
    }], 'snapshot-lazy-standard-crud');

    const [first, second] = await Promise.all([
      routeCapabilityRuntime.load({ kind: 'standard-list', targetKey: 'records.list' }),
      routeCapabilityRuntime.load({ kind: 'standard-list', targetKey: 'records.list' }),
    ]);

    expect(first).toBe(second);
    expect(typeof first).toBe('function');
    expect(load).toHaveBeenCalledTimes(1);
  });

  it('normalizes a lazy standard CRUD loader failure and allows a retry', async () => {
    const resource = {
      resourceKey: 'retry.records',
      apiRoute: '/api/retry-records',
      listViewKey: 'retry.records.list',
      createEmptyFilters: () => ({}),
      buildListQuery: () => ({}),
      mapListItem: (value: unknown) => value as Record<string, unknown>,
      getRowId: () => 'record-1',
    };
    const failure = new Error('resource chunk unavailable');
    const primary = vi.fn().mockRejectedValue(failure);
    const recovery = vi.fn().mockResolvedValue({ default: resource });
    const load = createRecoverableCapabilityLoader(primary, recovery);
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'retry-records', displayName: 'Retry Records', version: '1.0.0' },
      standardCrudCapabilities: [{
        kind: 'standard-list',
        targetKey: 'retry.records.list',
        load,
      }],
    }], 'snapshot-lazy-standard-crud-retry');

    await expect(routeCapabilityRuntime.load({
      kind: 'standard-list',
      targetKey: 'retry.records.list',
    })).rejects.toMatchObject({ code: 'ROUTE_CAPABILITY_LOAD_FAILED', cause: failure });
    const [firstRetry, concurrentRetry] = await Promise.all([
      routeCapabilityRuntime.load({
        kind: 'standard-list',
        targetKey: 'retry.records.list',
      }),
      routeCapabilityRuntime.load({
        kind: 'standard-list',
        targetKey: 'retry.records.list',
      }),
    ]);
    const cached = await routeCapabilityRuntime.load({
      kind: 'standard-list',
      targetKey: 'retry.records.list',
    });

    expect(firstRetry).toEqual(expect.any(Function));
    expect(concurrentRetry).toBe(firstRetry);
    expect(cached).toBe(firstRetry);
    expect(primary).toHaveBeenCalledTimes(1);
    expect(recovery).toHaveBeenCalledTimes(1);
  });

  it('rejects a late lazy standard CRUD implementation from an old snapshot', async () => {
    const resource = {
      resourceKey: 'stale.records',
      apiRoute: '/api/stale-records',
      listViewKey: 'stale.records.list',
      createEmptyFilters: () => ({}),
      buildListQuery: () => ({}),
      mapListItem: (value: unknown) => value as Record<string, unknown>,
      getRowId: () => 'record-1',
    };
    let completeLoad!: (value: { default: typeof resource }) => void;
    const load = vi.fn(() => new Promise<{ default: typeof resource }>((resolve) => {
      completeLoad = resolve;
    }));
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'stale-records', displayName: 'Stale Records', version: '1.0.0' },
      standardCrudCapabilities: [{
        kind: 'standard-list',
        targetKey: 'stale.records.list',
        load,
      }],
    }], 'snapshot-lazy-standard-crud-old');

    const result = routeCapabilityRuntime.load({
      kind: 'standard-list',
      targetKey: 'stale.records.list',
    });
    ModuleRegistry.replaceAll([], 'snapshot-lazy-standard-crud-current');
    completeLoad({ default: resource });

    await expect(result).rejects.toMatchObject({ code: 'ROUTE_CAPABILITY_STALE' });
    expect(load).toHaveBeenCalledTimes(1);
  });

  it('rejects a lazy standard CRUD implementation whose loaded target does not match its descriptor', async () => {
    const load = vi.fn().mockResolvedValue({
      default: {
        resourceKey: 'mismatched.records',
        apiRoute: '/api/mismatched-records',
        listViewKey: 'another.records.list',
        createEmptyFilters: () => ({}),
        buildListQuery: () => ({}),
        mapListItem: (value: unknown) => value as Record<string, unknown>,
        getRowId: () => 'record-1',
      },
    });
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'mismatched-records', displayName: 'Mismatched Records', version: '1.0.0' },
      standardCrudCapabilities: [{
        kind: 'standard-list',
        targetKey: 'expected.records.list',
        load,
      }],
    }], 'snapshot-lazy-standard-crud-mismatch');

    await expect(routeCapabilityRuntime.load({
      kind: 'standard-list',
      targetKey: 'expected.records.list',
    })).rejects.toMatchObject({
      code: 'ROUTE_CAPABILITY_LOAD_FAILED',
      cause: expect.objectContaining({
        message: "Standard CRUD capability target mismatch: expected 'expected.records.list', got 'another.records.list'.",
      }),
    });
  });

  it('removes a failed load so a later request can retry the same capability', async () => {
    function RecoveredPage() {
      return null;
    }

    const load = vi.fn()
      .mockRejectedValueOnce(new Error('chunk unavailable'))
      .mockResolvedValueOnce({ default: RecoveredPage });
    const module = {
      metadata: {
        moduleId: 'retry-example',
        displayName: 'Retry Example',
        version: '1.0.0',
      },
      routeCapabilities: [
        { kind: 'custom', targetKey: 'retry.page', load },
      ],
    } as unknown as FrontendModule;
    ModuleRegistry.replaceAll([module], 'snapshot-retry');

    await expect(
      routeCapabilityRuntime.load({ kind: 'custom', targetKey: 'retry.page' }),
    ).rejects.toMatchObject({
      code: 'ROUTE_CAPABILITY_LOAD_FAILED',
      cause: expect.objectContaining({ message: 'chunk unavailable' }),
    });

    await expect(
      routeCapabilityRuntime.load({ kind: 'custom', targetKey: 'retry.page' }),
    ).resolves.toBe(RecoveredPage);
    expect(load).toHaveBeenCalledTimes(2);
  });

  it('rejects a late result from an old snapshot and loads the current snapshot implementation', async () => {
    function OldPage() {
      return null;
    }
    function CurrentPage() {
      return null;
    }

    let completeOldLoad!: (value: { default: typeof OldPage }) => void;
    const oldLoad = vi.fn(() => new Promise<{ default: typeof OldPage }>((resolve) => {
      completeOldLoad = resolve;
    }));
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'old', displayName: 'Old', version: '1.0.0' },
      routeCapabilities: [
        { kind: 'custom', targetKey: 'snapshot.page', load: oldLoad },
      ],
    }], 'snapshot-old');

    const oldResult = routeCapabilityRuntime.load({ kind: 'custom', targetKey: 'snapshot.page' });

    const currentLoad = vi.fn().mockResolvedValue({ default: CurrentPage });
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'current', displayName: 'Current', version: '2.0.0' },
      routeCapabilities: [
        { kind: 'custom', targetKey: 'snapshot.page', load: currentLoad },
      ],
    }], 'snapshot-current');
    completeOldLoad({ default: OldPage });

    await expect(oldResult).rejects.toThrow('ROUTE_CAPABILITY_STALE');
    await expect(
      routeCapabilityRuntime.load({ kind: 'custom', targetKey: 'snapshot.page' }),
    ).resolves.toBe(CurrentPage);
    expect(oldLoad).toHaveBeenCalledTimes(1);
    expect(currentLoad).toHaveBeenCalledTimes(1);
  });

  it('reports a stable local error when the target is missing from the active snapshot', async () => {
    ModuleRegistry.replaceAll([], 'snapshot-missing');

    await expect(
      routeCapabilityRuntime.load({ kind: 'custom', targetKey: 'missing.page' }),
    ).rejects.toMatchObject({
      code: 'ROUTE_CAPABILITY_NOT_FOUND',
      message: 'ROUTE_CAPABILITY_NOT_FOUND: custom:missing.page',
    });
  });

  it('reports a stable local conflict when the active snapshot has duplicate capability bindings', async () => {
    const duplicateModules = ['first', 'second'].map((moduleId) => ({
      metadata: { moduleId, displayName: moduleId, version: '1.0.0' },
      routeCapabilities: [{
        kind: 'custom' as const,
        targetKey: 'duplicate.page',
        load: vi.fn().mockResolvedValue({ default: () => null }),
      }],
    }));
    ModuleRegistry.replaceAll(duplicateModules, 'snapshot-conflict');

    await expect(
      routeCapabilityRuntime.load({ kind: 'custom', targetKey: 'duplicate.page' }),
    ).rejects.toMatchObject({
      code: 'ROUTE_CAPABILITY_CONFLICT',
      message: 'ROUTE_CAPABILITY_CONFLICT: custom:duplicate.page',
    });
  });

  it('distinguishes a target registered for another capability kind', async () => {
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'kind-example', displayName: 'Kind Example', version: '1.0.0' },
      routeCapabilities: [{
        kind: 'custom',
        targetKey: 'kind.page',
        load: vi.fn().mockResolvedValue({ default: () => null }),
      }],
    }], 'snapshot-kind-mismatch');

    await expect(
      routeCapabilityRuntime.load({ kind: 'standard-list', targetKey: 'kind.page' }),
    ).rejects.toMatchObject({
      code: 'ROUTE_CAPABILITY_KIND_MISMATCH',
      message: 'ROUTE_CAPABILITY_KIND_MISMATCH: standard-list:kind.page',
    });
  });

  it('normalizes a chunk loader failure without hiding its cause', async () => {
    const chunkFailure = new Error('GET /assets/page.js returned 404');
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'load-failure', displayName: 'Load Failure', version: '1.0.0' },
      routeCapabilities: [{
        kind: 'custom',
        targetKey: 'broken.page',
        load: vi.fn().mockRejectedValue(chunkFailure),
      }],
    }], 'snapshot-load-failure');

    await expect(
      routeCapabilityRuntime.load({ kind: 'custom', targetKey: 'broken.page' }),
    ).rejects.toMatchObject({
      code: 'ROUTE_CAPABILITY_LOAD_FAILED',
      message: 'ROUTE_CAPABILITY_LOAD_FAILED: custom:broken.page',
      cause: chunkFailure,
    });
  });

  it('rejects a loaded module without a renderable default export', async () => {
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'invalid-export', displayName: 'Invalid Export', version: '1.0.0' },
      routeCapabilities: [{
        kind: 'custom',
        targetKey: 'invalid-export.page',
        load: vi.fn().mockResolvedValue({ namedPage: () => null }),
      }],
    } as unknown as FrontendModule], 'snapshot-invalid-export');

    await expect(
      routeCapabilityRuntime.load({ kind: 'custom', targetKey: 'invalid-export.page' }),
    ).rejects.toMatchObject({
      code: 'ROUTE_CAPABILITY_LOAD_FAILED',
      message: 'ROUTE_CAPABILITY_LOAD_FAILED: custom:invalid-export.page',
    });
  });

  it('normalizes an empty route chunk module as a stable load failure', async () => {
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'empty-export', displayName: 'Empty Export', version: '1.0.0' },
      routeCapabilities: [{
        kind: 'custom',
        targetKey: 'empty-export.page',
        load: vi.fn().mockResolvedValue(undefined),
      }],
    } as unknown as FrontendModule], 'snapshot-empty-export');

    await expect(
      routeCapabilityRuntime.load({ kind: 'custom', targetKey: 'empty-export.page' }),
    ).rejects.toMatchObject({
      code: 'ROUTE_CAPABILITY_LOAD_FAILED',
      message: 'ROUTE_CAPABILITY_LOAD_FAILED: custom:empty-export.page',
    });
  });

  it('rejects a non-component object exported where a component type is required', async () => {
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'element-export', displayName: 'Element Export', version: '1.0.0' },
      routeCapabilities: [{
        kind: 'custom',
        targetKey: 'element-export.page',
        load: vi.fn().mockResolvedValue({
          default: { $$typeof: Symbol.for('route-capability.not-a-component') },
        }),
      }],
    } as unknown as FrontendModule], 'snapshot-element-export');

    await expect(
      routeCapabilityRuntime.load({ kind: 'custom', targetKey: 'element-export.page' }),
    ).rejects.toMatchObject({ code: 'ROUTE_CAPABILITY_LOAD_FAILED' });
  });

  it('adapts an existing registered custom page behind the capability seam', async () => {
    function RegisteredPage() {
      return null;
    }

    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'registered', displayName: 'Registered', version: '1.0.0' },
      customPages: [{ pageKey: 'registered.page', component: RegisteredPage }],
    }], 'snapshot-registered-custom');

    await expect(
      routeCapabilityRuntime.load({ kind: 'custom', targetKey: 'registered.page' }),
    ).resolves.toBe(RegisteredPage);
  });

  it('adapts an existing legacy route component without exposing module identity', async () => {
    function LegacyRoutePage() {
      return null;
    }

    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'legacy', displayName: 'Legacy', version: '1.0.0' },
      routes: [{ path: '/legacy/path', component: LegacyRoutePage }],
    }], 'snapshot-legacy-route');

    await expect(
      routeCapabilityRuntime.load({ kind: 'custom', targetKey: '/legacy/path' }),
    ).resolves.toBe(LegacyRoutePage);
  });

  it('loads the standard-list host through a registered list view target', async () => {
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'list', displayName: 'List', version: '1.0.0' },
      standardListViews: [{
        viewKey: 'example.list',
        component: () => null,
      }],
    }], 'snapshot-standard-list');

    const component = await routeCapabilityRuntime.load({
      kind: 'standard-list',
      targetKey: 'example.list',
    });

    expect(component).toEqual(expect.any(Function));
  });

  it('loads the standard CRUD detail host through its registered detail target', async () => {
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'detail', displayName: 'Detail', version: '1.0.0' },
      standardCrudResources: [{
        resourceKey: 'example.resource',
        apiRoute: '/api/examples',
        listViewKey: 'example.list',
        detailViewKey: 'example.detail',
        createEmptyFilters: () => ({}),
        buildListQuery: () => ({}),
        mapListItem: (value) => value as Record<string, unknown>,
        getRowId: () => 'example-id',
      }],
    }], 'snapshot-standard-detail');

    const component = await routeCapabilityRuntime.load({
      kind: 'standard-crud-detail',
      targetKey: 'example.detail',
    });

    expect(component).toEqual(expect.any(Function));
  });

  it('loads the standard-list host through a standard CRUD list target', async () => {
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'crud-list', displayName: 'CRUD List', version: '1.0.0' },
      standardCrudResources: [{
        resourceKey: 'crud-list.resource',
        apiRoute: '/api/crud-list',
        listViewKey: 'crud-list.view',
        createEmptyFilters: () => ({}),
        buildListQuery: () => ({}),
        mapListItem: (value) => value as Record<string, unknown>,
        getRowId: () => 'crud-list-id',
      }],
    }], 'snapshot-standard-crud-list');

    const component = await routeCapabilityRuntime.load({
      kind: 'standard-list',
      targetKey: 'crud-list.view',
    });

    expect(component).toEqual(expect.any(Function));
  });

  it('does not execute capability loaders during module initialization', async () => {
    const load = vi.fn().mockResolvedValue({ default: () => null });
    const initialize = vi.fn();
    ModuleRegistry.replaceAll([{
      metadata: { moduleId: 'initialize', displayName: 'Initialize', version: '1.0.0' },
      routeCapabilities: [{ kind: 'custom', targetKey: 'initialize.page', load }],
      initialize,
    }], 'snapshot-initialize');

    await ModuleRegistry.initializeAll();

    expect(initialize).toHaveBeenCalledTimes(1);
    expect(load).not.toHaveBeenCalled();
  });
});
