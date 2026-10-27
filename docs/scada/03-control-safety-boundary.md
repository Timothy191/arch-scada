# Arch SCADA Control Safety Boundary

## Executive Summary

This document defines the control safety boundary for Arch SCADA, a desktop SCADA system for coal processing plant monitoring and automation. The system follows a **read-only-first** philosophy with explicit authorization, validation, and audit trails for any machine control operations. Control operations are **disabled by default** and require site engineering authorization, risk assessment, and independent validation before activation.

## Non-Negotiable Safety Requirements

### 1. No Live Machinery Control During Development
- **Requirement:** No commands to actual field devices during initial development
- **Implementation:** Control adapter is simulated (returns success without PLC write)
- **Verification:** All control operations log "SIMULATED - NO DEVICE WRITE"

### 2. No Automatic Shutdowns Based on Telemetry
- **Requirement:** System never automatically shuts down machinery because a telemetry threshold was exceeded
- **Rationale:** Telemetry may be stale, incorrect, or misconfigured
- **Implementation:** Alarms are generated for human review, not automatic actions
- **Exception:** None - operator must manually initiate shutdowns

### 3. No Replacement for Certified Safety Systems
- **Requirement:** Arch SCADA is not a replacement for:
  - Safety PLCs (certified IEC 61508 SIL-rated)
  - Hardwired emergency stops
  - Safety relays and interlocks
  - Local manual controls
- **Rationale:** Software-based control cannot provide guaranteed safety response times
- **Implementation:** Document that Arch SCADA operates at a different safety integrity level
- **Verification:** All control commands have interlock check bypass (for safety system validation)

### 4. Independent Safety Functions Remain Effective
- **Requirement:** If Arch SCADA crashes, loses power, loses networking, or becomes compromised:
  - Safety PLCs continue operating
  - Hardwired E-stops remain functional
  - Safety interlocks remain active
  - Local manual controls remain available
- **Rationale:** Software failures must not disable safety systems
- **Implementation:** Arch SCADA does not control safety-critical interlocks
- **Verification:** Safety system validation independent of Arch SCADA

### 5. Cause-and-Effect Matrix Required
- **Requirement:** Before enabling any control function:
  - Approved cause-and-effect matrix documented
  - Risk assessment completed
  - Safe-state analysis performed
  - Site engineering authorization obtained
- **Implementation:** Control operations blocked until C&E matrix uploaded and verified
- **Verification:** Control boundary validates C&E matrix before allowing commands

### 6. Read-Only Monitoring Until Authorization
- **Requirement:** System operates in read-only mode until separate authorization for control
- **Implementation:** Control adapter is simulated until "REAL CONTROL" flag set (protected)
- **Verification:** All control operations return "SIMULATED" until authorization

### 7. Command Validation and Verification
- **Requirement:** Every control command must:
  - Validate operator permissions
  - Validate command parameters (range, type, format)
  - Check interlock conditions
  - Require explicit operator confirmation
  - Wait for PLC feedback
  - Mark unconfirmed if timeout or error
  - Log immutable audit record
- **Implementation:** Control boundary enforces all validation steps
- **Verification:** Failed commands are reported as unconfirmed, never as successful

### 8. No Automatic Restarts After Faults
- **Requirement:** System never automatically restarts machinery after:
  - Fault detection
  - Communication recovery
  - Application restart
- **Rationale:** Automatic restart could cause equipment damage or safety hazards
- **Implementation:** Restart requires explicit operator command
- **Verification:** Auto-restart logic is absent from codebase

## Control Architecture

### Read-Only-First Control Boundary

