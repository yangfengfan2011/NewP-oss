import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import type { FrontendBehaviorBindings } from '@icp-platform/sdk';

// Vite 配置已构建正式 Catalog 工具；测试只读取同一映射，不在并行 worker 中重建它。
const repositoryRoot = resolve(__dirname, '../../../..');
const output = execFileSync('dotnet', ['run', '--project', resolve(repositoryRoot, 'server/tools/component-source-build/ComponentSource.Build.csproj'),
  '-c', 'Release', '--no-build', '--no-restore', '--', 'frontend-entries', repositoryRoot],
{ encoding: 'utf8', timeout: 60000, windowsHide: true });
const entries = JSON.parse(output.trim().split(/\r?\n/).at(-1)!) as Record<string, string>;
const roots = new Map(Object.values(entries).map(entry => dirname(dirname(entry)))
  .filter(root => existsSync(resolve(root, 'newp.plugin.json')))
  .map(root => [JSON.parse(readFileSync(resolve(root, 'newp.plugin.json'), 'utf8')).moduleId as string, root]));
const sources = import.meta.glob<Record<string, unknown>>('../../../modules/**/src/*.{ts,tsx}');

export function componentRoot(moduleId: string): string {
  const root = roots.get(moduleId);
  if (!root) throw new Error(`Authoring component '${moduleId}' was not found through the formal catalog.`);
  return root;
}

export function readComponentJson(moduleId: string, relative: string) {
  return JSON.parse(readFileSync(resolve(componentRoot(moduleId), relative), 'utf8'));
}

export async function loadComponentSource<T = Record<string, unknown>>(moduleId: string, relative: string): Promise<T> {
  const absolute = resolve(componentRoot(moduleId), 'src', relative);
  const loader = Object.entries(sources).find(([path]) => resolve(__dirname, path) === absolute)?.[1];
  if (!loader) throw new Error(`Component source '${moduleId}/${relative}' was not found.`);
  const loaded = await loader();
  // index 的真实懒资源同样在收集期预载，冷转换不进入交互测试预算。
  if (relative === 'index.ts') {
    const bindings = loaded.default as FrontendBehaviorBindings;
    await Promise.all((bindings.standardCrudCapabilities ?? []).map(binding => binding.load()));
  }
  return loaded as T;
}
