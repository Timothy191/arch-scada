#!/usr/bin/env python3
"""ArchSCADA Pro `/swarm` orchestrator.

Unifies the specialist registry (specialists_registry.yaml), the dated memory
ledger (conductor/memory.md), the Conductor track registry (conductor/tracks.md),
the MCP inventory (.mcp.json) and live pre-context (deliver_precontext.sh --json)
into deterministic, ready-to-paste `/swarm` worker briefs and plan.json manifests.

Standard library only (no PyYAML): the registry is parsed by a small block-style
YAML subset parser defined below.

Modes (exactly one):
  --list                      list specialists (add --json for machine output)
  --brief <Specialist>        print the worker brief (--json: structured brief,
                              --context-only: only the live pre-context section)
  --validate                  validate registry, ownership, MCP and 3D assets
  --generate-plan             emit a plan.json manifest (--task, --specialists, --out)
  --self-test                 validation + briefs for every specialist + negative tests

Exit codes: 0 success, 1 validation/self-test failure, 2 usage error.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

# --------------------------------------------------------------------------- constants
REPO_ROOT = Path(__file__).resolve().parents[2]
REGISTRY_PATH = REPO_ROOT / "specialists_registry.yaml"
MEMORY_PATH = REPO_ROOT / "conductor" / "memory.md"
TRACKS_PATH = REPO_ROOT / "conductor" / "tracks.md"
MCP_PATH = REPO_ROOT / ".mcp.json"
PRECONTEXT_SCRIPT = REPO_ROOT / "deliver_precontext.sh"
TASK_START_SCRIPT = REPO_ROOT / "task_start.sh"
EXPORTED_DIR = REPO_ROOT / "ArchScada" / "Assets" / "3D" / "Exported"
EXPORTED_WEB_DIR = REPO_ROOT / "ArchScada" / "Assets" / "3D" / "ExportedWeb"
MASTER_USDA = REPO_ROOT / "ArchScada" / "Assets" / "3D" / "ArchCoalPlant.usda"

SCHEMA_VERSION = "2.0"
EXPECTED_SPECIALISTS = ("Desktop", "Web", "ThreeD", "OpenUSD", "Testing", "Documentation", "Security")
REQUIRED_SPECIALIST_FIELDS = (
    "name", "worker_name", "role", "effort_tier", "owned_paths", "forbidden_paths",
    "mcp_servers", "verification_gates", "memory_refs",
)
EFFORT_TIERS = ("light", "standard", "heavy")
KNOWN_FACETS = ("git", "tracks", "assets", "openusd", "mcp")
EXPECTED_GLB_COUNT = 24
GLB_MAGIC = b"glTF"
GLB_VERSION = 2
GLB_HEADER_BYTES = 12
MIN_USDA_BYTES = 100 * 1024
MEMORY_ID_RE = re.compile(r"^###\s+(M-\d{2})(?:\s+\((\d{4}-\d{2}-\d{2})\))?:\s*(.+?)\s*$")
MEMORY_UPDATED_RE = re.compile(r"\*\*Last Updated:\*\*\s*(\d{4}-\d{2}-\d{2})")
LEDGER_ROW_RE = re.compile(r"^\|\s*`(ACT-\d{2})`\s*\|\s*([^|]+?)\s*\|\s*(.+?)\s*\|\s*$")
TRACK_LINE_RE = re.compile(r"^-\s+\[(?P<mark>[ xX~-])\]\s+\*\*Track:\s*(?P<title>.+?)\*\*\s*$")
TRACK_LINK_RE = re.compile(r"\*Link:\s*\[[^\]]*\]\((?P<href>[^)]+)\)\*")
SUMMARY_MAX_CHARS = 320
STANDARDS = (
    "Zero mock facades or dummy returns; every script/command must execute authentic operations.",
    "Clean code: extract magic constants to UPPER_SNAKE_CASE, guard clauses over deep nesting, "
    "contextual error handling (never swallow errors), semantic naming.",
    "Evidence-based verification: run your gates and report the exact command output.",
    "Edit ONLY your owned paths; request cross-slice changes on the board by agent name.",
)
GATE_TIMEOUT_NOTE = "Run each gate from the repository root; a non-zero exit code means the gate failed."


class OrchestratorError(Exception):
    """Raised for unrecoverable input errors (missing file, malformed registry)."""


# --------------------------------------------------------------------------- YAML subset
_KEY_RE = re.compile(r"^([A-Za-z0-9_][A-Za-z0-9_.\-]*)\s*:(?:\s+(.*))?$")


def _strip_comment(line: str) -> str:
    """Remove a trailing `# comment` that is not inside quotes."""
    quote: str | None = None
    for index, char in enumerate(line):
        if quote:
            if char == quote:
                quote = None
        elif char in "\"'":
            quote = char
        elif char == "#" and (index == 0 or line[index - 1] in " \t"):
            return line[:index].rstrip()
    return line.rstrip()


def _parse_scalar(raw: str) -> Any:
    text = raw.strip()
    if not text:
        return ""
    if text[0] == '"':
        if len(text) < 2 or text[-1] != '"':
            raise OrchestratorError(f"unterminated double-quoted scalar: {raw!r}")
        return json.loads(text)
    if text[0] == "'":
        if len(text) < 2 or text[-1] != "'":
            raise OrchestratorError(f"unterminated single-quoted scalar: {raw!r}")
        return text[1:-1].replace("''", "'")
    if text.startswith("[") and text.endswith("]"):
        inner = text[1:-1].strip()
        return [_parse_scalar(part) for part in inner.split(",")] if inner else []
    lowered = text.lower()
    if lowered in ("true", "false"):
        return lowered == "true"
    if lowered in ("null", "~"):
        return None
    if re.fullmatch(r"-?\d+", text):
        return int(text)
    return text


def _parse_block(lines: list[tuple[int, str]], index: int, indent: int) -> tuple[Any, int]:
    if lines[index][1] == "-" or lines[index][1].startswith("- "):
        return _parse_list(lines, index, indent)
    return _parse_map(lines, index, indent)


def _parse_list(lines: list[tuple[int, str]], index: int, indent: int) -> tuple[list[Any], int]:
    items: list[Any] = []
    while index < len(lines) and lines[index][0] == indent and (lines[index][1] == "-" or lines[index][1].startswith("- ")):
        content = lines[index][1][1:].strip()
        if not content:
            index += 1
            if index >= len(lines) or lines[index][0] <= indent:
                items.append(None)
                continue
            value, index = _parse_block(lines, index, lines[index][0])
            items.append(value)
        elif _KEY_RE.match(content) and content[0] not in "\"'":
            lines[index] = (indent + 2, content)  # re-seat the inline first key of a map item
            value, index = _parse_map(lines, index, indent + 2)
            items.append(value)
        else:
            items.append(_parse_scalar(content))
            index += 1
    return items, index


def _parse_map(lines: list[tuple[int, str]], index: int, indent: int) -> tuple[dict[str, Any], int]:
    mapping: dict[str, Any] = {}
    while index < len(lines) and lines[index][0] == indent and not lines[index][1].startswith("- "):
        match = _KEY_RE.match(lines[index][1])
        if not match:
            raise OrchestratorError(f"invalid mapping line (expected 'key: value'): {lines[index][1]!r}")
        key, rest = match.group(1), match.group(2)
        index += 1
        if rest is not None and rest.strip():
            mapping[key] = _parse_scalar(rest)
            continue
        has_child = index < len(lines) and (
            lines[index][0] > indent
            or (lines[index][0] == indent and (lines[index][1] == "-" or lines[index][1].startswith("- ")))
        )
        if not has_child:
            mapping[key] = None
            continue
        mapping[key], index = _parse_block(lines, index, lines[index][0])
    return mapping, index


def parse_yaml(text: str) -> Any:
    """Parse the block-style YAML subset used by specialists_registry.yaml."""
    lines: list[tuple[int, str]] = []
    for raw in text.splitlines():
        stripped = _strip_comment(raw.replace("\t", "    "))
        if stripped.strip():
            lines.append((len(stripped) - len(stripped.lstrip(" ")), stripped.strip()))
    if not lines:
        raise OrchestratorError("empty YAML document")
    value, end = _parse_block(lines, 0, lines[0][0])
    if end != len(lines):
        raise OrchestratorError(f"unparsed YAML content starting at: {lines[end][1]!r}")
    return value


# --------------------------------------------------------------------------- data loading
def _read_text(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except OSError as exc:
        raise OrchestratorError(f"cannot read {path}: {exc}") from exc


def load_registry(path: Path = REGISTRY_PATH) -> dict[str, Any]:
    document = parse_yaml(_read_text(path))
    if not isinstance(document, dict) or not isinstance(document.get("specialists"), list):
        raise OrchestratorError(f"{path.name}: top-level 'specialists' list missing")
    document["_sha256"] = hashlib.sha256(_read_text(path).encode("utf-8")).hexdigest()
    return document


@dataclass
class Memory:
    last_updated: str
    entries: dict[str, dict[str, str]] = field(default_factory=dict)
    ledger: list[dict[str, str]] = field(default_factory=list)


def _summarize(body_lines: list[str]) -> str:
    for line in body_lines:
        text = line.strip()
        if not text.startswith(("*", "-")):
            continue
        text = re.sub(r"\*\*|`", "", text.lstrip("*- ").strip())
        return text if len(text) <= SUMMARY_MAX_CHARS else text[: SUMMARY_MAX_CHARS - 1].rstrip() + "…"
    return ""


def load_memory(path: Path = MEMORY_PATH) -> Memory:
    text = _read_text(path)
    updated = MEMORY_UPDATED_RE.search(text)
    if not updated:
        raise OrchestratorError(f"{path.name}: '**Last Updated:** YYYY-MM-DD' header missing")
    memory = Memory(last_updated=updated.group(1))
    current: str | None = None
    body: list[str] = []

    def flush() -> None:
        if current:
            memory.entries[current]["summary"] = _summarize(body)

    for line in text.splitlines():
        heading = MEMORY_ID_RE.match(line)
        if heading:
            flush()
            current, body = heading.group(1), []
            memory.entries[current] = {
                "id": current, "date": heading.group(2) or memory.last_updated, "title": heading.group(3), "summary": "",
            }
            continue
        if line.startswith("#"):
            flush()
            current, body = None, []
        elif current:
            body.append(line)
        row = LEDGER_ROW_RE.match(line)
        if row:
            memory.ledger.append({"id": row.group(1), "owner": row.group(2).strip("` "), "outcome": row.group(3)})
    flush()
    return memory


def load_tracks(path: Path = TRACKS_PATH) -> list[dict[str, Any]]:
    tracks: list[dict[str, Any]] = []
    lines = _read_text(path).splitlines()
    for position, line in enumerate(lines):
        match = TRACK_LINE_RE.match(line)
        if not match:
            continue
        link = TRACK_LINK_RE.search(lines[position + 1]) if position + 1 < len(lines) else None
        href = link.group("href") if link else ""
        track_dir = (path.parent / href).resolve().parent if href else None
        meta_path = track_dir / "metadata.json" if track_dir else None
        metadata: dict[str, Any] = {}
        if meta_path and meta_path.is_file():
            metadata = json.loads(_read_text(meta_path))
        tracks.append({
            "title": match.group("title"),
            "checked": match.group("mark").lower() == "x",
            "link": href,
            "link_exists": bool(href) and (path.parent / href).resolve().is_file(),
            "id": metadata.get("id", track_dir.name if track_dir else ""),
            "status": metadata.get("status", "completed" if match.group("mark").lower() == "x" else "unknown"),
        })
    return tracks


def load_mcp(path: Path = MCP_PATH) -> dict[str, dict[str, Any]]:
    try:
        servers = json.loads(_read_text(path)).get("mcpServers", {})
    except json.JSONDecodeError as exc:
        raise OrchestratorError(f"{path.name}: invalid JSON: {exc}") from exc
    if not isinstance(servers, dict) or not servers:
        raise OrchestratorError(f"{path.name}: 'mcpServers' missing or empty")
    return servers


def load_precontext() -> dict[str, Any]:
    """Run deliver_precontext.sh --json and return the parsed live facts."""
    if not PRECONTEXT_SCRIPT.is_file():
        raise OrchestratorError(f"missing {PRECONTEXT_SCRIPT}")
    result = subprocess.run(["bash", str(PRECONTEXT_SCRIPT), "--json"], capture_output=True, text=True, cwd=REPO_ROOT)
    if result.returncode != 0:
        raise OrchestratorError(f"deliver_precontext.sh --json failed ({result.returncode}): {result.stderr.strip()}")
    try:
        return json.loads(result.stdout)
    except json.JSONDecodeError as exc:
        raise OrchestratorError(f"deliver_precontext.sh --json emitted invalid JSON: {exc}") from exc


# --------------------------------------------------------------------------- ownership
def _is_glob(path: str) -> bool:
    return any(char in path for char in "*?[")


def covers(broad: str, narrow: str) -> bool:
    """True when `broad` equals or (as a directory) contains `narrow`."""
    if broad == narrow:
        return True
    return broad.endswith("/") and narrow.startswith(broad)


def find_ownership_conflicts(specialists: list[dict[str, Any]]) -> list[str]:
    """Return human-readable overlap conflicts between different specialists' owned_paths."""
    conflicts: list[str] = []
    for left_index, left in enumerate(specialists):
        for right in specialists[left_index + 1:]:
            for a in left["owned_paths"]:
                for b in right["owned_paths"]:
                    if a == b:
                        conflicts.append(f"{left['name']} and {right['name']} both own exactly '{a}'")
                        continue
                    for broad_owner, broad, narrow_owner, narrow in ((left, a, right, b), (right, b, left, a)):
                        if not covers(broad, narrow):
                            continue
                        carved = any(covers(forbidden, narrow) for forbidden in broad_owner["forbidden_paths"])
                        if not carved:
                            conflicts.append(
                                f"{broad_owner['name']} owns '{broad}' which contains '{narrow}' owned by "
                                f"{narrow_owner['name']} (add the narrower path to {broad_owner['name']}.forbidden_paths)"
                            )
    return conflicts


