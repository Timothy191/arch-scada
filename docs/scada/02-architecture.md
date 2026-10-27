# Arch SCADA Architecture

## Executive Summary

Arch SCADA is a fork of Rapid SCADA v6 customized for coal processing plant monitoring and automation. The system prioritizes offline-first desktop operation with cross-platform support (Windows/Linux) via Avalonia UI, while retaining web interface capabilities for remote access. The architecture follows a read-only-first control philosophy with explicit safety boundaries for eventual machine control integration.

## Architectural Principles

1. **Offline-First**: Essential SCADA functions (monitoring, alarms, events, local configuration) operate without internet connectivity
2. **Cross-Platform Desktop**: Primary HMI uses Avalonia UI for Windows and Linux operator workstations
3. **Layered Safety**: Read-only monitoring by default; control requires explicit authorization and validation
4. **Modular Reuse**: Leverage Rapid SCADA's proven communication, server, and protocol components
5. **Configurable Process Model**: Equipment types, tag definitions, and alarm rules are data-driven, not hardcoded
6. **Durable Storage**: Local historian with PostgreSQL (or SQLite for single-node deployments)
7. **Secure by Design**: Least privilege, audit trails, network segmentation, dependency management

## System Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                      Operator Workstation                         │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │           Arch SCADA Desktop HMI (Avalonia UI)           │  │
│  │  • Plant Overview  • Equipment Views  • Alarms           │  │
│  │  • Trends          • Reports       • Configuration       │  │
│  └──────────────────────┬───────────────────────────────────┘  │
│                         │ Local IPC/HTTP                        │
└─────────────────────────┼───────────────────────────────────────┘
                          │
          ┌───────────────┴───────────────┐
          │                               │
┌─────────▼─────────┐         ┌──────────▼──────────┐
│  Local SCADA Core  │         │   Remote Web UI     │
│  (Forked from      │         │   (ScadaWeb -       │
│   Rapid SCADA)     │         │    optional)        │
├───────────────────┤         ├─────────────────────┤
│ • ScadaServer      │         │ • ScadaWeb          │
│ • ScadaComm        │         │ • Remote Views      │
│ • Local Historian  │         │ • Remote Alarms     │
│ • Control Safety   │         │ • Configuration     │
│   Boundary         │         └──────────┬──────────┘
└─────────┬─────────┘                    │
          │                              │
          │                              │
┌─────────▼──────────────────────────────▼─────────────────────┐
│                    Communication Layer                           │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐           │
│  │ Modbus TCP  │  │  OPC UA     │  │   MQTT      │           │
│  │ Driver      │  │  Driver     │  │  Client     │           │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘           │
└─────────┼────────────────┼────────────────┼───────────────────┘
          │                │                │
    ┌─────▼─────┐    ┌─────▼─────┐    ┌─────▼─────┐
    │   PLC 1   │    │   PLC 2   │    │   PLC 3   │
    │ (Motors)  │    │ (Sensors) │    │ (Drives)  │
    └───────────┘    └───────────┘    └───────────┘
```

## Component Architecture

### 1. Desktop HMI Layer (Avalonia UI)

**Purpose:** Primary operator interface for local monitoring and eventual control

**Components:**
- `ArchScada.Desktop` - Main Avalonia UI application
- `ArchScada.Desktop.Common` - Shared desktop logic and view models
- `ArchScada.Desktop.Views` - XAML views for plant screens
- `ArchScada.Desktop.Controls` - Custom industrial controls (gauges, indicators, valves)
- `ArchScada.Desktop.Charts` - Trend visualization components

**Key Screens:**
- Plant Overview Dashboard
- Crushing and Conveyor Overview
- Thickener Circuit Overview
- Magnetite Preparation Overview
- Pumps, Motors, Tanks, Valves
- Live Measurements and Historical Trends
- Active Alarms and Alarm History
- Equipment Health and Communication Status
- Production and Downtime Reports
- Tag Browser and Instrument Diagnostics
- User Administration and Permissions
- System Health, Backups, Configuration

**Data Flow:**
```
Avalonia UI ←→ Local IPC/HTTP ←→ ScadaServer REST API
                      ↓
                Local Configuration Cache
