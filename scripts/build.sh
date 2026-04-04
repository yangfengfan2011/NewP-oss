#!/usr/bin/env bash
set -euo pipefail

echo "=== 构建 ICP 新平台 ==="

# 构建后端
echo "=== 构建后端 ==="
dotnet build ICP-Platform.sln

# 构建前端
echo ""
echo "=== 构建前端 ==="
cd frontend
pnpm build

echo ""
echo "=== 构建完成 ==="
