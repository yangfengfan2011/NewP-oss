import { describe, expect, it } from 'vitest';
import { createBuiltinRuntimePluginManifest } from '../builtin-runtime-plugins';
import { loadPluginManifestWith } from '../plugin-manifest-loader';
import type { FrontendModule } from '@icp-platform/sdk';

/**
 * plugin-ops is a host-managed module: it must always be registered,
 * even when loadPluginManifest() returns manifestFound: true.
 */
describe('plugin-ops host-managed module registration', () => {
  it('excludes plugin-ops from the generated runtime manifest (it is host-managed)', () => {
    const manifest = createBuiltinRuntimePluginManifest();
    const moduleIds = manifest.plugins.map((p) => p.moduleId);

    expect(moduleIds).not.toContain('plugin-ops');
  });

  it('loads plugin-ops as an internal module via manifest', async () => {
    const internalModules = {
      'plugin-ops': async () => await import('../../../../modules/plugin-ops/src/index'),
    };

    const result = await loadPluginManifestWith({
      fetchManifest: async () => ({
        ok: true,
        status: 200,
        statusText: 'OK',
        json: async () => ({
          plugins: [{ moduleId: 'plugin-ops' }],
        }),
      }),
      internalModules,
      logger: { info: () => {}, warn: () => {} },
    });

    expect(result.manifestFound).toBe(true);
    expect(result.modules).toHaveLength(1);
    const mod = result.modules[0] as FrontendModule;
    expect(mod.metadata.moduleId).toBe('plugin-ops');
    expect(mod.routes).toHaveLength(1);
    expect(mod.menus).toHaveLength(1);
    expect(mod.routes?.[0].path).toBe('/plugin-ops');
    expect(mod.routes?.[0].requireAuth).toBe(true);
    expect(mod.routes?.[0].permissions).toBeUndefined();
    expect(mod.menus?.[0].permissions).toBeUndefined();
    expect(mod.menus?.[0].children?.[0].path).toBe('/plugin-ops');
    expect(mod.menus?.[0].children?.[0].permissions).toBeUndefined();
  });

  it('registerShellModules merges host-managed plugin-ops even when manifest is found', async () => {
    const internalModules = {
      'plugin-ops': async () => await import('../../../../modules/plugin-ops/src/index'),
    };

    const result = await loadPluginManifestWith({
      fetchManifest: async () => ({
        ok: true,
        status: 200,
        statusText: 'OK',
        json: async () => ({
          plugins: [{ moduleId: 'plugin-ops' }],
        }),
      }),
      internalModules,
      logger: { info: () => {}, warn: () => {} },
    });

    const mod = result.modules[0] as FrontendModule;
    expect(mod.routes[0].path).toBe('/plugin-ops');
    expect(mod.menus[0].id).toBe('plugin-ops');
  });
});
