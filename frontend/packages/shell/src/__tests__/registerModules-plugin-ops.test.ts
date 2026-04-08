import { describe, expect, it, vi, beforeEach } from 'vitest';
import type { FrontendModule } from '@icp-platform/sdk';

/**
 * plugin-ops is a host-managed module: it must always be registered,
 * even when loadPluginManifest() returns manifestFound: true.
 */
describe('plugin-ops host-managed module registration', () => {
  beforeEach(() => {
    vi.resetModules();
  });

  it('includes plugin-ops in the builtin runtime plugin entries', async () => {
    const { BUILTIN_RUNTIME_PLUGIN_ENTRIES } = await import('../builtin-runtime-plugins');
    const moduleIds = BUILTIN_RUNTIME_PLUGIN_ENTRIES.map((e) => e.moduleId);
    expect(moduleIds).toContain('plugin-ops');
  });

  it('does NOT include plugin-ops in the generated runtime manifest (host-managed, not runtime-installed)', async () => {
    const { createBuiltinRuntimePluginManifest } = await import('../builtin-runtime-plugins');
    const manifest = createBuiltinRuntimePluginManifest();
    const moduleIds = manifest.plugins.map((p) => p.moduleId);
    expect(moduleIds).not.toContain('plugin-ops');
  });

  it('registerShellModules registers plugin-ops when manifest is found and does NOT include plugin-ops', async () => {
    const registered: FrontendModule[] = [];

    // A fake module that the manifest returns (simulating a different runtime plugin)
    const fakeModule: FrontendModule = {
      metadata: { moduleId: 'some-other-plugin', displayName: 'Other', version: '1.0.0' },
      routes: [],
      menus: [],
      permissions: [],
    };

    vi.doMock('../plugin-manifest-loader', () => ({
      loadPluginManifest: vi.fn().mockResolvedValue({
        manifestFound: true,
        modules: [fakeModule],
        errors: [],
      }),
    }));

    vi.doMock('@icp-platform/sdk', () => ({
      ModuleRegistry: {
        register: vi.fn((mod: FrontendModule) => {
          registered.push(mod);
        }),
      },
    }));

    // Import registerShellModules after mocking
    const { registerShellModules } = await import('../registerModules');
    await registerShellModules();

    // The fake module from the manifest should be registered
    const registeredIds = registered.map((m) => m.metadata.moduleId);
    expect(registeredIds).toContain('some-other-plugin');

    // plugin-ops must also be registered (host-managed guarantee)
    expect(registeredIds).toContain('plugin-ops');
  });

  it('plugin-ops module has metadata.moduleId matching the HOST_MANAGED_MODULE_IDS key', async () => {
    // Import the actual plugin-ops module
    const mod = await import('../../../../modules/plugin-ops/src/index');
    const pluginOps = mod.default as FrontendModule;

    // The moduleId should be 'plugin-ops' so the host-managed filter works correctly
    expect(pluginOps.metadata.moduleId).toBe('plugin-ops');
  });
});