# --------------------------------------------------------------------------- validation
@dataclass
class Check:
    name: str
    ok: bool
    detail: str

    def as_dict(self) -> dict[str, Any]:
        return {"name": self.name, "ok": self.ok, "detail": self.detail}


def _glb_problems(directory: Path) -> tuple[list[str], int, int]:
    """Return (problems, glb_count, total_bytes) for a GLB tier directory."""
    if not directory.is_dir():
        return [f"{directory.relative_to(REPO_ROOT)} is missing"], 0, 0
    problems: list[str] = []
    total = 0
    files = sorted(directory.glob("*.glb"))
    for glb in files:
        total += glb.stat().st_size
        with glb.open("rb") as handle:
            header = handle.read(GLB_HEADER_BYTES)
        version = int.from_bytes(header[4:8], "little") if len(header) == GLB_HEADER_BYTES else -1
        if header[:4] != GLB_MAGIC or version != GLB_VERSION:
            problems.append(f"{glb.name}: invalid glTF v2 header")
    return problems, len(files), total


def validate_specialists(registry: dict[str, Any], mcp: dict[str, Any], memory: Memory, root: Path = REPO_ROOT) -> list[Check]:
    checks: list[Check] = []
    specialists = registry["specialists"]
    names = [s.get("name") for s in specialists]

    schema_ok = registry.get("schema_version") == SCHEMA_VERSION
    checks.append(Check("registry.schema_version", schema_ok, f"{registry.get('schema_version')!r} (expected {SCHEMA_VERSION!r})"))
    missing = [n for n in EXPECTED_SPECIALISTS if n not in names]
    extra = [n for n in names if n not in EXPECTED_SPECIALISTS]
    dupes = sorted({n for n in names if names.count(n) > 1})
    checks.append(Check("registry.specialists", not (missing or extra or dupes),
                        f"{len(names)} specialists; missing={missing} unexpected={extra} duplicated={dupes}"))

    field_problems: list[str] = []
    for spec in specialists:
        label = spec.get("name", "<unnamed>")
        for key in REQUIRED_SPECIALIST_FIELDS:
            if spec.get(key) is None or spec.get(key) == "":
                field_problems.append(f"{label}: missing '{key}'")
        if spec.get("effort_tier") not in EFFORT_TIERS:
            field_problems.append(f"{label}: effort_tier {spec.get('effort_tier')!r} not in {EFFORT_TIERS}")
        for key in ("owned_paths", "mcp_servers", "verification_gates", "memory_refs"):
            if not spec.get(key):
                field_problems.append(f"{label}: '{key}' must be non-empty")
        for gate in spec.get("verification_gates") or []:
            if not isinstance(gate, dict) or not gate.get("id") or not gate.get("command"):
                field_problems.append(f"{label}: gate needs 'id' and 'command': {gate!r}")
        for facet in spec.get("precontext_facets") or []:
            if facet not in KNOWN_FACETS:
                field_problems.append(f"{label}: unknown precontext facet {facet!r}")
    checks.append(Check("registry.fields", not field_problems, "; ".join(field_problems) or "all required fields present"))

    if field_problems:  # later checks index into the fields validated above
        return checks

    conflicts = find_ownership_conflicts(specialists)
    checks.append(Check("ownership.no_overlaps", not conflicts, "; ".join(conflicts) or "zero overlapping owned_paths conflicts"))

    global_forbidden = registry.get("global_forbidden_paths") or []
    intrusions = [
        f"{s['name']} owns '{p}' inside global-forbidden '{g}'"
        for s in specialists for p in s["owned_paths"] for g in global_forbidden if covers(g, p)
    ]
    self_conflicts = [
        f"{s['name']} owns '{p}' but forbids '{f}'"
        for s in specialists for p in s["owned_paths"] for f in s["forbidden_paths"] if p == f
    ]
    checks.append(Check("ownership.forbidden_consistency", not (intrusions or self_conflicts), "; ".join(intrusions + self_conflicts) or "no owned path is forbidden"))

    absent = [f"{s['name']}: {p}" for s in specialists for p in s["owned_paths"] if not _is_glob(p) and not (root / p).exists()]
    checks.append(Check("ownership.paths_exist", not absent, "; ".join(absent) or "every owned path exists on disk"))

    unknown_mcp = [f"{s['name']}->{m}" for s in specialists for m in s["mcp_servers"] if m not in mcp]
    checks.append(Check("mcp.servers_exist", not unknown_mcp,
                        "; ".join(unknown_mcp) or f"all referenced MCP servers exist in .mcp.json ({len(mcp)} configured)"))

    unknown_memory = [f"{s['name']}->{m}" for s in specialists for m in s["memory_refs"] if m not in memory.entries]
    checks.append(Check("memory.refs_exist", not unknown_memory,
                        "; ".join(unknown_memory) or f"all memory_refs resolve ({len(memory.entries)} entries in memory.md)"))
    covered = {m for s in specialists for m in s["memory_refs"]}
    uncovered = sorted(set(memory.entries) - covered)
    checks.append(Check("memory.all_entries_assigned", not uncovered, f"unassigned memories: {uncovered}" if uncovered else "every memory entry is routed to at least one specialist"))
    return checks


