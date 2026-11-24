#!/usr/bin/env bash
# ==============================================================================
# Arch SCADA - Automated 6-Gate Quality & Stability Verification Suite
# ==============================================================================
# Usage:
#   ./verify-quality-gates.sh [--skip-gui] [--clean]
#
# Options:
#   --skip-gui   Skip Gate 6 (Desktop 3D Engine GUI capture & GPU budget check)
#   --clean      Wipe all bin/ and obj/ directories across the repo before Gate 1
#   -h, --help   Show this help message
# ==============================================================================
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO_ROOT"

SKIP_GUI=0
CLEAN_BUILD=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --skip-gui)
      SKIP_GUI=1
      shift
      ;;
    --clean)
      CLEAN_BUILD=1
      shift
      ;;
    -h|--help)
      sed -n '2,12p' "${BASH_SOURCE[0]}"
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 2
      ;;
  esac
done

WEB_PID=""
WEB_TMP=""
GUI_TMP=""

cleanup() {
  if [[ -n "${WEB_PID:-}" ]] && kill -0 "$WEB_PID" 2>/dev/null; then
    kill "$WEB_PID" 2>/dev/null || true
    wait "$WEB_PID" 2>/dev/null || true
  fi
  if [[ -n "${WEB_TMP:-}" ]] && [[ -d "$WEB_TMP" ]]; then
    rm -rf "$WEB_TMP"
  fi
  if [[ -n "${GUI_TMP:-}" ]] && [[ -d "$GUI_TMP" ]]; then
    rm -rf "$GUI_TMP"
  fi
}
trap cleanup EXIT INT TERM

if [[ "$CLEAN_BUILD" -eq 1 ]]; then
  echo "Cleaning existing bin/ and obj/ directories..."
  find "$REPO_ROOT" -maxdepth 5 -type d \( -name bin -o -name obj \) -not -path '*/.*/*' -prune -exec rm -rf {} +
fi

# ==============================================================================
# Gate 1: Full 18-Solution Release Build & Strict Zero-Warning Gate
# ==============================================================================
echo "=== [GATE 1/6] Full 18-Solution Release Build & Strict Zero-Warning Gate ==="
./build-all.sh Release
dotnet build Plant.sln -c Release -warnaserror
echo "[GATE 1/6] Full 18-Solution Release Build & Strict Zero-Warning Gate PASS"

# ==============================================================================
# Gate 2: Complete Unit, Integration & Stability Test Gate
# ==============================================================================
echo
echo "=== [GATE 2/6] Complete Unit, Integration & Stability Test Gate ==="
dotnet test Plant.sln -c Release --no-build -warnaserror
echo "[GATE 2/6] Complete Unit, Integration & Stability Test Gate PASS"

# ==============================================================================
# Gate 3: 3D GLB Binary Header & Topology Gate
# ==============================================================================
echo
echo "=== [GATE 3/6] 3D GLB Binary Header & Topology Gate ==="
python3 - <<'PY'
import glob
import os
import re
import struct
import sys

export_dir = os.path.join("ArchScada", "Assets", "3D", "Exported")
glb_paths = sorted(glob.glob(os.path.join(export_dir, "*.glb")))

if len(glb_paths) != 16:
    print(f"ERROR: Expected 16 .glb files in {export_dir}, found {len(glb_paths)}", file=sys.stderr)
    sys.exit(1)

exported_names = sorted(os.path.basename(p) for p in glb_paths)

for path in glb_paths:
    size = os.path.getsize(path)
    if size <= 8192:
        print(f"ERROR: {path} size {size} bytes <= 8192 bytes", file=sys.stderr)
        sys.exit(1)
    with open(path, "rb") as f:
        header = f.read(12)
    if len(header) < 12:
        print(f"ERROR: {path} truncated header", file=sys.stderr)
        sys.exit(1)
    magic, version, length = struct.unpack("<III", header)
    if magic != 0x46546C67 or version != 2:
        print(f"ERROR: {path} invalid glTF v2 header (magic=0x{magic:08X}, version={version})", file=sys.stderr)
        sys.exit(1)
    if length != size:
        print(f"ERROR: {path} header length {length} != file size {size}", file=sys.stderr)
        sys.exit(1)

