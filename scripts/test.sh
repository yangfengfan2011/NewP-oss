#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
cd "$repo_root"

echo "=== 运行测试 ==="

echo "=== 当前文档检查 ==="
pwsh -NoProfile -File "$repo_root/scripts/check-current-docs.ps1" -Root "$repo_root"

echo ""
echo "=== dic 本地化检查 ==="
pwsh "$repo_root/scripts/validate-dic-localization.ps1" -Root "$repo_root"

echo ""
# 运行后端测试
echo "=== 后端测试 ==="
dotnet test ICP-Platform.sln --configuration Release

echo ""
echo "=== 模型 DTO 同步检查 ==="
dotnet run \
  --project server/tools/model-dto-sync/ModelDtoSync.csproj \
  --configuration Release \
  --no-build \
  --no-restore \
  -- \
  --check "$repo_root"

# 运行前端测试
echo ""
echo "=== 前端测试 ==="
corepack pnpm --dir frontend test

echo ""
echo "=== 测试完成 ==="