def validate_environment() -> list[Check]:
    checks: list[Check] = []
    for label, directory in (("Exported", EXPORTED_DIR), ("ExportedWeb", EXPORTED_WEB_DIR)):
        problems, count, total = _glb_problems(directory)
        ok = not problems and count == EXPECTED_GLB_COUNT
        checks.append(Check(f"assets.{label}", ok,
                            f"{count}/{EXPECTED_GLB_COUNT} GLBs, {total / 1048576:.2f} MiB" + (f"; {'; '.join(problems)}" if problems else "")))
    usda_ok = MASTER_USDA.is_file() and MASTER_USDA.stat().st_size >= MIN_USDA_BYTES
    checks.append(Check("assets.ArchCoalPlant.usda", usda_ok,
                        f"{MASTER_USDA.stat().st_size / 1024:.0f} KiB" if MASTER_USDA.is_file() else "missing"))
    for script in (PRECONTEXT_SCRIPT, TASK_START_SCRIPT):
        syntax = subprocess.run(["bash", "-n", str(script)], capture_output=True, text=True) if script.is_file() else None
        checks.append(Check(f"scripts.{script.name}", bool(syntax and syntax.returncode == 0),
                            "bash -n OK" if syntax and syntax.returncode == 0 else (syntax.stderr.strip() if syntax else "missing")))
    return checks


