#!/usr/bin/env bash
# ==============================================================================
# Arch SCADA - Automated Database & Configuration Backup Producer Script
# ==============================================================================
# Generates timestamped PostgreSQL dump (archscada_historian_<YYYYMMDD_HHMMSS>.sql.gz),
# SQLite fallback database backup (if present), and control audit JSONL copy
# (if present) with SHA-256 checksums in ${ARCHSCADA_BACKUP_DIR:-/var/backups/archscada}.
# Includes an isolated end-to-end --self-test mode validated by verify-backup.sh.
#
# Usage:
#   ./deploy/scripts/create-backup.sh [--backup-dir /var/backups/archscada]
#   ./deploy/scripts/create-backup.sh --self-test
# ==============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BACKUP_DIR="${ARCHSCADA_BACKUP_DIR:-/var/backups/archscada}"
DATA_DIR="${ARCHSCADA_DATA_DIR:-/var/lib/archscada}"
LOG_DIR="${ARCHSCADA_LOG_DIR:-/var/log/archscada}"
PGHOST_WAS_SET="${PGHOST+1}"
PGPORT_WAS_SET="${PGPORT+1}"
PGHOST="${PGHOST:-localhost}"
PGPORT="${PGPORT:-${ARCHSCADA_PGPORT:-5432}}"
PGDATABASE="${PGDATABASE:-archscada_historian}"
PGUSER="${PGUSER:-archscada}"
PG_CONTAINER="${ARCHSCADA_PG_CONTAINER:-archscada-historian-pg}"
SELF_TEST=0

usage() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Options:
  --backup-dir <path>  Target directory for backups (default: ${BACKUP_DIR})
  --data-dir <path>    Data directory containing SQLite DBs (default: ${DATA_DIR})
  --log-dir <path>     Log directory containing audit logs (default: ${LOG_DIR})
  --self-test          Run an isolated temporary backup generation and verification self-test
  -h, --help           Show this help message
EOF
}

log_info() {
    printf '[%s] [INFO] %s\n' "$(date -u +'%Y-%m-%dT%H:%M:%SZ')" "$*"
}

log_error() {
    printf '[%s] [ERROR] %s\n' "$(date -u +'%Y-%m-%dT%H:%M:%SZ')" "$*" >&2
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --backup-dir)
            BACKUP_DIR="$2"
            shift 2
            ;;
        --data-dir)
            DATA_DIR="$2"
            shift 2
            ;;
        --log-dir)
            LOG_DIR="$2"
            shift 2
            ;;
        --self-test)
            SELF_TEST=1
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            log_error "Unknown argument: $1"
            usage
            exit 2
            ;;
    esac
done

generate_checksum() {
    local target_file="$1"
    (
        cd "$(dirname "${target_file}")"
        sha256sum "$(basename "${target_file}")" > "$(basename "${target_file}").sha256"
    )
}

