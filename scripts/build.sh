#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
cd "$repo_root"

echo "=== 构建 ICP 新平台 ==="

# 构建后端
echo "=== 构建后端 ==="
dotnet build ICP-Platform.sln --configuration Release

# 构建前端
echo ""
echo "=== 构建前端 ==="
corepack pnpm --dir frontend build

echo ""
echo "=== 构建完成 ==="
