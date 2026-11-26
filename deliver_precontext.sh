#!/usr/bin/env bash
# deliver_precontext.sh - live project pre-context for /swarm specialists.
#
# Usage:
#   bash deliver_precontext.sh           # human-readable sections
#   bash deliver_precontext.sh --json    # structured facts (consumed by deploy/scripts/swarm_orchestrator.py)
#
# Facts reported (all computed live, nothing hard-coded):
#   git state + recent commits, Conductor track status (conductor/tracks.md + metadata.json + plan.md task counts),
#   3D asset tiers (Exported vs ExportedWeb count/MiB), OpenUSD stage health (ArchCoalPlant.usda),
#   .mcp.json server inventory, optional plant-config.json summary.
# Env: OPENUSD_MCP_PYTHON overrides the openusd-mcp interpreter used for scene_stats.
set -euo pipefail
PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$PROJECT_ROOT"

OUTPUT_FORMAT="text"
case "${1:-}" in
  "") ;;
  --json) OUTPUT_FORMAT="json" ;;
  -h|--help) sed -n '2,13p' "${BASH_SOURCE[0]}"; exit 0 ;;
  *) echo "Unknown option: $1 (use --json)" >&2; exit 2 ;;
esac

export PROJECT_ROOT OUTPUT_FORMAT
export GIT_BRANCH="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo unknown)"
export GIT_COMMIT="$(git rev-parse HEAD 2>/dev/null || echo none)"
export GIT_MESSAGE="$(git log -1 --format='%s' 2>/dev/null || echo 'no message')"
export GIT_COMMIT_TIME="$(git log -1 --format='%ci' 2>/dev/null || echo unknown)"
export GIT_RECENT="$(git log --format='%h %an %ad | %s' -5 --date=short 2>/dev/null || true)"
export OPENUSD_PYTHON="${OPENUSD_MCP_PYTHON:-$HOME/.local/share/uv/tools/openusd-mcp/bin/python}"

exec python3 - <<'PYEOF'
import json
import os
import re
import subprocess
from pathlib import Path

ROOT = Path(os.environ["PROJECT_ROOT"])
BYTES_PER_MIB = 1048576
EXPORTED = ROOT / "ArchScada/Assets/3D/Exported"
EXPORTED_WEB = ROOT / "ArchScada/Assets/3D/ExportedWeb"
USDA = ROOT / "ArchScada/Assets/3D/ArchCoalPlant.usda"
USDA_REL = "ArchScada/Assets/3D/ArchCoalPlant.usda"
SCENE_STATS_TIMEOUT_S = 60
TRACK_RE = re.compile(r"^-\s+\[(?P<mark>[ xX~-])\]\s+\*\*Track:\s*(?P<title>.+?)\*\*\s*$")
LINK_RE = re.compile(r"\*Link:\s*\[[^\]]*\]\((?P<href>[^)]+)\)\*")
TASK_RE = re.compile(r"^\s*-\s+\[(?P<mark>[ xX])\]\s+Task:", re.MULTILINE)


def tier(directory: Path) -> dict:
    files = sorted(directory.glob("*.glb")) if directory.is_dir() else []
    total = sum(f.stat().st_size for f in files)
    return {"count": len(files), "bytes": total, "mib": round(total / BYTES_PER_MIB, 2)}


def tracks() -> list:
    registry = ROOT / "conductor/tracks.md"
    if not registry.is_file():
        return []
    lines = registry.read_text(encoding="utf-8").splitlines()
    found = []
    for position, line in enumerate(lines):
        match = TRACK_RE.match(line)
        if not match:
            continue
        link = LINK_RE.search(lines[position + 1]) if position + 1 < len(lines) else None
        track_dir = (registry.parent / link.group("href")).resolve().parent if link else None
        meta = {}
        done = total = 0
        if track_dir and (track_dir / "metadata.json").is_file():
            meta = json.loads((track_dir / "metadata.json").read_text(encoding="utf-8"))
        if track_dir and (track_dir / "plan.md").is_file():
            marks = [m.group("mark").lower() for m in TASK_RE.finditer((track_dir / "plan.md").read_text(encoding="utf-8"))]
            total, done = len(marks), marks.count("x")
        found.append({
            "id": meta.get("id", track_dir.name if track_dir else match.group("title")),
            "title": match.group("title"),
            "status": meta.get("status", "completed" if match.group("mark").lower() == "x" else "unknown"),
            "updated_at": meta.get("updated_at", ""),
            "done_tasks": done,
            "total_tasks": total,
        })
    return found


