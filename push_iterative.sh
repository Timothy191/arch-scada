#!/bin/bash

cd /home/tim/Projects/Plant

MAX_ROUNDS=100
ROUND=0

while [ $ROUND -lt $MAX_ROUNDS ]; do
  ROUND=$((ROUND + 1))
  CURRENT=$(git ls-remote origin refs/heads/master 2>/dev/null | awk '{print $1}')
  echo "=== Round $ROUND: origin/master = $CURRENT ==="
  
  # Find commits whose parent is current origin/master
  PUSHABLE=""
  for commit in $(git log --reverse --format="%H" origin/master..master); do
    parent=$(git log --format="%P" -1 $commit)
    if [ "$parent" = "$CURRENT" ]; then
      PUSHABLE="$PUSHABLE $commit"
    fi
  done
  
  if [ -z "$PUSHABLE" ]; then
    echo "No more pushable commits found."
    break
  fi
  
  PUSHED_ANY=0
  for commit in $PUSHABLE; do
    SUBJECT=$(git log --format="%s" -1 $commit)
    echo "Trying to push: $commit $SUBJECT"
    RESULT=$(git push origin $commit:refs/heads/master 2>&1)
    EXIT_CODE=$?
    if [ $EXIT_CODE -eq 0 ]; then
      echo "  SUCCESS: $commit"
      PUSHED_ANY=1
    else
      echo "  FAILED: $commit"
      echo "$RESULT" | head -5
    fi
  done
  
  if [ $PUSHED_ANY -eq 0 ]; then
    echo "No commits could be pushed in this round. Stopping."
    break
  fi
  
  sleep 1
done

echo "=== Final origin/master ==="
git ls-remote origin refs/heads/master 2>&1
echo "=== Local master ==="
git rev-parse master
