#!/usr/bin/env bash

set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  ./scripts/pack-unified-plugin.sh --module-name Audit [--configuration Release] [--output-root artifacts/plugin-packages] [--force]
EOF
}

script_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ps_script="$script_root/pack-unified-plugin.ps1"
module_name=""
configuration=""
output_root=""
force=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --module-name)
      module_name="${2:-}"
      shift 2
      ;;
    --configuration)
      configuration="${2:-}"
      shift 2
      ;;
    --output-root)
      output_root="${2:-}"
      shift 2
      ;;
    --force)
      force=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 1
      ;;
  esac
done

if [[ -z "$module_name" ]]; then
  echo "Missing --module-name." >&2
  usage
  exit 1
fi

ps_args=("-ModuleName" "$module_name")

if [[ -n "$configuration" ]]; then
  ps_args+=("-Configuration" "$configuration")
fi

if [[ -n "$output_root" ]]; then
  ps_args+=("-OutputRoot" "$output_root")
fi

if [[ "$force" == true ]]; then
  ps_args+=("-Force")
fi

if command -v pwsh >/dev/null 2>&1; then
  exec pwsh -NoProfile -ExecutionPolicy Bypass -File "$ps_script" "${ps_args[@]}"
fi

if command -v powershell.exe >/dev/null 2>&1; then
  if command -v wslpath >/dev/null 2>&1; then
    ps_script="$(wslpath -w "$ps_script")"
  fi

  ps_args_for_powershell=("-ModuleName" "$module_name")

  if [[ -n "$configuration" ]]; then
    ps_args_for_powershell+=("-Configuration" "$configuration")
  fi

  if [[ -n "$output_root" ]]; then
    if command -v wslpath >/dev/null 2>&1; then
      output_root="$(wslpath -w "$output_root")"
    fi
    ps_args_for_powershell+=("-OutputRoot" "$output_root")
  fi

  if [[ "$force" == true ]]; then
    ps_args_for_powershell+=("-Force")
  fi

  exec powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$ps_script" "${ps_args_for_powershell[@]}"
fi

echo "Neither pwsh nor powershell.exe was found, so this wrapper cannot run the pack script." >&2
exit 1
