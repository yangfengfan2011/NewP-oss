import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { FrontendModule } from '@icp-platform/sdk';
import { DEFAULT_PLUGIN_MANIFEST_URL, loadPluginManifestWith } from './plugin-manifest-loader';

// 这组测试锁定 Shell 的运行时装载契约：
// manifest 缺失要 fail-open，单个插件错误必须保持逐条目隔离。
describe('plugin-manifest-loader', () => {
  const logger = {
    info: vi.fn(),
    warn: vi.fn(),
  };

  const runtimeModule: FrontendModule = {
    metadata: {
      moduleId: 'runtime-test',
      displayName: 'Runtime Test',
      version: '1.0.0',
    },
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('returns manifestFound: false when manifest returns 404', async () => {
    // 404 代表研发期尚未发布运行时 manifest，
    // 这里必须降级，而不是直接让 Shell 启动失败。
    const fetchManifest = vi.fn().mockResolvedValue({
      ok: false,
      status: 404,
      statusText: 'Not Found',
      json: vi.fn(),
    });

    const result = await loadPluginManifestWith({
      fetchManifest,
      logger,
    });

    expect(fetchManifest).toHaveBeenCalledWith(DEFAULT_PLUGIN_MANIFEST_URL);
    expect(result).toEqual({
      manifestFound: false,
      modules: [],
      errors: [],
    });
    expect(logger.info).toHaveBeenCalledOnce();
    expect(logger.warn).not.toHaveBeenCalled();
  });

  it('returns manifestFound: false when fetch fails with network error', async () => {
    // 传输失败与“manifest 不可用”按同一类降级处理，
    // 这样 Shell 仍能依赖宿主管理模块继续启动。
    const fetchManifest = vi.fn().mockRejectedValue(new TypeError('fetch failed'));

    const result = await loadPluginManifestWith({
      fetchManifest,
      logger,
    });

    expect(result.manifestFound).toBe(false);
    expect(result.modules).toEqual([]);
    expect(result.errors).toEqual([]);
    expect(logger.info).toHaveBeenCalledOnce();
    expect(logger.warn).not.toHaveBeenCalled();
  });

  it('loads plugins successfully from manifest', async () => {
    const fetchManifest = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        plugins: [{ url: '/plugin-assets/Audit/1.0.0/entry.js' }],
      }),
    });
    const importModule = vi.fn().mockResolvedValue({
      default: runtimeModule,
    });

    const result = await loadPluginManifestWith({
      fetchManifest,
      importModule,
      logger,
    });

    expect(result.manifestFound).toBe(true);
    expect(result.modules).toEqual([runtimeModule]);
    expect(result.errors).toEqual([]);
    expect(importModule).toHaveBeenCalledWith('/plugin-assets/Audit/1.0.0/entry.js');
  });

  it('handles partial plugin load failures and continues with successful modules', async () => {
    // 单个坏条目不能丢掉已经成功装载的模块，
    // 也不能掩盖到底是哪个插件装载失败。
    const fetchManifest = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        plugins: [
          { url: '/good-plugin.js' },
          { url: '/bad-plugin.js' },
        ],
      }),
    });
    const importModule = vi
      .fn()
      .mockResolvedValueOnce({ default: runtimeModule })
      .mockRejectedValueOnce(new Error('dynamic import failed'));

    const result = await loadPluginManifestWith({
      fetchManifest,
      importModule,
      logger,
    });

    expect(result.manifestFound).toBe(true);
    expect(result.modules).toEqual([runtimeModule]);
    expect(result.errors).toHaveLength(1);
    expect(result.errors[0].entry.url).toBe('/bad-plugin.js');
    expect(result.errors[0].error.message).toContain('dynamic import failed');
    expect(logger.warn).toHaveBeenCalledOnce();
  });

  it('handles missing named export as a plugin load error', async () => {
    // exportName 属于 manifest 契约的一部分；
    // 如果它指向不存在的导出，错误应当落在该条目自身。
    const fetchManifest = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        plugins: [{ url: '/plugin.js', exportName: 'nonExistent' }],
      }),
    });
    const importModule = vi.fn().mockResolvedValue({
      default: runtimeModule,
    });

    const result = await loadPluginManifestWith({
      fetchManifest,
      importModule,
      logger,
    });

    expect(result.manifestFound).toBe(true);
    expect(result.modules).toEqual([]);
    expect(result.errors).toHaveLength(1);
    expect(result.errors[0].error.message).toContain('does not export nonExistent');
  });

  it('uses a custom manifest URL when explicitly provided', async () => {
    // 显式注入的 manifestUrl 必须覆盖默认地址，
    // 这样测试才能把装载器固定到可控清单源。
    const customUrl = '/custom/manifest.json';
    const fetchManifest = vi.fn().mockResolvedValue({
      ok: false,
      status: 404,
      statusText: 'Not Found',
      json: vi.fn(),
    });

    await loadPluginManifestWith({
      manifestUrl: customUrl,
      fetchManifest,
      logger,
    });

    expect(fetchManifest).toHaveBeenCalledWith(customUrl);
  });

  it('resolves internal module identifiers through Vite-managed registry', async () => {
    // 稳定的 moduleId 查找保证宿主管理路径不依赖运行时 URL，
    // 这也是内建 workspace 插件可预测装载的基础。
    const fetchManifest = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        plugins: [{ moduleId: 'sample-plugin' }],
      }),
    });
    const internalModules = {
      'sample-plugin': async () => await import('./plugins/sample-plugin'),
    };

    const result = await loadPluginManifestWith({
      fetchManifest,
      internalModules,
      logger,
    });

    expect(result.manifestFound).toBe(true);
    expect(result.modules).toHaveLength(1);
    expect(result.modules[0].metadata.moduleId).toBe('sample-runtime-plugin');
    expect(result.modules[0].metadata.displayName).toBe('Sample Runtime Plugin');
    expect(result.errors).toEqual([]);
  });

  it('supports both moduleId and url in the same manifest', async () => {
    // 混合 manifest 是允许的，因为宿主管理插件和外部运行时插件
    // 可以在同一个 Shell 会话里并存。
    const fetchManifest = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        plugins: [
          { moduleId: 'sample-plugin' },
          { url: '/external-plugin.js' },
        ],
      }),
    });
    const internalModules = {
      'sample-plugin': async () => await import('./plugins/sample-plugin'),
    };
    const importModule = vi.fn().mockResolvedValue({
      default: runtimeModule,
    });

    const result = await loadPluginManifestWith({
      fetchManifest,
      internalModules,
      importModule,
      logger,
    });

    expect(result.manifestFound).toBe(true);
    expect(result.modules).toHaveLength(2);
    expect(result.modules[0].metadata.moduleId).toBe('sample-runtime-plugin');
    expect(result.modules[1].metadata.moduleId).toBe('runtime-test');
    expect(result.errors).toEqual([]);
  });

  it('reports error when internal moduleId is not found in registry', async () => {
    // 注册表缺项通常意味着宿主管理 id 漂移，或模块根本没有被打包进来；
    // 这种情况应按单条目失败，而不是拖垮整个 manifest。
    const fetchManifest = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        plugins: [{ moduleId: 'non-existent' }],
      }),
    });
    const internalModules = {
      'sample-plugin': async () => await import('./plugins/sample-plugin'),
    };

    const result = await loadPluginManifestWith({
      fetchManifest,
      internalModules,
      logger,
    });

    expect(result.manifestFound).toBe(true);
    expect(result.modules).toEqual([]);
    expect(result.errors).toHaveLength(1);
    expect(result.errors[0].error.message).toContain('Internal module non-existent not found');
  });

  it('end-to-end: loads sample-plugin using default internal module registry', async () => {
    // 这里守的是默认 import.meta.glob 注册路径，
    // 确保 workspace 模块在不依赖测试桩的情况下也能被真实解析。
    const fetchManifest = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        plugins: [{ moduleId: 'sample-plugin' }],
      }),
    });

    const result = await loadPluginManifestWith({
      fetchManifest,
      logger,
    });

    expect(result.manifestFound).toBe(true);
    expect(result.modules).toHaveLength(1);
    expect(result.modules[0].metadata.moduleId).toBe('sample-runtime-plugin');
    expect(result.modules[0].metadata.displayName).toBe('Sample Runtime Plugin');
    expect(result.errors).toEqual([]);
  });

  it('end-to-end: loads demo module using default internal module registry', async () => {
    // demo 模块覆盖了默认注册表的另一条分支，
    // 避免测试只验证一种内建 moduleId 形态。
    const fetchManifest = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      statusText: 'OK',
      json: async () => ({
        plugins: [{ moduleId: 'demo' }],
      }),
    });

    const result = await loadPluginManifestWith({
      fetchManifest,
      logger,
    });

    expect(result.manifestFound).toBe(true);
    expect(result.modules).toHaveLength(1);
    expect(result.modules[0].metadata.moduleId).toBe('demo');
    expect(result.errors).toEqual([]);
  });
});