def validate_tracks(tracks: list[dict[str, Any]]) -> list[Check]:
    broken = [t["title"] for t in tracks if not t["link_exists"]]
    return [Check("conductor.tracks_registered", bool(tracks) and not broken,
                  f"{len(tracks)} tracks; broken links: {broken}" if broken else f"{len(tracks)} tracks, all links resolve")]


def validate_all() -> list[Check]:
    registry, memory, mcp, tracks = load_registry(), load_memory(), load_mcp(), load_tracks()
    return validate_specialists(registry, mcp, memory) + validate_tracks(tracks) + validate_environment()


def print_checks(checks: list[Check], label: str = "VALIDATION") -> bool:
    for check in checks:
        print(f"[{'PASS' if check.ok else 'FAIL'}] {check.name}: {check.detail}")
    failed = [c for c in checks if not c.ok]
    print(f"{label} {'FAILED' if failed else 'PASSED'}: {len(checks) - len(failed)}/{len(checks)} checks passed")
    return not failed


# --------------------------------------------------------------------------- briefs
def find_specialist(registry: dict[str, Any], query: str) -> dict[str, Any]:
    wanted = query.casefold()
    for spec in registry["specialists"]:
        if wanted == spec["name"].casefold() or wanted in (a.casefold() for a in spec.get("aliases") or []):
            return spec
    available = ", ".join(s["name"] for s in registry["specialists"])
    raise OrchestratorError(f"unknown specialist {query!r}; available: {available}")