```

**Offline Behavior:**
- Configuration cached locally (SQLite config database)
- Last-known values displayed with stale-data warnings
- Alarms and events persisted locally
- View navigation and historical data available from local historian
- Only writes blocked when offline (config changes, control commands)

### 2. SCADA Core Layer (Forked from Rapid SCADA)

**Purpose:** Reuse proven SCADA engine, communication, and storage components

**Components (Unchanged or Minimally Modified):**
- `ScadaCommon` - Core types, utilities, data structures
- `ScadaCommon.Log` - Logging infrastructure
- `ScadaCommon.MultiDb` - Database abstraction
- `ScadaComm` - Communication engine and drivers
- `ScadaServer` - Server engine and core logic
- `ScadaAgent` - Agent system (if needed for distributed tasks)

**Components (Modified):**
- `ScadaServerEngine` - Extended with control safety boundary
- `ScadaServerCommon` - Enhanced with offline configuration management
- Database schema - Extended for coal plant-specific entities

**Drivers (Reused):**
- Modbus TCP/RTU - Primary protocol for PLCs
- OPC UA - Modern industrial equipment
- MQTT - IoT sensors and messaging
- OPC Classic - Legacy equipment
- Simulator - Testing and development

### 3. Control Safety Boundary Layer (NEW)

**Purpose:** Explicit validation and authorization layer for machine control

**Components:**
- `ArchScada.ControlBoundary` - Control validation engine
- `ArchScada.ControlBoundary.Interface` - Control command interface
- `ArchScada.ControlBoundary.Simulator` - Simulated control adapter (read-only)
- `ArchScada.ControlBoundary.Real` - Real control adapter (disabled until authorized)
- `ArchScada.ControlBoundary.Audit` - Immutable audit trail

**Control Lifecycle:**
```
Desktop HMI → Control Interface → Validation → Safety Check → Command → PLC
                ↓                    ↓               ↓            ↓
           Authentication      Command Valid    Interlock   Feedback
           Authorization       Range Check      Check       Verification
           Session Check       Rate Limit       Timeout     Audit Log
```

**Safety Rules:**
- No automatic shutdowns based on telemetry thresholds
- No automatic restarts after faults
- All commands require explicit operator confirmation
- Commands timeout and are marked unconfirmed if no feedback
- Failed commands are logged and reported, never assumed successful
- Safety PLCs, hardwired E-stops, and local controls remain effective
- Control disabled until risk assessment, cause-and-effect matrix, and site authorization complete

### 4. Web Interface Layer (Optional - ScadaWeb)

**Purpose:** Remote access for supervisors, engineers, and support

**Components (Reused with modifications):**
- `ScadaWeb` - ASP.NET Core web application
- `ScadaWebCommon` - Shared web logic
- `OpenPlugins` - Chart, Scheme, Report plugins

**Modifications:**
- Read-only access for remote users (no control via web)
- Restricted view permissions per user role
- Rate limiting and session timeouts
- Only accessible from internal network (no internet exposure)

### 5. Local Storage Layer

**Primary Database:** PostgreSQL
- **Purpose:** Production historian and configuration storage
- **Schema:** Extended Rapid SCADA schema with coal plant tables
- **Location:** Local server or embedded PostgreSQL on workstation
- **Backup:** Scheduled local backups with verification

**Fallback Storage:** SQLite
- **Purpose:** Single-node deployments, lightweight installations
- **Usage:** Configuration cache, offline operation, development
- **Synchronization:** Reconcile with PostgreSQL when connectivity restored

**Archive Options:**
- Built-in PostgreSQL historian (ModArcPostgreSql)
- InfluxDB for high-frequency data (optional, ModArcInfluxDb)
- File-based archives for long-term cold storage

### 6. Configuration Management Layer

**Purpose:** Version-controlled, validated configuration with recovery

**Components:**
- `ArchScada.Config` - Configuration management service
- `ArchScada.Config.Validation` - Schema validation
- `ArchScada.Config.Versioning` - Git-based version control
- `ArchScada.Config.Migrations` - Automated schema migrations

**Configuration Structure:**
```
ArchScada Project/
├── Config/
│   ├── ScadaServerConfig.xml
│   ├── ScadaCommConfig.xml
│   ├── Equipment/
│   │   ├── Areas.xml
│   │   ├── EquipmentTypes.xml
│   │   ├── Devices.xml
│   │   └── Tags.xml
│   ├── Alarms/
│   │   ├── AlarmLimits.xml
│   │   ├── AlarmPriorities.xml
│   │   └── AlarmSuppression.xml
│   └── Views/
│       ├── Mimics/
│       └── Dashboards/
├── Database/
│   └── Migrations/
└── Templates/
    └── CoalPlant/
