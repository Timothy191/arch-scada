# ArchCoalPlant — Rapid SCADA v6 / Arch SCADA Field PLC Configuration

This project contains the Rapid SCADA v6 (`ScadaAdmin` / `ScadaComm` / `ScadaServer`) configuration for the **Arch Coal Preparation Plant**, mapping field PLCs, online analyzers, centrifuge telemetry, and environmental/safety sensors.

## Directory Structure

```text
Projects/ArchCoalPlant/
├── ArchCoalPlant.rsproj                                  # Rapid SCADA v6 project file
├── Deployment.xml                                        # ScadaAdmin deployment profile
├── BaseXML/                                              # Configuration database (XML)
│   ├── Cnl.xml                                           # Channel & tag mappings
│   ├── CommLine.xml                                      # Communication lines (1..3)
│   ├── Device.xml                                        # Field PLCs, OPC UA & MQTT devices
│   ├── Obj.xml                                           # Plant area hierarchy (Objs 1..6)
│   ├── User.xml                                          # Users incl. read-only role (arch_readonly)
│   └── ...                                               # Standard Rapid SCADA v6 lookup tables
├── Instances/Default/
│   ├── ScadaComm/Config/ScadaCommConfig.xml              # Comm lines & device polling
│   ├── ScadaComm/Config/DrvMqttClient_021..023.xml       # MQTT topic subscriptions
│   └── ScadaServer/Config/ScadaServerConfig.xml          # Archiving (Basic + PostgreSQL) & security
└── README.md
```

> **Note:** Relative symlinks `Instances/Default/ScadaComm/ScadaCommConfig.xml` and `Instances/Default/ScadaServer/ScadaServerConfig.xml` point into `Config/` for convenience while preserving the standard Rapid SCADA v6 `Config/` layout expected by `ScadaAdmin`.

---

## Communication Lines & PLC Map

### Line 1 — Modbus TCP Field PLCs (`DrvCnlBasic` `TcpClient` + `DrvModbus`)
| Device # | Code | Equipment | IP : Port | Unit ID | Poll Period |
|---|---|---|---|---|---|
| `1` | `CR101` | CR-101 Primary Jaw Crusher PLC | `10.10.1.11:502` | `1` | `1s` |
| `2` | `SC102` | SC-102 Vibrating Screen 1 PLC | `10.10.1.12:502` | `2` | `1s` |
| `3` | `CV101` | CV-101 Main Feed Conveyor PLC | `10.10.1.13:502` | `3` | `1s` |
| `4` | `CV102` | CV-102 Clean Coal Conveyor PLC | `10.10.1.14:502` | `4` | `1s` |
| `5` | `TK201` | TK-201 High-Rate Thickener PLC | `10.10.2.21:502` | `5` | `2s` |
| `6` | `PP201` | PP-201 Underflow Pump A PLC | `10.10.2.22:502` | `6` | `2s` |
| `7` | `PP202` | PP-202 Underflow Pump B (Standby) PLC | `10.10.2.23:502` | `7` | `2s` |
| `8` | `PP203` | PP-203 Flocculant Dosing Pump PLC | `10.10.2.24:502` | `8` | `2s` |
| `9` | `TK301` | TK-301 Magnetite & DMC Circuit PLC | `10.10.3.31:502` | `9` | `2s` |

### Line 2 — OPC UA Process Analyzers & Centrifuge (`DrvOpcUa`)
| Device # | Code | Equipment | Endpoint | Poll Period |
|---|---|---|---|---|
| `11` | `AN601` | AN-601 PGNAA Online Coal Analyzer | `opc.tcp://10.10.6.61:4840` | `5s` |
| `12` | `CF501` | CF-501 Fine Coal Centrifuge | `opc.tcp://10.10.5.51:4840` | `2s` |

### Line 3 — MQTT Field Dust, Belt-Rip & Environmental Sensors (`DrvCnlMqtt` + `DrvMqttClient`)
Broker: `10.10.7.10:1883` (`ClientID: arch-coal-scada-comm`, read-only user `scada_ro`).

| Device # | Code | Sensor Group | Config File |
|---|---|---|---|
| `21` | `ENV_DUST` | Particulate Monitors (PM10 / PM2.5) | `DrvMqttClient_021.xml` |
| `22` | `BELT_RIP` | Conveyor Belt-Rip Safety Switches | `DrvMqttClient_022.xml` |
| `23` | `ENV_GAS` | Silo Tunnel CH4 & Ambient Sensors | `DrvMqttClient_023.xml` |

---

## Tag & Channel Table (`BaseXML/Cnl.xml`)

