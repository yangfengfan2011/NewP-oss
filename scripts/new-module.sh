#!/usr/bin/env bash

set -euo pipefail

usage() {
  cat <<'EOF'
用法：
  ./scripts/new-module.sh --module-name ExampleModule [--display-name "示例模块"] [--skip-integration]
  ./scripts/new-module.sh --module-name ExternalSample --external-plugin [--output-path external-plugins/ExternalSample]
EOF
}

script_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ps_script="$script_root/new-module.ps1"
ps_args=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --module-name)
      ps_args+=("-ModuleName" "${2:-}")
      shift 2
      ;;
    --display-name)
      ps_args+=("-DisplayName" "${2:-}")
      shift 2
      ;;
    --skip-integration)
      ps_args+=("-SkipIntegration")
      shift
      ;;
    --external-plugin)
      ps_args+=("-ExternalPlugin")
      shift
      ;;
    --output-path)
      ps_args+=("-OutputPath" "${2:-}")
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

if ! printf '%s\n' "${ps_args[@]}" | grep -q -- '^-ModuleName$'; then
  echo "缺少 --module-name 参数。" >&2
  usage
  exit 1
fi

if command -v pwsh >/dev/null 2>&1; then
  echo "Built-in modules are discovered through apps/host-api/modules/<ModuleName>/plugin.json and the active module runtime catalog."
  echo "Do not modify other Host/Shell internals for normal module integration unless a separate design approves it."
  exec pwsh -NoProfile -ExecutionPolicy Bypass -File "$ps_script" "${ps_args[@]}"
fi

if command -v powershell.exe >/dev/null 2>&1; then
  if command -v wslpath >/dev/null 2>&1; then
    ps_script="$(wslpath -w "$ps_script")"
  fi

  echo "Built-in modules are discovered through apps/host-api/modules/<ModuleName>/plugin.json and the active module runtime catalog."
  echo "Do not modify other Host/Shell internals for normal module integration unless a separate design approves it."
  exec powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$ps_script" "${ps_args[@]}"
fi

echo "未找到 pwsh 或 powershell.exe，当前环境无法通过 .sh 包装器执行脚手架。" >&2
exit 1
