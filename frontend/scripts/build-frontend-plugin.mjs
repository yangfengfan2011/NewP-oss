#!/usr/bin/env node
import { build } from 'vite';
import { resolve, dirname, relative, isAbsolute } from 'path';
import { fileURLToPath } from 'url';
import { existsSync, readFileSync } from 'fs';
import { createRequire } from 'node:module';
import { createHash } from 'node:crypto';
import { discoverFrontendEntries } from './component-sources.mjs';

const __dirname = dirname(fileURLToPath(import.meta.url));

// Get module name from command line args
const moduleName = process.argv[2];

if (!moduleName) {
  console.error('Error: Module name is required');
  console.error('Usage: node build-frontend-plugin.mjs <module-name>');
  process.exit(1);
}

const hasInvalidModuleName =
  isAbsolute(moduleName) ||
  moduleName.includes('/') ||
  moduleName.includes('\\') ||
  moduleName === '.' ||
  moduleName === '..' ||
  moduleName.includes('..');

if (hasInvalidModuleName) {
  console.error('Error: Module name must not contain path separators or traversal segments');
  process.exit(1);
}

// Construct paths
const modulesRoot = resolve(__dirname, '../modules');
const uiSourceRoot = resolve(__dirname, '../packages/ui/src');
const artifactsRoot = resolve(__dirname, '../../artifacts/frontend-plugins');
const entryPath = discoverFrontendEntries(resolve(__dirname, '../..'))[moduleName];
const outDir = resolve(artifactsRoot, moduleName);
const releaseOption = process.argv.indexOf('--runtime-release');
const releaseBytes = releaseOption >= 0 ? readFileSync(process.argv[releaseOption + 1]) : null;
const release = releaseBytes ? JSON.parse(releaseBytes.toString('utf8')) : null;
const sharedPackages = release ? Object.keys(release.frontend).filter((key) => key !== 'binding') : [];

// 正式发行核对实际解析依赖；开发期原入口继续沿现有配置构建。
if (release) {
  const require = createRequire(resolve(__dirname, '../package.json'));
  for (const packageName of sharedPackages) {
    const packagePath = packageName.startsWith('@icp-platform/')
      ? resolve(__dirname, '../packages', packageName.slice('@icp-platform/'.length), 'package.json')
      : require.resolve(`${packageName}/package.json`);
    const actual = JSON.parse(readFileSync(packagePath, 'utf8')).version;
    if (actual !== release.frontend[packageName]) {
      throw new Error(`COMPONENT_BUNDLE_RUNTIME_MISMATCH: ${packageName} expected ${release.frontend[packageName]}, actual ${actual}`);
    }
  }
}

const isSharedImport = (id) => sharedPackages.some((name) => id === name || id.startsWith(`${name}/`));
const artifactIndex = {
  name: 'newp-component-frontend-artifacts',
  generateBundle(_options, bundle) {
    if (!release) return;
    const files = Object.fromEntries(Object.keys(bundle).sort().map((name) => {
      const artifact = bundle[name];
      return [name, artifact.type === 'chunk' ? {
        imports: [...artifact.imports, ...artifact.dynamicImports].filter((id) => !isSharedImport(id)).sort(),
        sharedImports: [...artifact.imports, ...artifact.dynamicImports].filter(isSharedImport).sort(),
        assets: [...artifact.referencedFiles].sort(),
      } : { imports: [], sharedImports: [], assets: [] }];
    }));
    this.emitFile({ type: 'asset', fileName: 'frontend-artifacts.json', source: JSON.stringify({
      schemaVersion: 1, entry: 'entry.js',
      platformRuntime: { version: release.version, contentDigest: createHash('sha256').update(releaseBytes).digest('hex') },
      files,
    }, null, 2) });
  },
};

const isPathInside = (parentPath, childPath) => {
  const relativePath = relative(parentPath, childPath);
  return relativePath !== '' && !relativePath.startsWith('..') && !isAbsolute(relativePath);
};

if (!entryPath || !isPathInside(modulesRoot, entryPath)) {
  console.error(`Error: Resolved entry path must stay under ${modulesRoot}`);
  process.exit(1);
}

if (!isPathInside(artifactsRoot, outDir)) {
  console.error(`Error: Resolved output path must stay under ${artifactsRoot}`);
  process.exit(1);
}

// Check if entry file exists
if (!existsSync(entryPath)) {
  console.error(`Error: Entry file not found at ${entryPath}`);
  process.exit(1);
}

console.log(`Building frontend plugin: ${moduleName}`);
console.log(`Entry: ${entryPath}`);
console.log(`Output: ${outDir}/entry.js`);

try {
  await build({
    configFile: false,
    plugins: [artifactIndex],
    resolve: {
      alias: [
        { find: '@icp-platform/ui/core', replacement: resolve(uiSourceRoot, 'core.ts') },
        { find: '@icp-platform/ui', replacement: resolve(uiSourceRoot, 'index.ts') }
      ]
    },
    build: {
      lib: {
        entry: entryPath,
        formats: ['es'],
        fileName: () => 'entry.js'
      },
      outDir,
      emptyOutDir: true,
      rollupOptions: {
        external: release ? isSharedImport : [
          'react',
          'react-dom',
          '@icp-platform/sdk',
          '@icp-platform/core'
        ],
        output: {
          globals: {
            react: 'React',
            'react-dom': 'ReactDOM'
          }
        }
      }
    }
  });
  
  console.log(`Successfully built ${moduleName} plugin`);
} catch (error) {
  console.error(`Failed to build ${moduleName} plugin:`, error);
  process.exit(1);
}