```
┌─────────────────────────────────────────────────────────────┐
│                   Desktop HMI (Avalonia UI)                   │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ Equipment Control Dialog                               │  │
│  │ • Stop Button (disabled in read-only mode)            │  │
│  │ • Start Button (disabled in read-only mode)            │  │
│  │ • Speed Setpoint (disabled in read-only mode)         │  │
│  │ • Interlock Status (read-only, always available)      │  │
│  └──────────────────────────────────────────────────────┘  │
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│              Control Interface Layer                         │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ Command Validation                                    │  │
│  │ • Authentication check                                │  │
│  │ • Authorization check (control permission)            │  │
│  │ • Session validity check                              │  │
│  │ • Parameter validation (range, type)                   │  │
│  │ • Rate limiting check                                  │  │
│  └──────────────────────────────────────────────────────┘  │
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│              Control Safety Boundary                         │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ Safety Checks                                          │  │
│  │ • Interlock condition check                           │  │
│  │ • Equipment state check                               │  │
│  │ • Communication quality check                          │  │
│  │ • Command timeout configuration                       │  │
│  │ • Cause-and-effect matrix validation                  │  │
│  └──────────────────────────────────────────────────────┘  │
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│              Control Adapter Selection                       │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ IF ControlAuthorized = FALSE                         │  │
│  │     → Simulated Adapter (logs, no PLC write)         │  │
│  │ ELSE IF ControlAuthorized = TRUE                      │  │
│  │     → Real Adapter (writes to PLC)                    │  │
│  └──────────────────────────────────────────────────────┘  │
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│              Driver Layer (ScadaComm)                       │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ Modbus Driver / OPC UA Driver                          │  │
│  │ • Write command to PLC register                       │  │
│  │ • Wait for confirmation                                │  │
│  │ • Return success/failure                               │  │
│  └──────────────────────────────────────────────────────┘  │
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│                    Field Device (PLC)                        │
│  ┌──────────────────────────────────────────────────────┐  │
│  │ • Execute command                                     │  │
│  │ • Return feedback                                     │  │
│  │ • Safety interlocks remain independent                │  │
│  └──────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

## Control Command Lifecycle

### Step 1: User Initiates Command
```
Operator clicks "Start Motor" button in Desktop HMI
↓
HMI displays confirmation dialog:
  "Start Motor M-101 (Crusher Feed)"
  Current State: Stopped
  Interlock: OK
  Confirm: [Yes] [No]
↓
Operator clicks "Yes"
```

### Step 2: Command Validation
```
Control Interface validates:
  ✓ User authenticated
  ✓ User has "CONTROL_MOTORS" permission
  ✓ Session is active (not expired)
  ✓ Command type: START_MOTOR
  ✓ Equipment ID: M-101
  ✓ Parameter: valid
  ✓ Rate limit: not exceeded (max 10 commands/minute)
↓
Validation passes → proceed to safety checks
Validation fails → return error to HMI, log attempt
```

### Step 3: Safety Boundary Checks
```
Control Safety Boundary checks:
  ✓ Interlock conditions satisfied
  ✓ Equipment state allows start (not faulted)
  ✓ Communication quality: Good (last update < 5 seconds)
  ✓ Timeout configured: 10 seconds
  ✓ Cause-and-effect matrix uploaded and verified
  ✓ Command matches C&E matrix rule
↓
All checks pass → proceed to adapter
Any check fails → return error to HMI, log failure reason
```

### Step 4: Adapter Selection
```
IF ControlAuthorized = FALSE
  → Use Simulated Adapter
  → Log: "SIMULATED - START M-101 - NO DEVICE WRITE"
  → Return success to HMI (command appeared to succeed)
  → Mark audit record as SIMULATED
ELSE IF ControlAuthorized = TRUE
  → Use Real Adapter
  → Proceed to driver layer
```

### Step 5: Driver Execution
```
Real Adapter sends command to driver:
  Driver writes to Modbus register / OPC UA node
  Driver waits for confirmation
  Timeout: 10 seconds
↓
IF confirmation received within timeout
  → Return success to adapter
ELSE
  → Return timeout error to adapter
```

### Step 6: Feedback Verification
```
Adapter receives driver result:
IF success
  → Verify equipment state changed (read status register)
  → State changed to RUNNING → command confirmed
  → State still STOPPED → command unconfirmed (log warning)
IF timeout/error
  → Command unconfirmed
  → Log error with PLC response
```

### Step 7: Audit Logging
```
Immutable audit record created:
  Timestamp: 2026-10-27T10:15:30Z
  User: operator1
  Command: START_MOTOR
  Equipment: M-101
  Parameters: {}
  Result: SUCCESS / TIMEOUT / UNCONFIRMED / SIMULATED
  Feedback: Equipment state = RUNNING
  C&E Rule: Rule-101
  Interlocks: [I-101, I-102]
  Communication Quality: Good
  Duration: 0.5 seconds
  Audit ID: UUID
