# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

## Build

Build all projects in dependency order using Visual Studio 2022 Release configuration, or use .NET CLI:

```bash
dotnet build ScadaCommon/ScadaCommon.sln -c Release
dotnet build ScadaAgent/ScadaAgent/ScadaAgent.sln -c Release
dotnet build ScadaComm/ScadaComm/ScadaComm.sln -c Release
dotnet build ScadaServer/ScadaServer/ScadaServer.sln -c Release
dotnet build ScadaWeb/ScadaWeb/ScadaWeb.sln -c Release
dotnet build ScadaAdmin/ScadaAdmin/ScadaAdmin.sln -c Release
dotnet build ScadaReport/ScadaReport.sln -c Release
dotnet build ScadaComm/OpenDrivers/OpenDrivers.sln -c Release
dotnet build ScadaComm/OpenDrivers2/OpenDrivers2.sln -c Release
dotnet build ScadaServer/OpenModules/OpenModules.sln -c Release
dotnet build ScadaWeb/Mimics/Mimics.sln -c Release
dotnet build ScadaWeb/OpenPlugins/OpenPlugins.sln -c Release
dotnet build ScadaAdmin/OpenExtensions/OpenExtensions.sln -c Release
```

Or via the batch script: `build-all.sh`

## Test

Run unit tests:

```bash
pnpm test  # runs all packages' test scripts (turbo)
```

or per-project with dotnet:

```bash
dotnet test ScadaCommon/ScadaCommon.Tests/ScadaCommon.Tests.csproj -c Release
```

Coverage thresholds: 40 statements / 27 branches / 28 functions / 40 lines.

## Architecture

This is Rapid SCADA v6 — an open-source industrial automation platform. Main components:

- **ScadaCommon** — core library (base classes, storage, file I/O)
- **ScadaAgent** — agent logic
- **ScadaComm** — communication drivers
- **ScadaServer** — server engine
- **ScadaWeb** — web UI
- **ScadaAdmin** — administration UI
- **ScadaReport** — reporting

Key constraints:
- Light mode only: canvas `#f3f4f6`, semantic OKLCH tokens from `@repo/theme`
- Glass surfaces use `@repo/ui/GlassCard` (`default` | `window` | `spotlight` | `glowborder` | `liquid`)
- Tailwind ≤16 utilities per element; canonical class order enforced via Biome
- Server Components by default; `'use client'` at leaves only
- Import via canonical exports (`@repo/ui/GlassCard`)
- `SKIP_TYPE_CHECK=true` only works locally (blocked when `CI=true`)

## Commands

```bash
pnpm dev          # Start dev server (portal on :3000, 0.0.0.0)
pnpm build        # Build all packages
pnpm test         # Unit tests
pnpm test:e2e     # Playwright E2E tests
pnpm quality      # Full quality gate
```

## Git

- Work on `main` branch only; no branches/worktrees/PRs unless explicitly asked
- Never print/commit `.env*` files
- Conventional commits expected

## Import helpers

If you have an OpenAI Codex config or Gemini CLI config you'd like imported, run `/import` from a terminal to scan and list importable items.