run_self_test() {
    local tmp_dir
    tmp_dir="$(mktemp -d -t archscada-backup-producer-selftest-XXXXXX)"
    trap 'rm -rf "${tmp_dir:-}"' EXIT INT TERM RETURN

    log_info "Running isolated backup producer self-test in ${tmp_dir}"

    local timestamp
    timestamp="$(date -u +'%Y%m%d_%H%M%S')"

    # 1. Mock PostgreSQL dump with valid header
    local pg_sql="${tmp_dir}/archscada_historian_${timestamp}.sql"
    cat > "${pg_sql}" <<'EOSQL'
-- PostgreSQL database dump
-- Arch SCADA Historian Automated Backup Self-Test
BEGIN;
CREATE TABLE IF NOT EXISTS telemetry_archive (
    tag_id TEXT NOT NULL,
    recorded_at TIMESTAMPTZ NOT NULL,
    val DOUBLE PRECISION NOT NULL
);
INSERT INTO telemetry_archive (tag_id, recorded_at, val)
VALUES ('CHPP_ROM_FEED_RATE', NOW(), 1250.5);
COMMIT;
EOSQL
    gzip -9f "${pg_sql}"
    local pg_gz="${pg_sql}.gz"
    generate_checksum "${pg_gz}"
    log_info "Generated mock PostgreSQL dump: $(basename "${pg_gz}") and $(basename "${pg_gz}").sha256"

    # 2. Mock SQLite fallback database
    local sqlite_db="${tmp_dir}/archscada_fallback_${timestamp}.db"
    if command -v sqlite3 >/dev/null 2>&1; then
        sqlite3 "${sqlite_db}" <<'EOSQLITE'
CREATE TABLE IF NOT EXISTS telemetry_cache (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    tag_id TEXT NOT NULL,
    val REAL NOT NULL
);
INSERT INTO telemetry_cache (tag_id, val) VALUES ('CHPP_MILL_CURRENT', 48.2);
EOSQLITE
    else
        printf 'SQLite format 3\000\004\000\001\001\000@  \000\000\000\001' > "${sqlite_db}"
    fi
    generate_checksum "${sqlite_db}"
    log_info "Generated mock SQLite backup: $(basename "${sqlite_db}") and $(basename "${sqlite_db}").sha256"

    # 3. Mock control audit JSONL copy
    local audit_jsonl="${tmp_dir}/archscada_control_audit_${timestamp}.jsonl"
    cat > "${audit_jsonl}" <<'EOJSONL'
{"timestamp":"2026-11-17T07:30:00Z","event":"BACKUP_SELFTEST","operator":"system","action":"PRODUCE","status":"OK"}
{"timestamp":"2026-11-17T07:30:01Z","event":"TELEMETRY_SNAP","operator":"system","action":"ARCHIVE","status":"OK"}
EOJSONL
    generate_checksum "${audit_jsonl}"
    log_info "Generated mock control audit log: $(basename "${audit_jsonl}") and $(basename "${audit_jsonl}").sha256"

    # 4. Locate verify-backup.sh
    local verify_script=""
    if [[ -x "${SCRIPT_DIR}/verify-backup.sh" ]]; then
        verify_script="${SCRIPT_DIR}/verify-backup.sh"
    elif [[ -x "./deploy/scripts/verify-backup.sh" ]]; then
        verify_script="./deploy/scripts/verify-backup.sh"
    elif [[ -x "/opt/archscada/deploy/scripts/verify-backup.sh" ]]; then
        verify_script="/opt/archscada/deploy/scripts/verify-backup.sh"
    else
        log_error "Could not locate verify-backup.sh executable"
        return 1
    fi

    log_info "Executing validation via ${verify_script} --backup-dir ${tmp_dir}"
    "${verify_script}" --backup-dir "${tmp_dir}"

    log_info "Backup producer self-test completed successfully."
}

