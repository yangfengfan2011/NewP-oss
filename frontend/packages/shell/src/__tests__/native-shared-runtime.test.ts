// @vitest-environment node

import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'vite';
import { expect, it } from 'vitest';
import config from '../../vite.config';

it('links all production native bridges and shares their exports with the Shell module graph', async () => {
  const shellRoot = fileURLToPath(new URL('../..', import.meta.url));
  const require = createRequire(join(shellRoot, 'package.json'));
  const shell = typeof config === 'function' ? config({ command: 'build', mode: 'production' }) : config;
  const bridge = (shell.plugins ?? []).flat().find(plugin => plugin && plugin.name === 'newp-native-shared-runtime');
  if (!bridge) throw new Error('Native shared runtime build plugin is missing.');
  const providers = [
    { name: '@icp-platform/core', slug: 'core', hasDefault: false },
    { name: '@icp-platform/sdk', slug: 'sdk', hasDefault: false },
    { name: 'react', slug: 'react', hasDefault: true },
    { name: 'react/jsx-runtime', slug: 'react-jsx', hasDefault: false },
    { name: 'react/jsx-dev-runtime', slug: 'react-jsx-dev', hasDefault: false },
    { name: 'react-dom', slug: 'react-dom', hasDefault: true },
    { name: 'react-dom/client', slug: 'react-dom-client', hasDefault: true },
  ];
  const aliases = shell.resolve?.alias as Record<string, string>;
  const root = mkdtempSync(join(tmpdir(), 'newp-shared-runtime-'));
  try {
    writeFileSync(join(root, 'package.json'), '{"type":"module"}');
    // 消费者从实际 provider 导入；桥与它必须共享同一个生产构建图，不能以 mock 模拟导出。
    writeFileSync(join(root, 'consumer.mjs'), providers.map((provider, index) =>
      `import ${index < 2 ? '* as ' : ''}provider${index} from ${JSON.stringify(provider.name)}; export { provider${index} };`).join('\n'));
    await build({
      configFile: false, root: shellRoot, mode: 'production', plugins: [bridge],
      define: { 'process.env.NODE_ENV': '"production"' },
      resolve: { alias: providers.map(provider => ({
        find: new RegExp(`^${provider.name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`),
        replacement: aliases[provider.name] ?? require.resolve(provider.name),
      })) },
      build: { outDir: join(root, 'dist'), emptyOutDir: true, rollupOptions: {
        input: join(root, 'consumer.mjs'), preserveEntrySignatures: 'strict', output: { entryFileNames: 'consumer.js' },
      } },
    });
    // 独立 Node ESM loader 链接真实产物，避免 Vitest 转译修饰掩盖 CommonJS 导出缺失。
    writeFileSync(join(root, 'verify.mjs'), `
import { Suspense, createElement, lazy } from './dist/shared-runtime/react.js';
import * as consumer from './dist/consumer.js';
import assert from 'node:assert/strict';
assert.equal(typeof createElement, 'function');
assert.equal(typeof lazy, 'function');
assert.equal(typeof Suspense, 'symbol');
let bindings = 0;
for (const [index, provider] of ${JSON.stringify(providers)}.entries()) {
  const actual = await import('./dist/shared-runtime/' + provider.slug + '.js');
  const expected = consumer['provider' + index];
  const names = Object.keys(expected).filter(name => name !== '__esModule' && name !== 'default');
  assert.deepEqual(Object.keys(actual).sort(), [...names, ...(provider.hasDefault ? ['default'] : [])].sort(), provider.name);
  for (const name of names) assert.equal(actual[name], expected[name], provider.name + '.' + name);
  if (provider.hasDefault) assert.equal(actual.default, expected, provider.name + '.default');
  bindings += names.length;
}
console.log(JSON.stringify({ bridges: 7, bindings }));
`);
    const result = JSON.parse(execFileSync(process.execPath, [join(root, 'verify.mjs')], {
      cwd: root, encoding: 'utf8', windowsHide: true, timeout: 5000,
    }).trim());
    expect(result.bridges).toBe(7);
    expect(result.bindings).toBeGreaterThan(0);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