↓
Audit record written to append-only log
  Tamper-evident storage (hash chain or WORM)
```

### Step 8: HMI Feedback
```
HMI displays result to operator:
  ✓ Success: "Motor M-101 started successfully"
  ✗ Timeout: "Command timeout - PLC did not respond"
  ⚠ Unconfirmed: "Command sent but state not verified"
  ℹ Simulated: "Command simulated (control not authorized)"
↓
HMI updates equipment display:
  Status icon updated
  Timestamp updated
  Communication quality indicator updated
```

## Control Authorization Process

### Pre-Authorization State (Default)
- **Control Mode:** READ_ONLY
- **Control Adapter:** SIMULATED
- **Control Commands:** Logged but not sent to PLC
- **Authorization:** Not authorized
- **Status:** "Monitoring Only - Control Disabled"

### Authorization Prerequisites
Before control can be authorized, the following must be completed:

1. **Risk Assessment**
   - Site-specific risk assessment completed
   - Hazard analysis performed
   - Risk mitigation documented
   - Approved by site safety engineer

2. **Cause-and-Effect Matrix**
   - C&E matrix documented for all controllable equipment
   - Safe-state analysis completed
   - Interlock dependencies mapped
   - Approved by plant engineering

3. **Control Logic Review**
   - Control boundary code reviewed by qualified engineer
   - Safety checks verified
   - Timeout configurations validated
   - Audit logging verified

4. **Test Validation**
   - Control logic tested with simulated PLC
   - Fault scenarios tested (timeout, communication loss)
   - Interlock bypass verified (for safety system validation)
   - Operator training completed

5. **Site Authorization**
   - Written authorization from plant manager
   - Authorization documented in project records
   - Authorization date and signatory recorded
   - Authorization scope defined (which equipment, which users)

### Authorization Activation
Once prerequisites are met, control is activated via:

```
1. Upload authorization document to system
2. Upload cause-and-effect matrix to system
3. System validates document and matrix
4. System prompts for administrative confirmation
5. Administrator confirms authorization
6. System sets ControlAuthorized = TRUE
7. System writes audit record: "CONTROL AUTHORIZED - [Scope]"
8. System switches from Simulated to Real adapter
9. System displays: "CONTROL AUTHORIZED - [Scope]"
```

### Authorization Deactivation
Control can be deactivated at any time:

```
1. Administrator initiates deactivation
2. System prompts for confirmation and reason
3. Administrator confirms
4. System sets ControlAuthorized = FALSE
5. System switches from Real to Simulated adapter
6. System writes audit record: "CONTROL DEAUTHORIZED - [Reason]"
7. System displays: "CONTROL DISABLED - READ ONLY"
```

**Deactivation Triggers:**
- Safety incident or near-miss
- Software bug in control logic
- Unauthorized access attempt
- Regulatory audit requirement
- Plant maintenance window
- Explicit deactivation by administrator

## Interlock Assumptions

### Arch SCADA Interlocks (Software)
- **Purpose:** Prevent unsafe operating states from SCADA perspective
- **Type:** Software-based interlocks
- **Reliability:** Same reliability as Arch SCADA application
- **Limitation:** Not safety-critical (can fail if app crashes)

**Examples:**
- Prevent starting motor if downstream equipment not running
- Prevent opening valve if downstream pressure too high
- Prevent changing setpoint if equipment in fault state

### Safety System Interlocks (Hardware)
- **Purpose:** Prevent unsafe operating states from safety perspective
- **Type:** Hardwired or safety PLC interlocks
- **Reliability:** Higher reliability (independent of SCADA)
- **Status:** Always active, cannot be bypassed by SCADA

**Examples:**
- Emergency stop button stops equipment immediately
- Safety PLC prevents unsafe sequence
- Hardwired interlock prevents conflicting valve positions
- Pressure relief valve independent of SCADA

### Interlock Hierarchy
```
Level 1: Safety System Interlocks (Hardware/Safety PLC)
  - Always active
  - Highest priority
  - Cannot be bypassed
  - Independent of SCADA