def openusd() -> dict:
    info = {"path": USDA_REL, "exists": USDA.is_file(), "bytes": USDA.stat().st_size if USDA.is_file() else 0,
            "prim_count": None, "mesh_count": None, "material_count": None, "up_axis": None,
            "node_id_count": 0, "source": "missing"}
    if not USDA.is_file():
        return info
    info["node_id_count"] = USDA.read_text(encoding="utf-8").count("archscada:nodeId")
    info["source"] = "text-scan"
    interpreter = os.environ["OPENUSD_PYTHON"]
    if not os.access(interpreter, os.X_OK):
        return info
    probe = ("import json; from openusd_mcp.tools import scene_stats; "
             f"print(json.dumps(scene_stats('{USDA}')))")
    result = subprocess.run([interpreter, "-c", probe], capture_output=True, text=True, timeout=SCENE_STATS_TIMEOUT_S)
    if result.returncode != 0:
        raise SystemExit(f"openusd scene_stats failed: {result.stderr.strip()}")
    stats = json.loads(result.stdout)
    info.update(prim_count=stats["prim_count"], mesh_count=stats["mesh_count"],
                material_count=stats["material_count"], up_axis=stats["up_axis"], source="openusd-mcp scene_stats")
    return info


def mcp_inventory() -> dict:
    path = ROOT / ".mcp.json"
    servers = json.loads(path.read_text(encoding="utf-8")).get("mcpServers", {}) if path.is_file() else {}
    return {"count": len(servers),
            "servers": [{"name": n, "transport": "http" if "url" in c else "stdio"} for n, c in servers.items()]}


def config_summary() -> str:
    path = ROOT / "plant-config.json"
    if not path.is_file():
        return "no plant-config.json found"
    return json.dumps(json.loads(path.read_text(encoding="utf-8")), indent=2)


exported, exported_web = tier(EXPORTED), tier(EXPORTED_WEB)
reduction = round(100 * (1 - exported_web["bytes"] / exported["bytes"]), 1) if exported["bytes"] else 0.0
facts = {
    "git": {"branch": os.environ["GIT_BRANCH"], "commit": os.environ["GIT_COMMIT"], "message": os.environ["GIT_MESSAGE"],
            "commit_time": os.environ["GIT_COMMIT_TIME"], "recent": [l for l in os.environ["GIT_RECENT"].splitlines() if l]},
    "tracks": tracks(),
    "assets": {"exported": exported, "exported_web": exported_web, "reduction_pct": reduction},
    "openusd": openusd(),
    "mcp": mcp_inventory(),
    "config_summary": config_summary(),
}

if os.environ["OUTPUT_FORMAT"] == "json":
    print(json.dumps(facts, indent=2))
    raise SystemExit(0)

git = facts["git"]
print("GIT_STATE:")
print(f"  current_branch: {git['branch']}")
print(f"  latest_commit:  {git['commit']}")
print(f"  latest_msg:     {git['message']}")
print(f"  commit_time:    {git['commit_time']}")
print("RECENT_COMMITS:")
print("\n".join(git["recent"]) or "no commits")
print("CONDUCTOR_TRACKS:")
for t in facts["tracks"] or [None]:
    if t is None:
        print("  (no tracks registered in conductor/tracks.md)")
        continue
    progress = f"{t['done_tasks']}/{t['total_tasks']} tasks" if t["total_tasks"] else "no task list"
    print(f"  {t['id']}: {t['status']} ({progress})")
print("ASSETS_3D:")
print(f"  Exported:    {exported['count']} GLBs, {exported['mib']:.2f} MiB (SharpGLTF master tier)")
print(f"  ExportedWeb: {exported_web['count']} GLBs, {exported_web['mib']:.2f} MiB (Meshopt web tier, -{reduction:.1f}%)")
usd = facts["openusd"]
print("OPENUSD:")
if usd["prim_count"] is not None:
    print(f"  {usd['path']}: {usd['prim_count']} prims, {usd['mesh_count']} meshes, {usd['material_count']} materials, "
          f"up={usd['up_axis']}, {usd['node_id_count']} archscada:nodeId nodes [{usd['source']}]")
else:
    print(f"  {usd['path']}: exists={usd['exists']}, {usd['bytes']} bytes, {usd['node_id_count']} archscada:nodeId nodes [{usd['source']}]")
print(f"MCP_SERVERS: ({facts['mcp']['count']})")
for s in facts["mcp"]["servers"]:
    print(f"  {s['name']} ({s['transport']})")
print("CONFIG_SUMMARY:")
print(facts["config_summary"])
PYEOF
