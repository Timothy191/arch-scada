#!/usr/bin/env bash
set -euo pipefail
PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$PROJECT_ROOT"

CURRENT_BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo unknown)
LATEST_COMMIT=$(git rev-parse HEAD 2>/dev/null || echo none)
LATEST_COMMIT_MSG=$(git log -1 --format="%s" 2>/dev/null || echo no\ message)
COMMIT_TIME=$(git log -1 --format="%ci" 2>/dev/null || echo unknown)

RECENT=$(git log --format="%h %an %ad | %s" -5 --date=short 2>/dev/null || echo no\ commits)

if [ -f plant-config.json ]; then
  CONFIG_SUMMARY=$(python -c "import json; print(json.dumps(json.load(open('plant-config.json')), indent=2))" 2>/dev/null || echo unable\ to\ parse)
else
  CONFIG_SUMMARY=no\ plant-config.json\ found
fi

echo "GIT_STATE:"
echo "  current_branch: $CURRENT_BRANCH"
echo "  latest_commit:  $LATEST_COMMIT"
echo "  latest_msg:     $LATEST_COMMIT_MSG"
echo "  commit_time:    $COMMIT_TIME"

echo "RECENT_COMMITS:"
echo "$RECENT"

echo "OPEN_ISSUES:"
echo "  (placeholder – populate with GitHub issue list)"

echo "CONFIG_SUMMARY:"
echo "$CONFIG_SUMMARY"
