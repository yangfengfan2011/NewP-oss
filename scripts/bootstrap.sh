#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
frontend_root="$repo_root/frontend"

echo "=== 初始化 ICP 开发环境 ==="

if ! command -v dotnet >/dev/null 2>&1; then
  echo "错误: 未找到 .NET SDK，请先安装 .NET SDK。"
  exit 1
fi

echo ".NET SDK 已安装: $(dotnet --version)"

if ! command -v node >/dev/null 2>&1; then
  echo "错误: 未找到 Node.js。"
  exit 1
fi

if ! toolchain_spec="$(node -e 'const config = require(process.argv[1]); const manager = config.packageManager; const runtime = config.devEngines && config.devEngines.runtime; if (!/^pnpm@\d+\.\d+\.\d+$/.test(manager) || !runtime || runtime.name !== "node" || !/^\d+\.\d+\.\d+$/.test(runtime.version)) process.exit(1); process.stdout.write(runtime.version + "|" + manager.slice(5));' "$frontend_root/package.json")"; then
  echo "错误: frontend/package.json 必须声明精确的 Node.js 与 pnpm 版本。"
  exit 1
fi

required_node_version="${toolchain_spec%%|*}"
required_pnpm_version="${toolchain_spec#*|}"
node_version="$(node --version)"
node_version="${node_version#v}"

if [ "$node_version" != "$required_node_version" ]; then
  echo "错误: 当前 Node.js 版本为 $node_version，需要精确版本 $required_node_version。"
  exit 1
fi

echo "Node.js 已安装: $node_version"

if ! command -v corepack >/dev/null 2>&1; then
  echo "错误: 未找到 Corepack，无法按 packageManager 使用 pnpm $required_pnpm_version。"
  exit 1
fi

if ! current_pnpm_version="$(cd "$frontend_root" && corepack pnpm --version)"; then
  echo "错误: Corepack 未能提供 packageManager 指定的 pnpm $required_pnpm_version。"
  exit 1
fi

if [ "$current_pnpm_version" != "$required_pnpm_version" ]; then
  echo "错误: Corepack 提供的 pnpm 版本为 $current_pnpm_version，需要 $required_pnpm_version。"
  exit 1
fi

echo "pnpm 已就绪（Corepack）: $current_pnpm_version"

echo
echo "=== 恢复后端依赖 ==="
dotnet restore ICP-Platform.sln

echo
echo "=== 安装前端依赖 ==="
(cd "$frontend_root" && corepack pnpm install --frozen-lockfile)

echo
echo "=== 初始化完成 ==="
echo
echo "后续步骤:"
echo "  1. 一键启动前后端: ./scripts/dev.sh"
echo "  2. 仅启动后端: cd apps/host-api && dotnet run"
echo "  3. 仅启动前端: cd frontend && corepack pnpm --filter @icp-platform/shell dev"
echo
