import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { FrontendModule } from '@icp-platform/sdk';
import { DEFAULT_PLUGIN_MANIFEST_URL, loadPluginManifestWith } from './plugin-manifest-loader';

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
        plugins: [{ url: '/test-plugin.js' }],
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
    expect(importModule).toHaveBeenCalledWith('/test-plugin.js');
  });

  it('handles partial plugin load failures and continues with successful modules', async () => {
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