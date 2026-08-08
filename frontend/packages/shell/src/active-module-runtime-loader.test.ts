import { describe, expect, it, vi } from 'vitest';
import type { FrontendBehaviorBindings, FrontendModule } from '@icp-platform/sdk';
import {
  ACTIVE_MODULE_RUNTIME_URL,
  loadActiveModuleRuntimeWith,
} from './active-module-runtime-loader';

describe('active-module-runtime-loader', () => {
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
});
