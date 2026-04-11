#!/usr/bin/env bash

set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  ./scripts/manage-unified-plugin.sh --action install --package-path <path> [--install-root artifacts/installed-plugins]
  ./scripts/manage-unified-plugin.sh --action enable --module-id <id> --version <version> [--install-root artifacts/installed-plugins]
  ./scripts/manage-unified-plugin.sh --action disable --module-id <id> [--install-root artifacts/installed-plugins]
  ./scripts/manage-unified-plugin.sh --action uninstall --module-id <id> [--install-root artifacts/installed-plugins]
  ./scripts/manage-unified-plugin.sh --action rollback --module-id <id> [--install-root artifacts/installed-plugins]
EOF
}

script_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ps_script="$script_root/manage-unified-plugin.ps1"
action=""
package_path=""
module_id=""
version=""
install_root="artifacts/installed-plugins"

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
    --action)
      action="${2:-}"
      shift 2
      ;;
    --package-path)
      package_path="${2:-}"
      shift 2
      ;;
    --module-id)
      module_id="${2:-}"
      shift 2
      ;;
    --version)
      version="${2:-}"
      shift 2
      ;;
    --install-root)
      install_root="${2:-}"
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

if [[ -z "$action" ]]; then
  echo "Missing --action." >&2
  usage
  exit 1
fi

ps_args=("-Action" "$action" "-InstallRoot" "$install_root")

case "$action" in
  install)
    if [[ -z "$package_path" ]]; then
      echo "Missing --package-path for install." >&2
      usage
      exit 1
    fi
    ps_args+=("-PackagePath" "$package_path")
    ;;
  enable)
    if [[ -z "$module_id" || -z "$version" ]]; then
      echo "Missing --module-id or --version for enable." >&2
      usage
      exit 1
    fi
    ps_args+=("-ModuleId" "$module_id" "-Version" "$version")
    ;;
  disable|uninstall|rollback)
    if [[ -z "$module_id" ]]; then
      echo "Missing --module-id for $action." >&2
      usage
      exit 1
    fi
    ps_args+=("-ModuleId" "$module_id")
    ;;
  *)
    echo "Unsupported action: $action" >&2
    usage
    exit 1
    ;;
esac

if command -v pwsh >/dev/null 2>&1; then
  if [[ "$is_windows_bash" -eq 1 ]]; then
    ps_script="$(normalize_for_windows_host "$ps_script")"

    converted_args=()
    index=0
    while [[ $index -lt ${#ps_args[@]} ]]; do
      key="${ps_args[$index]}"
      value="${ps_args[$((index + 1))]}"
      if [[ "$key" == "-PackagePath" || "$key" == "-InstallRoot" ]]; then
        value="$(normalize_for_windows_host "$value")"
      fi
      converted_args+=("$key" "$value")
      index=$((index + 2))
    done
    ps_args=("${converted_args[@]}")
  fi

  exec pwsh -NoProfile -ExecutionPolicy Bypass -File "$ps_script" "${ps_args[@]}"
fi

if command -v powershell.exe >/dev/null 2>&1; then
  ps_script="$(normalize_for_windows_host "$ps_script")"

  converted_args=()
  index=0
  while [[ $index -lt ${#ps_args[@]} ]]; do
    key="${ps_args[$index]}"
    value="${ps_args[$((index + 1))]}"
    if [[ "$key" == "-PackagePath" || "$key" == "-InstallRoot" ]]; then
      value="$(normalize_for_windows_host "$value")"
    fi
    converted_args+=("$key" "$value")
    index=$((index + 2))
  done

  exec powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$ps_script" "${converted_args[@]}"
fi

echo "Neither pwsh nor powershell.exe was found, so this wrapper cannot run the management script." >&2
exit 1
