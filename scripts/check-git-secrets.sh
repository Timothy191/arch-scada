#!/usr/bin/env bash
# Fail if any git configuration contains GitHub token patterns or credentials
# embedded in URLs. Matching values are never printed; only file and line.
#
# Scans every config file git reads (including includes), plus credential-store
# files. Wired into the lefthook pre-push hook.
set -euo pipefail

fail=0

scan_file() {
  local file="$1"
  [[ -f "$file" ]] || return 0
  if [[ ! -r "$file" ]]; then
    echo "WARN: unreadable config file: $file" >&2
    return 0
  fi
  if ! awk '
    function kind(line) {
      if (line ~ /ghp_[A-Za-z0-9]+/)             return "ghp_token"
      if (line ~ /github_pat_[A-Za-z0-9_]+/)     return "github_fine_grained_pat"
      if (line ~ /x-access-token/)               return "x-access-token"
      if (line ~ /https?:\/\/[^\/[:space:]@]+@/) return "url_userinfo"
      return ""
    }
    {
      k = kind($0)
      if (k != "") {
        printf "FAIL %s:%d pattern=%s\n", FILENAME, FNR, k
        found = 1
      }
    }
    END { exit(found ? 1 : 0) }
  ' "$file"; then
    fail=1
  fi
}

declare -A seen=()

# Origins of every config value git resolves (local, global, system, includes).
# --name-only guarantees values are never emitted here.
while IFS=$'\t' read -r origin _; do
  [[ "$origin" == file:* ]] || continue
  path="${origin#file:}"
  [[ -n "${seen[$path]:-}" ]] && continue
  seen[$path]=1
  scan_file "$path"
done < <(git config --list --show-origin --name-only 2>/dev/null || true)

# Plaintext credential stores are not always listed by `git config`.
for extra in \
  "$HOME/.git-credentials" \
  "${XDG_CONFIG_HOME:-$HOME/.config}/git/credentials"
do
  [[ -n "${seen[$extra]:-}" ]] && continue
  seen[$extra]=1
  scan_file "$extra"
done

if [[ "$fail" -ne 0 ]]; then
  echo "ERROR: git configuration contains GitHub tokens or embedded URL credentials." >&2
  echo "Remove credentials from remote URLs and credential helpers, then rotate the token." >&2
  exit 1
fi

echo "PASS: no GitHub tokens or embedded URL credentials in git configuration."
