namespace ArchScada.ControlBoundary;

public enum ControlExecutionStatus
{
    Simulated,
    Success,
    Rejected,
    Blocked,
    Timeout,
    Unconfirmed
}

public enum CommunicationQuality
{
    Good,
    Degraded,
    Poor,
    Bad
}

public sealed record ControlCommandRequest
{
    public string OperatorId { get; init; } = string.Empty;
    public IReadOnlyCollection<string> OperatorPermissions { get; init; } = Array.Empty<string>();
    public string RequiredPermission { get; init; } = string.Empty;
    public bool IsSessionActive { get; init; } = true;
    public string CommandType { get; init; } = string.Empty;
    public string EquipmentId { get; init; } = string.Empty;
    public double? NumericParameter { get; init; }
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();
    public bool OperatorConfirmed { get; init; }
    public bool InterlocksSatisfied { get; init; }
    public IReadOnlyCollection<string> InterlockIds { get; init; } = Array.Empty<string>();
    public bool EquipmentFaulted { get; init; }
    public CommunicationQuality CommunicationQuality { get; init; } = CommunicationQuality.Good;
    public TimeSpan? LastTelemetryAge { get; init; }
    public bool IsAutomaticTrigger { get; init; }
    public string? TriggerSource { get; init; }
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ControlCommandResult
{
    public bool Accepted { get; init; }
    public bool ExecutedOnDevice { get; init; }
    public bool IsSimulated { get; init; }
    public ControlExecutionStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? FailureReason { get; init; }
    public string? EquipmentFeedbackState { get; init; }
    public string? MatchedCauseAndEffectRuleId { get; init; }
    public string AuditId { get; init; } = string.Empty;
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class ControlAuthorizationState
{
    /// <summary>
    /// Disabled by default. Must be explicitly authorized by site engineering / plant manager.
    /// </summary>
    public bool IsRealControlAuthorized { get; set; }

    /// <summary>
    /// Must be true (Cause-and-Effect matrix uploaded and verified) before real control is allowed.
    /// </summary>
    public bool CauseAndEffectVerified { get; set; }

    public string? AuthorizationDocumentId { get; set; }
    public string? AuthorizedBy { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public string? Scope { get; set; }
    public IReadOnlyCollection<string> AuthorizedEquipmentIds { get; set; } = Array.Empty<string>();

    public bool CanExecuteRealControl => IsRealControlAuthorized && CauseAndEffectVerified;
}

public sealed record CauseAndEffectRule
{
    public string RuleId { get; init; } = string.Empty;
    public string EquipmentId { get; init; } = string.Empty;
    public string CommandType { get; init; } = string.Empty;
    public string RequiredPermission { get; init; } = string.Empty;
    public double? MinParameter { get; init; }
    public double? MaxParameter { get; init; }
    public string SafeState { get; init; } = string.Empty;
    public IReadOnlyCollection<string> RequiredInterlocks { get; init; } = Array.Empty<string>();
    public bool IsApproved { get; init; } = true;
}

public sealed record ControlAuditEntry
{
    public string AuditId { get; init; } = Guid.NewGuid().ToString("D");
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string User { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string Equipment { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();
    public string Result { get; init; } = string.Empty;
    public string? Feedback { get; init; }
    public string? CeRule { get; init; }
    public IReadOnlyCollection<string> Interlocks { get; init; } = Array.Empty<string>();
    public string CommunicationQuality { get; init; } = string.Empty;
    public double DurationSeconds { get; init; }
    public bool Simulated { get; init; }
    public string? Reason { get; init; }
    public string PreviousHash { get; init; } = string.Empty;
    public string Hash { get; init; } = string.Empty;
}
