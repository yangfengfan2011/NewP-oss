import { describe, expect, it, vi, beforeEach } from 'vitest';
import type { FrontendModule } from '@icp-platform/sdk';

/**
 * 这组测试锁定 host-managed 的边界：
 * 运行时 manifest 可以提供外部插件，但 plugin-ops 和 metadata-config-center
 * 仍然必须由 Shell 额外完成注册。
 */
describe('plugin-ops host-managed module registration', () => {
  beforeEach(() => {
    vi.resetModules();
  });

  it('includes plugin-ops in the builtin runtime plugin entries', async () => {
    const { BUILTIN_RUNTIME_PLUGIN_ENTRIES } = await import('../builtin-runtime-plugins');
    const moduleIds = BUILTIN_RUNTIME_PLUGIN_ENTRIES.map((e) => e.moduleId);
    expect(moduleIds).toContain('plugin-ops');
    expect(moduleIds).toContain('metadata-config-center');
  });

  it('does NOT include plugin-ops in the generated runtime manifest (host-managed, not runtime-installed)', async () => {
    const { createBuiltinRuntimePluginManifest } = await import('../builtin-runtime-plugins');
    const manifest = createBuiltinRuntimePluginManifest();
    const moduleIds = manifest.plugins.map((p) => p.moduleId);
    expect(moduleIds).not.toContain('plugin-ops');
    expect(moduleIds).not.toContain('metadata-config-center');
  });

  it('registerShellModules registers plugin-ops when manifest is found and does NOT include plugin-ops', async () => {
    const registered: FrontendModule[] = [];

    // 这里模拟一个运行时安装插件，用来证明即使 manifest 路径成功，
    // 宿主管理模块的补注册也必须继续发生。
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

    // mock 之后重新导入，确保 registerShellModules 真正读取到
    // 这次测试要保护的 manifest loader 和 registry 边界。
    const { registerShellModules } = await import('../registerModules');
    await registerShellModules();

    // manifest 提供的模块仍然要走正常注册流程。
    const registeredIds = registered.map((m) => m.metadata.moduleId);
    expect(registeredIds).toContain('some-other-plugin');

    // host-managed 模块是“额外补齐”，不是替代 manifest 模块。
    expect(registeredIds).toContain('plugin-ops');
    expect(registeredIds).toContain('MetadataConfigCenter');
  });

  it('plugin-ops module has metadata.moduleId matching the HOST_MANAGED_MODULE_IDS key', async () => {
    // 过滤键和值对象里的 moduleId 必须保持一致，
    // 否则 host-managed 注册路径会静默失效。
    const mod = await import('../../../../modules/plugin-ops/src/index');
    const pluginOps = mod.default as FrontendModule;

    expect(pluginOps.metadata.moduleId).toBe('plugin-ops');
  });
});
