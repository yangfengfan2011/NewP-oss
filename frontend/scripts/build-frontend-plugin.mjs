#!/usr/bin/env node
import { build } from 'vite';
import { resolve, dirname, relative, isAbsolute } from 'path';
import { fileURLToPath } from 'url';
import { existsSync } from 'fs';
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
        external: [
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
