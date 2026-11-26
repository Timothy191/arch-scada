#!/usr/bin/env bash
# task_start.sh – wrapper that delivers pre‑context to a specialist
# Usage:   bash task_start.sh <specialist-name>
# Example: bash task_start.sh Web

set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$PROJECT_ROOT"

SPECIALIST="${1:-}"

if [[ -z "$SPECIALIST" ]]; then
  echo "Usage: $0 <specialist-name>"
  echo "Available specialists: $(grep '^  - name:' specialists_registry.yaml | awk '{print $2}' | tr '\n' ' ')"
  exit 1
fi

# run the pre‑context script
PRE=$(bash deliver_precontext.sh)

# helper to extract value from a matching marker line
extract() {
  local marker="$1"
  echo "$PRE" | grep -m1 "$marker:" | sed -E "s/^.*${marker}:[[:space:]]*//"
}

# map specialist to pre‑context extraction
case "$SPECIALIST" in
  Web)
    echo "--- WEB PRE‑CONTEXT ---"
    echo "Current branch: $(extract 'current_branch')"
    echo "Latest commit : $(extract 'latest_commit')"
    echo "Commit message: $(extract 'latest_msg')"
    echo "Recent commits:"
    echo "$PRE" | grep -A5 "RECENT_COMMITS:" | tail -n +2
    echo "Config summary:"
    echo "$PRE" | grep -A1 "CONFIG_SUMMARY:" | tail -1
    ;;
  Desktop)
    echo "--- DESKTOP PRE‑CONTEXT ---"
    echo "Current branch: $(extract 'current_branch')"
    echo "Latest commit : $(extract 'latest_commit')"
    echo "Config summary:"
    echo "$PRE" | grep -A1 "CONFIG_SUMMARY:" | tail -1
    ;;
  ThreeD|3D)
    echo "--- 3D PRE‑CONTEXT ---"
    echo "Current branch: $(extract 'current_branch')"
    echo "Latest commit : $(extract 'latest_commit')"
    echo "Recent commits:"
    echo "$PRE" | grep -A5 "RECENT_COMMITS:" | tail -n +2
    ;;
  Documentation)
    echo "--- DOCUMENTATION PRE‑CONTEXT ---"
    echo "Current branch: $(extract 'current_branch')"
    echo "Latest commit : $(extract 'latest_commit')"
    ;;
  Testing)
    echo "--- TESTING PRE‑CONTEXT ---"
    echo "Current branch: $(extract 'current_branch')"
    echo "Latest commit : $(extract 'latest_commit')"
    ;;
  *)
    # fallback – dump full pre‑context
    echo "--- FULL PRE‑CONTEXT (no specialist filter) ---"
    echo "$PRE"
    ;;
esac