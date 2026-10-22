import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { SourceTextModule, runInContext } from 'node:vm';
import { build, loadConfigFromFile } from 'vite';

test('the real Workspace browser release evaluates every chunk without Node globals', async t => {
  const frontendRoot = fileURLToPath(new URL('../', import.meta.url));
  const shellRoot = join(frontendRoot, 'packages/shell');
  const require = createRequire(join(shellRoot, 'package.json'));
  const { JSDOM } = require('jsdom');
  const root = mkdtempSync(join(tmpdir(), 'newp-component-browser-'));
  const dom = new JSDOM('<!doctype html><html><body></body></html>', { runScripts: 'outside-only' });
  try {
    execFileSync(process.execPath, [join(frontendRoot, 'scripts/build-frontend-plugin.mjs'), 'workspace',
      '--runtime-release', join(frontendRoot, '../server/plugin-platform/ComponentBundles/platform-runtime-release.json')],
    { cwd: frontendRoot, stdio: 'pipe', windowsHide: true });
    const shell = (await loadConfigFromFile({ command: 'build', mode: 'production' }, join(shellRoot, 'vite.config.ts'))).config;
    const bridge = shell.plugins.flat().find(plugin => plugin && plugin.name === 'newp-native-shared-runtime');
    assert.ok(bridge, 'the actual Shell shared-runtime provider must participate');
    writeFileSync(join(root, 'entry.mjs'), 'export {};');
    await build({ configFile: false, root: shellRoot, mode: 'production', plugins: [bridge],
      resolve: shell.resolve, logLevel: 'silent',
      build: { outDir: join(root, 'dist'), emptyOutDir: true,
        rollupOptions: { input: join(root, 'entry.mjs') } },
    });
    const context = dom.getInternalVMContext();
    assert.equal(runInContext('typeof process', context), 'undefined');
    assert.equal(runInContext('typeof require', context), 'undefined');
    assert.equal(runInContext('typeof Buffer', context), 'undefined');
    assert.equal(runInContext('typeof global', context), 'undefined');
    const shared = {
      '@icp-platform/core': 'core', '@icp-platform/sdk': 'sdk', react: 'react',
      'react/jsx-runtime': 'react-jsx', 'react/jsx-dev-runtime': 'react-jsx-dev',
      'react-dom': 'react-dom', 'react-dom/client': 'react-dom-client',
    };
    const modules = new Map();
    // 所有依赖在同一无 Node 全局的浏览器 context 中链接，不能用 Node import 掩盖自由 process。
    const load = url => {
      if (!modules.has(url.href)) modules.set(url.href, new SourceTextModule(readFileSync(url, 'utf8'), {
        context, identifier: url.href,
        importModuleDynamically: async (specifier, referencing) => evaluate(resolveModule(specifier, referencing)),
      }));
      return modules.get(url.href);
    };
    const resolveModule = (specifier, referencing) => {
      const url = specifier in shared
        ? pathToFileURL(join(root, 'dist/shared-runtime', shared[specifier] + '.js'))
        : new URL(specifier, referencing.identifier);
      assert.equal(url.protocol, 'file:');
      return load(url);
    };
    const evaluate = async module => {
      if (module.status === 'unlinked') await module.link(resolveModule);
      if (module.status === 'linked') await module.evaluate();
      return module;
    };
    const artifactsRoot = join(frontendRoot, '../artifacts/frontend-plugins/workspace');
    const index = JSON.parse(readFileSync(join(artifactsRoot, 'frontend-artifacts.json'), 'utf8'));
    const chunks = Object.keys(index.files).filter(file => /\.(?:mjs|js)$/.test(file));
    assert.ok(chunks.some(file => file.startsWith('WorkspaceObjectCreateInput-')));
    assert.ok(chunks.some(file => file.startsWith('WorkspaceObjectEditInput-')));
    for (const chunk of chunks) await evaluate(load(pathToFileURL(join(artifactsRoot, chunk))));
    for (const slug of Object.values(shared)) await evaluate(load(pathToFileURL(join(root, 'dist/shared-runtime', slug + '.js'))));
    assert.equal(runInContext('typeof process', context), 'undefined');
    t.diagnostic(JSON.stringify({ browserChunks: chunks.length, sharedBridges: Object.keys(shared).length, process: 'undefined' }));
  } finally {
    dom.window.close();
    // root 由本测试独占创建，仅清理该精确临时目录。
    rmSync(root, { recursive: true, force: true });
  }
});
