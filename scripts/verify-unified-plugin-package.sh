#!/usr/bin/env bash

set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  ./scripts/verify-unified-plugin-package.sh --package-path <path>
EOF
}

script_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ps_script="$script_root/verify-unified-plugin-package.ps1"
package_path=""

is_windows_bash=0
if [[ -n "${MSYSTEM:-}" || -n "${MINGW_PREFIX:-}" || -n "${CYGWIN:-}" ]]; then
  is_windows_bash=1
fi

normalize_for_windows_host() {
  if command -v wslpath >/dev/null 2>&1; then
    wslpath -w "$1"
    return
  fi

  if command -v cygpath >/dev/null 2>&1; then
    cygpath -w "$1"
    return
  fi

  printf '%s\n' "$1"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --package-path)
      if [[ $# -lt 2 ]] || [[ "$2" == -* ]]; then
        package_path=""
        break
      fi
      package_path="$2"
      shift 2
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

if [[ -z "$package_path" ]]; then
  echo "Missing --package-path." >&2
  usage
  exit 1
fi

ps_args=("-PackagePath" "$package_path")

if command -v pwsh >/dev/null 2>&1; then
  if [[ "$is_windows_bash" -eq 1 ]]; then
    ps_script="$(normalize_for_windows_host "$ps_script")"
    ps_args=("-PackagePath" "$(normalize_for_windows_host "$package_path")")
  fi

  exec pwsh -NoProfile -ExecutionPolicy Bypass -File "$ps_script" "${ps_args[@]}"
fi

if command -v powershell.exe >/dev/null 2>&1; then
  ps_script="$(normalize_for_windows_host "$ps_script")"
  ps_args_for_powershell=("-PackagePath" "$(normalize_for_windows_host "$package_path")")

  exec powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$ps_script" "${ps_args_for_powershell[@]}"
fi

echo "Neither pwsh nor powershell.exe was found, so this wrapper cannot run the verification script." >&2
exit 1
