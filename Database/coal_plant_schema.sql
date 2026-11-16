-- Arch SCADA Coal Processing Plant Schema Extension
-- Compatible with PostgreSQL and SQLite for primary/fallback storage.
-- Extends Rapid SCADA with coal plant areas, equipment assets, telemetry tags,
-- alarm journal, and immutable control audit log tables.

CREATE TABLE IF NOT EXISTS plant_areas (
    area_id VARCHAR(64) NOT NULL PRIMARY KEY,
    name VARCHAR(128) NOT NULL,
    description TEXT,
    sort_order INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS equipment_assets (
    equipment_id VARCHAR(64) NOT NULL PRIMARY KEY,
    area_id VARCHAR(64) NOT NULL,
    name VARCHAR(128) NOT NULL,
    equipment_type VARCHAR(64) NOT NULL,
    plc_device_id VARCHAR(64),
    interlock_summary TEXT,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (area_id) REFERENCES plant_areas(area_id)
);

CREATE INDEX IF NOT EXISTS idx_equipment_assets_area_id
    ON equipment_assets (area_id);

CREATE TABLE IF NOT EXISTS telemetry_tags (
    tag_id VARCHAR(64) NOT NULL PRIMARY KEY,
    equipment_id VARCHAR(64) NOT NULL,
    name VARCHAR(128) NOT NULL,
    data_type VARCHAR(32) NOT NULL DEFAULT 'Analog',
    unit VARCHAR(32) NOT NULL DEFAULT '',
    register_address VARCHAR(64),
    min_limit DOUBLE PRECISION NOT NULL DEFAULT 0,
    max_limit DOUBLE PRECISION NOT NULL DEFAULT 100,
    low_alarm_limit DOUBLE PRECISION,
    high_alarm_limit DOUBLE PRECISION,
    high_high_alarm_limit DOUBLE PRECISION,
    stale_timeout_seconds INTEGER NOT NULL DEFAULT 30,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (equipment_id) REFERENCES equipment_assets(equipment_id)
);

CREATE INDEX IF NOT EXISTS idx_telemetry_tags_equipment_id
    ON telemetry_tags (equipment_id);

CREATE TABLE IF NOT EXISTS alarm_journal (
    alarm_id VARCHAR(64) NOT NULL,
    timestamp_utc TIMESTAMP NOT NULL,
    severity VARCHAR(32) NOT NULL,
    area_id VARCHAR(64) NOT NULL,
    equipment_id VARCHAR(64) NOT NULL,
    tag_id VARCHAR(64),
    message TEXT NOT NULL,
    event_type VARCHAR(32) NOT NULL DEFAULT 'Raised',
    is_acknowledged BOOLEAN NOT NULL DEFAULT FALSE,
    acknowledged_at_utc TIMESTAMP,
    acknowledged_by VARCHAR(128),
    PRIMARY KEY (alarm_id, timestamp_utc, event_type),
    FOREIGN KEY (area_id) REFERENCES plant_areas(area_id),
    FOREIGN KEY (equipment_id) REFERENCES equipment_assets(equipment_id)
);

CREATE INDEX IF NOT EXISTS idx_alarm_journal_timestamp
    ON alarm_journal (timestamp_utc);

CREATE INDEX IF NOT EXISTS idx_alarm_journal_equipment_id
    ON alarm_journal (equipment_id);

CREATE TABLE IF NOT EXISTS control_audit_log (
    audit_id VARCHAR(64) NOT NULL PRIMARY KEY,
    timestamp_utc TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    operator_id VARCHAR(128) NOT NULL,
    equipment_id VARCHAR(64) NOT NULL,
    command_name VARCHAR(64) NOT NULL,
    command_payload TEXT,
    validation_status VARCHAR(32) NOT NULL,
    execution_status VARCHAR(32) NOT NULL,
    reason TEXT,
    FOREIGN KEY (equipment_id) REFERENCES equipment_assets(equipment_id)
);

CREATE INDEX IF NOT EXISTS idx_control_audit_log_timestamp
    ON control_audit_log (timestamp_utc);

CREATE INDEX IF NOT EXISTS idx_control_audit_log_equipment_id
    ON control_audit_log (equipment_id);