with open(os.path.join("ArchScada", "ArchScada.Desktop", "Controls", "ThreeD", "Plant3DSceneData.cs"), encoding="utf-8") as f:
    desktop_refs = sorted(set(re.findall(r"[A-Za-z0-9_]{4,}\.glb", f.read())))

with open(os.path.join("ScadaWeb", "ScadaWeb", "ScadaWeb", "Pages", "DigitalTwin3D.cshtml"), encoding="utf-8") as f:
    web_refs = sorted(set(re.findall(r'"([A-Za-z0-9_]+\.glb)"', f.read())))

if desktop_refs != exported_names:
    print(f"ERROR: Plant3DSceneData.cs GLB mismatch.\n  Expected: {exported_names}\n  Found:    {desktop_refs}", file=sys.stderr)
    sys.exit(1)

if web_refs != exported_names:
    print(f"ERROR: DigitalTwin3D.cshtml GLB mismatch.\n  Expected: {exported_names}\n  Found:    {web_refs}", file=sys.stderr)
    sys.exit(1)

print(f"Verified 16/16 glTF v2 .glb files (>8 KB) matching Plant3DSceneData.cs and DigitalTwin3D.cshtml.")
PY
echo "[GATE 3/6] 3D GLB Binary Header & Topology Gate PASS"

# ==============================================================================
# Gate 4: Backup & Tamper-Evident Audit Self-Test Gate
# ==============================================================================
echo
echo "=== [GATE 4/6] Backup & Tamper-Evident Audit Self-Test Gate ==="
./deploy/scripts/create-backup.sh --self-test
./deploy/scripts/verify-backup.sh --self-test
echo "[GATE 4/6] Backup & Tamper-Evident Audit Self-Test Gate PASS"

# ==============================================================================
# Gate 5: ScadaWeb Read-Only Security & /DigitalTwin3D HTTP Gate
# ==============================================================================
echo
echo "=== [GATE 5/6] ScadaWeb Read-Only Security & /DigitalTwin3D HTTP Gate ==="
WEB_TMP="$(mktemp -d -t archscada-gate5-web-XXXXXX)"
dotnet publish ScadaWeb/ScadaWeb/ScadaWeb/ScadaWeb.csproj -c Release -o "$WEB_TMP" --nologo -v quiet -p:AllowMissingPrunePackageData=true

WEB_PORT="${ARCHSCADA_GATE5_PORT:-5189}"
WEB_URL="http://127.0.0.1:${WEB_PORT}"

(
  cd "$WEB_TMP"
  ASPNETCORE_URLS="$WEB_URL" ASPNETCORE_ENVIRONMENT="Production" \
    exec dotnet "$WEB_TMP/ScadaWeb.dll" --urls "$WEB_URL"
) > "$WEB_TMP/scadaweb.log" 2>&1 &
WEB_PID=$!

READY=0
for _ in $(seq 1 40); do
  if ! kill -0 "$WEB_PID" 2>/dev/null; then
    echo "ERROR: ScadaWeb exited prematurely. Log:" >&2
    cat "$WEB_TMP/scadaweb.log" >&2 || true
    exit 1
  fi
  if curl -s -o /dev/null -w "%{http_code}" "${WEB_URL}/DigitalTwin3D" | grep -q '^200$'; then
    READY=1
    break
  fi
  sleep 0.25
done

if [[ "$READY" -ne 1 ]]; then
  echo "ERROR: ScadaWeb failed to become ready on ${WEB_URL} within 10s. Log:" >&2
  cat "$WEB_TMP/scadaweb.log" >&2 || true
  exit 1
fi

STATUS_PAGE="$(curl -sS -o /dev/null -w "%{http_code}" "${WEB_URL}/DigitalTwin3D")"
[[ "$STATUS_PAGE" == "200" ]] || { echo "ERROR: GET /DigitalTwin3D returned HTTP ${STATUS_PAGE}" >&2; exit 1; }

TELEM_BODY="$(curl -fsS "${WEB_URL}/DigitalTwin3D?handler=Telemetry")"
if ! grep -q '"readOnly":true' <<<"$TELEM_BODY"; then
  echo "ERROR: Telemetry JSON missing \"readOnly\":true -> $TELEM_BODY" >&2
  exit 1
fi

