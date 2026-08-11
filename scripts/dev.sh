#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
frontend_root="$repo_root/frontend"
log_dir="$repo_root/.codex-temp"
frontend_url="http://localhost:3000"
backend_url="http://localhost:5000"
frontend_out_log="$log_dir/dev-frontend.out.log"
frontend_err_log="$log_dir/dev-frontend.err.log"
backend_out_log="$log_dir/dev-host-api.out.log"
backend_err_log="$log_dir/dev-host-api.err.log"
frontend_pid_file="$log_dir/dev-frontend.pid"
backend_pid_file="$log_dir/dev-host-api.pid"
backend_startup_timeout_seconds=120
frontend_startup_timeout_seconds=90
log_tail_lines=40

wait_for_url() {
  local url="$1"
  local timeout="${2:-60}"
  local elapsed=0

  while [ "$elapsed" -lt "$timeout" ]; do
    if curl -fsS "$url" >/dev/null 2>&1; then
      return 0
    fi

    if curl -sS "$url" >/dev/null 2>&1; then
      return 0
    fi

    sleep 1
    elapsed=$((elapsed + 1))
  done

  return 1
}

show_log_tail() {
  local title="$1"
  local path="$2"

  echo
  echo "=== $title ==="

  if [ ! -f "$path" ]; then
    echo "(log file not found: $path)"
    return
  fi

  if [ ! -s "$path" ]; then
    echo "(no log output)"
    return
  fi

  tail -n "$log_tail_lines" "$path"
}

show_startup_diagnostics() {
  show_log_tail "Host API stdout tail" "$backend_out_log"
  show_log_tail "Host API stderr tail" "$backend_err_log"
  show_log_tail "Frontend stdout tail" "$frontend_out_log"
  show_log_tail "Frontend stderr tail" "$frontend_err_log"
}

ensure_frontend_dependencies() {
  local frontend_node_modules="$frontend_root/node_modules"
  local needs_install=false

  if [ ! -d "$frontend_node_modules" ]; then
    needs_install=true
  elif ! (cd "$frontend_root" && corepack pnpm --filter @icp-platform/shell exec vite --version) >/dev/null 2>&1; then
    needs_install=true
  fi

  if [ "$needs_install" != true ]; then
    return
  fi

  echo "Preparing frontend dependencies with pnpm..."
  (cd "$frontend_root" && corepack pnpm install)
}

port_in_use() {
  local port="$1"
  if command -v lsof >/dev/null 2>&1; then
    lsof -iTCP:"$port" -sTCP:LISTEN >/dev/null 2>&1
    return $?
  fi

  if command -v ss >/dev/null 2>&1; then
    ss -ltn "( sport = :$port )" | tail -n +2 | grep -q .
    return $?
  fi

  return 1
}

echo "=== Start ICP local dev environment ==="

command -v dotnet >/dev/null 2>&1 || { echo "ERROR: dotnet was not found."; exit 1; }
command -v node >/dev/null 2>&1 || { echo "ERROR: node was not found."; exit 1; }
command -v corepack >/dev/null 2>&1 || { echo "ERROR: Corepack was not found."; exit 1; }

if ! toolchain_spec="$(node -e 'const config = require(process.argv[1]); const manager = config.packageManager; const runtime = config.devEngines && config.devEngines.runtime; if (!/^pnpm@\d+\.\d+\.\d+$/.test(manager) || !runtime || runtime.name !== "node" || !/^\d+\.\d+\.\d+$/.test(runtime.version)) process.exit(1); process.stdout.write(runtime.version + "|" + manager.slice(5));' "$frontend_root/package.json")"; then
  echo "ERROR: frontend/package.json must declare exact Node.js and pnpm versions."
  exit 1
fi

required_node_version="${toolchain_spec%%|*}"
required_pnpm_version="${toolchain_spec#*|}"
node_version="$(node --version)"
node_version="${node_version#v}"

if [ "$node_version" != "$required_node_version" ]; then
  echo "ERROR: Node.js $required_node_version is required; current version is $node_version."
  exit 1
fi

if ! current_pnpm_version="$(cd "$frontend_root" && corepack pnpm --version)"; then
  echo "ERROR: Corepack could not provide pnpm $required_pnpm_version from packageManager."
  exit 1
fi

if [ "$current_pnpm_version" != "$required_pnpm_version" ]; then
  echo "ERROR: Corepack provided pnpm $current_pnpm_version; packageManager requires $required_pnpm_version."
  exit 1
fi

ensure_frontend_dependencies

if port_in_use 5000; then
  echo "ERROR: port 5000 is already in use."
  exit 1
fi

if port_in_use 3000; then
  echo "ERROR: port 3000 is already in use."
  exit 1
fi

mkdir -p "$log_dir"
cd "$repo_root"

rm -f "$frontend_out_log" "$frontend_err_log" "$backend_out_log" "$backend_err_log" "$frontend_pid_file" "$backend_pid_file"

echo "Starting Host API..."
dotnet run --project "$repo_root/apps/host-api/HostApi.csproj" >"$backend_out_log" 2>"$backend_err_log" &
backend_pid=$!
printf '%s' "$backend_pid" >"$backend_pid_file"

backend_ready=true
frontend_ready=false
frontend_started=false

wait_for_url "$backend_url/api/auth/current-user" "$backend_startup_timeout_seconds" || backend_ready=false

if [ "$backend_ready" = true ]; then
  echo "Starting Frontend Shell..."
  (cd "$frontend_root" && exec corepack pnpm --filter @icp-platform/shell dev) >"$frontend_out_log" 2>"$frontend_err_log" &
  frontend_pid=$!
  frontend_started=true
  frontend_ready=true
  printf '%s' "$frontend_pid" >"$frontend_pid_file"

  wait_for_url "$frontend_url" "$frontend_startup_timeout_seconds" || frontend_ready=false
  if [ "$frontend_ready" != true ] && ! kill -0 "$frontend_pid" >/dev/null 2>&1; then
    frontend_ready=false
  fi
else
  frontend_pid="not started"
fi

echo
echo "=== Startup result ==="
echo "Host API PID: $backend_pid"
echo "Frontend PID: $frontend_pid"
echo "Host API logs: $backend_out_log / $backend_err_log"
echo "Frontend logs: $frontend_out_log / $frontend_err_log"
echo "PID files: $backend_pid_file / $frontend_pid_file"
echo
echo "Host API:  $backend_url"
echo "Frontend:  $frontend_url"
echo
echo "Identity login uses the configured local administrator; bootstrap an empty catalog with the Host identity maintenance command."
echo "Stop services: ./scripts/stop-dev.sh"

if [ "$backend_ready" != true ] || [ "$frontend_ready" != true ]; then
  echo
  if [ "$backend_ready" != true ]; then
    echo "Host API did not become ready within ${backend_startup_timeout_seconds} seconds."
  fi

  if [ "$backend_ready" = true ] && [ "$frontend_ready" != true ]; then
    if [ "$frontend_started" = true ] && ! kill -0 "$frontend_pid" >/dev/null 2>&1; then
      echo "Frontend Shell exited before becoming ready."
    else
      echo "Frontend Shell did not become ready within ${frontend_startup_timeout_seconds} seconds."
    fi
  fi

  show_startup_diagnostics
  exit 1
fi

wait
