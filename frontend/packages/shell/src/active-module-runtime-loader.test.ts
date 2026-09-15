import { afterEach, describe, expect, it, vi } from 'vitest';
import type { FrontendBehaviorBindings, FrontendModule } from '@icp-platform/sdk';
import {
  ACTIVE_MODULE_RUNTIME_URL,
  loadActiveModuleRuntimeWith,
} from './active-module-runtime-loader';

describe('active-module-runtime-loader', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('loads every required frontend entry and binds behavior implementations to canonical identity', async () => {
    const organizationBindings: FrontendBehaviorBindings = {
      behaviorKey: 'organization.frontend',
      customPages: [{ pageKey: 'organization.tree.page', component: () => null }],
    };
    const pluginOps: FrontendModule = {
      metadata: {
        moduleId: 'plugin-ops',
        displayName: 'legacy plugin ops',
        version: '0.1.0',
      },
      customPages: [],
    };
    const fetchCatalog = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        schemaVersion: '1.0',
        snapshotId: 'snapshot-1',
        planHash: 'snapshot-1',
        modules: [
          {
            moduleId: 'Organization',
            version: '1.0.0',
            displayName: '组织结构',
            dependencies: [],
            frontend: {
              moduleId: 'organization',
              behaviorKey: 'organization.frontend',
              bindingMode: 'behavior-bindings',
              bindings: ['organization.tree.page'],
            },
          },
          {
            moduleId: 'PluginOps',
            version: '1.0.0',
            displayName: '插件运维',
            dependencies: [],
            frontend: {
              moduleId: 'plugin-ops',
              behaviorKey: null,
              bindingMode: 'legacy-module',
              bindings: [],
            },
          },
        ],
      }),
    });
    const internalModules = {
      organization: vi.fn().mockResolvedValue({ default: organizationBindings }),
      'plugin-ops': vi.fn().mockResolvedValue({ default: pluginOps }),
    };

    const result = await loadActiveModuleRuntimeWith({ fetchCatalog, internalModules });

    expect(fetchCatalog).toHaveBeenCalledWith(ACTIVE_MODULE_RUNTIME_URL);
    expect(result.snapshotId).toBe('snapshot-1');
    expect(result.modules.map((module) => module.metadata)).toEqual([
      {
        moduleId: 'Organization',
        displayName: '组织结构',
        version: '1.0.0',
        description: undefined,
      },
      {
        moduleId: 'PluginOps',
        displayName: '插件运维',
        version: '1.0.0',
        description: undefined,
      },
    ]);
  });

  it('rejects the whole catalog when one required entry fails', async () => {
    const fetchCatalog = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        schemaVersion: '1.0',
        snapshotId: 'snapshot-failed',
        planHash: 'snapshot-failed',
        modules: [
          {
            moduleId: 'Organization',
            version: '1.0.0',
            displayName: '组织结构',
            dependencies: [],
            frontend: {
              moduleId: 'organization',
              behaviorKey: 'organization.frontend',
              bindingMode: 'behavior-bindings',
              bindings: [],
            },
          },
        ],
      }),
    });

    await expect(loadActiveModuleRuntimeWith({
      fetchCatalog,
      internalModules: {
        organization: vi.fn().mockRejectedValue(new Error('entry import failed')),
      },
    })).rejects.toThrow('MODULE_PROJECTION_FAILED');
  });

  it('does not fail open when the active catalog is unavailable', async () => {
    const fetchCatalog = vi.fn().mockResolvedValue({
      ok: false,
      status: 404,
      statusText: 'Not Found',
      json: vi.fn(),
    });

    await expect(loadActiveModuleRuntimeWith({ fetchCatalog, internalModules: {} }))
      .rejects.toThrow('ACTIVE_MODULE_RUNTIME_UNAVAILABLE');
  });

  it('projects lazy capability bindings without executing their implementation loaders', async () => {
    const capabilityLoad = vi.fn().mockResolvedValue({ default: () => null });
    const bindings: FrontendBehaviorBindings = {
      behaviorKey: 'lazy.frontend',
      routeCapabilities: [{
        kind: 'custom',
        targetKey: 'lazy.page',
        load: capabilityLoad,
      }],
    };
    const fetchCatalog = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        schemaVersion: '1.0',
        snapshotId: 'snapshot-lazy',
        planHash: 'snapshot-lazy',
        modules: [{
          moduleId: 'Lazy',
          version: '1.0.0',
          displayName: 'Lazy',
          dependencies: [],
          frontend: {
            moduleId: 'lazy',
            behaviorKey: 'lazy.frontend',
            bindingMode: 'behavior-bindings',
            bindings: ['lazy.page'],
          },
        }],
      }),
    });

    const result = await loadActiveModuleRuntimeWith({
      fetchCatalog,
      internalModules: {
        lazy: vi.fn().mockResolvedValue({ default: bindings }),
      },
    });

    expect(result.modules[0].routeCapabilities).toEqual(bindings.routeCapabilities);
    expect(capabilityLoad).not.toHaveBeenCalled();
  });

  it('validates lazy standard CRUD descriptor coverage without executing implementation loaders', async () => {
    const resourceLoad = vi.fn().mockResolvedValue({
      default: {
        resourceKey: 'records.resource',
        dataAdapter: {
          list: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }),
          get: async () => {
            const normalized = { fields: {}, summaries: {} };
            return { normalized, value: normalized };
          },
          getRowId: () => 'record-1',
        },
        listViewKey: 'records.list',
        detailViewKey: 'records.detail',
      },
    });
    const bindings: FrontendBehaviorBindings = {
      behaviorKey: 'records.frontend',
      standardCrudCapabilities: [
        { kind: 'standard-list', targetKey: 'records.list', load: resourceLoad },
        { kind: 'standard-crud-detail', targetKey: 'records.detail', load: resourceLoad },
      ],
    };
    const fetchCatalog = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        schemaVersion: '1.0',
        snapshotId: 'snapshot-lazy-standard-crud',
        planHash: 'snapshot-lazy-standard-crud',
        modules: [{
          moduleId: 'Records',
          version: '1.0.0',
          displayName: 'Records',
          dependencies: [],
          frontend: {
            moduleId: 'records',
            behaviorKey: 'records.frontend',
            bindingMode: 'behavior-bindings',
            bindings: ['records.list', 'records.detail'],
          },
        }],
      }),
    });

    const result = await loadActiveModuleRuntimeWith({
      fetchCatalog,
      internalModules: {
        records: vi.fn().mockResolvedValue({ default: bindings }),
      },
    });

    expect(result.modules[0].standardCrudCapabilities).toEqual(bindings.standardCrudCapabilities);
    expect(result.modules[0].standardCrudResources).toBeUndefined();
    expect(resourceLoad).not.toHaveBeenCalled();
  });

  it('rejects a behavior entry whose declared bindings are not covered by descriptors', async () => {
    const fetchCatalog = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        schemaVersion: '1.0',
        snapshotId: 'snapshot-missing-binding',
        planHash: 'snapshot-missing-binding',
        modules: [{
          moduleId: 'MissingBinding',
          version: '1.0.0',
          displayName: 'Missing Binding',
          dependencies: [],
          frontend: {
            moduleId: 'missing-binding',
            behaviorKey: 'missing.frontend',
            bindingMode: 'behavior-bindings',
            bindings: ['missing.page'],
          },
        }],
      }),
    });

    await expect(loadActiveModuleRuntimeWith({
      fetchCatalog,
      internalModules: {
        'missing-binding': vi.fn().mockResolvedValue({
          default: { behaviorKey: 'missing.frontend' } satisfies FrontendBehaviorBindings,
        }),
      },
    })).rejects.toThrow('MODULE_PROJECTION_FAILED');
  });

  it('rejects an unknown route capability kind before publishing the candidate', async () => {
    const fetchCatalog = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        schemaVersion: '1.0',
        snapshotId: 'snapshot-unknown-kind',
        planHash: 'snapshot-unknown-kind',
        modules: [{
          moduleId: 'UnknownKind',
          version: '1.0.0',
          displayName: 'Unknown Kind',
          dependencies: [],
          frontend: {
            moduleId: 'unknown-kind',
            behaviorKey: 'unknown.frontend',
            bindingMode: 'behavior-bindings',
            bindings: ['unknown.page'],
          },
        }],
      }),
    });
    const untrustedBindings = {
      behaviorKey: 'unknown.frontend',
      routeCapabilities: [{
        kind: 'future-pack',
        targetKey: 'unknown.page',
        load: vi.fn().mockResolvedValue({ default: () => null }),
      }],
    } as unknown as FrontendBehaviorBindings;

    await expect(loadActiveModuleRuntimeWith({
      fetchCatalog,
      internalModules: {
        'unknown-kind': vi.fn().mockResolvedValue({ default: untrustedBindings }),
      },
    })).rejects.toThrow('MODULE_PROJECTION_FAILED');
  });

  it('rejects duplicate capability target keys before publishing the candidate', async () => {
    const fetchCatalog = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        schemaVersion: '1.0',
        snapshotId: 'snapshot-duplicate-binding',
        planHash: 'snapshot-duplicate-binding',
        modules: [{
          moduleId: 'DuplicateBinding',
          version: '1.0.0',
          displayName: 'Duplicate Binding',
          dependencies: [],
          frontend: {
            moduleId: 'duplicate-binding',
            behaviorKey: 'duplicate.frontend',
            bindingMode: 'behavior-bindings',
            bindings: ['duplicate.page'],
          },
        }],
      }),
    });
    const load = vi.fn().mockResolvedValue({ default: () => null });

    await expect(loadActiveModuleRuntimeWith({
      fetchCatalog,
      internalModules: {
        'duplicate-binding': vi.fn().mockResolvedValue({
          default: {
            behaviorKey: 'duplicate.frontend',
            routeCapabilities: [
              { kind: 'custom', targetKey: 'duplicate.page', load },
              { kind: 'custom', targetKey: 'duplicate.page', load },
            ],
          } satisfies FrontendBehaviorBindings,
        }),
      },
    })).rejects.toThrow('MODULE_PROJECTION_FAILED');
    expect(load).not.toHaveBeenCalled();
  });

  it('rejects a capability descriptor without a loader function', async () => {
    const fetchCatalog = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        schemaVersion: '1.0',
        snapshotId: 'snapshot-invalid-descriptor',
        planHash: 'snapshot-invalid-descriptor',
        modules: [{
          moduleId: 'InvalidDescriptor',
          version: '1.0.0',
          displayName: 'Invalid Descriptor',
          dependencies: [],
          frontend: {
            moduleId: 'invalid-descriptor',
            behaviorKey: 'invalid.frontend',
            bindingMode: 'behavior-bindings',
            bindings: ['invalid.page'],
          },
        }],
      }),
    });
    const untrustedBindings = {
      behaviorKey: 'invalid.frontend',
      routeCapabilities: [{
        kind: 'custom',
        targetKey: 'invalid.page',
        load: 'not-a-loader',
      }],
    } as unknown as FrontendBehaviorBindings;

    await expect(loadActiveModuleRuntimeWith({
      fetchCatalog,
      internalModules: {
        'invalid-descriptor': vi.fn().mockResolvedValue({ default: untrustedBindings }),
      },
    })).rejects.toThrow('MODULE_PROJECTION_FAILED');
  });

  it('rejects capability target conflicts across modules before publishing the candidate', async () => {
    const fetchCatalog = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        schemaVersion: '1.0',
        snapshotId: 'snapshot-cross-module-conflict',
        planHash: 'snapshot-cross-module-conflict',
        modules: ['First', 'Second'].map((moduleId) => ({
          moduleId,
          version: '1.0.0',
          displayName: moduleId,
          dependencies: [],
          frontend: {
            moduleId: moduleId.toLowerCase(),
            behaviorKey: `${moduleId.toLowerCase()}.frontend`,
            bindingMode: 'behavior-bindings',
            bindings: ['shared.page'],
          },
        })),
      }),
    });
    const page = () => null;

    await expect(loadActiveModuleRuntimeWith({
      fetchCatalog,
      internalModules: {
        first: vi.fn().mockResolvedValue({
          default: {
            behaviorKey: 'first.frontend',
            customPages: [{ pageKey: 'shared.page', component: page }],
          } satisfies FrontendBehaviorBindings,
        }),
        second: vi.fn().mockResolvedValue({
          default: {
            behaviorKey: 'second.frontend',
            customPages: [{ pageKey: 'shared.page', component: page }],
          } satisfies FrontendBehaviorBindings,
        }),
      },
    })).rejects.toThrow('MODULE_PROJECTION_FAILED');
  });

  it('projects a standard-data entry without importing plugin runtime code', async () => {
    const fetchStandardCrud = vi.fn(async () => ({
      ok: true,
      status: 200,
      json: async () => ({ fields: { EID: 'asset-1' } }),
    } as Response));
    vi.stubGlobal('fetch', fetchStandardCrud);
    const fetchCatalog = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        schemaVersion: '1.0',
        snapshotId: 'snapshot-standard-data',
        planHash: 'plan-standard-data',
        modules: [
          {
            moduleId: 'Partner.AssetCatalog',
            version: '1.0.0',
            displayName: 'Asset Catalog',
            dependencies: [],
            frontend: {
              projectionUrl: '/plugins/runtime/modules/Partner.AssetCatalog/projections/standard-data.json',
              blueprintUrl: '/plugins/runtime/modules/Partner.AssetCatalog/authoring/newp.plugin.json',
              behaviorKey: 'standard-data.v1',
              bindingMode: 'standard-data',
              bindings: ['Partner.AssetCatalog.Asset', 'Partner.AssetCatalog.Audit'],
            },
          },
        ],
      }),
    });
    const fetchJson = vi.fn(async (url: string) => {
      if (url.endsWith('newp.plugin.json')) {
        return {
          moduleId: 'Partner.AssetCatalog',
          version: '1.0.0',
          standardData: {
            resources: [
              {
                key: 'Asset',
                displayName: 'Asset',
                displayField: 'Name',
                detailViewKey: 'partner.asset.detail',
                fields: [
                  { name: 'Name', type: 'string', required: true },
                  { name: 'IsEnabled', type: 'boolean', required: false },
                ],
              },
              {
                key: 'Audit',
                displayName: 'Audit',
                displayField: 'Name',
                fields: [
                  { name: 'Name', type: 'string', required: true },
                ],
              },
            ],
          },
        };
      }

      return {
        moduleSlug: 'partner-asset-catalog',
        resources: [
          {
            resourceKey: 'Asset',
            apiRoute: '/api/standard/partner-asset-catalog/assets',
            pageRoute: '/assets',
            detailViewKey: 'partner.asset.detail',

          },
          {
            resourceKey: 'Audit',
            apiRoute: '/api/standard/partner-asset-catalog/audits',
            pageRoute: '/audits',

          },
        ],
      };
    });
    const importModule = vi.fn();

    const result = await loadActiveModuleRuntimeWith({
      fetchCatalog,
      fetchJson,
      importModule,
      internalModules: {},
    });

    expect(importModule).not.toHaveBeenCalled();
    expect(result.modules).toHaveLength(1);
    const module = result.modules[0];
    const resource = module.standardCrudResources?.find((candidate) =>
      candidate.resourceKey === 'Partner.AssetCatalog.Asset');
    expect(resource).toMatchObject({
      resourceKey: 'Partner.AssetCatalog.Asset',
      listViewKey: 'partner-asset-catalog.asset.list',
      listRoutePath: '/assets',
      detailViewKey: 'partner.asset.detail',
      detailRoutePath: '/assets/:id',
    });
    expect(resource?.fields).toEqual([
      expect.objectContaining({ key: 'Name', modelField: 'Name', kind: 'text', required: true }),
      expect.objectContaining({ key: 'IsEnabled', modelField: 'IsEnabled', kind: 'boolean', required: false }),
    ]);
    expect(resource?.fields?.find((field) => field.key === 'Name')).not.toHaveProperty('defaultValue');
    expect(resource?.dataAdapter).toMatchObject({
      list: expect.any(Function),
      get: expect.any(Function),
      create: expect.any(Function),
      update: expect.any(Function),
      remove: expect.any(Function),
      exportList: expect.any(Function),
    });
    expect(resource?.dataAdapter.getRowId({ EID: 'asset-1' })).toBe('asset-1');
    expect(Object.keys(resource?.actionHandlers ?? {})).toEqual(['disable', 'enable']);
    expect(module.standardCrudResources).toHaveLength(2);
    expect(module.standardCrudResources?.find((candidate) =>
      candidate.resourceKey === 'Partner.AssetCatalog.Audit')).toMatchObject({
      listViewKey: 'partner-asset-catalog.audit.list',
      listRoutePath: '/audits',
      detailViewKey: undefined,
      detailRoutePath: undefined,
    });
    await resource?.actionHandlers?.disable({ id: 'asset-1', row: {}, featurePosition: 'disable-position' });
    expect(fetchStandardCrud).toHaveBeenNthCalledWith(
      1,
      '/api/standard/partner-asset-catalog/assets/asset-1/disable',
      { method: 'POST', headers: { 'X-NewP-Feature': 'disable-position' } },
    );
    await resource?.actionHandlers?.enable({ id: 'asset-1', row: {}, featurePosition: 'enable-position' });
    expect(fetchStandardCrud).toHaveBeenNthCalledWith(
      2,
      '/api/standard/partner-asset-catalog/assets/asset-1/enable',
      { method: 'POST', headers: { 'X-NewP-Feature': 'enable-position' } },
    );
    expect(module.permissions).toBeUndefined();
  });
});
