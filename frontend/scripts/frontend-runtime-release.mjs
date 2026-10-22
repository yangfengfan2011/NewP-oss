import { createRequire } from 'node:module';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';

const frontendRoot = fileURLToPath(new URL('../', import.meta.url));
const require = createRequire(resolve(frontendRoot, 'package.json'));

// 平台 Shell 和组件 builder 使用同一受控发行声明核对实际解析依赖，不另维护版本名单。
export function validateFrontendRuntimeRelease(release) {
  const packages = Object.keys(release.frontend).filter((name) => name !== 'binding');
  for (const name of packages) {
    const path = name.startsWith('@icp-platform/')
      ? resolve(frontendRoot, 'packages', name.slice('@icp-platform/'.length), 'package.json')
      : require.resolve(`${name}/package.json`);
    const actual = JSON.parse(readFileSync(path, 'utf8')).version;
    if (actual !== release.frontend[name]) {
      throw new Error(`COMPONENT_BUNDLE_RUNTIME_MISMATCH: ${name} expected ${release.frontend[name]}, actual ${actual}`);
    }
  }
  return packages;
}
