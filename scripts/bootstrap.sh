#!/usr/bin/env bash
set -euo pipefail

echo "=== 初始化 ICP 新平台开发环境 ==="

# 检查 .NET SDK
if ! command -v dotnet &> /dev/null; then
    echo "错误: 未找到 .NET SDK，请先安装 .NET 8.0 或更高版本"
    exit 1
fi

echo "✓ .NET SDK 已安装: $(dotnet --version)"

# 检查 Node.js
if ! command -v node &> /dev/null; then
    echo "错误: 未找到 Node.js，请先安装 Node.js 18 或更高版本"
    exit 1
fi

echo "✓ Node.js 已安装: $(node --version)"

# 检查 pnpm
if ! command -v pnpm &> /dev/null; then
    echo "正在安装 pnpm..."
    npm install -g pnpm
fi

echo "✓ pnpm 已安装: $(pnpm --version)"

# 恢复后端依赖
echo ""
echo "=== 恢复后端依赖 ==="
dotnet restore

# 安装前端依赖
echo ""
echo "=== 安装前端依赖 ==="
cd frontend
pnpm install

echo ""
echo "=== 初始化完成 ==="
echo ""
echo "后续步骤:"
echo "  1. 运行后端: cd apps/host-api && dotnet run"
echo "  2. 运行前端: cd frontend && pnpm dev"
echo ""
