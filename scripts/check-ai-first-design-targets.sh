#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"

cd "$repo_root"

echo "=== AI-first design target checks ==="
pwsh -NoProfile -ExecutionPolicy Bypass -File "$script_dir/check-ai-first-design-targets.ps1"