def _peer_owned(registry: dict[str, Any], spec: dict[str, Any]) -> list[dict[str, str]]:
    return [
        {"path": path, "owner": peer["name"]}
        for peer in registry["specialists"] if peer["name"] != spec["name"]
        for path in peer["owned_paths"]
    ]


def build_context_lines(spec: dict[str, Any], facts: dict[str, Any]) -> list[str]:
    lines: list[str] = []
    facets = spec.get("precontext_facets") or list(KNOWN_FACETS)
    git = facts["git"]
    if "git" in facets:
        lines.append(f"- Git: `{git['branch']}` @ `{git['commit'][:9]}` — {git['message']} ({git['commit_time']})")
    if "tracks" in facets:
        for track in facts["tracks"]:
            progress = f"{track['done_tasks']}/{track['total_tasks']} tasks" if track["total_tasks"] else "no task list"
            lines.append(f"- Track `{track['id']}`: {track['status']} ({progress})")
    if "assets" in facets:
        assets = facts["assets"]
        lines.append(
            f"- 3D assets: Exported {assets['exported']['count']} GLBs / {assets['exported']['mib']:.2f} MiB (SharpGLTF master), "
            f"ExportedWeb {assets['exported_web']['count']} GLBs / {assets['exported_web']['mib']:.2f} MiB "
            f"(Meshopt web tier, -{assets['reduction_pct']:.1f}%)"
        )
    if "openusd" in facets:
        usd = facts["openusd"]
        stats = (f"{usd['prim_count']} prims, {usd['mesh_count']} meshes, {usd['material_count']} materials, up={usd['up_axis']}"
                 if usd.get("prim_count") is not None else "stage stats unavailable (openusd-mcp python missing)")
        lines.append(f"- OpenUSD `{usd['path']}`: {stats}; {usd['node_id_count']} `archscada:nodeId` nodes ({usd['source']})")
    if "mcp" in facets:
        mcp = facts["mcp"]
        lines.append(f"- MCP inventory ({mcp['count']}): " + ", ".join(f"`{s['name']}` ({s['transport']})" for s in mcp["servers"]))
    return lines


