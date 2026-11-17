# Arch SCADA: Codebase Onboarding, Architecture Insights & Workspace Stabilization

**Prepared by:** Chief of Staff  
**Date:** November 16, 2026  
**Scope:** Arch SCADA / Rapid SCADA v6 Industrial Automation Workspace

---

## 1. Executive Summary

Arch SCADA is a hardened, offline-first industrial supervisory control and data acquisition platform tailored for modern **Coal Handling and Preparation Plants (CHPP)**. It builds upon a modular fork of the battle-tested **Rapid SCADA v6** architecture and introduces a modern cross-platform **Avalonia UI (.NET 10)** operator desktop HMI, an **ISA-18.2 compliant alarm management engine**, a **tamper-evident control safety boundary**, and an interactive **3D digital twin viewport**.

Upon initial onboarding, the repository was in an advanced development state with rich core features, but suffered from workspace ergonomic gaps (lack of a unified root solution, contradictory `CLAUDE.md` documentation inherited from web templates, and an unpopulated project memory).

This onboarding initiative has stabilized the workspace, validated 100% build and test health across all 16 solutions and projects, established unified root tooling, initialized the AgentsRoom shared knowledge base, and outlined prioritized strategic steps for ongoing development.

---

## 2. Workspace Stabilization Accomplishments

