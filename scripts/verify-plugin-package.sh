#!/usr/bin/env bash

set -euo pipefail

usage() {
  cat <<'EOF'
用法：
  ./scripts/verify-plugin-package.sh --package-path artifacts/plugin-packages/Audit
EOF
}

script_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ps_script="$script_root/verify-plugin-package.ps1"
ps_args=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --package-path)
      ps_args+=("-PackagePath" "${2:-}")
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "未知参数：$1" >&2
      usage
      exit 1
      ;;
  esac
done

if ! printf '%s\n' "${ps_args[@]}" | grep -q -- '^-PackagePath$'; then
  echo "缺少 --package-path 参数。" >&2
  usage
  exit 1
fi

if command -v pwsh >/dev/null 2>&1; then
  exec pwsh -NoProfile -ExecutionPolicy Bypass -File "$ps_script" "${ps_args[@]}"
fi

if command -v powershell.exe >/dev/null 2>&1; then
  if command -v wslpath >/dev/null 2>&1; then
    ps_script="$(wslpath -w "$ps_script")"
  fi

  exec powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$ps_script" "${ps_args[@]}"
fi

echo "未找到 pwsh 或 powershell.exe，当前环境无法通过 .sh 包装器执行校验脚本。" >&2
exit 1
