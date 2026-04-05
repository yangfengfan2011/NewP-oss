#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
log_dir="$repo_root/.codex-temp"
stopped_any=false

stop_pid_file() {
  local pid_file="$1"

  if [ ! -f "$pid_file" ]; then
    return
  fi

  local pid
  pid="$(tr -d '[:space:]' < "$pid_file")"

  if [ -n "$pid" ] && kill -0 "$pid" >/dev/null 2>&1; then
    kill "$pid" >/dev/null 2>&1 || true
    stopped_any=true
    echo "Stopped pid:$pid"
  fi

  rm -f "$pid_file"
}

stop_pid_file "$log_dir/dev-host-api.pid"
stop_pid_file "$log_dir/dev-frontend.pid"

for port in 5000 3000; do
  if command -v lsof >/dev/null 2>&1; then
    while IFS= read -r pid; do
      [ -n "$pid" ] || continue
      kill "$pid" >/dev/null 2>&1 || true
      stopped_any=true
      echo "Stopped port:$port pid:$pid"
    done < <(lsof -tiTCP:"$port" -sTCP:LISTEN 2>/dev/null || true)
  elif command -v ss >/dev/null 2>&1; then
    while IFS= read -r pid; do
      [ -n "$pid" ] || continue
      kill "$pid" >/dev/null 2>&1 || true
      stopped_any=true
      echo "Stopped port:$port pid:$pid"
    done < <(ss -ltnp "( sport = :$port )" 2>/dev/null | sed -n 's/.*pid=\([0-9]\+\).*/\1/p' | sort -u)
  fi
done

if [ "$stopped_any" != true ]; then
  echo "No tracked dev processes were running."
fi
