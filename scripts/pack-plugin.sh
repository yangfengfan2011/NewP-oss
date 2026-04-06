#!/usr/bin/env bash

set -euo pipefail

usage() {
  cat <<'EOF'
用法：
  ./scripts/pack-plugin.sh --module-name Audit
  ./scripts/pack-plugin.sh --project-path server/modules/Audit/AuditModule.csproj [--configuration Release] [--output-root artifacts/plugin-packages] [--host-version-min 1.0.0] [--host-version-max 2.0.0] [--force]
EOF
}

script_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ps_script="$script_root/pack-plugin.ps1"
ps_args=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --module-name)
      ps_args+=("-ModuleName" "${2:-}")
      shift 2
      ;;
    --project-path)
      ps_args+=("-ProjectPath" "${2:-}")
      shift 2
      ;;
    --configuration)
      ps_args+=("-Configuration" "${2:-}")
      shift 2
      ;;
    --output-root)
      ps_args+=("-OutputRoot" "${2:-}")
      shift 2
      ;;
    --host-version-min)
      ps_args+=("-HostVersionMin" "${2:-}")
      shift 2
      ;;
    --host-version-max)
      ps_args+=("-HostVersionMax" "${2:-}")
      shift 2
      ;;
    --force)
      ps_args+=("-Force")
      shift
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

if ! printf '%s\n' "${ps_args[@]}" | grep -Eq -- '^-ProjectPath$|^-ModuleName$'; then
  echo "缺少 --project-path 或 --module-name 参数。" >&2
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

echo "未找到 pwsh 或 powershell.exe，当前环境无法通过 .sh 包装器执行打包脚本。" >&2
exit 1
