#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
cd "$repo_root"

echo "=== 运行测试 ==="

# 运行后端测试
echo "=== 后端测试 ==="
dotnet test ICP-Platform.sln --configuration Release

# 运行前端测试
echo ""
echo "=== 前端测试 ==="
corepack pnpm --dir frontend test

echo ""
echo "=== AI-first 设计检查 ==="
"$repo_root/scripts/check-ai-first-design-targets.sh"

echo ""
echo "=== dic 本地化检查 ==="
pwsh "$repo_root/scripts/validate-dic-localization.ps1" -Root "$repo_root"

echo ""
echo "=== 测试完成 ==="
