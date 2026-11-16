# Phase 5: ScadaWeb Read-Only Hardening

## 1. Overview

This document defines the mandatory security and safety hardening controls for **Phase 5 (`ScadaWeb`)**, the browser-based read-only monitoring portal for Arch SCADA. It extends the control safety boundary established in [03-control-safety-boundary.md](03-control-safety-boundary.md) and is codified in machine-readable form in [`ArchScada/Config/ReadOnlyWebPolicy.json`](../../ArchScada/Config/ReadOnlyWebPolicy.json).

`ScadaWeb` exists strictly for **read-only operational visibility** (live telemetry, equipment statuses, alarm logs, historian trends, and 3D digital twin status). It must **never** act as a remote control surface for plant machinery.

---

## 2. Hardening Pillars

### 2.1 Zero Remote Control Enforcement

- **No PLC Write Path:** `ScadaWeb` must not reference, load, or instantiate any PLC write adapter, Modbus coil/register writer, or OPC-UA write client.
- **HTTP Method Allowlist:** Only `GET`, `HEAD`, and `OPTIONS` are permitted on application and telemetry endpoints.
- **Write Method Rejection:** All `POST`, `PUT`, `PATCH`, and `DELETE` requests outside explicit authentication lifecycle routes (`/api/v1/auth/login`, `/api/v1/auth/logout`, `/api/v1/auth/refresh`) are rejected at the middleware and reverse-proxy layers with `403 Forbidden`.
- **UI & API Contract:** Every API response carries `X-Scada-Mode: read-only`, and the web UI renders no start/stop, setpoint, or interlock-bypass controls.
- **Independent Safety Systems:** Hardwired E-stops, safety relays, and certified safety PLCs remain completely isolated from `ScadaWeb`.

### 2.2 Internal-Network-Only Binding

- **No Public Exposure:** `ScadaWeb` must **never** bind to `0.0.0.0` on a public interface or be exposed to the public internet.
- **Loopback + Internal Proxy:** The `ScadaWeb` application process binds exclusively to `127.0.0.1:8443` behind an internal reverse proxy on the plant OT/DMZ boundary.
- **CIDR Allowlist:** Ingress is restricted to approved internal subnets defined in `ReadOnlyWebPolicy.json`:
  - `127.0.0.1/32` (local host / reverse proxy)
  - `10.20.0.0/16` (internal plant corporate/engineering VPN)
  - `192.168.100.0/24` (plant control-room monitoring VLAN)
- **Transport Security:** TLS 1.3 minimum (`minVersion: TLSv1.3`) with HSTS enabled and strict CORS (`https://scada.internal.plant.local` only; wildcard origins prohibited).

### 2.3 Role-Based View Restrictions (RBAC)

Access is **deny-by-default** and scoped to read-only roles:

| Role | Permitted Scopes | Allowed Plant Areas | Historian CSV Export | Diagnostics View |
| :--- | :--- | :--- | :---: | :---: |
| `Viewer` | `telemetry:read`, `equipment:read`, `alarms:read` | `Overview`, `CrushingConveyors`, `ThickenerCircuit`, `MagnetitePrep` | No | No |
| `OperatorReadOnly` | + `trends:read`, `digital-twin:read` | All (`*`) | No | No |
| `EngineerReadOnly` | + `diagnostics:read`, `historian:export` | All (`*`) | Yes | Yes |
| `Auditor` | `audit:read`, `policy:read`, `alarms:read` | All (`*`) | Yes | Yes |

No role in `ScadaWeb` grants `control:write`, `setpoint:write`, or `plc:acknowledge` permissions.

### 2.4 Rate Limiting

Sliding-window rate limiting protects the SCADA telemetry backend from accidental client loops or denial-of-service conditions:

- **Read API (`apiRead`):** `120` requests/min per IP, `180` requests/min per authenticated session, burst `30`.
- **Live Telemetry Streams (`telemetryStream`):** Max `3` concurrent SSE/WebSocket connections per session and `10` per IP.
- **Authentication (`authLogin`):** `10` requests/min per IP; account/IP lockout for `15` minutes after `5` consecutive failed login attempts.
- **Historian Exports (`historianExport`):** `20` requests/hour per session.
- **Response Behavior:** Exceeded limits return `429 Too Many Requests` with a `Retry-After` header and emit an audit log entry.

### 2.5 Session Timeouts & Cookie Hardening

- **Idle Timeout:** `15` minutes of inactivity invalidates the session.
- **Absolute Max Lifetime:** `8` hours maximum session duration regardless of activity (aligned with plant shift length).
- **Concurrency Limit:** Maximum `2` concurrent active sessions per user.
- **Cookie Flags:** `__Host-ScadaWebSession` with `HttpOnly`, `Secure`, and `SameSite=Strict`.
- **Revocation:** Immediate server-side session revocation on logout, timeout, or RBAC role change.

---

## 3. Audit & Verification Checklist

Before enabling `ScadaWeb` on any internal plant network:

1. Validate [`ArchScada/Config/ReadOnlyWebPolicy.json`](../../ArchScada/Config/ReadOnlyWebPolicy.json) parses cleanly and `readOnlyEnforcement.allowRemoteControl` is `false`.
2. Confirm `ScadaWeb` listens only on `127.0.0.1` (`ss -tlnp | grep 8443`).
3. Verify `POST`/`PUT`/`DELETE` requests to telemetry/equipment routes return `403 Forbidden`.
4. Verify unauthenticated requests are rejected and `Viewer` accounts cannot access `/diagnostics` or CSV export endpoints.
5. Confirm `429 Too Many Requests` triggers under burst load and sessions expire after `15` minutes idle.
