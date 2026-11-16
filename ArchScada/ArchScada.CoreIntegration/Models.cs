namespace ArchScada.CoreIntegration;

/// <summary>
/// Represents a coal processing plant area (e.g., Crushing, Conveyors, Thickeners, Magnetite Prep).
/// </summary>
public sealed record PlantAreaConfig
{
    public string AreaId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int SortOrder { get; init; }
}

/// <summary>
/// Represents a physical equipment asset monitored by Arch SCADA.
/// </summary>
public sealed record EquipmentDefinition
{
    public string EquipmentId { get; init; } = string.Empty;
    public string AreaId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string EquipmentType { get; init; } = string.Empty;
    public string? PlcDeviceId { get; init; }
    public string? InterlockSummary { get; init; }
}

/// <summary>
/// Represents telemetry tag metadata and alarm thresholds.
/// </summary>
public sealed record TagMetadata
{
    public string TagId { get; init; } = string.Empty;
    public string EquipmentId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DataType { get; init; } = "Analog";
    public string Unit { get; init; } = string.Empty;
    public string? RegisterAddress { get; init; }
    public double MinLimit { get; init; }
    public double MaxLimit { get; init; } = 100.0;
    public double? LowAlarmLimit { get; init; }
    public double? HighAlarmLimit { get; init; }
    public double? HighHighAlarmLimit { get; init; }
    public int StaleTimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// Offline-capable snapshot of plant areas, equipment definitions, and tag metadata.
/// </summary>
public sealed record PlantConfigurationSnapshot
{
    public string PlantId { get; init; } = "arch-coal-plant";
    public string Version { get; init; } = "1.0";
    public DateTimeOffset SavedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<PlantAreaConfig> Areas { get; init; } = [];
    public IReadOnlyList<EquipmentDefinition> Equipment { get; init; } = [];
    public IReadOnlyList<TagMetadata> Tags { get; init; } = [];
}

/// <summary>
/// Represents a single timestamped telemetry sample stored in the local historian.
/// </summary>
public sealed record TelemetrySample
{
    public string TagId { get; init; } = string.Empty;
    public double Value { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Quality { get; init; } = "Good";
}

/// <summary>
/// Represents an immutable alarm lifecycle entry in the local alarm journal.
/// </summary>
public sealed record AlarmJournalEntry
{
    public string AlarmId { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Severity { get; init; } = "Medium";
    public string AreaId { get; init; } = string.Empty;
    public string EquipmentId { get; init; } = string.Empty;
    public string? TagId { get; init; }
    public string Message { get; init; } = string.Empty;
    public string EventType { get; init; } = "Raised";
    public bool IsAcknowledged { get; init; }
    public DateTimeOffset? AcknowledgedAtUtc { get; init; }
    public string? AcknowledgedBy { get; init; }
}

/// <summary>
/// Represents a live tag value returned by ScadaServer.
/// </summary>
public sealed record TagValueSnapshot
{
    public string TagId { get; init; } = string.Empty;
    public double Value { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Quality { get; init; } = "Good";
}

/// <summary>
/// Represents ScadaServer health and runtime status.
/// </summary>
public sealed record ServerHealthStatus
{
    public bool IsOnline { get; init; }
    public string ServerVersion { get; init; } = string.Empty;
    public DateTimeOffset ServerTimeUtc { get; init; }
    public int ActiveChannelCount { get; init; }
}
