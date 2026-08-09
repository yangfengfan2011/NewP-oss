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
      customPages: [],
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
              bindings: ['Partner.AssetCatalog.Asset'],
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
                fields: [
                  { name: 'Name', type: 'string', required: true },
                  { name: 'IsEnabled', type: 'boolean', required: false },
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
            permissionCodes: [
              'Partner.AssetCatalog.Asset.View',
              'Partner.AssetCatalog.Asset.Create',
              'Partner.AssetCatalog.Asset.Update',
              'Partner.AssetCatalog.Asset.Delete',
            ],
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
    const resource = module.standardCrudResources?.[0];
    expect(resource).toMatchObject({
      resourceKey: 'Partner.AssetCatalog.Asset',
      apiRoute: '/api/standard/partner-asset-catalog/assets',
      listViewKey: 'partner-asset-catalog.asset.list',
      listRoutePath: '/assets',
      detailViewKey: 'partner-asset-catalog.asset.detail',
      detailRoutePath: '/assets/:id',
      permissions: ['Partner.AssetCatalog.Asset.View'],
    });
    expect(resource?.fields).toEqual([
      expect.objectContaining({ key: 'Name', modelField: 'Name', kind: 'text', required: true }),
      expect.objectContaining({ key: 'IsEnabled', modelField: 'IsEnabled', kind: 'boolean', required: false }),
    ]);
    expect(resource?.getRowId({ EID: 'asset-1' })).toBe('asset-1');
    expect(Object.keys(resource?.actionHandlers ?? {})).toEqual(['disable', 'enable']);
    const refresh = vi.fn();
    await resource?.actionHandlers?.disable({ id: 'asset-1', row: {}, refresh });
    expect(fetchStandardCrud).toHaveBeenNthCalledWith(
      1,
      '/api/standard/partner-asset-catalog/assets/asset-1/disable',
      { method: 'POST' },
    );
    await resource?.actionHandlers?.enable({ id: 'asset-1', row: {}, refresh });
    expect(fetchStandardCrud).toHaveBeenNthCalledWith(
      2,
      '/api/standard/partner-asset-catalog/assets/asset-1',
      {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ IsEnabled: true }),
      },
    );
    expect(refresh).toHaveBeenCalledTimes(2);
    expect(module.permissions?.map((permission) => permission.code)).toEqual([
      'Partner.AssetCatalog.Asset.View',
      'Partner.AssetCatalog.Asset.Create',
      'Partner.AssetCatalog.Asset.Update',
      'Partner.AssetCatalog.Asset.Delete',
    ]);
  });
});
