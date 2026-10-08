import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  resolve: { alias: [
    { find: '@icp-platform/core', replacement: fileURLToPath(new URL('../../packages/core/src/index.ts', import.meta.url)) },
    { find: '@icp-platform/sdk', replacement: fileURLToPath(new URL('../../packages/sdk/src/index.ts', import.meta.url)) },
  ] },
});
