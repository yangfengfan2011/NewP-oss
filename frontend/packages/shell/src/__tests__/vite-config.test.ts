// @vitest-environment node

import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath, URL } from 'node:url';
import config from '../../vite.config';

describe('vite config', () => {
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
});