Level 2: Arch SCADA Interlocks (Software)
  - Active when SCADA running
  - Medium priority
  - Can be bypassed for safety system validation
  - Dependent on SCADA

Level 3: Operator Discretion
  - Operator can override Level 2
  - Cannot override Level 1
  - Requires explicit authorization
  - Audit logged
```

### Interlock Bypass
**Requirement:** Ability to bypass Arch SCADA interlocks for safety system validation

**Process:**
1. Operator requests interlock bypass
2. System displays interlock being bypassed
3. System requires confirmation from two operators (dual control)
4. System requires reason documentation
5. System logs bypass with audit record
6. Interlock bypassed for single command
7. Interlock re-enforces after command
8. Audit record reviewed by safety engineer

**Never Bypass:**
- Safety system interlocks (Level 1)
- Emergency stop functionality
- Hardwired protections

## Safe-State Analysis

### Safe-State Definition
For each controllable equipment, define the safe state:

| Equipment Type | Safe State | Rationale |
|----------------|------------|-----------|
| Motor | Stopped | Prevent uncontrolled motion |
| Valve | Closed | Prevent uncontrolled flow |
| Pump | Stopped | Prevent overpressure |
| Conveyor | Stopped | Prevent material buildup |
| Crusher | Stopped | Prevent equipment damage |
| Thickener | Normal operation | Prevent process upset |

### Safe-State Transition
**Requirement:** Arch SCADA never automatically transitions to safe state

**Process:**
1. Operator detects unsafe condition (via alarm or observation)
2. Operator initiates safe-state transition (manual command)
3. Control boundary validates command
4. Safety checks performed
5. Command sent to PLC
6. Equipment transitions to safe state
7. Audit record logged

**Automatic Safe-State (PROHIBITED):**
- System does NOT automatically stop motor on high temperature
- System does NOT automatically close valve on high pressure
- System does NOT automatically initiate emergency shutdown

**Rationale:** Automatic safe-state could be triggered by:
- Stale telemetry
- Sensor failure
- Configuration error
- Software bug
- Communication loss

## Failure Modes

### Communication Loss
**Scenario:** Communication with PLC lost

**Behavior:**
- Mark equipment as "Unavailable"
- Display last-known state with stale warning
- Block all control commands (control requires communication)
- Continue monitoring other equipment
- Log communication loss event
- Attempt reconnection with backoff

**Control State:** Commands blocked until communication restored

### Command Timeout
**Scenario:** Command sent to PLC but no confirmation received

**Behavior:**
- Mark command as "Unconfirmed"
- Display timeout warning to operator
- Do NOT assume command succeeded
- Do NOT retry automatically
- Log timeout event
- Allow operator to retry manually

**Control State:** Equipment state unknown until verified

### Equipment Fault
**Scenario:** PLC reports equipment fault

**Behavior:**
- Mark equipment as "Faulted"
- Display fault status and code
- Block control commands (except fault reset if authorized)
- Generate alarm
- Log fault event

**Control State:** Requires fault clearance before control

### Application Crash
**Scenario:** Arch SCADA Desktop HMI crashes

**Behavior:**
- Safety systems remain independent (hardware interlocks, E-stops)
- PLC continues operating with last valid commands
- Application restarts automatically
- State reconstructed from historian
- Audit log preserved

**Control State:** No control commands during crash, equipment maintains last state

### Database Failure
**Scenario:** PostgreSQL database unavailable

**Behavior:**
- Switch to SQLite fallback (if configured)
- Buffer commands in memory (bounded queue)
- Log critical alarms to file
- Block configuration changes
- Block control commands (no audit trail possible)

**Control State:** Commands blocked until database restored

### Power Loss
**Scenario:** Operator workstation loses power

**Behavior:**
- Safety systems remain independent
- PLC continues operating
- SCADA server (if separate) continues running
- Desktop HMI unavailable until power restored
- No control commands during outage

**Control State:** Equipment maintains last state

### Security Compromise
**Scenario:** Unauthorized access attempt detected

**Behavior:**
- Deactivate control authorization immediately
- Switch to Simulated adapter
- Lock user accounts
- Alert security team
- Log security event
- Require manual re-authorization

**Control State:** Commands blocked until investigation complete

## Commissioning Gates

### Gate 1: Read-Only Commissioning
**Objective:** Verify monitoring functionality without control

**Criteria:**
- [ ] All equipment tags configured and receiving data
- [ ] Communication quality indicators accurate
- [ ] Alarms detected and displayed correctly
- [ ] Historical data archived successfully
- [ ] Desktop HMI displays correct plant state
- [ ] Operator training completed for monitoring

**Approval:** Plant Manager, Controls Engineer

### Gate 2: Control Logic Validation
**Objective:** Validate control logic with simulated PLC

**Criteria:**
- [ ] Control boundary code reviewed
- [ ] Simulated adapter tests pass
- [ ] Safety checks verified
- [ ] Timeout configurations validated
- [ ] Audit logging verified
- [ ] Fault scenarios tested (timeout, communication loss)
- [ ] Interlock bypass verified

**Approval:** Safety Engineer, Controls Engineer

### Gate 3: Safety System Validation
**Objective:** Verify safety systems remain independent

**Criteria:**
- [ ] Safety PLC interlocks verified
- [ ] Hardwired E-stops verified
- [ ] Safety system operates independently of SCADA
- [ ] SCADA crash does not affect safety systems
- [ ] Power loss does not affect safety systems
- [ ] Interlock bypass procedure verified

**Approval:** Safety Engineer, Plant Manager

### Gate 4: Cause-and-Effect Matrix Approval
**Objective:** Approve control logic documentation

**Criteria:**
- [ ] Cause-and-effect matrix documented
- [ ] Safe-state analysis completed
- [ ] Interlock dependencies mapped
- [ ] Risk assessment completed
- [ ] Matrix reviewed by plant engineering
- [ ] Matrix uploaded to system

**Approval:** Plant Engineering, Safety Engineer

### Gate 5: Control Authorization
**Objective:** Authorize control operations

**Criteria:**
- [ ] All previous gates passed
- [ ] Risk assessment approved
- [ ] C&E matrix approved
- [ ] Control logic validated
- [ ] Safety systems validated
- [ ] Operator training completed for control
- [ ] Written authorization from plant manager

**Approval:** Plant Manager (final authority)

### Gate 6: Production Authorization
**Objective:** Authorize production operation with control

**Criteria:**
- [ ] Control authorization gate passed
- [ ] Test operation completed successfully
- [ ] No safety incidents during test
- [ ] Operator feedback positive
- [ ] Audit log review shows no issues
- [ ] Final safety sign-off

**Approval:** Plant Manager, Safety Engineer

## Documentation Requirements

### Control Authorization Document
**Content:**
- Authorization scope (equipment, users, operations)
- Authorization date and expiry
- Authorizing signatory and title
- Prerequisites checklist
- Risk assessment reference
- C&E matrix reference
- Conditions for deactivation

### Cause-and-Effect Matrix
**Content:**
- Equipment list and controllable operations
- Control logic for each operation
- Interlock dependencies
- Safe-state definition
- Pre-conditions for operation
- Post-conditions for operation
- Failure mode handling

### Risk Assessment
**Content:**
- Hazard identification
- Risk analysis (severity, likelihood)
- Risk mitigation measures
- Residual risk acceptance
- Safety function requirements
- Verification methods

### Safety Validation Report
**Content:**
- Safety system validation results
- Interlock verification results
- Failure mode test results
- Independence verification results
- Identified issues and resolutions
- Approval signatures

### Audit Log Review
**Content:**
- Audit log sample review
- Command traceability verification
- Interlock bypass review
- Security event review
- Tamper-evident storage verification

## Training Requirements

### Operator Training
**Topics:**
- Arch SCADA monitoring interface
- Alarm handling procedures
- Equipment status interpretation
- Communication quality indicators
- Stale data warnings
- Emergency procedures
- Control authorization understanding (if applicable)

### Controls Engineer Training
**Topics:**
- Control boundary architecture
- Command validation logic
- Safety check implementation
- Interlock configuration
- C&E matrix management
- Audit log review
- Troubleshooting control issues

### Safety Engineer Training
**Topics:**
- Control safety boundary overview
- Safety system independence
- Interlock hierarchy
- Safe-state analysis
- Risk assessment process
- Commissioning gates
- Deactivation procedures

## Maintenance and Change Control

### Control Logic Changes
**Process:**
1. Change request submitted
2. Risk assessment updated
3. C&E matrix updated
4. Control code modified
5. Code review performed
6. Testing performed (simulated)
7. Safety verification performed
8. Change approved
9. Change deployed
10. Audit record created

### Authorization Scope Changes
**Process:**
1. Change request submitted
2. Risk assessment updated
3. New authorization document created
4. Old authorization deactivated
5. New authorization activated
6. Audit record created

### Safety System Changes
**Process:**
1. Safety system change (independent of SCADA)
2. Re-validate safety system independence
3. Update Arch SCADA interlock configuration if needed
4. Re-test interlock bypass
5. Document changes
6. Re-approve control authorization if scope changed

## References

### Standards
- IEC 61508: Functional Safety of Electrical/Electronic/Programmable Electronic Safety-related Systems
- IEC 61511: Functional Safety - Safety Instrumented Systems for the Process Industry Sector
- ISA-84: Safety Instrumented Systems (SIS)
- ISA-88: Batch Control Systems
- ISA-95: Enterprise-Control System Integration

### Best Practices
- "Control Systems Security" - NIST SP 800-82
- "Industrial Control Systems Security" - DHS CISA
- "Safety Instrumented Systems" - CCPS
- "Process Safety Management" - OSHA 1910.119

## Appendices

### Appendix A: Control Command Types
| Command Type | Description | Equipment | Authorization Level |
|-------------|-------------|-----------|---------------------|
| START_MOTOR | Start motor | Motors | CONTROL_MOTORS |
| STOP_MOTOR | Stop motor | Motors | CONTROL_MOTORS |
| SET_SPEED | Set motor speed | VFDs | CONTROL_MOTORS |
| OPEN_VALVE | Open valve | Valves | CONTROL_VALVES |
| CLOSE_VALVE | Close valve | Valves | CONTROL_VALVES |
| SET_POSITION | Set valve position | Control Valves | CONTROL_VALVES |
| START_PUMP | Start pump | Pumps | CONTROL_PUMPS |
| STOP_PUMP | Stop pump | Pumps | CONTROL_PUMPS |
| RESET_FAULT | Clear equipment fault | All | CONTROL_RESET |

### Appendix B: Communication Quality Thresholds
| Quality Level | Last Update | Control Allowed |
|---------------|-------------|-----------------|
| Good | < 5 seconds | Yes |
| Degraded | 5-30 seconds | No (read-only) |
| Poor | 30-60 seconds | No (read-only) |
| Bad | > 60 seconds | No (unavailable) |

### Appendix C: Timeout Configurations
| Operation | Timeout | Retry Policy |
|-----------|----------|--------------|
| Motor Start | 10 seconds | Manual retry only |
| Motor Stop | 5 seconds | Manual retry only |
| Valve Open | 15 seconds | Manual retry only |
| Valve Close | 15 seconds | Manual retry only |
| Setpoint Change | 10 seconds | Manual retry only |
| Fault Reset | 10 seconds | Manual retry only |

### Appendix D: Audit Record Schema
```json
{
  "auditId": "uuid",
  "timestamp": "ISO8601",
  "user": "username",
  "command": "command_type",
  "equipment": "equipment_id",
  "parameters": {},
  "result": "SUCCESS|TIMEOUT|UNCONFIRMED|SIMULATED",
  "feedback": "equipment_state",
  "ceRule": "rule_id",
  "interlocks": ["interlock_id"],
  "communicationQuality": "Good|Degraded|Poor|Bad",
  "duration": "seconds",
  "simulated": true|false,
  "hash": "sha256"
}
```

## Sign-Off

**Document Owner:** Controls Engineer
**Reviewers:** Safety Engineer, Plant Manager
**Approval:** Plant Manager
**Effective Date:** TBD (upon authorization)
**Review Date:** Annually or after significant changes
