#!/usr/bin/env bash
# ==============================================================================
# Arch SCADA - User-Space systemd --user Service Suite Setup Script
# ==============================================================================
# Installs user-space systemd unit files from deploy/systemd-user/ into
# ~/.config/systemd/user/, reloads the user systemd daemon, enables the daily
# backup timer and the login-started PLC simulator, and prints service
# management instructions.
# ==============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
UNIT_SRC_DIR="${DEPLOY_DIR}/systemd-user"
UNIT_DST_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"

if [[ ! -d "${UNIT_SRC_DIR}" ]]; then
    echo "ERROR: Missing unit source directory: ${UNIT_SRC_DIR}" >&2
    exit 1
fi

if ! command -v systemctl >/dev/null 2>&1; then
    echo "ERROR: systemctl not found in PATH" >&2
    exit 1
fi

mkdir -p "${UNIT_DST_DIR}"
mkdir -p "${HOME}/.local/share/archscada/backups"
mkdir -p "${HOME}/.local/share/ArchScada/logs"

echo "Installing Arch SCADA user-space systemd units into ${UNIT_DST_DIR}..."
for unit_file in "${UNIT_SRC_DIR}"/*; do
    [[ -f "${unit_file}" ]] || continue
    install -m 0644 "${unit_file}" "${UNIT_DST_DIR}/$(basename "${unit_file}")"
    echo "  Installed $(basename "${unit_file}")"
done

echo "Reloading systemd --user daemon..."
systemctl --user daemon-reload

echo "Enabling archscada-backup.timer..."
systemctl --user enable --now archscada-backup.timer

# The PLC simulator must run at every login so the HMI splash SERVER/DRIVERS
# readiness checks find live endpoints (HTTP :5000, Native TCP :10000, Modbus :1502).
# Set ARCHSCADA_SKIP_PLC_SIM=1 to opt out (e.g. when a real ScadaServer owns these ports).
if [[ "${ARCHSCADA_SKIP_PLC_SIM:-0}" != "1" ]]; then
    echo "Enabling and (re)starting archscada-plc-sim.service..."
    systemctl --user enable archscada-plc-sim.service
    systemctl --user restart archscada-plc-sim.service
else
    echo "Skipping archscada-plc-sim.service enablement (ARCHSCADA_SKIP_PLC_SIM=1)."
fi

echo
echo "User-space systemd units installed; archscada-backup.timer and archscada-plc-sim.service enabled."
echo "Installed unit files:"
systemctl --user list-unit-files "archscada*" --no-pager || true
echo
echo "Useful commands:"
echo "  Start simulator & web UI : systemctl --user start archscada-plc-sim archscada-web"
echo "  Start desktop HMI        : systemctl --user start archscada-desktop"
echo "  Check service status     : systemctl --user status archscada-plc-sim archscada-web archscada-backup.timer"
echo "  Follow live logs         : journalctl --user -u archscada-plc-sim -u archscada-web -f"
echo "  Run manual backup now    : systemctl --user start archscada-backup.service"
