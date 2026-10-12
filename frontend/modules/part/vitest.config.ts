import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { existsSync } from 'node:fs';
import { defineConfig } from 'vitest/config';

// 源码根归组后，仍从真实 workspace 根找到平台共享包。
let frontendRoot = dirname(fileURLToPath(import.meta.url));
while (!existsSync(join(frontendRoot, 'pnpm-workspace.yaml'))) {
  const parent = dirname(frontendRoot);
  if (parent === frontendRoot) throw new Error('Frontend workspace not found.');
  frontendRoot = parent;
}
export default defineConfig({
  resolve: { alias: [
    { find: '@icp-platform/core', replacement: join(frontendRoot, 'packages/core/src/index.ts') },
    { find: '@icp-platform/sdk', replacement: join(frontendRoot, 'packages/sdk/src/index.ts') },
  ] },
});