```

**Version Control:**
- Configuration stored in Git repository
- Schema validation before commit
- Migration scripts for database changes
- Rollback capability to previous configuration versions

### 7. Protocol and Driver Layer

**Purpose:** Communication with field devices and PLCs

**Drivers (Reused from Rapid SCADA):**
- **Modbus TCP/RTU** - Primary driver for coal plant PLCs
  - Support for holding registers, input registers, coils, discrete inputs
  - Configurable polling rates and timeouts
  - Connection quality monitoring
- **OPC UA** - Modern industrial equipment
  - Secure OPC UA client with encryption
  - Browse and subscribe capabilities
  - Certificate management
- **MQTT** - IoT sensors and messaging
  - MQTT client for subscribing to sensor data
  - Publisher for sending data to external systems
- **OPC Classic** - Legacy equipment
  - COM/DCOM-based OPC DA client
  - For legacy PLCs and controllers

**Driver Configuration:**
- Per-device connection parameters
- Polling rate optimization
- Retry policies and backoff
- Quality indicators and stale-data detection
- Communication event logging

### 8. Process Model Layer

**Purpose:** Data model for coal plant equipment and measurements

**Entities:**
- **Plant** - Top-level plant identifier
- **Area** - Plant area (Crushing, Conveyors, Thickeners, etc.)
- **Equipment** - Physical equipment (Motor, Pump, Valve, Tank, etc.)
- **Device** - PLC or field device associated with equipment
- **Tag** - Measurement point (analog, digital, counter, derived)
- **Alarm** - Alarm condition definition
- **Interlock** - Equipment interlock references (read-only)
- **Maintenance** - Maintenance state and event history

**Tag Attributes:**
- Tag ID and name
- Equipment association
- PLC/device mapping (register address, OPC node ID)
- Data type (analog, digital, counter, derived)
- Engineering units
- Expected range and validation rules
- Communication quality threshold
- Stale-data timeout
- Alarm limits (high, high-high, low, low-low)
- Alarm priority and acknowledgement policy

**Equipment State Model:**
- **Running** - Confirmed running state
- **Stopped** - Confirmed stopped state
- **Faulted** - Equipment fault detected
- **Interlocked** - Interlock condition active
- **Unavailable** - Communication lost
- **Unknown** - State cannot be determined

**Critical Rule:** Never infer "stopped" from communication loss. Distinguish confirmed states from unknown conditions.

### 9. Alarm and Event Layer

**Purpose:** Alarm detection, presentation, acknowledgement, and audit

**Alarm Lifecycle:**
1. **Detection** - Configured alarm condition violated
2. **Validation** - Check for alarm flood suppression, deadband, and delay
3. **Persistence** - Store alarm locally with timestamp and context
4. **Presentation** - Display to authorized operators with severity
5. **Acknowledgement** - Operator acknowledges alarm (separate from clearance)
6. **Return-to-Normal** - Condition returns within limits
7. **Escalation** - Notification to supervisors if unacknowledged
8. **Audit** - Complete audit history preserved

**Alarm Features:**
- Configurable deadbands and delays
- Alarm suppression rules (visible and auditable)
- Alarm grouping and prioritization
- Recurrence tracking
- Never silently suppress safety-critical alarms
- Persistent storage even if offline
- Notification channels (email, SMS - locally configured)

### 10. Reporting and Analytics Layer

**Purpose:** Production metrics, downtime tracking, and maintenance reports

**Reports:**
- Production summary (throughput, tons processed)
- Downtime analysis (equipment, duration, root cause)
- Equipment health reports (MTBF, MTTR)
- Alarm statistics (frequency, equipment, response time)
- Communication quality reports
- Maintenance schedules and history

**Calculations:**
- Derived tags based on source measurements
- Production totals and rates
- Efficiency metrics
- Downtime categorization

**Audit:** All calculations must identify source tags, method, units, and timestamp.

## Deployment Topology

### Single-Node Deployment (Small Plant)
```
┌─────────────────────────────────────┐
│    Operator Workstation (Linux)     │
│  ┌───────────────────────────────┐ │
│  │ Arch SCADA Desktop HMI         │ │
│  │ ScadaServer + ScadaComm        │ │
│  │ PostgreSQL (embedded)          │ │
│  └───────────────────────────────┘ │
└──────────────┬──────────────────────┘
               │
      ┌────────▼────────┐
      │ Field Devices  │
      │ (Modbus/OPC)   │
      └────────────────┘
```

### Multi-Node Deployment (Large Plant)
```
┌──────────────────────┐  ┌──────────────────────┐
│  Operator Station 1  │  │  Operator Station 2  │
│  (Desktop HMI)      │  │  (Desktop HMI)      │
└──────────┬───────────┘  └──────────┬───────────┘
           │                         │
           └───────────┬─────────────┘
                       │
