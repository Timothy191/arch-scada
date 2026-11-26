# Implementation Plan: swarm_and_3d_pro_upgrade_20261126

## Phase 1: Swarm Module Upgrade (swarm-module-architect)
- [x] Task: Upgrade `specialists_registry.yaml` to v2.0 with 7 specialists, ownership carve-outs, MCP servers, gates, memory refs and effort tiers
- [x] Task: Create `deploy/scripts/swarm_orchestrator.py` (`--list`, `--brief`, `--validate`, `--generate-plan`, `--self-test`) using stdlib only
- [x] Task: Upgrade `task_start.sh` (`--json`, `--swarm-brief`, `--list`, 7 specialists) delegating to the orchestrator
- [x] Task: Upgrade `deliver_precontext.sh` with Conductor tracks, `Exported` vs `ExportedWeb` MiB, OpenUSD health, MCP inventory and `--json`
- [x] Task: Register this track in `conductor/tracks.md`

## Phase 2: Pro 3D Visuals & Quality Gates (sibling workers)
- [x] Task: Web3D UI/UX polish in `DigitalTwin3D.cshtml` (WCAG focus rings, lighting modes, compass, telemetry badge)
- [x] Task: Gate 3 OpenUSD stage check, Gate 4 `swarm_orchestrator.py --self-test`, and `ThreeDSyncTests` web-tier/OpenUSD parity tests
- [x] Task: Run full `./verify-quality-gates.sh` and mark the track `completed`
