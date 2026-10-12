import { execFileSync } from 'node:child_process';
import { join } from 'node:path';

// 前端构建沿同一正式 Catalog 取当前映射；dotnet run 同时防止旁读过期构建工具。
export function discoverFrontendEntries(repositoryRoot) {
  const project = join(repositoryRoot, 'server/tools/component-source-build/ComponentSource.Build.csproj');
  const output = execFileSync('dotnet', ['run', '--project', project, '-c', 'Release', '--verbosity', 'quiet', '--no-launch-profile', '--', 'frontend-entries', repositoryRoot], {
    encoding: 'utf8', timeout: 60000, windowsHide: true,
  });
  return JSON.parse(output.trim().split(/\r?\n/).at(-1));
}
