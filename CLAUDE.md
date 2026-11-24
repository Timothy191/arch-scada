# CLAUDE.md

This file provides guidance to Claude Code and AI agents when working with code in this repository.

## Build

Build the core Arch SCADA solution and tests directly:

```bash
dotnet build Plant.sln -c Release
```

Or build all Rapid SCADA + Arch SCADA solutions in dependency order using the provided build script:

```bash
./build-all.sh Release
```

Individual solutions can be built per module:
```bash
dotnet build ScadaCommon/ScadaCommon.sln -c Release
dotnet build ScadaAgent/ScadaAgent/ScadaAgent.sln -c Release
dotnet build ScadaComm/ScadaComm/ScadaComm.sln -c Release
dotnet build ScadaServer/ScadaServer/ScadaServer.sln -c Release
dotnet build ScadaWeb/ScadaWeb/ScadaWeb.sln -c Release -p:AllowMissingPrunePackageData=true
dotnet build ScadaAdmin/ScadaAdmin/ScadaAdmin.sln -c Release
dotnet build ScadaReport/ScadaReport.sln -c Release
dotnet build ArchScada/ArchScada.sln -c Release
```

## Test

Run all unit and integration tests from the root directory:

```bash
dotnet test
```

Or target individual test projects:

```bash
dotnet test tests/ArchScada.ControlBoundary.Tests/ArchScada.ControlBoundary.Tests.csproj
dotnet test tests/ArchScada.CoreIntegration.Tests/ArchScada.CoreIntegration.Tests.csproj
dotnet test tests/ArchScada.Desktop.Tests/ArchScada.Desktop.Tests.csproj
dotnet test tests/ScadaCommon.Tests/ScadaCommon.Tests.csproj
```

Test conventions:
- Framework: **xUnit** + **Moq**
- Naming: `<Component>.Tests`
- Location: `tests/<Component>.Tests/`

## Quality Gates

Run the full 6-gate build, test, 3D GLB topology, backup audit, `ScadaWeb` read-only HTTP security, and Desktop 3D GPU performance verification suite:

```bash
./verify-quality-gates.sh
```

Useful flags:
- `./verify-quality-gates.sh --skip-gui` — headless CI mode (skips Gate 6 Desktop OpenGL capture).
- `./verify-quality-gates.sh --clean` — wipes `bin/` and `obj/` across the repository before running Gate 1.

## Architecture

This is **Arch SCADA** — a fork and specialized adaptation of Rapid SCADA v6 customized for industrial coal handling & preparation plant (CHPP) monitoring and automation.

### Key Components

- **ArchScada.Desktop** — Cross-platform operator workstation HMI built on **Avalonia UI** (.NET 10). Features 2D/3D plant visualization, ISA-18.2 alarm management, coupled process simulation, trend charts, and role-based access control (RBAC).
- **ArchScada.ControlBoundary** — Safety boundary engine governing write commands, interlocks, read-back verification, and SHA-256 audit logging.
- **ArchScada.CoreIntegration** — SCADA integration layer providing IPC/TCP adapters (`NativeScadaServerAdapter`, `ScadaServerClient`), local SQLite/PostgreSQL historian cache, and real-time tag synchronization.
- **ArchScada.Unity** — 3D digital twin assets and PhysX simulation for coal preparation plant machinery.
- **ScadaCommon** — Core Rapid SCADA foundation libraries (data models, protocols, crypto, logging).
- **ScadaServer** — Backend processing engine, historical archives, and tag calculations.
- **ScadaComm** — Communication server managing industrial field drivers (Modbus TCP, OPC UA, MQTT).
- **ScadaWeb** — Optional ASP.NET Core web interface hardened for read-only remote access.
- **ScadaAdmin** — Project management and configuration database editor.

### Key Industrial Standards & Constraints
- **Safety First**: Read-only monitoring by default. Control actions require explicit operator privileges and validation through `ControlBoundaryEngine`.
- **ISA-18.2**: Alarm lifecycle management (Unacknowledged, Acknowledged, Suppressed, Shelved).
- **Offline-First**: Desktop client operates autonomously with local historian and cached configuration.
- **Industrial Styling**: Avalonia desktop HMI designed for industrial control rooms with dark/industrial styling.

## Running the Application

To launch the Arch SCADA Desktop HMI:

```bash
dotnet run --project ArchScada/ArchScada.Desktop/ArchScada.Desktop.csproj
```

To run in simulated telemetry mode:
The desktop application automatically initializes `SimulatedTelemetryService` and `CoupledPlantProcessModel` if live SCADA server connectivity is offline.

## Install

Publish and install the Release suite (`ArchScada.Desktop`, `ArchScada.PlcSimulator` if present, `ScadaServer`, `ScadaComm`, `ScadaAgent`, `ScadaWeb`, `Projects/ArchCoalPlant` snapshot, `deploy/scripts/`, `~/.local/bin/archscada*` wrappers including `archscada-plc-sim`, FreeDesktop `.desktop` launcher, and `systemd --user` units) onto the local workstation:

```bash
./deploy/scripts/install-local.sh
```

Custom target directories can be specified with `--prefix <dir>` (default `~/.local/share/archscada`) and `--bin-dir <dir>` (default `~/.local/bin`).

### Local PostgreSQL 16 Historian & User-Space Services

- **Provision PostgreSQL 16 Historian**: Starts `archscada-historian-pg` (`deploy/docker-compose.historian.yml`, bound to `127.0.0.1:${ARCHSCADA_PGPORT:-5432}` or `55432` if `5432` is occupied), applies `Database/coal_plant_schema.sql` and Rapid SCADA storage DDL idempotently, seeds baseline `plant_areas`, and runs an end-to-end backup verification:
  ```bash
  ./deploy/scripts/provision-postgres.sh
  ```
- **User-Space `systemd --user` Suite**: Installs `archscada-plc-sim.service`, `archscada-web.service`, `archscada-desktop.service`, `archscada-backup.service`, and `archscada-backup.timer` into `~/.config/systemd/user/` and enables the daily backup timer:
  ```bash
  ./deploy/scripts/setup-systemd-user.sh
  systemctl --user start archscada-plc-sim archscada-web
  ```
- **PLC Simulator CLI**: Installed at `~/.local/bin/archscada-plc-sim` (supports `--self-test` for automated verification).

## Git Workflow
- Working branch: `develop`
- Follow conventional commits: `feat(...)`, `fix(...)`, `docs(...)`, `chore(...)`
- Never commit credentials, secrets, or `.env*` files
