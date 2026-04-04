#!/usr/bin/env bash
set -euo pipefail

echo "=== 运行测试 ==="

# 运行后端测试
echo "=== 后端测试 ==="
if [ -d "tests" ] && [ "$(find tests -name '*.csproj' 2>/dev/null | wc -l)" -gt 0 ]; then
    dotnet test
else
    echo "暂无后端测试项目"
fi

# 运行前端测试
echo ""
echo "=== 前端测试 ==="
cd frontend
if grep -q '"test"' package.json 2>/dev/null; then
    pnpm test
else
    echo "暂无前端测试配置"
fi

echo ""
echo "=== 测试完成 ==="
