#!/usr/bin/env bash
set -euo pipefail

echo "=== 初始化 ICP 开发环境 ==="

if ! command -v dotnet >/dev/null 2>&1; then
  echo "错误: 未找到 .NET SDK，请先安装 .NET SDK。"
  exit 1
fi

echo ".NET SDK 已安装: $(dotnet --version)"

if ! command -v node >/dev/null 2>&1; then
  echo "错误: 未找到 Node.js，请先安装 Node.js 18 或更高版本。"
  exit 1
fi

echo "Node.js 已安装: $(node --version)"

if ! command -v pnpm >/dev/null 2>&1; then
  echo "未找到 pnpm，开始安装..."
  npm install -g pnpm
fi

echo "pnpm 已安装: $(pnpm --version)"

echo
echo "=== 恢复后端依赖 ==="
dotnet restore

echo
echo "=== 安装前端依赖 ==="
cd frontend
pnpm install
cd ..

echo
echo "=== 初始化完成 ==="
echo
echo "后续步骤:"
echo "  1. 一键启动前后端: ./scripts/dev.sh"
echo "  2. 仅启动后端: cd apps/host-api && dotnet run"
echo "  3. 仅启动前端: cd frontend && pnpm dev"
echo