STATUS_GLB="$(curl -sS -o /dev/null -w "%{http_code}" "${WEB_URL}/models3d/stockpile_cone.glb")"
[[ "$STATUS_GLB" == "200" ]] || { echo "ERROR: GET /models3d/stockpile_cone.glb returned HTTP ${STATUS_GLB}" >&2; exit 1; }

STATUS_POST="$(curl -sS -o /dev/null -w "%{http_code}" -X POST "${WEB_URL}/DigitalTwin3D")"
[[ "$STATUS_POST" == "403" ]] || { echo "ERROR: POST /DigitalTwin3D returned HTTP ${STATUS_POST} (expected 403)" >&2; exit 1; }

kill "$WEB_PID" 2>/dev/null || true
wait "$WEB_PID" 2>/dev/null || true
WEB_PID=""
rm -rf "$WEB_TMP"
WEB_TMP=""
echo "[GATE 5/6] ScadaWeb Read-Only Security & /DigitalTwin3D HTTP Gate PASS"

# ==============================================================================
# Gate 6: Desktop 3D Engine GPU & Performance Budget Gate
# ==============================================================================
echo
echo "=== [GATE 6/6] Desktop 3D Engine GPU & Performance Budget Gate ==="
if [[ "$SKIP_GUI" -eq 1 ]]; then
  echo "[GATE 6/6] Desktop 3D Engine GPU & Performance Budget Gate SKIPPED (--skip-gui)"
elif [[ -z "${WAYLAND_DISPLAY:-}" && -z "${DISPLAY:-}" ]]; then
  echo "[GATE 6/6] Desktop 3D Engine GPU & Performance Budget Gate SKIPPED (no display server)"
else
  GUI_TMP="$(mktemp -d -t archscada-gate6-3d-XXXXXX)"
  DESKTOP_BIN="./ArchScada/ArchScada.Desktop/bin/Release/net10.0/ArchScada.Desktop"
  [[ -x "$DESKTOP_BIN" ]] || { echo "ERROR: Desktop binary not found at $DESKTOP_BIN" >&2; exit 1; }

  "$DESKTOP_BIN" --capture-3d-views "$GUI_TMP"

  python3 - "$GUI_TMP" <<'PY'
import glob
import json
import os
import sys

out_dir = sys.argv[1]
pngs = sorted(glob.glob(os.path.join(out_dir, "*.png")))
if len(pngs) != 8:
    print(f"ERROR: Expected 8 captured PNGs in {out_dir}, found {len(pngs)}", file=sys.stderr)
    sys.exit(1)

for p in pngs:
    if os.path.getsize(p) <= 0:
        print(f"ERROR: Empty PNG captured: {p}", file=sys.stderr)
        sys.exit(1)

metrics_path = os.path.join(out_dir, "perf_metrics.json")
if not os.path.isfile(metrics_path):
    print(f"ERROR: Missing perf_metrics.json in {out_dir}", file=sys.stderr)
    sys.exit(1)

with open(metrics_path, encoding="utf-8") as f:
    m = json.load(f)

cold_ms = float(m["ColdGlbLoadMs"])
flow_us = float(m["FlowAndPoseMicrosPerFrame"])
draw_calls = int(m["LastFrameDrawCalls"])
hw_accel = bool(m["IsHardwareAccelerated"])

print(
    f"3D Metrics: PNGs={len(pngs)}, IsHardwareAccelerated={hw_accel}, "
    f"ColdGlbLoadMs={cold_ms}, FlowAndPoseMicrosPerFrame={flow_us}, LastFrameDrawCalls={draw_calls}"
)

if cold_ms >= 2000.0:
    print(f"ERROR: ColdGlbLoadMs {cold_ms} >= 2000", file=sys.stderr)
    sys.exit(1)
if flow_us >= 500.0:
    print(f"ERROR: FlowAndPoseMicrosPerFrame {flow_us} >= 500", file=sys.stderr)
    sys.exit(1)
if hw_accel and draw_calls >= 100:
    print(f"ERROR: LastFrameDrawCalls {draw_calls} >= 100 under hardware acceleration", file=sys.stderr)
    sys.exit(1)
PY

  rm -rf "$GUI_TMP"
  GUI_TMP=""
  echo "[GATE 6/6] Desktop 3D Engine GPU & Performance Budget Gate PASS"
fi

echo
echo "ALL QUALITY GATES COMPLETED SUCCESSFULLY."