create_production_backup() {
    log_info "Initiating Arch SCADA production backup to ${BACKUP_DIR}"
    mkdir -p "${BACKUP_DIR}"

    local timestamp
    timestamp="$(date -u +'%Y%m%d_%H%M%S')"
    local backed_up_any=0

    # 1. PostgreSQL Database Dump
    local pg_backup="${BACKUP_DIR}/archscada_historian_${timestamp}.sql.gz"
    local pg_tmp="${pg_backup}.tmp"
    local pg_container_running=0
    if command -v docker >/dev/null 2>&1 && [[ -n "$(docker ps -q --filter "name=^${PG_CONTAINER}$" --filter "status=running" 2>/dev/null || true)" ]]; then
        pg_container_running=1
    fi

    local pg_dumped=0
    if command -v pg_dump >/dev/null 2>&1 && pg_isready -h "${PGHOST}" -p "${PGPORT}" >/dev/null 2>&1; then
        # Avoid hitting an unrelated server on default port 5432 when archscada-historian-pg is running
        if [[ -n "${PGHOST_WAS_SET}" || -n "${PGPORT_WAS_SET}" || -n "${ARCHSCADA_PGPORT:-}" || "${pg_container_running}" -eq 0 ]]; then
            log_info "Creating PostgreSQL dump for database '${PGDATABASE}' via host pg_dump (${PGHOST}:${PGPORT})"
            if pg_dump -h "${PGHOST}" -p "${PGPORT}" -U "${PGUSER}" -d "${PGDATABASE}" --clean --if-exists > "${pg_tmp}" 2>/dev/null; then
                gzip -9f "${pg_tmp}"
                mv -f "${pg_tmp}.gz" "${pg_backup}"
                generate_checksum "${pg_backup}"
                log_info "PostgreSQL backup created: ${pg_backup}"
                backed_up_any=1
                pg_dumped=1
            else
                rm -f "${pg_tmp}" "${pg_tmp}.gz"
                log_info "Host pg_dump failed against ${PGHOST}:${PGPORT}; checking container fallback"
            fi
        fi
    fi

    if (( pg_dumped == 0 )) && (( pg_container_running == 1 )); then
        if docker exec "${PG_CONTAINER}" pg_isready -U "${PGUSER}" -d "${PGDATABASE}" >/dev/null 2>&1; then
            log_info "Creating PostgreSQL dump for database '${PGDATABASE}' via docker exec ${PG_CONTAINER}"
            if docker exec "${PG_CONTAINER}" pg_dump -U "${PGUSER}" -d "${PGDATABASE}" --clean --if-exists > "${pg_tmp}"; then
                gzip -9f "${pg_tmp}"
                mv -f "${pg_tmp}.gz" "${pg_backup}"
                generate_checksum "${pg_backup}"
                log_info "PostgreSQL backup created: ${pg_backup}"
                backed_up_any=1
                pg_dumped=1
            else
                rm -f "${pg_tmp}" "${pg_tmp}.gz"
                log_error "docker exec pg_dump failed in container ${PG_CONTAINER}"
                return 1
            fi
        fi
    fi

    if (( pg_dumped == 0 )); then
        log_info "Live PostgreSQL server not reachable at ${PGHOST}:${PGPORT} and container ${PG_CONTAINER} not running"
    fi

    # 2. SQLite Fallback Database Backup (if present)
    local sqlite_candidates=(
        "${DATA_DIR}/archscada.db"
        "${DATA_DIR}/historian.db"
        "${DATA_DIR}/archscada_fallback.db"
        "${DATA_DIR}/fallback.sqlite"
    )
    for src_db in "${sqlite_candidates[@]}"; do
        if [[ -f "${src_db}" ]]; then
            local sqlite_backup="${BACKUP_DIR}/archscada_fallback_${timestamp}.db"
            log_info "Found SQLite database at ${src_db}; creating backup at ${sqlite_backup}"
            if command -v sqlite3 >/dev/null 2>&1; then
                sqlite3 -readonly "${src_db}" ".backup '${sqlite_backup}'"
            else
                cp "${src_db}" "${sqlite_backup}"
            fi
            generate_checksum "${sqlite_backup}"
            log_info "SQLite backup created: ${sqlite_backup}"
            backed_up_any=1
            break
        fi
    done

    # 3. Control Audit Log Backup (if present)
    local audit_candidates=(
        "${LOG_DIR}/control_audit.jsonl"
        "${DATA_DIR}/control_audit.jsonl"
        "${LOG_DIR}/audit.jsonl"
    )
    for src_audit in "${audit_candidates[@]}"; do
        if [[ -f "${src_audit}" ]]; then
            local audit_backup="${BACKUP_DIR}/archscada_control_audit_${timestamp}.jsonl"
            log_info "Found control audit log at ${src_audit}; copying to ${audit_backup}"
            cp "${src_audit}" "${audit_backup}"
            generate_checksum "${audit_backup}"
            log_info "Control audit backup created: ${audit_backup}"
            break
        fi
    done

    if (( backed_up_any == 0 )); then
        log_error "No database backup could be created (neither reachable PostgreSQL nor SQLite database found in ${DATA_DIR})"
        return 1
    fi

    log_info "Arch SCADA backup generation completed successfully in ${BACKUP_DIR}"
}

if (( SELF_TEST == 1 )); then
    run_self_test
    exit 0
fi

create_production_backup
exit 0