def build_brief(registry: dict[str, Any], memory: Memory, facts: dict[str, Any], spec: dict[str, Any]) -> dict[str, Any]:
    memories = [memory.entries[ref] for ref in spec["memory_refs"]]
    return {
        "specialist": spec["name"],
        "worker_name": spec["worker_name"],
        "role": spec["role"],
        "effort_tier": spec["effort_tier"],
        "responsibilities": spec.get("responsibilities") or [],
        "memories": memories,
        "owned_paths": spec["owned_paths"],
        "forbidden_paths": spec["forbidden_paths"],
        "global_forbidden_paths": registry.get("global_forbidden_paths") or [],
        "peer_owned_paths": _peer_owned(registry, spec),
        "mcp_servers": spec["mcp_servers"],
        "verification_gates": spec["verification_gates"],
        "context": build_context_lines(spec, facts),
        "standards": list(STANDARDS),
    }


def render_context_text(brief: dict[str, Any]) -> str:
    header = f"--- {brief['specialist'].upper()} PRE-CONTEXT ---"
    return "\n".join([header, *brief["context"]])


def _render_peer_paths(peer_paths: list[dict[str, str]]) -> list[str]:
    grouped: dict[str, list[str]] = {}
    for item in peer_paths:
        grouped.setdefault(item["owner"], []).append(f"`{item['path']}`")
    return [f"- **{owner}**: " + ", ".join(paths) for owner, paths in grouped.items()]


