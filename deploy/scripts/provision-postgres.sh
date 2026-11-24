#!/usr/bin/env bash
# ==============================================================================
# Arch SCADA - Local PostgreSQL 16 Historian Provisioning & Verification Script
# ==============================================================================
# Starts the local PostgreSQL 16 Docker container (archscada-historian-pg),
# applies the CHPP historian schema and Rapid SCADA storage schema idempotently,
# seeds baseline plant_areas, and runs a live backup & verification check.
# ==============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
COMPOSE_FILE="${REPO_ROOT}/deploy/docker-compose.historian.yml"
CONTAINER_NAME="${ARCHSCADA_PG_CONTAINER:-archscada-historian-pg}"
PGDATABASE="${PGDATABASE:-archscada_historian}"
PGUSER="${PGUSER:-archscada}"
ARCHSCADA_PGPASSWORD="${ARCHSCADA_PGPASSWORD:-archscada_local_dev}"
BACKUP_CHECK_DIR="/tmp/archscada-live-pg-backup-check"

log_info() {
    printf '[%s] [INFO] %s\n' "$(date -u +'%Y-%m-%dT%H:%M:%SZ')" "$*"
}

log_error() {
    printf '[%s] [ERROR] %s\n' "$(date -u +'%Y-%m-%dT%H:%M:%SZ')" "$*" >&2
}

port_is_listening() {
    local port="$1"
    if command -v rg >/dev/null 2>&1; then
        ss -tln | rg -q ":${port}[[:space:]]"
    else
        ss -tln | grep -Eq ":${port}[[:space:]]"
    fi
}

resolve_pg_port() {
    if [[ -n "${ARCHSCADA_PGPORT:-}" ]]; then
        printf '%s\n' "${ARCHSCADA_PGPORT}"
        return 0
    fi

    # Reuse existing container host port if archscada-historian-pg already exists
    local existing_port=""
    if command -v docker >/dev/null 2>&1; then
        existing_port="$(docker inspect -f '{{(index (index .NetworkSettings.Ports "5432/tcp") 0).HostPort}}' "${CONTAINER_NAME}" 2>/dev/null || true)"
        if [[ -z "${existing_port}" ]]; then
            existing_port="$(docker inspect -f '{{range $p, $conf := .HostConfig.PortBindings}}{{if eq $p "5432/tcp"}}{{(index $conf 0).HostPort}}{{end}}{{end}}' "${CONTAINER_NAME}" 2>/dev/null || true)"
        fi
    fi

    if [[ -n "${existing_port}" ]]; then
        printf '%s\n' "${existing_port}"
        return 0
    fi

    if port_is_listening 5432; then
        log_info "Host port 5432 is occupied; defaulting ARCHSCADA_PGPORT to 55432" >&2
        if port_is_listening 55432; then
            log_error "Fallback port 55432 is also occupied; set ARCHSCADA_PGPORT explicitly"
            return 1
        fi
        printf '55432\n'
    else
        printf '5432\n'
    fi
}

psql_exec() {
    docker exec -i "${CONTAINER_NAME}" psql -v ON_ERROR_STOP=1 -U "${PGUSER}" -d "${PGDATABASE}" "$@"
}

ARCHSCADA_PGPORT="$(resolve_pg_port)"
export ARCHSCADA_PGPORT
export ARCHSCADA_PGPASSWORD

log_info "Starting ${CONTAINER_NAME} on 127.0.0.1:${ARCHSCADA_PGPORT} via ${COMPOSE_FILE}"
docker compose -f "${COMPOSE_FILE}" up -d --wait

log_info "Applying Database/coal_plant_schema.sql idempotently..."
psql_exec < "${REPO_ROOT}/Database/coal_plant_schema.sql"

log_info "Ensuring Rapid SCADA storage schema (project.*) is initialized..."
has_project_app="$(psql_exec -tAc "SELECT to_regclass('project.app') IS NOT NULL;")"
if [[ "${has_project_app}" != "t" ]]; then
    psql_exec -c "CREATE SCHEMA IF NOT EXISTS project AUTHORIZATION ${PGUSER};"
    for sql_file in \
        "${REPO_ROOT}/Database/Storage/sql/04_app.sql" \
        "${REPO_ROOT}/Database/Storage/sql/05_app_config.sql" \
        "${REPO_ROOT}/Database/Storage/sql/06_app_storage.sql" \
        "${REPO_ROOT}/Database/Storage/sql/07_view_file.sql" \
        "${REPO_ROOT}/Database/Storage/sql/inserts.sql"; do
        psql_exec < "${sql_file}"
    done
else
    log_info "Schema project.app already exists; skipping Rapid SCADA base DDL."
fi

log_info "Verifying CHPP historian tables..."
required_tables=(
    "plant_areas"
    "equipment_assets"
    "telemetry_tags"
    "alarm_journal"
    "control_audit_log"
)

for tbl in "${required_tables[@]}"; do
    exists="$(psql_exec -tAc "SELECT to_regclass('public.${tbl}') IS NOT NULL;")"
    if [[ "${exists}" != "t" ]]; then
        log_error "Missing expected CHPP historian table: ${tbl}"
        exit 1
    fi
done
log_info "All 5 CHPP historian tables verified."

log_info "Seeding baseline plant_areas idempotently..."
psql_exec <<'EOSQL'
INSERT INTO plant_areas (area_id, name, description, sort_order)
VALUES
    ('CRUSH', 'Crushing & Conveyors', 'ROM Hopper, Feeder, Jaw/Roll Crushers, and Main Overland Conveyors', 1),
    ('THICK', 'Thickener Circuit', 'High-Rate Clarifier Thickener, Underflow Pumps, and Flocculant Dosing', 2),
    ('MAG',   'Magnetite Preparation', 'Dense Medium Preparation, Ball Mill, and Magnetic Separator Circuit', 3),
    ('QUAL',  'Quality & Dewatering', 'Centrifuges, Product Screens, Ash/Moisture Analyzers, and Loadout', 4),
    ('ENV',   'Environmental & Safety', 'Dust Suppression, Methane/CO Gas Monitoring, and Fire Protection', 5)
ON CONFLICT (area_id) DO NOTHING;
EOSQL

area_count="$(psql_exec -tAc "SELECT COUNT(*) FROM plant_areas;")"
if [[ -z "${area_count}" || "${area_count}" -lt 5 ]]; then
    log_error "Expected at least 5 baseline rows in plant_areas, found '${area_count}'"
    exit 1
fi
log_info "Baseline plant_areas verified (${area_count} rows)."

log_info "Running live end-to-end backup & verification check in ${BACKUP_CHECK_DIR}..."
rm -rf "${BACKUP_CHECK_DIR}"
mkdir -p "${BACKUP_CHECK_DIR}"

PGHOST="127.0.0.1" \
PGPORT="${ARCHSCADA_PGPORT}" \
PGUSER="${PGUSER}" \
PGDATABASE="${PGDATABASE}" \
PGPASSWORD="${ARCHSCADA_PGPASSWORD}" \
    "${REPO_ROOT}/deploy/scripts/create-backup.sh" --backup-dir "${BACKUP_CHECK_DIR}"

"${REPO_ROOT}/deploy/scripts/verify-backup.sh" --backup-dir "${BACKUP_CHECK_DIR}"

log_info "PostgreSQL 16 historian provisioning and live backup check PASSED (127.0.0.1:${ARCHSCADA_PGPORT})."