┌──────────────────────▼──────────────────────┐
│          SCADA Server (Linux)               │
│  ┌──────────────────────────────────────┐  │
│  │ ScadaServer + ScadaComm              │  │
│  │ PostgreSQL (dedicated server)        │  │
│  │ Control Safety Boundary              │  │
│  └──────────────────────────────────────┘  │
└──────────────────────┬──────────────────────┘
                       │
      ┌────────────────┼────────────────┐
      │                │                │
┌─────▼─────┐   ┌─────▼─────┐   ┌─────▼─────┐
│   PLC 1   │   │   PLC 2   │   │   PLC 3   │
│ (Area A)  │   │ (Area B)  │   │ (Area C)  │
└───────────┘   └───────────┘   └───────────┘
```

### Network Segmentation

```
┌─────────────────────────────────────────────────┐
│              Enterprise IT Network              │
│  (Email, ERP, Office - NO SCADA ACCESS)       │
└────────────────────┬────────────────────────────┘
                     │ Firewall (DMZ)
┌────────────────────▼────────────────────────────┐
│               SCADA Network                     │
│  ┌──────────────────────────────────────────┐  │
│  │ SCADA Server + Operator Workstations      │  │
│  │ (Desktop HMI + Optional Web UI)         │  │
│  └──────────────────┬───────────────────────┘  │
└─────────────────────┼──────────────────────────┘
                      │ Firewall
┌─────────────────────▼──────────────────────────┐
│               Control Network                  │
│  ┌──────────────────────────────────────────┐  │
│  │ PLCs, Drives, I/O, Field Devices         │  │
│  │ (Modbus, OPC UA, MQTT)                   │  │
│  └──────────────────────────────────────────┘  │
└─────────────────────────────────────────────────┘
```

## Communication Flows

### 1. Real-Time Data Flow
```
PLC/Device → Driver → ScadaComm → ScadaServer → Historian → Desktop HMI
                                    ↓
                              Alarm Engine
                                    ↓
                              Alarm Notification
```

### 2. Control Command Flow (Future, Authorized)
```
Desktop HMI → Control Interface → Validation → Safety Check → Driver → PLC
                ↓                    ↓               ↓            ↓
           AuthN/AuthZ          Command Valid   Interlock   Feedback
           Session Check         Range Check     Timeout     Verify
                                Rate Limit                   Audit
```

### 3. Configuration Change Flow
```
Desktop HMI → Config Service → Validation → Git Commit → DB Migration
                ↓                    ↓               ↓           ↓
           AuthN/AuthZ          Schema Check    Version     Apply
           Permission            Syntax Check   Backup      Restart
