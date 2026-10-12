import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// UI 叶入口在组件包内编译；仅公开 exports 可以解析，根入口不得吞掉子路径。
export function createUiSourceAliases() {
  const root = fileURLToPath(new URL('../packages/ui/', import.meta.url));
  const pkg = JSON.parse(readFileSync(resolve(root, 'package.json'), 'utf8'));
  return Object.entries(pkg.exports).map(([subpath, entry]) => {
    const target = typeof entry === 'string' ? entry : entry.import;
    const candidates = target.startsWith('./src/')
      ? [resolve(root, target)]
      : target.startsWith('./dist/') && target.endsWith('.js')
        ? ['.ts', '.tsx'].map(extension => resolve(root, target.replace('./dist/', './src/').slice(0, -3) + extension))
        : [];
    const sources = candidates.filter(existsSync);
    if (sources.length !== 1) throw new Error(`UI_SOURCE_EXPORT_INVALID: ${pkg.name}${subpath.slice(1)} must have one existing source owner.`);
    const specifier = pkg.name + (subpath === '.' ? '' : subpath.slice(1));
    return { find: new RegExp(`^${specifier.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`), replacement: sources[0] };
  });
}
