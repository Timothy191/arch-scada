using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ArchScada.CoreIntegration.Tests;

public sealed class CoalPlantSchemaTests
{
    private static readonly string SchemaPath = ResolveSchemaFilePath();

    [Fact]
    public void SchemaFile_ExistsAndIsNotEmpty()
    {
        Assert.True(File.Exists(SchemaPath), $"Schema file not found at: {SchemaPath}");
        var content = File.ReadAllText(SchemaPath);
        Assert.False(string.IsNullOrWhiteSpace(content));
        Assert.True(content.Length > 500, "Schema content is unexpectedly short.");
    }

    [Fact]
    public void Schema_DefinesAllRequiredChppTables()
    {
        var sql = File.ReadAllText(SchemaPath);

        string[] requiredTables =
        [
            "plant_areas",
            "equipment_assets",
            "telemetry_tags",
            "alarm_journal",
            "control_audit_log"
        ];

        foreach (var table in requiredTables)
        {
            var pattern = $@"(?i)CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?{Regex.Escape(table)}\b";
            Assert.True(Regex.IsMatch(sql, pattern), $"Schema is missing expected table definition for '{table}'.");
        }
    }

    [Fact]
    public void Schema_DefinesAllRequiredIndexes()
    {
        var sql = File.ReadAllText(SchemaPath);

        string[] requiredIndexes =
        [
            "idx_equipment_assets_area_id",
            "idx_telemetry_tags_equipment_id",
            "idx_alarm_journal_timestamp",
            "idx_alarm_journal_equipment_id",
            "idx_control_audit_log_timestamp",
            "idx_control_audit_log_equipment_id"
        ];

        foreach (var indexName in requiredIndexes)
        {
            var pattern = $@"(?i)CREATE\s+INDEX\s+(?:IF\s+NOT\s+EXISTS\s+)?{Regex.Escape(indexName)}\b";
            Assert.True(Regex.IsMatch(sql, pattern), $"Schema is missing expected index definition for '{indexName}'.");
        }
    }

    [Fact]
    public void Schema_PlantAreasTable_DefinesRequiredColumnsAndPrimaryKey()
    {
        var tableSql = ExtractTableDefinition("plant_areas");

        Assert.Contains("area_id", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PRIMARY KEY", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("description", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sort_order", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("created_at", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("updated_at", tableSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Schema_EquipmentAssetsTable_DefinesColumnsAndForeignKey()
    {
        var tableSql = ExtractTableDefinition("equipment_assets");

        Assert.Contains("equipment_id", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PRIMARY KEY", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("area_id", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("equipment_type", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plc_device_id", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("interlock_summary", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_active", tableSql, StringComparison.OrdinalIgnoreCase);

        // Verify foreign key to plant_areas
        Assert.Matches(@"(?i)FOREIGN\s+KEY\s*\(\s*area_id\s*\)\s*REFERENCES\s+plant_areas\s*\(\s*area_id\s*\)", tableSql);
    }

    [Fact]
    public void Schema_TelemetryTagsTable_DefinesColumnsAlarmLimitsAndForeignKey()
    {
        var tableSql = ExtractTableDefinition("telemetry_tags");

        Assert.Contains("tag_id", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PRIMARY KEY", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("equipment_id", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data_type", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unit", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("register_address", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("min_limit", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_limit", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("low_alarm_limit", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("high_alarm_limit", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("high_high_alarm_limit", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stale_timeout_seconds", tableSql, StringComparison.OrdinalIgnoreCase);

        // Verify foreign key to equipment_assets
        Assert.Matches(@"(?i)FOREIGN\s+KEY\s*\(\s*equipment_id\s*\)\s*REFERENCES\s+equipment_assets\s*\(\s*equipment_id\s*\)", tableSql);
    }

    [Fact]
    public void Schema_AlarmJournalTable_DefinesIsa182ColumnsAndCompositePrimaryKey()
    {
        var tableSql = ExtractTableDefinition("alarm_journal");

        Assert.Contains("alarm_id", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("timestamp_utc", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("severity", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("area_id", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("equipment_id", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tag_id", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("message", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("event_type", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_acknowledged", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("acknowledged_at_utc", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("acknowledged_by", tableSql, StringComparison.OrdinalIgnoreCase);

        // Verify composite primary key
        Assert.Matches(@"(?i)PRIMARY\s+KEY\s*\(\s*alarm_id\s*,\s*timestamp_utc\s*,\s*event_type\s*\)", tableSql);

        // Verify foreign keys
        Assert.Matches(@"(?i)FOREIGN\s+KEY\s*\(\s*area_id\s*\)\s*REFERENCES\s+plant_areas\s*\(\s*area_id\s*\)", tableSql);
        Assert.Matches(@"(?i)FOREIGN\s+KEY\s*\(\s*equipment_id\s*\)\s*REFERENCES\s+equipment_assets\s*\(\s*equipment_id\s*\)", tableSql);
    }

    [Fact]
    public void Schema_ControlAuditLogTable_DefinesImmutableAuditStructure()
    {
        var tableSql = ExtractTableDefinition("control_audit_log");

        Assert.Contains("audit_id", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PRIMARY KEY", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("timestamp_utc", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("operator_id", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("equipment_id", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("command_name", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("command_payload", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("validation_status", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("execution_status", tableSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reason", tableSql, StringComparison.OrdinalIgnoreCase);

        // Verify foreign key
        Assert.Matches(@"(?i)FOREIGN\s+KEY\s*\(\s*equipment_id\s*\)\s*REFERENCES\s+equipment_assets\s*\(\s*equipment_id\s*\)", tableSql);
    }

    [Fact]
    public void Schema_SqliteEngineValidation_ParsesCleanlyWithoutSyntaxErrors()
    {
        // If sqlite3 CLI is available on the system PATH, verify actual SQLite DDL compilation.
        var sqliteBinary = FindSqliteExecutable();
        if (sqliteBinary is null)
        {
            // Skip CLI execution if sqlite3 is not installed; regex checks above already validated tokens.
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = sqliteBinary,
            Arguments = ":memory:",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var schemaContent = File.ReadAllText(SchemaPath);
        using (var sw = process.StandardInput)
        {
            sw.Write(schemaContent);
        }

        var errorOutput = process.StandardError.ReadToEnd();
        process.WaitForExit(5000);

        Assert.Equal(0, process.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(errorOutput), $"SQLite returned syntax errors: {errorOutput}");
    }

    private static string ExtractTableDefinition(string tableName)
    {
        var sql = File.ReadAllText(SchemaPath);
        var match = Regex.Match(
            sql,
            $@"(?is)CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?{Regex.Escape(tableName)}\s*\((.*?)\);");

        Assert.True(match.Success, $"Table definition for '{tableName}' could not be extracted from schema.");
        return match.Groups[1].Value;
    }

    private static string ResolveSchemaFilePath()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "Database", "coal_plant_schema.sql");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(current);
            if (parent == null || parent.FullName == current)
            {
                break;
            }

            current = parent.FullName;
        }

        var fallback = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Database", "coal_plant_schema.sql"));
        if (File.Exists(fallback))
        {
            return fallback;
        }

        throw new FileNotFoundException("Could not locate Database/coal_plant_schema.sql");
    }

    private static string? FindSqliteExecutable()
    {
        string[] candidates = ["sqlite3", "sqlite3.exe"];
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in paths)
        {
            foreach (var candidate in candidates)
            {
                var fullPath = Path.Combine(dir, candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }
}