### 2.1 Unified Root Solution Architecture
- **Problem:** Developers and IDEs (Visual Studio, JetBrains Rider, VS Code C# Dev Kit) attempting `dotnet build` or `dotnet test` from the repository root encountered MSBuild errors due to the absence of a root solution file.
- **Solution:** 
  - Generated and structured `Plant.sln` at the root and `ArchScada/ArchScada.sln` under the ArchScada subsystem.
  - Linked all primary Arch SCADA libraries (`ArchScada.ControlBoundary`, `ArchScada.CoreIntegration`, `ArchScada.Desktop`) and all test suites (`ArchScada.ControlBoundary.Tests`, `ArchScada.CoreIntegration.Tests`, `ArchScada.Desktop.Tests`, `ScadaCommon.Tests`).
  - **Result:** Running `dotnet test` at repository root now executes all **57 unit and integration tests** in ~9 seconds with a **100% pass rate**.

### 2.2 Realigned `CLAUDE.md` Guidance
- **Problem:** The repository’s guidance file contained contradictory instructions referencing Node.js, `pnpm`, `turbo`, Tailwind, and web component constraints not present in this .NET/C# repository.
- **Solution:** Fully rewrote `CLAUDE.md` to document the actual .NET 10 / C# CLI workflows, build order, test commands, architecture principles, and execution guidelines.

### 2.3 Populated Shared Project Memory
- **Problem:** AgentsRoom project memory mirror was uninitialized, leaving collaborating agents without shared context on architecture and conventions.
- **Solution:** Synchronized foundational knowledge notes via `memory_save`:
  - `architecture-overview` (System taxonomy, component relationships, CHPP plant mapping).
  - `conventions-and-standards` (Build/test commands, ISA-18.2 standards, control boundary enforcement).

### 2.4 End-to-End Build Verification
- Executed full repository build via `./build-all.sh Release`.
- Verified clean compilation across all 13 Rapid SCADA solutions and 3 Arch SCADA subsystems with **0 compilation errors**.

---

## 3. Architecture Deep-Dive

```
┌─────────────────────────────────────────────────────────────────────────┐
│                      Operator Workstation (Linux / Win)                 │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │                    ArchScada.Desktop (Avalonia UI)                │  │
│  │  • Plant Overview (2D Synoptic & 3D PhysX Viewport)               │  │
│  │  • ISA-18.2 Alarm Lifecycle Management (Unack, Ack, Shelved)      │  │
│  │  • Coupled CHPP Process Physics & Micro-Optimization Panel        │  │
│  │  • Operator Handover, RBAC, and Shift Journal                     │  │
│  └───────────────────────────────────┬───────────────────────────────┘  │
│                                      │ Local IPC / TCP                  │
└──────────────────────────────────────┼──────────────────────────────────┘
                                       │
┌──────────────────────────────────────▼──────────────────────────────────┐
│                         Core Integration & Safety                       │
│  ┌───────────────────────────────────┐   ┌───────────────────────────┐  │
│  │     ArchScada.ControlBoundary     │   │ ArchScada.CoreIntegration │  │
│  │  • Interlock Validation           │   │ • NativeScadaServerAdapter│  │
│  │  • PLC Read-Back Verification     │   │ • Local Historian Cache   │  │
│  │  • SHA-256 Audit Trail            │   │ • Local Config Store      │  │
│  └───────────────────────────────────┘   └─────────────┬─────────────┘  │
└────────────────────────────────────────────────────────┼────────────────┘
                                                         │
┌────────────────────────────────────────────────────────▼────────────────┐
│                       Rapid SCADA v6 Engine Layer                       │
│  ┌───────────────────┐   ┌───────────────────┐   ┌───────────────────┐  │
│  │    ScadaServer    │   │     ScadaComm     │   │     ScadaWeb      │  │
│  │ Historical Archive│   │ Modbus, OPC, MQTT │   │ Hardened Read-Only│  │
│  └───────────────────┘   └─────────┬─────────┘   └───────────────────┘  │
└────────────────────────────────────┼────────────────────────────────────┘
                                     │
                     ┌───────────────┼───────────────┐
                     │               │               │
               ┌─────▼─────┐   ┌─────▼─────┐   ┌─────▼─────┐
               │ Modbus TCP│   │  OPC UA   │   │ MQTT Env  │
               │Plant PLCs │   │ Analyzers │   │  Sensors  │
               └───────────┘   └───────────┘   └───────────┘
```

### 3.1 Subsystem Highlights
1. **ArchScada.Desktop**:
   - Modern Avalonia 11 MVVM application running on .NET 10.
   - Built-in simulation fallback (`SimulatedTelemetryService`, `CoupledPlantProcessModel`) for offline testing without live hardware.
   - 3D viewport control (`Plant3DViewportControl`) with depth fog, camera orbiting, and click-to-select equipment diagnostics.
2. **ArchScada.ControlBoundary**:
   - Implements zero-trust industrial command dispatch.
   - Enforces read-only defaults; all setpoint or start/stop operations require operator privilege and generate verifiable SHA-256 audit logs.
3. **ArchScada.CoreIntegration**:
   - Bridges the desktop HMI with Rapid SCADA's `ScadaServer` via high-throughput socket communication (`NativeScadaServerAdapter`).
   - SQLite/PostgreSQL local historian ensures data persistence during communication blackouts.
4. **Projects/ArchCoalPlant**:
   - Complete configuration mapping for Line 1 (Modbus TCP PLCs), Line 2 (OPC UA PGNAA analyzer and centrifuges), and Line 3 (MQTT particulate and belt safety sensors).

---

## 4. Current Test Suite Health

| Test Suite | Framework | Status | Tests | Duration |
|---|---|---|---|---|
| `ArchScada.ControlBoundary.Tests` | xUnit + Moq | PASS | 12 | ~500 ms |
| `ArchScada.CoreIntegration.Tests` | xUnit + Moq | PASS | 17 | ~350 ms |
| `ArchScada.Desktop.Tests` | xUnit + Moq | PASS | 27 | ~700 ms |
| `ScadaCommon.Tests` | xUnit | PASS | 1 | ~30 ms |
| **Total** | | **PASS** | **57** | **~1.6s execution** |

---

## 5. Strategic Insights & Opportunities

1. **Cryptographic & Dependency Modernization**:
   - `ScadaCommon.MultiDb` raises NuGet warnings (`NU1902`, `NU1903`) on `System.Security.Cryptography.Pkcs`. Updating this dependency will eliminate known vulnerability warnings.
   - `ScadaUtils.Crypto.cs` relies on legacy .NET APIs (`RijndaelManaged` and `RNGCryptoServiceProvider`). Migrating to `Aes` and `RandomNumberGenerator` aligns the codebase with .NET 10 best practices.
2. **Continuous Integration (CI/CD)**:
   - With `Plant.sln` established, a streamlined GitHub Actions or local CI pipeline can run `dotnet test` and verify pull requests automatically on every push.
3. **Database Migration & Verification**:
   - `Database/coal_plant_schema.sql` defines the PostgreSQL historian schema. A test harness or automated containerized DB integration test would validate schema migrations against live PostgreSQL instances.
4. **Desktop HMI Packaging**:
   - Avalonia publish profiles (e.g. self-contained Linux x64 and Windows x64 binaries) can be codified for single-click operator deployment.

---

## 6. Proposed Backlog Tickets

The following tickets are prepared for approval to be logged in the project backlog:

1. **#1 Modernize Cryptographic APIs and Dependency Versions** (Type: `security`)
   - Update `System.Security.Cryptography.Pkcs` in `ScadaCommon.MultiDb`.
   - Refactor `RijndaelManaged` to `Aes` and `RNGCryptoServiceProvider` to `RandomNumberGenerator`.
   - Assigned to: `backend` (`role:backend`).

2. **#2 Configure Automated CI Pipeline for Build and Test** (Type: `chore`)
   - Create `.github/workflows/ci.yml` running `dotnet test Plant.sln` on PRs and merges to `develop`.
   - Assigned to: `devops` (`role:devops`).

3. **#3 Automated PostgreSQL Historian Integration Tests** (Type: `test`)
   - Implement integration test fixtures validating `LocalHistorianStore` against PostgreSQL schemas.
   - Assigned to: `qa` (`role:qa`).
