// @vitest-environment node

import { describe, expect, it, vi } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath, URL } from 'node:url';
import config from '../../vite.config';

describe('vite config', () => {
  it('includes the merged Part authoring declaration in the implementation registry', () => {
    const serverConfig = typeof config === 'function' ? config({ command: 'serve', mode: 'test' }) : config;
    const registry = (serverConfig.plugins ?? []).flat()
      .find((plugin) => plugin && plugin.name === 'newp-frontend-entry-registry');
    if (!registry || !('load' in registry) || typeof registry.load !== 'function') {
      throw new Error('Frontend entry registry is missing.');
    }
    const source = registry.load.call({} as never, '\0virtual:newp-frontend-entry-registry', {});
    expect(source).toContain('"part":');
    expect(source).toContain('/part/src/index.ts');
  });

  it('keeps metadata api traffic under the shared /api proxy so page routes do not collide', () => {
    const serverConfig = typeof config === 'function' ? config({ command: 'serve', mode: 'test' }) : config;
    const proxy = serverConfig.server?.proxy;

    expect(proxy).toBeDefined();
    expect(proxy).toHaveProperty('/api');
    expect(proxy?.['/api']).toMatchObject({
      target: 'http://127.0.0.1:5000',
      changeOrigin: true,
    });
    expect(serverConfig.server).toMatchObject({
      host: '127.0.0.1',
      port: 3000,
      strictPort: true,
    });
    expect(proxy).not.toHaveProperty('/metadata-config');
  });

  it('proxies only compiler-owned runtime resources under /plugins so page routes remain SPA routes', () => {
    const serverConfig = typeof config === 'function' ? config({ command: 'serve', mode: 'test' }) : config;
    const proxy = serverConfig.server?.proxy;
    const runtimeProxy = Object.entries(proxy ?? {})
      .find(([context]) => context.startsWith('^/plugins/'));

    expect(proxy).not.toHaveProperty('/plugins');
    expect(runtimeProxy).toBeDefined();
    expect(runtimeProxy?.[1]).toMatchObject({
      target: 'http://127.0.0.1:5000',
      changeOrigin: true,
    });
    const matchesRuntimeProxy = new RegExp(runtimeProxy?.[0] ?? '$a');
    expect(matchesRuntimeProxy.test('/plugins/active-module-runtime.json')).toBe(true);
    expect(matchesRuntimeProxy.test('/plugins/frontend-plugin-manifest.json')).toBe(true);
    expect(matchesRuntimeProxy.test('/plugins/localization-manifest.json')).toBe(true);
    expect(matchesRuntimeProxy.test('/plugins/metadata-runtime-manifest.json')).toBe(true);
    expect(matchesRuntimeProxy.test('/plugins/runtime/modules/Partner.AssetCatalog/metadata/models/Asset.model.json')).toBe(true);
    expect(matchesRuntimeProxy.test('/plugins/partner-asset-catalog/assets')).toBe(false);
  });

  it('builds the implementation registry outside the runtime loader', () => {
    const serverConfig = typeof config === 'function' ? config({ command: 'serve', mode: 'test' }) : config;
    const pluginNames = (serverConfig.plugins ?? [])
      .flat()
      .filter((plugin): plugin is Exclude<typeof plugin, false | null | undefined> => Boolean(plugin))
      .map((plugin) => plugin.name);
    const loaderSource = readFileSync(
      fileURLToPath(new URL('../active-module-runtime-loader.ts', import.meta.url)),
      'utf8',
    );

    expect(pluginNames).toContain('newp-frontend-entry-registry');
    expect(loaderSource).not.toContain(['import', 'meta', 'glob'].join('.'));
    expect(loaderSource).not.toContain(['..', '..', '..', 'modules', '*', 'src', 'index.ts'].join('/'));
  });

  it('emits production analysis artifacts without hiding Vite chunk warnings or forcing chunk policy', () => {
    const serverConfig = typeof config === 'function' ? config({ command: 'build', mode: 'production' }) : config;

    expect(serverConfig.build).toMatchObject({
      manifest: true,
      sourcemap: true,
    });
    expect(serverConfig.build).not.toHaveProperty('chunkSizeWarningLimit');
    expect(serverConfig.build?.rollupOptions?.output).toBeUndefined();
  });

  it('redirects localhost requests once to the IPv4 shell origin', () => {
    const serverConfig = typeof config === 'function' ? config({ command: 'serve', mode: 'test' }) : config;
    const redirectPlugin = (serverConfig.plugins ?? [])
      .flat()
      .find((plugin) => plugin && plugin.name === 'newp-localhost-ipv4-redirect');
    const configureServer = redirectPlugin && 'configureServer' in redirectPlugin
      ? redirectPlugin.configureServer
      : undefined;
    expect(typeof configureServer).toBe('function');

    const use = vi.fn();
    if (typeof configureServer !== 'function') {
      throw new Error('IPv4 redirect plugin is missing configureServer.');
    }
    configureServer({ middlewares: { use } } as never);
    const middleware = use.mock.calls[0]?.[0] as (
      request: { headers: { host?: string }; url?: string },
      response: { statusCode: number; setHeader: ReturnType<typeof vi.fn>; end: ReturnType<typeof vi.fn> },
      next: ReturnType<typeof vi.fn>,
    ) => void;
    const response = { statusCode: 200, setHeader: vi.fn(), end: vi.fn() };
    const next = vi.fn();

    middleware(
      { headers: { host: 'localhost:3000' }, url: '/login?source=bookmark' },
      response,
      next,
    );

    expect(response.statusCode).toBe(307);
    expect(response.setHeader).toHaveBeenCalledWith(
      'Location',
      'http://127.0.0.1:3000/login?source=bookmark',
    );
    expect(response.end).toHaveBeenCalledOnce();
    expect(next).not.toHaveBeenCalled();
  });
});