def render_brief_text(brief: dict[str, Any]) -> str:
    out: list[str] = [
        f"@{brief['worker_name']} You are the `{brief['specialist']}` specialist worker in the ArchSCADA Pro swarm "
        f"(effort tier: {brief['effort_tier']}).",
        "",
        "### Role",
        brief["role"],
        *[f"- {item}" for item in brief["responsibilities"]],
        "",
        "### Dated Memories (conductor/memory.md)",
        *[f"- **{m['id']} ({m['date']})** {m['title']} — {m['summary']}" for m in brief["memories"]],
        "",
        "### File Boundaries",
        "**Owned — you may edit:**",
        *[f"- `{p}`" for p in brief["owned_paths"]],
        "**Forbidden — never edit (explicit carve-outs + global):**",
        *[f"- `{p}`" for p in brief["forbidden_paths"] + brief["global_forbidden_paths"]],
        "**Owned by peers — request changes on the board by name:**",
        *_render_peer_paths(brief["peer_owned_paths"]),
        "",
        "### MCP Servers (from .mcp.json)",
        *[f"- `{name}`" for name in brief["mcp_servers"]],
        "",
        "### Live Pre-Context",
        *brief["context"],
        "",
        "### Verification Gates",
        GATE_TIMEOUT_NOTE,
        *[f"{i}. `{gate['id']}`: `{gate['command']}`" for i, gate in enumerate(brief["verification_gates"], 1)],
        "",
        "### Real-World Engineering Standards (Mandatory)",
        *[f"- {item}" for item in brief["standards"]],
        "",
        "Post `@coordinator [done] <summary>` with the exact verification output when finished.",
    ]
    return "\n".join(out)


# --------------------------------------------------------------------------- plan.json
def generate_plan(task: str, selected: list[str] | None) -> dict[str, Any]:
    checks = validate_all()
    failed = [c for c in checks if not c.ok]
    if failed:
        raise OrchestratorError("refusing to generate plan; validation failed: " + "; ".join(f"{c.name}: {c.detail}" for c in failed))
    registry, memory, facts = load_registry(), load_memory(), load_precontext()
    specs = [find_specialist(registry, name) for name in selected] if selected else registry["specialists"]
    briefs = [build_brief(registry, memory, facts, spec) for spec in specs]
    return {
        "schema_version": SCHEMA_VERSION,
        "project": registry.get("project", "ArchSCADA"),
        "objective": task,
        "provenance": {
            "registry_sha256": registry["_sha256"],
            "memory_last_updated": memory.last_updated,
            "git_branch": facts["git"]["branch"],
            "git_commit": facts["git"]["commit"],
        },
        "validation": {"ok": True, "checks": [c.as_dict() for c in checks]},
        "ledger": memory.ledger,
        "tasks": [
            {
                "id": f"T-{index:02d}",
                "specialist": brief["specialist"],
                "worker_name": brief["worker_name"],
                "effort_tier": brief["effort_tier"],
                "owned_paths": brief["owned_paths"],
                "forbidden_paths": brief["forbidden_paths"],
                "mcp_servers": brief["mcp_servers"],
                "memory_refs": [m["id"] for m in brief["memories"]],
                "verification_gates": brief["verification_gates"],
                "brief": render_brief_text(brief),
            }
            for index, brief in enumerate(briefs, 1)
        ],
    }


# --------------------------------------------------------------------------- self-test
def _clone_specs(registry: dict[str, Any]) -> list[dict[str, Any]]:
    return json.loads(json.dumps(registry["specialists"]))


