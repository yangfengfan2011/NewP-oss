#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
log_dir="$repo_root/.codex-temp"
frontend_url="http://localhost:3000"
backend_url="http://localhost:5000"
frontend_out_log="$log_dir/dev-frontend.out.log"
frontend_err_log="$log_dir/dev-frontend.err.log"
backend_out_log="$log_dir/dev-host-api.out.log"
backend_err_log="$log_dir/dev-host-api.err.log"
frontend_pid_file="$log_dir/dev-frontend.pid"
backend_pid_file="$log_dir/dev-host-api.pid"

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
command -v pnpm >/dev/null 2>&1 || { echo "ERROR: pnpm was not found."; exit 1; }

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

echo "Starting Frontend Shell..."
pnpm -C "$repo_root/frontend" --filter @icp-platform/shell dev >"$frontend_out_log" 2>"$frontend_err_log" &
frontend_pid=$!
printf '%s' "$frontend_pid" >"$frontend_pid_file"

backend_ready=true
frontend_ready=true

wait_for_url "$backend_url/api/auth/current-user" 60 || backend_ready=false
wait_for_url "$frontend_url" 60 || frontend_ready=false

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
echo "Default login: admin / admin123"
echo "Stop services: ./scripts/stop-dev.sh"

if [ "$backend_ready" != true ] || [ "$frontend_ready" != true ]; then
  echo
  echo "One or more services did not become ready in time. Check the logs."
  exit 1
fi

wait
