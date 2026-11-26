# Specification: swarm_and_3d_pro_upgrade_20261126

## Goal
Upgrade the workspace `/swarm` module from five generic page specialists to a deterministic, validated
orchestration layer for seven production ArchSCADA specialists, alongside the Pro 3D visual upgrades
(Web3D UI/UX, Desktop quality gates) delivered by sibling swarm workers.

## Scope (swarm-module-architect slice)
| Artifact | Requirement |
| :--- | :--- |
| `specialists_registry.yaml` | Schema `2.0`: `Desktop`, `Web`, `ThreeD`, `OpenUSD`, `Testing`, `Documentation`, `Security`, each with `owned_paths`, `forbidden_paths`, `mcp_servers`, `verification_gates`, `memory_refs`, `effort_tier`. |
| `deploy/scripts/swarm_orchestrator.py` | Stdlib-only CLI: `--list`, `--brief <Specialist>`, `--validate`, `--generate-plan`, `--self-test`. Parses registry, `conductor/memory.md`, `conductor/tracks.md`, `.mcp.json`; calls `deliver_precontext.sh --json`. |
| `task_start.sh` | `--json`, `--swarm-brief`, `--list`; all 7 specialists (+ aliases `3D`, `USD`, `Docs`, `Sec`); delegates to the orchestrator. |
| `deliver_precontext.sh` | Replaces the `OPEN_ISSUES` placeholder with live Conductor track status, `Exported` vs `ExportedWeb` count/MiB, OpenUSD stage health and `.mcp.json` inventory; `--json` mode. |

## Validation contract (`--validate`)
1. Registry schema `2.0` with exactly the 7 specialists and all required fields.
2. Zero overlapping `owned_paths`, except explicit carve-outs (broader owner lists the narrower path in `forbidden_paths`).
3. Every owned path exists; every referenced MCP server exists in `.mcp.json`; every `memory_refs` id resolves in `memory.md` and every memory (`M-01`..`M-09`) is routed to a specialist.
4. `Exported/` and `ExportedWeb/` each hold 24 valid `glTF` v2 GLBs; `ArchCoalPlant.usda` exists (> 100 KiB).
5. Every Conductor track link resolves; `task_start.sh` / `deliver_precontext.sh` pass `bash -n`.

## Non-goals
Edits to `DigitalTwin3D.cshtml`, `Plant3DOpenGlCanvas.cs` or `verify-quality-gates.sh` (owned by sibling workers; Gate 4 calls `--self-test`).