def run_self_test() -> bool:
    results: list[Check] = []

    def record(name: str, ok: bool, detail: str) -> None:
        results.append(Check(name, ok, detail))

    # 1. parser round-trip on an inline document
    sample = parse_yaml("a: 1\nb:\n  - x\n  - 'it''s'\nc:\n  - id: g\n    command: \"echo 'hi' # not a comment\"  # trailing\nd: []\n")
    record("selftest.yaml_parser", sample == {"a": 1, "b": ["x", "it's"], "c": [{"id": "g", "command": "echo 'hi' # not a comment"}], "d": []}, repr(sample))

    # 2. live validation
    checks = validate_all()
    ok = print_checks(checks)
    record("selftest.validate_all", ok, f"{sum(c.ok for c in checks)}/{len(checks)} live checks")

    registry, memory, mcp, facts = load_registry(), load_memory(), load_mcp(), load_precontext()

    # 3. negative: overlapping ownership and unknown MCP must be detected
    mutated = _clone_specs(registry)
    mutated[1]["owned_paths"].append("ArchScada/ArchScada.Desktop/Views/")
    record("selftest.detects_overlap", bool(find_ownership_conflicts(mutated)), "synthetic Web->Desktop overlap flagged")
    bad_registry = dict(registry, specialists=_clone_specs(registry))
    bad_registry["specialists"][0]["mcp_servers"].append("ghost-mcp")
    ghost = [c for c in validate_specialists(bad_registry, mcp, memory) if c.name == "mcp.servers_exist"][0]
    record("selftest.detects_unknown_mcp", not ghost.ok, ghost.detail)

    # 4. briefs for every specialist
    for name in EXPECTED_SPECIALISTS:
        spec = find_specialist(registry, name)
        text = render_brief_text(build_brief(registry, memory, facts, spec))
        missing = [token for token in (
            *[f"**{ref} (" for ref in spec["memory_refs"]],
            *[f"`{p}`" for p in spec["owned_paths"]],
            *[g["command"] for g in spec["verification_gates"]],
            "### File Boundaries", "### Verification Gates", f"@{spec['worker_name']}",
        ) if token not in text]
        record(f"selftest.brief.{name}", not missing, f"{len(text)} chars" if not missing else f"missing {missing}")

    # 5. plan round-trip
    plan = generate_plan("self-test", None)
    round_trip = json.loads(json.dumps(plan))
    record("selftest.plan", len(round_trip["tasks"]) == len(EXPECTED_SPECIALISTS) and round_trip["validation"]["ok"], f"{len(round_trip['tasks'])} tasks")

    extra = [c for c in results if c.name != "selftest.validate_all"]
    print_checks(extra, label="SELF-TEST CHECKS")
    passed = all(c.ok for c in results)
    print(f"SELF-TEST {'PASSED' if passed else 'FAILED'}: {sum(c.ok for c in results)}/{len(results)} groups")
    return passed


# --------------------------------------------------------------------------- CLI
def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="ArchSCADA Pro /swarm orchestrator", formatter_class=argparse.RawDescriptionHelpFormatter, epilog=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--list", action="store_true", help="list specialists")
    mode.add_argument("--brief", metavar="SPECIALIST", help="print a /swarm worker brief")
    mode.add_argument("--validate", action="store_true", help="validate registry, ownership, MCP servers and 3D assets")
    mode.add_argument("--generate-plan", action="store_true", help="emit a plan.json manifest")
    mode.add_argument("--self-test", action="store_true", help="full validation + brief generation for all specialists")
    parser.add_argument("--json", action="store_true", help="machine-readable output (--list, --brief)")
    parser.add_argument("--context-only", action="store_true", help="with --brief: only the live pre-context section")
    parser.add_argument("--task", default="ArchSCADA Pro swarm task", help="objective recorded in plan.json")
    parser.add_argument("--specialists", help="comma-separated specialists for --generate-plan (default: all)")
    parser.add_argument("--out", type=Path, help="write plan.json here instead of stdout")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        if args.list:
            registry = load_registry()
            rows = [{"name": s["name"], "worker_name": s["worker_name"], "effort_tier": s["effort_tier"], "role": s["role"]} for s in registry["specialists"]]
            print(json.dumps(rows, indent=2) if args.json else "\n".join(f"{r['name']:<14} {r['effort_tier']:<9} {r['role']}" for r in rows))
            return 0
        if args.brief:
            registry = load_registry()
            brief = build_brief(registry, load_memory(), load_precontext(), find_specialist(registry, args.brief))
            if args.json:
                print(json.dumps(brief, indent=2))
            else:
                print(render_context_text(brief) if args.context_only else render_brief_text(brief))
            return 0
        if args.validate:
            return 0 if print_checks(validate_all()) else 1
        if args.generate_plan:
            selected = [s.strip() for s in args.specialists.split(",") if s.strip()] if args.specialists else None
            payload = json.dumps(generate_plan(args.task, selected), indent=2) + "\n"
            if args.out:
                args.out.parent.mkdir(parents=True, exist_ok=True)
                args.out.write_text(payload, encoding="utf-8")
                print(f"wrote {args.out}")
            else:
                sys.stdout.write(payload)
            return 0
        return 0 if run_self_test() else 1
    except OrchestratorError as exc:
        print(f"swarm_orchestrator: {exc}", file=sys.stderr)
        return 2 if args.brief else 1


if __name__ == "__main__":
    sys.exit(main())