```

### 4. Offline Data Flow
```
Local Cache → Desktop HMI (stale warning)
Local DB → Desktop HMI (historical data)
Local Alarm Store → Desktop HMI (alarm history)
```

## Threading and Concurrency

### ScadaServer Threading
- **Main Thread:** Orchestration and lifecycle management
- **Communication Thread:** ScadaComm driver polling
- **Archive Thread:** Historian data persistence
- **Alarm Thread:** Alarm evaluation and notification
- **API Thread:** REST API for HMI and web interface

### Desktop HMI Threading
- **UI Thread:** Avalonia UI rendering and interaction
- **Data Update Thread:** Periodic data refresh from server
- **Background Task Thread:** Reports, calculations, exports

### Thread Safety
- Shared data protected by locks or concurrent collections
- Message queues for cross-thread communication
- No blocking operations on UI thread
- Bounded queues to prevent memory exhaustion

## Fault Handling and Recovery

### Communication Loss
- Detect loss within configured timeout
- Mark devices as "unavailable" with timestamp
- Display last-known values with stale-data warning
- Attempt reconnection with exponential backoff
- Log all communication events
- Do not infer equipment state from communication loss

### Application Crash
- Automatic restart via systemd/supervisor
- Database recovery via PostgreSQL WAL
- Configuration restored from Git repository
- State reconstructed from historian
- Crash logs sent to error monitoring

### Database Failure
- Failover to SQLite fallback (if configured)
- Buffer data in memory (bounded queue)
- Log critical alarms to file
- Manual database recovery procedures

### Disk Space Exhaustion
- Monitor disk space and alert before exhaustion
- Automatic archive rotation
- Emergency mode: stop archiving, keep alarms
- Manual cleanup procedures

## Security Boundaries

### Authentication
- Local user accounts (ScadaWebCommon user system)
- Active Directory integration (ModActiveDirectory)
- Multi-factor authentication for control operations (future)
- Session timeout and re-authentication

### Authorization
- Role-based access control (RBAC)
- Per-equipment view permissions
- Control operation permissions (separate from view)
- Configuration change permissions

### Network Security
- Network segmentation (IT, SCADA, Control)
- Firewall rules between zones
- No internet exposure from SCADA network
- VPN for remote access (authorized only)

### Data Security
- Encrypted database at rest (PostgreSQL encryption)
- Encrypted communication (TLS for OPC UA, optional for Modbus)
- Secrets stored outside source control
- Audit logs tamper-evident

### Dependency Security
- Regular dependency updates
- Vulnerability scanning
- Signed release artifacts
- Controlled update process

## Offline Operation Capabilities

### What Works Offline
- View last-known equipment states (with stale warnings)
- Browse historical trends from local historian
- View alarm and event history
- Navigate plant mimics and dashboards
- Access configuration (cached locally)
- Generate reports from local data
- User authentication (cached credentials)

### What Requires Online
- Real-time data updates
- Control commands (block offline)
- Configuration changes (block offline)
- Remote web access
- External notifications (email, SMS)
- Database synchronization (multi-node)

### Recovery Procedures
- Automatic reconnection when connectivity restored
- Buffered data flush to historian
- Configuration synchronization
- Data reconciliation for conflicting updates
- Clear indication of sync status

## Performance Considerations

### Data Rate Targets
- Modbus polling: 1-10 Hz per device (configurable)
- OPC UA subscription: 100-1000 Hz for critical tags
- Historian write: Batched for efficiency
- HMI refresh: 1-5 Hz for real-time displays

### Resource Limits
- Maximum tags: 10,000 (configurable)
- Maximum concurrent alarms: 1,000 (with flood suppression)
- Maximum historical data: Configured retention policy
- Memory usage: Monitored and alerted

### Optimization
- Data compression for historian
- Lazy loading for historical queries
- Caching for frequently accessed views
- Connection pooling for database

## Technology Stack Summary

| Layer | Technology | Purpose |
|-------|-----------|---------|
| Desktop UI | Avalonia UI (.NET 10) | Cross-platform HMI |
| SCADA Core | Rapid SCADA v6 | Proven SCADA engine |
| Drivers | Modbus, OPC UA, MQTT | Field device communication |
| Database | PostgreSQL / SQLite | Historian and config |
| Web UI | ASP.NET Core | Optional remote access |
| Build | MSBuild / dotnet CLI | Build system |
| Config | Git + XML | Version control |
| Testing | xUnit + Moq | Test framework (to be added) |

## Migration Strategy from Rapid SCADA

### Phase 1: Foundation
- Fork repository
- Verify reproducible build
- Set up Avalonia UI project structure
- Establish test infrastructure

### Phase 2: Desktop Shell
- Migrate ScadaAdmin functionality to Avalonia
- Implement plant overview screens
- Build equipment status displays
- Add alarm visualization

### Phase 3: Core Integration
- Integrate ScadaServer with Desktop HMI
- Implement local configuration caching
- Add offline operation mode
- Extend database schema for coal plant

### Phase 4: Control Safety
- Implement control boundary layer
- Add simulated control adapter
- Build command validation
- Create audit trail

### Phase 5: Web Interface (Optional)
- Configure ScadaWeb for read-only access
- Restrict permissions for remote users
- Add rate limiting and session management

## Known Architectural Risks

1. **UI Framework Migration:** WinForms to Avalonia is significant effort
2. **Test Coverage:** Upstream has minimal tests; Arch SCADA needs comprehensive testing
3. **Offline-First:** Current design is server-centric; requires architectural changes
4. **Control Safety:** Industrial control requires rigorous validation
5. **Cross-Platform Desktop:** Avalonia ecosystem maturity vs WinForms
6. **Multi-Solution Build:** 13 sequential builds are error-prone

## Success Criteria

- [ ] Desktop HMI runs on Windows and Linux
- [ ] System operates offline for essential functions
- [ ] All critical alarms persist and are available offline
- [ ] Configuration is version-controlled and recoverable
- [ ] Communication quality indicators are accurate
- [ ] Control commands blocked until explicitly authorized
- [ ] All commands have immutable audit trail
- [ ] System recovers gracefully from communication loss
- [ ] Database backups are automated and verified
- [ ] Comprehensive test suite passes
- [ ] Security boundaries are documented and enforced