| CnlNum | TagCode | Name | Device | Area (`ObjNum`) | Type |
|---|---|---|---|---|---|
| `101` | `CR101_TPH` | CR-101 Crusher Throughput | `1` (`CR101`) | `2` Crushing & Conveyors | Input |
| `102` | `CR101_KW` | CR-101 Crusher Motor Power | `1` (`CR101`) | `2` Crushing & Conveyors | Input |
| `103` | `CR101_BRG` | CR-101 Bearing Temperature | `1` (`CR101`) | `2` Crushing & Conveyors | Input |
| `111` | `SC102_VIB` | SC-102 Screen Vibration | `2` (`SC102`) | `2` Crushing & Conveyors | Input |
| `112` | `SC102_KW` | SC-102 Screen Power | `2` (`SC102`) | `2` Crushing & Conveyors | Input |
| `121` | `CV101_SPD` | CV-101 Belt Speed | `3` (`CV101`) | `2` Crushing & Conveyors | Input |
| `122` | `CV101_AMP` | CV-101 Drive Current | `3` (`CV101`) | `2` Crushing & Conveyors | Input |
| `131` | `CV102_SPD` | CV-102 Belt Speed | `4` (`CV102`) | `2` Crushing & Conveyors | Input |
| `132` | `CV102_TMP` | CV-102 Gearbox Temperature | `4` (`CV102`) | `2` Crushing & Conveyors | Input |
| `201` | `TK201_LVL` | TK-201 Thickener Bed Level | `5` (`TK201`) | `3` Thickener Circuit | Input |
| `202` | `TK201_TRQ` | TK-201 Rake Torque | `5` (`TK201`) | `3` Thickener Circuit | Input |
| `203` | `TK201_TRB` | TK-201 Overflow Turbidity | `5` (`TK201`) | `3` Thickener Circuit | Input |
| `211` | `PP201_FLW` | PP-201 Underflow Flow A | `6` (`PP201`) | `3` Thickener Circuit | Input |
| `212` | `PP201_PRS` | PP-201 Discharge Pressure | `6` (`PP201`) | `3` Thickener Circuit | Input |
| `221` | `PP202_FLW` | PP-202 Standby Underflow Flow B | `7` (`PP202`) | `3` Thickener Circuit | Input |
| `231` | `PP203_DOS` | PP-203 Flocculant Dosing Rate | `8` (`PP203`) | `3` Thickener Circuit | Input |
| `301` | `TK301_SG` | TK-301 Medium Specific Gravity | `9` (`TK301`) | `4` Magnetite Preparation | Input |
| `302` | `TK301_LVL` | TK-301 Medium Tank Level | `9` (`TK301`) | `4` Magnetite Preparation | Input |
| `303` | `MS301_REC` | MS-301 Magnetite Recovery | `9` (`TK301`) | `4` Magnetite Preparation | Input |
| `304` | `DM301_FLW` | DM-301 DMC Feed Flow | `9` (`TK301`) | `4` Magnetite Preparation | Input |
| `305` | `DM301_PRS` | DM-301 Cyclone Inlet Pressure | `9` (`TK301`) | `4` Magnetite Preparation | Input |
| `501` | `CF501_RPM` | CF-501 Basket Speed | `12` (`CF501`) | `5` Quality & Dewatering | Input |
| `502` | `CF501_VIB` | CF-501 Bearing Vibration | `12` (`CF501`) | `5` Quality & Dewatering | Input |
| `503` | `CF501_TORQ` | CF-501 Scroll Differential Torque | `12` (`CF501`) | `5` Quality & Dewatering | Input |
| `504` | `CF501_MST` | CF-501 Product Moisture | `12` (`CF501`) | `5` Quality & Dewatering | Input |
| `601` | `AN601_ASH` | AN-601 Coal Ash Content | `11` (`AN601`) | `5` Quality & Dewatering | Input |
| `602` | `AN601_MST` | AN-601 Total Moisture | `11` (`AN601`) | `5` Quality & Dewatering | Input |
| `603` | `AN601_SUL` | AN-601 Total Sulfur | `11` (`AN601`) | `5` Quality & Dewatering | Input |
| `604` | `AN601_CAL` | AN-601 Calorific Value (GCV) | `11` (`AN601`) | `5` Quality & Dewatering | Input |
| `701` | `ENV_DUST_PM10` | Crusher House Dust PM10 | `21` (`ENV_DUST`) | `6` Environmental & Safety | Input |
| `702` | `ENV_DUST_PM25` | Transfer Tower Dust PM2.5 | `21` (`ENV_DUST`) | `6` Environmental & Safety | Input |
| `711` | `CV101_RIP` | CV-101 Belt-Rip Trip Status | `22` (`BELT_RIP`) | `6` Environmental & Safety | Input |
| `712` | `CV102_RIP` | CV-102 Belt-Rip Trip Status | `22` (`BELT_RIP`) | `6` Environmental & Safety | Input |
| `721` | `ENV_CH4_PPM` | Silo Tunnel CH4 Concentration | `23` (`ENV_GAS`) | `6` Environmental & Safety | Input |

---

## Security & Archiving Posture

- **Read-Only Field Telemetry:** `EnableCommands` and `EnableFileCommands` are set to `false` in `ScadaCommConfig.xml`, and `CmdEnabled` is `false` on all three communication lines. All channels in `Cnl.xml` are `CnlTypeID=1` (Input only).
- **Read-Only User Account:** `BaseXML/User.xml` includes `arch_readonly` (and `guest`) assigned to `RoleID=3` (`ScadaGuest` — view-only, no telecontrol commands).
- **Dual Archiving:** `ScadaServerConfig.xml` enables `ModArcBasic` (`Cur`, `Min`, `Hour`, `Events`) alongside `ModArcPostgreSql` (`MinCopy`, `HourCopy`, `EventsCopy` with `ReadOnly=true` and `Connection="PostgreSqlConn"`).

---

## Opening & Deploying in `ScadaAdmin`

1. Launch **Rapid SCADA Administrator (`ScadaAdmin`)**.
2. Choose **Open Project** and select `Projects/ArchCoalPlant/ArchCoalPlant.rsproj`.
3. In the **Project Explorer**:
   - Expand **Configuration Database** to inspect **Channels**, **Devices**, **Communication Lines**, and **Objects**.
   - Expand **Instances → Default → Communicator** to review/adjust PLC IP addresses (`10.10.x.x`), Modbus register maps (`DrvModbus`), OPC UA node mappings (`DrvOpcUa`), or MQTT broker credentials (`10.10.7.10:1883`).
   - Expand **Instances → Default → Server** to configure the `PostgreSqlConn` connection string for `ModArcPostgreSql`.
4. Click **Deploy → Upload Configuration** (using `Default Profile` in `Deployment.xml`) to push the BaseXML and instance configurations to `ScadaServer` and `ScadaComm` and restart the services.
