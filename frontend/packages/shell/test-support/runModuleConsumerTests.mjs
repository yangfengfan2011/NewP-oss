// 模块元数据远端消费者测试入口：运行所有经 componentTestInputs 读取真实模块元数据或源码的测试。
// 约定：测试文件 import 了 componentTestInputs 即属于此类，无需另行登记；新增消费者自动纳入。
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { startVitest } from 'vitest/node';

const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const testsRoot = join(packageRoot, 'src');

function* testFiles(directory) {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) yield* testFiles(path);
    else if (/\.test\.tsx?$/.test(entry.name)) yield path;
  }
}

const consumers = [...testFiles(testsRoot)]
  .filter(path => readFileSync(path, 'utf8').includes('componentTestInputs'))
  .map(path => relative(packageRoot, path).split(sep).join('/'));
if (consumers.length === 0) throw new Error('未发现 componentTestInputs 消费者，请检查入口约定。');
console.log(`module metadata consumers: ${consumers.length} files`);

const vitest = await startVitest('test', consumers, { run: true, root: packageRoot });
await vitest?.close();
process.exit(process.exitCode ?? 0);
