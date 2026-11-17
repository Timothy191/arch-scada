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

## Git Workflow
- Working branch: `develop`
- Follow conventional commits: `feat(...)`, `fix(...)`, `docs(...)`, `chore(...)`
- Never commit credentials, secrets, or `.env*` files
